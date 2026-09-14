using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using BillingControl.Services.WhatsApp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModelOutboundContentKind = BillingControl.Models.WhatsAppOutboundContentKind;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private sealed record QueueFixture(
        int ContactId,
        int AddressId,
        int CustomerId,
        int EngagementId,
        int ServiceId,
        int ConversationId,
        int RequestId,
        int ItemId,
        int? ContactParticipantId,
        int? ScopeId);

    private static WhatsAppOutboundQueueService QueueService(
        AppDbContext db,
        IDataProtectionProvider keys,
        IWhatsAppProvider? provider = null) =>
        new(db, new DocumentRequestBatchService(db), keys, WhatsAppTestClock(), provider);

    private static WhatsAppOutboundPreviewInput QueuePreviewInput(
        QueueFixture fixture,
        WhatsAppOutboundContent? content = null,
        int? contactId = null,
        int? conversationId = null,
        string actor = "phase10b-actor",
        string source = "BillingControl.WhatsApp") =>
        new(
            contactId ?? fixture.ContactId,
            conversationId ?? fixture.ConversationId,
            new[] { new DocumentBatchRequestSelection(fixture.RequestId, 1) },
            content ?? new WhatsAppTextContent("Please provide the outstanding document."),
            actor,
            source);

    private static WhatsAppProviderCapabilities GroupTestCapabilities() => new(
        supportsDirectMessaging: true,
        supportsGroupMessaging: true,
        supportsText: true,
        supportsTemplateMessaging: true);

    private static async Task<QueueFixture> AddQueueFixtureAsync(
        AppDbContext db,
        string prefix,
        bool group = false,
        bool addScope = true,
        ContactWhatsAppConsentState consent = ContactWhatsAppConsentState.OptedIn,
        bool addContactParticipant = true,
        bool addBusinessSender = true)
    {
        var service = await AddDocumentServiceAsync(db, $"queue-service-{prefix}");
        var documentFixture = await AddDocumentFixtureAsync(db, $"queue-document-{prefix}", service.Id);
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken($"queue-template-{prefix}"), 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, DocumentToken($"queue-template-item-{prefix}"));
        var request = await AddDocumentRequestAsync(
            db,
            documentFixture.WorkItemId,
            template.Id,
            status: DocumentRequestStatus.ReadyToSend);
        var requestItem = await AddDocumentRequestItemAsync(
            db,
            request.Id,
            templateItem.Id,
            DocumentToken($"queue-request-item-{prefix}"));

        var contact = await AddContactAsync(db, $"queue-contact-{prefix}");
        var address = await AddContactAddressAsync(
            db,
            contact.Id,
            $"+6013{Random.Shared.Next(10_000_000, 100_000_000)}",
            consentState: consent,
            consentRecordedAt: consent == ContactWhatsAppConsentState.Unknown ? null : ConversationTestTime,
            consentSource: consent == ContactWhatsAppConsentState.Unknown ? null : "Phase 10B test",
            consentEvidenceReference: consent == ContactWhatsAppConsentState.OptedIn ? $"evidence-{prefix}" : null,
            lastOptOutAt: consent == ContactWhatsAppConsentState.DoNotWhatsApp ? ConversationTestTime : null,
            lastOptOutReason: consent == ContactWhatsAppConsentState.DoNotWhatsApp ? "Phase 10B test opt-out" : null);
        var customerId = await CustomerIdForAsync(db, documentFixture.EngagementId);
        await AddContactLinkAsync(db, contact.Id, customerId);

        var conversations = WhatsAppService(db);
        var conversation = await conversations.CreateConversationAsync(
            group
                ? GroupConversationInput($"queue-conversation-{prefix}-{Guid.NewGuid():N}")
                : DirectConversationInput(address.Id, $"queue-conversation-{prefix}-{Guid.NewGuid():N}"));

        int? contactParticipantId = null;
        if (group && addContactParticipant)
        {
            var joined = await conversations.AddParticipantAsync(
                conversation.Id,
                ContactParticipantInput(
                    conversation.Version,
                    contact.Id,
                    address.Id,
                    address.NormalizedE164,
                    $"queue-contact-participant-{prefix}-{Guid.NewGuid():N}"));
            contactParticipantId = joined.Participants.Single(x =>
                x.ParticipantKind == WhatsAppParticipantKind.Contact && x.ContactId == contact.Id).Id;
            conversation = await conversations.GetConversationDetailsAsync(conversation.Id)
                ?? throw new InvalidOperationException("The test conversation was not found after adding a participant.");
        }

        if (group && addBusinessSender)
        {
            var joined = await conversations.AddParticipantAsync(
                conversation.Id,
                BusinessSenderInput(
                    conversation.Version,
                    $"queue-business-sender-{prefix}-{Guid.NewGuid():N}"));
            conversation = await conversations.GetConversationDetailsAsync(conversation.Id)
                ?? throw new InvalidOperationException("The test conversation was not found after adding a participant.");
            _ = joined;
        }

        int? scopeId = null;
        if (addScope)
        {
            var approved = await conversations.ApproveEngagementScopeAsync(
                conversation.Id,
                ApprovalInput(conversation.Version, documentFixture.EngagementId));
            scopeId = approved.EngagementScopes.Single(x => x.EngagementId == documentFixture.EngagementId).Id;
        }

        return new(
            contact.Id,
            address.Id,
            customerId,
            documentFixture.EngagementId,
            service.Id,
            conversation.Id,
            request.Id,
            requestItem.Id,
            contactParticipantId,
            scopeId);
    }

    [PostgresFact]
    public async Task Phase10BPreviewValidDirectIsReadOnlyAndReturnsExactProposedRequest()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "direct-preview");
        var service = QueueService(db, new EphemeralDataProtectionProvider());

        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));

        Assert.NotNull(preview.PreviewToken);
        Assert.NotEmpty(preview.PreviewToken);
        Assert.Equal(fixture.ContactId, preview.Contact.Id);
        Assert.Equal(fixture.ConversationId, preview.Conversation.Id);
        Assert.Equal(WhatsAppConversationStatus.Active, preview.Conversation.Status);
        Assert.Equal(1, preview.Conversation.AuthorizationVersion);
        Assert.Equal(WhatsAppDestinationKind.Direct, preview.Destination.Kind);
        Assert.Equal(preview.Destination.ProviderDestinationKey, preview.ProposedRequest.Destination.ProviderDestinationKey);
        Assert.Equal(64, preview.ParticipantSetHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", preview.ParticipantSetHash);
        Assert.Single(preview.Requests);
        Assert.Single(preview.Requests[0].UnresolvedItems);
        Assert.Equal(0, await db.DocumentRequestBatches.CountAsync());
        Assert.Equal(0, await db.WhatsAppOutboundMessages.CountAsync());
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == fixture.RequestId).Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsDirectContactMismatch()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "direct-contact-mismatch");
        var other = await AddContactAsync(db, "direct-contact-mismatch-other");
        await AddContactLinkAsync(db, other.Id, fixture.CustomerId);
        var service = QueueService(db, new EphemeralDataProtectionProvider());

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.PreviewAsync(QueuePreviewInput(fixture, contactId: other.Id)));

        Assert.Contains("different Contact", exception.Message);
        Assert.Equal(0, await db.WhatsAppOutboundMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsInactiveDirectAnchor()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "direct-inactive-anchor");
        await db.ContactWhatsAppAddresses.Where(x => x.Id == fixture.AddressId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, false));

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(db, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(fixture)));

        Assert.Contains("inactive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsUnknownOrDoNotWhatsAppDirectConsent()
    {
        await using var unknownDb = await Fresh();
        var unknown = await AddQueueFixtureAsync(unknownDb, "direct-unknown-consent", consent: ContactWhatsAppConsentState.Unknown);
        var unknownException = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(unknownDb, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(unknown)));
        Assert.Contains("consent", unknownException.Message, StringComparison.OrdinalIgnoreCase);

        await using var optedOutDb = await Fresh();
        var optedOut = await AddQueueFixtureAsync(optedOutDb, "direct-do-not", consent: ContactWhatsAppConsentState.DoNotWhatsApp);
        var optedOutException = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(optedOutDb, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(optedOut)));
        Assert.Contains("consent", optedOutException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BPreviewValidGroupRequiresExplicitContactAndCurrentScope()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "group-valid", group: true);
        var provider = new DeterministicFakeWhatsAppProvider(GroupTestCapabilities());
        var preview = await QueueService(db, new EphemeralDataProtectionProvider(), provider)
            .PreviewAsync(QueuePreviewInput(fixture));

        Assert.Equal(WhatsAppDestinationKind.Group, preview.Destination.Kind);
        Assert.NotNull(preview.ProposedRequest.Destination.ProviderConversationKey);
        Assert.Equal(2, preview.ActiveParticipants.Count);
        Assert.Contains(preview.ActiveParticipants, x =>
            x.ParticipantKind == WhatsAppParticipantKind.Contact && x.ContactId == fixture.ContactId);
        Assert.Equal(fixture.ScopeId, Assert.Single(preview.EngagementScopes).Id);
        Assert.Empty(provider.CapturedRequests);
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsGroupWithoutActiveParticipant()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "group-no-participant", group: true, addScope: false, addContactParticipant: false, addBusinessSender: false);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(db, new EphemeralDataProtectionProvider(), new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()))
                .PreviewAsync(QueuePreviewInput(fixture)));

        Assert.Contains("active participant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsGroupActiveUnknownExternal()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "group-unknown", group: true);
        db.WhatsAppConversationParticipants.Add(new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = fixture.ConversationId,
            ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
            ProviderParticipantKey = $"queue-unknown-{Guid.NewGuid():N}",
            DisplayNameSnapshot = "Unmapped group member",
            JoinedAt = ConversationTestTime
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(db, new EphemeralDataProtectionProvider(), new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()))
                .PreviewAsync(QueuePreviewInput(fixture)));

        Assert.Contains("UnknownExternal", exception.Message);
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsUnrelatedGroupContactEvenWhenCustomerLinkExists()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "group-unrelated-contact", group: true);
        var other = await AddContactAsync(db, "group-unrelated-contact-other");
        await AddContactLinkAsync(db, other.Id, fixture.CustomerId);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(db, new EphemeralDataProtectionProvider(), new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()))
                .PreviewAsync(QueuePreviewInput(fixture, contactId: other.Id)));

        Assert.Contains("explicitly represented", exception.Message);
    }

    [PostgresFact]
    public async Task Phase10BPreviewRejectsInactiveConversationAndNeedsAuthorizationReview()
    {
        await using var inactiveDb = await Fresh();
        var inactive = await AddQueueFixtureAsync(inactiveDb, "conversation-inactive");
        var conversationService = WhatsAppService(inactiveDb);
        var conversation = await inactiveDb.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == inactive.ConversationId);
        await conversationService.DeactivateConversationAsync(conversation.Id, ConversationLifecycleInput(conversation.Version, "Phase 10B inactive test"));
        var inactiveException = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(inactiveDb, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(inactive)));
        Assert.Contains("not active", inactiveException.Message, StringComparison.OrdinalIgnoreCase);

        await using var reviewDb = await Fresh();
        var review = await AddQueueFixtureAsync(reviewDb, "conversation-review", group: true);
        var reviewConversation = await reviewDb.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == review.ConversationId);
        await WhatsAppService(reviewDb).RecordUnknownMembershipChangeAsync(
            reviewConversation.Id,
            new WhatsAppConversationUnknownMembershipChangeInput(
                reviewConversation.Version,
                "Phase 10B review test",
                "phase10b-test",
                "BillingControl.WhatsApp"));
        var reviewException = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(reviewDb, new EphemeralDataProtectionProvider(), new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()))
                .PreviewAsync(QueuePreviewInput(review)));
        Assert.Contains("not active", reviewException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BPreviewRequiresCurrentActiveEngagementScope()
    {
        await using var missingDb = await Fresh();
        var missing = await AddQueueFixtureAsync(missingDb, "scope-missing", addScope: false);
        var missingException = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(missingDb, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(missing)));
        Assert.Contains("stale or no longer authorized", missingException.Message);

        await using var staleDb = await Fresh();
        var stale = await AddQueueFixtureAsync(staleDb, "scope-stale");
        await staleDb.WhatsAppConversations.Where(x => x.Id == stale.ConversationId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.AuthorizationVersion, 2));
        var staleException = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(staleDb, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(stale)));
        Assert.Contains("stale", staleException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BContactCustomerLinkAloneCannotAuthorizeOutboundDisclosure()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "link-not-auth", addScope: false);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            QueueService(db, new EphemeralDataProtectionProvider()).PreviewAsync(QueuePreviewInput(fixture)));

        Assert.Contains("authorized", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.WhatsAppOutboundMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsRequestThatStopsBeingReadyToSendAfterPreview()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "request-status-stale");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));
        await db.DocumentRequests.Where(x => x.Id == fixture.RequestId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Status, DocumentRequestStatus.Paused));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.QueueAsync(preview.PreviewToken));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.WhatsAppOutboundMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsLaterRequestRevisionAfterPreview()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "request-revision-stale");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));
        await db.DocumentRequests.Where(x => x.Id == fixture.RequestId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Status, DocumentRequestStatus.Superseded));
        var templateId = await db.DocumentRequests.Where(x => x.Id == fixture.RequestId)
            .Select(x => x.DocumentRequirementTemplateId).SingleAsync();
        var workItemId = await db.DocumentRequests.Where(x => x.Id == fixture.RequestId)
            .Select(x => x.WorkItemId).SingleAsync();
        var later = await AddDocumentRequestAsync(db, workItemId,
            templateId,
            revision: 2,
            status: DocumentRequestStatus.ReadyToSend);
        var templateItemId = await db.DocumentRequirementTemplateItems
            .Where(x => x.DocumentRequirementTemplateId == templateId).Select(x => x.Id).SingleAsync();
        await AddDocumentRequestItemAsync(db, later.Id, templateItemId, DocumentToken("queue-later-revision-item"));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.QueueAsync(preview.PreviewToken));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsRequestItemFactChangeAfterPreview()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "item-stale");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));
        await db.DocumentRequestItems.Where(x => x.Id == fixture.ItemId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Status, DocumentRequestItemStatus.Requested));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.QueueAsync(preview.PreviewToken));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsRequestRecordVersionChangeEvenWhenStatusIsRestored()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "request-version-stale");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));
        var requestVersion = await db.DocumentRequests.Where(x => x.Id == fixture.RequestId).Select(x => x.Version).SingleAsync();
        var requests = new DocumentRequestService(db, WhatsAppTestClock());
        await requests.TransitionAsync(fixture.RequestId, new(
            DocumentRequestStatus.Paused, requestVersion, "phase10b-test", "BillingControl.WhatsApp", "version stale"));
        var pausedVersion = await db.DocumentRequests.Where(x => x.Id == fixture.RequestId).Select(x => x.Version).SingleAsync();
        await requests.TransitionAsync(fixture.RequestId, new(
            DocumentRequestStatus.ReadyToSend, pausedVersion, "phase10b-test", "BillingControl.WhatsApp", "restore status"));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.QueueAsync(preview.PreviewToken));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsParticipantChangeAfterPreview()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "participant-stale", group: true);
        var keys = new EphemeralDataProtectionProvider();
        var service = QueueService(db, keys, new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()));
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));
        var conversation = await db.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == fixture.ConversationId);
        await WhatsAppService(db).AddParticipantAsync(
            fixture.ConversationId,
            BusinessSenderInput(conversation.Version, $"queue-late-sender-{Guid.NewGuid():N}"));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.QueueAsync(preview.PreviewToken));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsAuthorizationVersionChangeAfterPreview()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "authorization-version-stale");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));
        await db.WhatsAppConversations.Where(x => x.Id == fixture.ConversationId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.AuthorizationVersion, 2));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.QueueAsync(preview.PreviewToken));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsScopeRevocationAndReapprovalAfterPreview()
    {
        await using var revokedDb = await Fresh();
        var revoked = await AddQueueFixtureAsync(revokedDb, "scope-revoked");
        var revokedService = QueueService(revokedDb, new EphemeralDataProtectionProvider());
        var revokedPreview = await revokedService.PreviewAsync(QueuePreviewInput(revoked));
        var revokedScope = await revokedDb.WhatsAppConversationEngagementScopes.AsNoTracking().SingleAsync(x => x.Id == revoked.ScopeId);
        var revokedConversation = await revokedDb.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == revoked.ConversationId);
        await WhatsAppService(revokedDb).RevokeEngagementScopeAsync(
            revoked.ConversationId,
            revoked.ScopeId!.Value,
            new(revokedConversation.Version, revokedScope.Version, "scope revoked", "phase10b-test", "BillingControl.WhatsApp"));
        var revokedException = await Assert.ThrowsAsync<BusinessException>(() => revokedService.QueueAsync(revokedPreview.PreviewToken));
        Assert.Contains("stale", revokedException.Message, StringComparison.OrdinalIgnoreCase);

        await using var reapprovedDb = await Fresh();
        var reapproved = await AddQueueFixtureAsync(reapprovedDb, "scope-reapproved");
        var reapprovedService = QueueService(reapprovedDb, new EphemeralDataProtectionProvider());
        var reapprovedPreview = await reapprovedService.PreviewAsync(QueuePreviewInput(reapproved));
        var oldScope = await reapprovedDb.WhatsAppConversationEngagementScopes.AsNoTracking().SingleAsync(x => x.Id == reapproved.ScopeId);
        var oldConversation = await reapprovedDb.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == reapproved.ConversationId);
        await WhatsAppService(reapprovedDb).RevokeEngagementScopeAsync(
            reapproved.ConversationId,
            reapproved.ScopeId!.Value,
            new(oldConversation.Version, oldScope.Version, "scope revoke before reapproval", "phase10b-test", "BillingControl.WhatsApp"));
        var currentConversation = await reapprovedDb.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == reapproved.ConversationId);
        var currentScope = await reapprovedDb.WhatsAppConversationEngagementScopes.AsNoTracking().SingleAsync(x => x.Id == reapproved.ScopeId);
        await WhatsAppService(reapprovedDb).ApproveEngagementScopeAsync(
            reapproved.ConversationId,
            new(currentConversation.Version, reapproved.EngagementId, currentScope.Version, "scope reapproved", "phase10b-test", "BillingControl.WhatsApp"));
        var reapprovedException = await Assert.ThrowsAsync<BusinessException>(() => reapprovedService.QueueAsync(reapprovedPreview.PreviewToken));
        Assert.Contains("stale", reapprovedException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsConsentOrDestinationChangeAfterPreview()
    {
        await using var consentDb = await Fresh();
        var consentFixture = await AddQueueFixtureAsync(consentDb, "consent-stale");
        var consentService = QueueService(consentDb, new EphemeralDataProtectionProvider());
        var consentPreview = await consentService.PreviewAsync(QueuePreviewInput(consentFixture));
        await consentDb.ContactWhatsAppAddresses.Where(x => x.Id == consentFixture.AddressId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ConsentState, ContactWhatsAppConsentState.DoNotWhatsApp));
        var consentException = await Assert.ThrowsAsync<BusinessException>(() => consentService.QueueAsync(consentPreview.PreviewToken));
        Assert.Contains("consent", consentException.Message, StringComparison.OrdinalIgnoreCase);

        await using var destinationDb = await Fresh();
        var destinationFixture = await AddQueueFixtureAsync(destinationDb, "destination-stale");
        var destinationService = QueueService(destinationDb, new EphemeralDataProtectionProvider());
        var destinationPreview = await destinationService.PreviewAsync(QueuePreviewInput(destinationFixture));
        await destinationDb.ContactWhatsAppAddresses.Where(x => x.Id == destinationFixture.AddressId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.ProviderWaId, "provider-recipient-changed"));
        var destinationException = await Assert.ThrowsAsync<BusinessException>(() => destinationService.QueueAsync(destinationPreview.PreviewToken));
        Assert.Contains("stale", destinationException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BQueueRejectsChangedContentAndInvalidPreviewToken()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "content-token");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));

        var contentException = await Assert.ThrowsAsync<BusinessException>(() =>
            service.QueueAsync(new WhatsAppOutboundQueueInput(
                preview.PreviewToken,
                new WhatsAppTextContent("A different exact body."))));
        Assert.Contains("content differs", contentException.Message, StringComparison.OrdinalIgnoreCase);

        var tokenChars = preview.PreviewToken.ToCharArray();
        tokenChars[^1] = tokenChars[^1] == 'A' ? 'B' : 'A';
        var tokenException = await Assert.ThrowsAsync<BusinessException>(() =>
            service.QueueAsync(new WhatsAppOutboundQueueInput(tokenChars.AsSpan().ToString())));
        Assert.Contains("token", tokenException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Phase10BParticipantSetHashIsDeterministicRegardlessOfInputOrder()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "hash-order", group: true);
        var preview = await QueueService(db, new EphemeralDataProtectionProvider(), new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()))
            .PreviewAsync(QueuePreviewInput(fixture));

        var reversed = preview.ActiveParticipants.Reverse().ToArray();

        Assert.Equal(preview.ParticipantSetHash, WhatsAppOutboundQueueService.ComputeParticipantSetHash(reversed));
        Assert.Matches("^[0-9a-f]{64}$", WhatsAppOutboundQueueService.ComputeParticipantSetHash(reversed));
    }

    [PostgresFact]
    public async Task Phase10BQueueFreezesExactDirectSnapshotAndKeepsRequestReadyToSend()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-direct-snapshot");
        var keys = new EphemeralDataProtectionProvider();
        var service = QueueService(db, keys);
        var before = await CaptureQueueBoundaryAsync(db, fixture);
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture, new WhatsAppTextContent("Exact queued body.")));

        var queued = await service.QueueAsync(preview.PreviewToken);

        Assert.False(queued.AlreadyQueued);
        Assert.Equal(DocumentRequestBatchStatus.Queued, queued.BatchStatus);
        Assert.Equal(WhatsAppOutboundMessageState.Queued, queued.MessageState);
        var snapshot = await db.WhatsAppOutboundBatchSnapshots.AsNoTracking()
            .SingleAsync(x => x.DocumentRequestBatchId == queued.DocumentRequestBatchId);
        var message = await db.WhatsAppOutboundMessages.AsNoTracking().SingleAsync(x => x.Id == queued.WhatsAppOutboundMessageId);
        Assert.Equal(fixture.ContactId, snapshot.ContactId);
        Assert.Equal(fixture.ConversationId, snapshot.WhatsAppConversationId);
        Assert.Equal(preview.Conversation.AuthorizationVersion, snapshot.ConversationAuthorizationVersion);
        Assert.Equal(preview.ParticipantSetHash, snapshot.ParticipantSetHash);
        Assert.Equal(preview.Destination.ProviderDestinationKey, message.ProviderDestinationKey);
        Assert.Equal("Exact queued body.", message.TextBody);
        Assert.Equal(ModelOutboundContentKind.Text, message.ContentKind);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(0, await db.WhatsAppOutboundMessageAttempts.CountAsync());
        Assert.Equal(before.RequestStatus, await db.DocumentRequests.Where(x => x.Id == fixture.RequestId).Select(x => x.Status).SingleAsync());
        Assert.Equal(before.ItemStatus, await db.DocumentRequestItems.Where(x => x.Id == fixture.ItemId).Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10BQueueCapturesExactParticipantsScopesRequestsAndItems()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-all-snapshots", group: true);
        var keys = new EphemeralDataProtectionProvider();
        var service = QueueService(db, keys, new DeterministicFakeWhatsAppProvider(GroupTestCapabilities()));
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture, new WhatsAppTemplateContent("document_request", "en", new[] { "Customer A", "Bank statement" })));

        var queued = await service.QueueAsync(preview.PreviewToken);

        var batchSnapshot = await db.WhatsAppOutboundBatchSnapshots.AsNoTracking()
            .SingleAsync(x => x.DocumentRequestBatchId == queued.DocumentRequestBatchId);
        var requestSnapshots = await db.WhatsAppOutboundRequestSnapshots.AsNoTracking().Where(x => x.WhatsAppOutboundBatchSnapshotId == batchSnapshot.Id).ToListAsync();
        var itemSnapshots = await db.WhatsAppOutboundItemSnapshots.AsNoTracking().Where(x => x.WhatsAppOutboundBatchSnapshotId == batchSnapshot.Id).ToListAsync();
        var participantSnapshots = await db.WhatsAppOutboundParticipantSnapshots.AsNoTracking().Where(x => x.WhatsAppOutboundBatchSnapshotId == batchSnapshot.Id).ToListAsync();
        var scopeSnapshots = await db.WhatsAppOutboundEngagementScopeSnapshots.AsNoTracking().Where(x => x.WhatsAppOutboundBatchSnapshotId == batchSnapshot.Id).ToListAsync();

        Assert.Single(requestSnapshots);
        Assert.Single(itemSnapshots);
        Assert.Equal(preview.ActiveParticipants.Count, participantSnapshots.Count);
        Assert.Single(scopeSnapshots);
        Assert.Equal(fixture.RequestId, requestSnapshots[0].DocumentRequestId);
        Assert.Equal(preview.Requests[0].Version, requestSnapshots[0].RequestVersion);
        Assert.Equal(fixture.ItemId, itemSnapshots[0].DocumentRequestItemId);
        Assert.Equal(preview.Requests[0].UnresolvedItems[0].Status, itemSnapshots[0].ItemStatusSnapshot);
        Assert.Equal(fixture.ScopeId, scopeSnapshots[0].WhatsAppConversationEngagementScopeId);
        Assert.Equal(preview.ActiveParticipants.Select(x => x.Id).OrderBy(x => x), participantSnapshots.Select(x => x.WhatsAppConversationParticipantId).OrderBy(x => x));
    }

    [PostgresFact]
    public async Task Phase10BQueueCapturesTemplateContentAndExactRoutingFactsWithoutProviderSend()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-template", group: true);
        var provider = new DeterministicFakeWhatsAppProvider(GroupTestCapabilities());
        var keys = new EphemeralDataProtectionProvider();
        var service = QueueService(db, keys, provider);
        var content = new WhatsAppTemplateContent("document_request", "en-MY", new[] { "Alpha", "Bank statement" });
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture, content));

        var queued = await service.QueueAsync(preview.PreviewToken);

        var message = await db.WhatsAppOutboundMessages.AsNoTracking().SingleAsync(x => x.Id == queued.WhatsAppOutboundMessageId);
        Assert.Equal(ModelOutboundContentKind.Template, message.ContentKind);
        Assert.Equal("document_request", message.TemplateName);
        Assert.Equal("en-MY", message.TemplateLanguage);
        Assert.Equal("[\"Alpha\",\"Bank statement\"]", message.TemplateParametersSnapshot);
        Assert.Equal(preview.Conversation.ProviderName, message.ProviderName);
        Assert.Equal(preview.Conversation.BusinessEndpointKey, message.BusinessEndpointKey);
        Assert.Empty(provider.CapturedRequests);
    }

    [PostgresFact]
    public async Task Phase10BQueueIsIdempotentForTheSameProtectedPreview()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-idempotent");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));

        var first = await service.QueueAsync(preview.PreviewToken);
        var second = await service.QueueAsync(preview.PreviewToken);

        Assert.False(first.AlreadyQueued);
        Assert.True(second.AlreadyQueued);
        Assert.Equal(first.DocumentRequestBatchId, second.DocumentRequestBatchId);
        Assert.Equal(first.WhatsAppOutboundMessageId, second.WhatsAppOutboundMessageId);
        Assert.Equal(1, await db.DocumentRequestBatches.CountAsync());
        Assert.Equal(1, await db.WhatsAppOutboundBatchSnapshots.CountAsync());
        Assert.Equal(1, await db.WhatsAppOutboundMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10BConcurrentQueueAttemptsCreateOneBatchAndOneMessage()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-concurrent");
        var keys = new EphemeralDataProtectionProvider();
        var preview = await QueueService(db, keys).PreviewAsync(QueuePreviewInput(fixture));
        await using var secondDb = Db();
        var firstService = QueueService(db, keys);
        var secondService = QueueService(secondDb, keys);

        var outcomes = await Task.WhenAll(
            new[] { firstService, secondService }.Select(async queueService =>
            {
                try
                {
                    return (Result: await queueService.QueueAsync(preview.PreviewToken), Error: (Exception?)null);
                }
                catch (Exception exception)
                {
                    return (Result: (WhatsAppOutboundQueueResult?)null, Error: exception);
                }
            }));

        Assert.Contains(outcomes, x => x.Result is not null);
        Assert.DoesNotContain(outcomes, x => x.Error is not null && x.Error is not BusinessException);
        await using var verify = Db();
        Assert.Equal(1, await verify.DocumentRequestBatches.CountAsync());
        Assert.Equal(1, await verify.WhatsAppOutboundBatchSnapshots.CountAsync());
        Assert.Equal(1, await verify.WhatsAppOutboundMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10BQueueWritesReadyToQueuedHistoryAndExactlyOneQueuedMessage()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-history");
        var service = QueueService(db, new EphemeralDataProtectionProvider());
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));

        var queued = await service.QueueAsync(preview.PreviewToken);

        var batch = await db.DocumentRequestBatches.AsNoTracking().Include(x => x.StatusHistory)
            .SingleAsync(x => x.Id == queued.DocumentRequestBatchId);
        Assert.Equal(DocumentRequestBatchStatus.Queued, batch.Status);
        Assert.Equal(new[] { DocumentRequestBatchStatus.Ready, DocumentRequestBatchStatus.Queued },
            batch.StatusHistory.OrderBy(x => x.Id).Select(x => x.NewStatus));
        Assert.Equal(1, await db.WhatsAppOutboundMessages.CountAsync(x => x.State == WhatsAppOutboundMessageState.Queued));
        Assert.Equal(0, await db.WhatsAppOutboundMessageAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10BQueueDoesNotCallProviderSendOrMutateFinancialWorkflow()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "queue-boundary");
        var provider = new DeterministicFakeWhatsAppProvider(new WhatsAppProviderCapabilities());
        var keys = new EphemeralDataProtectionProvider();
        var service = QueueService(db, keys, provider);
        var before = await CaptureQueueBoundaryAsync(db, fixture);
        var preview = await service.PreviewAsync(QueuePreviewInput(fixture));

        _ = await service.QueueAsync(preview.PreviewToken);

        var after = await CaptureQueueBoundaryAsync(db, fixture);
        Assert.Empty(provider.CapturedRequests);
        Assert.Equal(before.BillingRecords, after.BillingRecords);
        Assert.Equal(before.WorkItems, after.WorkItems);
        Assert.Equal(before.Invoices, after.Invoices);
        Assert.Equal(before.Receipts, after.Receipts);
        Assert.Equal(before.Assignments, after.Assignments);
        Assert.Equal(DocumentRequestStatus.ReadyToSend, after.RequestStatus);
        Assert.Equal(DocumentRequestItemStatus.Missing, after.ItemStatus);
        Assert.Equal(0, await db.WhatsAppOutboundMessageAttempts.CountAsync());
    }

    private static async Task<QueueBoundary> CaptureQueueBoundaryAsync(AppDbContext db, QueueFixture fixture)
    {
        var request = await db.DocumentRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.RequestId);
        var item = await db.DocumentRequestItems.AsNoTracking().SingleAsync(x => x.Id == fixture.ItemId);
        return new(
            await db.BillingRecords.CountAsync(),
            await db.WorkItems.CountAsync(),
            await db.Invoices.CountAsync(),
            await db.CustomerReceipts.CountAsync(),
            await db.WorkerAssignments.CountAsync(),
            request.Status,
            item.Status);
    }

    private sealed record QueueBoundary(
        int BillingRecords,
        int WorkItems,
        int Invoices,
        int Receipts,
        int Assignments,
        DocumentRequestStatus RequestStatus,
        DocumentRequestItemStatus ItemStatus);
}
