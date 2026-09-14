using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using BillingControl.Services.WhatsApp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private sealed record QueuedSendFixture(
        QueueFixture Queue,
        WhatsAppOutboundMessage Message,
        DeterministicFakeWhatsAppProvider Provider);

    private static WhatsAppOutboundSendService SendService(
        AppDbContext db,
        IWhatsAppProvider provider,
        BusinessClock? clock = null) =>
        new(
            db,
            QueueService(db, new EphemeralDataProtectionProvider(), provider),
            clock ?? WhatsAppTestClock(),
            provider);

    private static async Task<QueuedSendFixture> AddQueuedSendFixtureAsync(
        AppDbContext db,
        string prefix,
        bool group = false,
        WhatsAppOutboundContent? content = null,
        DeterministicFakeWhatsAppProvider? provider = null)
    {
        var fixture = await AddQueueFixtureAsync(db, prefix, group: group);
        provider ??= new DeterministicFakeWhatsAppProvider(
            group ? GroupTestCapabilities() : new WhatsAppProviderCapabilities());
        var queue = QueueService(db, new EphemeralDataProtectionProvider(), provider);
        var preview = await queue.PreviewAsync(QueuePreviewInput(fixture, content));
        var queued = await queue.QueueAsync(preview.PreviewToken);
        db.ChangeTracker.Clear();
        var message = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == queued.WhatsAppOutboundMessageId);
        return new(fixture, message, provider);
    }

    private static WhatsAppOutboundSendInput SendInput(
        QueuedSendFixture fixture,
        long? expectedVersion = null,
        string actor = "phase10c-sender",
        string source = "BillingControl.WhatsApp.Manual") =>
        new(
            fixture.Message.Id,
            expectedVersion ?? fixture.Message.Version,
            actor,
            source);

    private static async Task AssertInvalidatedAsync(
        AppDbContext db,
        QueuedSendFixture fixture)
    {
        db.ChangeTracker.Clear();
        var message = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        var batch = await db.DocumentRequestBatches.AsNoTracking()
            .Include(x => x.StatusHistory)
            .SingleAsync(x => x.Id == fixture.Message.DocumentRequestBatchId);
        Assert.Equal(WhatsAppOutboundMessageState.Cancelled, message.State);
        Assert.Equal(DocumentRequestBatchStatus.Invalidated, batch.Status);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(0, await db.WhatsAppOutboundMessageAttempts.CountAsync(
            x => x.WhatsAppOutboundMessageId == fixture.Message.Id));
        Assert.Contains(batch.StatusHistory, x =>
            x.Action == "SendRevalidationFailed" &&
            x.NewStatus == DocumentRequestBatchStatus.Invalidated);
    }

    private static async Task SetRequestStatusAsync(
        AppDbContext db,
        int requestId,
        DocumentRequestStatus status)
    {
        var request = await db.DocumentRequests.SingleAsync(x => x.Id == requestId);
        request.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<(int RequestId, int ItemId)> AddUnrelatedCurrentRequestAsync(
        AppDbContext db,
        QueueFixture fixture,
        string prefix)
    {
        var source = await db.DocumentRequests.AsNoTracking()
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .SingleAsync(x => x.Id == fixture.RequestId);
        var engagement = await db.Engagements.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Service)
            .SingleAsync(x => x.Id == source.WorkItem.BillingRecord.EngagementId);
        var billing = new BillingRecord
        {
            EngagementId = engagement.Id,
            PeriodStart = new DateOnly(2026, 2, 1),
            PeriodEnd = new DateOnly(2026, 2, 28),
            Status = BillingStatus.Upcoming,
            RevenueShareBaseAmount = 1000m,
            Amount = 1000m,
            CustomerName = engagement.Customer.Name,
            ServiceName = engagement.Service.Name
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();
        var workItem = new WorkItem { BillingRecordId = billing.Id, Status = WorkStatus.Upcoming };
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync();
        var request = await AddDocumentRequestAsync(
            db,
            workItem.Id,
            source.DocumentRequirementTemplateId,
            status: DocumentRequestStatus.ReadyToSend);
        var templateItemId = await db.DocumentRequestItems.AsNoTracking()
            .Where(x => x.DocumentRequestId == fixture.RequestId)
            .Select(x => x.DocumentRequirementTemplateItemId)
            .SingleAsync();
        var item = await AddDocumentRequestItemAsync(
            db,
            request.Id,
            templateItemId!.Value,
            $"unrelated-{prefix}");
        return (request.Id, item.Id);
    }

    [PostgresFact]
    public async Task Phase10CValidDirectAcceptedSendActivatesCurrentRequestAndItem()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(
            db,
            "send-direct-accepted",
            content: new WhatsAppTextContent("Immutable direct body."));

        var result = await SendService(db, fixture.Provider).SendAsync(SendInput(fixture));

        Assert.Equal(WhatsAppSendDisposition.Accepted, result.Disposition);
        Assert.Equal(WhatsAppOutboundMessageState.Accepted, result.MessageState);
        Assert.Equal(DocumentRequestBatchStatus.Sent, result.BatchStatus);
        Assert.False(result.AlreadyAccepted);
        Assert.Single(fixture.Provider.CapturedRequests);
        Assert.Equal("Immutable direct body.",
            Assert.IsType<WhatsAppTextContent>(fixture.Provider.CapturedRequests[0].Content).Text);

        Assert.Equal(DocumentRequestStatus.Requested,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestItemStatus.Requested,
            await db.DocumentRequestItems.Where(x => x.Id == fixture.Queue.ItemId)
                .Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CValidGroupAcceptedSendUsesFrozenGroupRoutingAndTemplate()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(
            db,
            "send-group-accepted",
            group: true,
            content: new WhatsAppTemplateContent(
                "document_request",
                "en-MY",
                ["Customer A", "Bank statement"]));

        var result = await SendService(db, fixture.Provider).SendAsync(SendInput(fixture));
        var request = Assert.Single(fixture.Provider.CapturedRequests);
        var content = Assert.IsType<WhatsAppTemplateContent>(request.Content);

        Assert.Equal(WhatsAppSendDisposition.Accepted, result.Disposition);
        Assert.Equal(WhatsAppDestinationKind.Group, request.Destination.Kind);
        Assert.Equal(fixture.Message.ProviderDestinationKey, request.Destination.ProviderDestinationKey);
        Assert.Equal(fixture.Message.LogicalMessageKey, request.LogicalMessageKey);
        Assert.Equal(fixture.Message.CorrelationId, request.CorrelationId);
        Assert.Equal(fixture.Message.ProviderName, request.AccountBinding.ProviderName);
        Assert.Equal(fixture.Message.BusinessEndpointKey, request.AccountBinding.BusinessEndpointKey);
        Assert.Equal("document_request", content.TemplateName);
        Assert.Equal("en-MY", content.Language);
        Assert.Equal(["Customer A", "Bank statement"], content.Parameters);
    }

    [PostgresFact]
    public async Task Phase10CManualInputCannotReplaceFrozenQueuedContentOrRouting()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(
            db,
            "send-immutable-input",
            content: new WhatsAppTextContent("Server-owned queued content."));

        // The send input contains only identity/version/audit fields. The
        // provider request is reconstructed from the durable queued message.
        _ = await SendService(db, fixture.Provider).SendAsync(new(
            fixture.Message.Id,
            fixture.Message.Version,
            "caller-cannot-replace-body",
            "BillingControl.WhatsApp.Manual"));

        var captured = Assert.Single(fixture.Provider.CapturedRequests);
        Assert.Equal("Server-owned queued content.",
            Assert.IsType<WhatsAppTextContent>(captured.Content).Text);
        Assert.Equal(fixture.Message.ProviderDestinationKey, captured.Destination.ProviderDestinationKey);
        Assert.Equal(fixture.Message.ProviderName, captured.AccountBinding.ProviderName);
    }

    [PostgresFact]
    public async Task Phase10CAcceptedResultWritesAttemptMessageBatchAndDocumentHistories()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-accepted-history");
        var result = await SendService(db, fixture.Provider).SendAsync(
            SendInput(fixture, actor: "accepted-auditor", source: "Phase10C.Tests"));

        var attempt = await db.WhatsAppOutboundMessageAttempts.AsNoTracking()
            .SingleAsync(x => x.Id == result.WhatsAppOutboundMessageAttemptId);
        var message = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        var batch = await db.DocumentRequestBatches.AsNoTracking()
            .Include(x => x.StatusHistory)
            .SingleAsync(x => x.Id == fixture.Message.DocumentRequestBatchId);
        var request = await db.DocumentRequests.AsNoTracking()
            .Include(x => x.StatusHistory)
            .SingleAsync(x => x.Id == fixture.Queue.RequestId);
        var item = await db.DocumentRequestItems.AsNoTracking()
            .Include(x => x.StatusHistory)
            .SingleAsync(x => x.Id == fixture.Queue.ItemId);

        Assert.Equal("Accepted", attempt.Disposition);
        Assert.Equal("accepted-auditor", attempt.Actor);
        Assert.Equal("Phase10C.Tests", attempt.Source);
        Assert.NotNull(attempt.CompletedAt);
        Assert.Single(fixture.Provider.CapturedRequests);
        Assert.Equal(message.ProviderMessageId, attempt.ProviderMessageId);
        Assert.Equal(WhatsAppOutboundMessageState.Accepted, message.State);
        Assert.Equal(DocumentRequestBatchStatus.Sent, batch.Status);
        Assert.Contains(batch.StatusHistory, x =>
            x.Action == "WhatsAppProviderAccepted" &&
            x.NewStatus == DocumentRequestBatchStatus.Sent &&
            x.Actor == "accepted-auditor");
        Assert.Equal(DocumentRequestStatus.Requested, request.Status);
        Assert.Contains(request.StatusHistory, x =>
            x.Action == "WhatsAppProviderAccepted" &&
            x.NewStatus == DocumentRequestStatus.Requested &&
            x.Actor == "accepted-auditor");
        Assert.Equal(DocumentRequestItemStatus.Requested, item.Status);
        Assert.Contains(item.StatusHistory, x =>
            x.Action == "WhatsAppProviderAccepted" &&
            x.NewStatus == DocumentRequestItemStatus.Requested &&
            x.Actor == "accepted-auditor");
    }

    [PostgresFact]
    public async Task Phase10CInactiveContactBeforeSendInvalidatesWithoutProviderAttempt()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-inactive-contact");
        var contact = await db.Contacts.SingleAsync(x => x.Id == fixture.Queue.ContactId);
        contact.IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CDeactivatedConversationBeforeSendInvalidatesWithoutProviderAttempt()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-inactive-conversation");
        var conversation = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        await WhatsAppService(db).DeactivateConversationAsync(
            conversation!.Id,
            ConversationLifecycleInput(conversation.Version, "Conversation closed before manual send"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CNeedsAuthorizationReviewBlocksGroupSend()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-review-required", group: true);
        var conversation = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        await WhatsAppService(db).AddParticipantAsync(
            conversation!.Id,
            BusinessSenderInput(conversation.Version, "phase10c-review-member"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CChangedAuthorizationVersionAfterReapprovalStillBlocksFrozenMessage()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-auth-version", group: true);
        var conversations = WhatsAppService(db);
        var conversation = await conversations.GetConversationDetailsAsync(fixture.Queue.ConversationId);
        var changed = await conversations.AddParticipantAsync(
            conversation!.Id,
            BusinessSenderInput(conversation.Version, "phase10c-auth-version-member"));
        var scope = changed.EngagementScopes.Single();
        var reapproved = await conversations.ApproveEngagementScopeAsync(
            changed.Id,
            ApprovalInput(changed.Version, fixture.Queue.EngagementId, scope.Version,
                "Reapprove after the queued snapshot was created"));
        Assert.Equal(WhatsAppConversationStatus.Active, reapproved.Status);
        Assert.True(reapproved.AuthorizationVersion > 1);

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CChangedParticipantSetBlocksBeforeProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-participant-changed", group: true);
        var conversation = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        await WhatsAppService(db).AddParticipantAsync(
            conversation!.Id,
            BusinessSenderInput(conversation.Version, "phase10c-participant-change"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CActiveUnknownExternalBlocksGroupSend()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-unknown-external", group: true);
        var conversation = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        await WhatsAppService(db).AddParticipantAsync(
            conversation!.Id,
            UnknownExternalInput(conversation.Version, "phase10c-unknown-external"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CRevokedScopeBlocksWithoutChangingRequestState()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-scope-revoked");
        var conversation = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        var scope = conversation!.EngagementScopes.Single();
        await WhatsAppService(db).RevokeEngagementScopeAsync(
            conversation.Id,
            scope.Id,
            new(conversation.Version, scope.Version, "Scope revoked before send",
                "phase10c-scope-reviewer", "BillingControl.WhatsApp"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CStaleScopeAfterParticipantChangeBlocksBeforeProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-scope-stale", group: true);
        var conversation = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        await WhatsAppService(db).AddParticipantAsync(
            conversation!.Id,
            BusinessSenderInput(conversation.Version, "phase10c-stale-scope-member"));

        var current = await WhatsAppService(db).GetConversationDetailsAsync(fixture.Queue.ConversationId);
        Assert.Equal(WhatsAppScopeAuthorizationState.Stale,
            current!.EngagementScopes.Single().AuthorizationState);
        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CDirectAnchorInactiveBlocksBeforeProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-direct-anchor-inactive");
        var address = await db.ContactWhatsAppAddresses.SingleAsync(x => x.Id == fixture.Queue.AddressId);
        await new ContactService(db, ContactTestClock()).SetAddressActiveAsync(
            address.Id,
            new(address.Version, false, false, "Retire direct anchor before send",
                "phase10c-contact-reviewer", "BillingControl.Contacts"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CDoNotWhatsAppConsentBlocksBeforeProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-do-not-whatsapp");
        var address = await db.ContactWhatsAppAddresses.SingleAsync(x => x.Id == fixture.Queue.AddressId);
        await new ContactService(db, ContactTestClock()).RecordConsentAsync(
            address.Id,
            new(address.Version, ContactWhatsAppConsentState.DoNotWhatsApp,
                "phase10c-contact", "withdrawal-evidence", "Consent withdrawn before send",
                "phase10c-contact-reviewer", "BillingControl.Contacts"));

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CDestinationMappingChangeBlocksFrozenDirectMessage()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-destination-changed");
        var address = await db.ContactWhatsAppAddresses.SingleAsync(x => x.Id == fixture.Queue.AddressId);
        address.ProviderWaId = "provider-destination-changed-after-queue";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CProviderCapabilityChangeBlocksFrozenSendBeforeProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-capability-changed");
        fixture.Provider.Capabilities = new WhatsAppProviderCapabilities(
            supportsDirectMessaging: false,
            supportsText: true);

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CLaterRequestRevisionInvalidatesQueuedMessage()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-later-revision");
        var original = await db.DocumentRequests.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Queue.RequestId);
        var templateItemId = await db.DocumentRequestItems.AsNoTracking()
            .Where(x => x.DocumentRequestId == original.Id)
            .Select(x => x.DocumentRequirementTemplateItemId)
            .SingleAsync();
        await SetRequestStatusAsync(db, original.Id, DocumentRequestStatus.Superseded);
        var later = await AddDocumentRequestAsync(
            db,
            original.WorkItemId,
            original.DocumentRequirementTemplateId,
            revision: original.Revision + 1,
            status: DocumentRequestStatus.Draft,
            supersedesRequestId: original.Id);
        await AddDocumentRequestItemAsync(db, later.Id, templateItemId!.Value, "later-revision-item");

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CRequestNoLongerReadyToSendInvalidatesWithoutActivation()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-request-paused");
        await SetRequestStatusAsync(db, fixture.Queue.RequestId, DocumentRequestStatus.Paused);

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
        Assert.Equal(DocumentRequestStatus.Paused,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CItemStatusChangeInvalidatesWithoutProviderAttempt()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-item-stale");
        var item = await db.DocumentRequestItems.SingleAsync(x => x.Id == fixture.Queue.ItemId);
        item.Status = DocumentRequestItemStatus.NotRequired;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
    }

    [PostgresFact]
    public async Task Phase10CContactCustomerLinkIsEvidenceOnlyAndCannotAuthorizeSend()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-link-evidence-only");
        var link = await db.ContactCustomerLinks.SingleAsync(x =>
            x.ContactId == fixture.Queue.ContactId && x.CustomerId == fixture.Queue.CustomerId);
        link.IsActive = false;
        link.EffectiveTo = new DateOnly(2026, 9, 13);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        await AssertInvalidatedAsync(db, fixture);
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CDefinitelyRejectedPersistsErrorRetryFactsAndDoesNotActivate()
    {
        await using var db = await Fresh();
        var provider = new DeterministicFakeWhatsAppProvider();
        provider.EnqueueBehavior(FakeWhatsAppSendBehavior.DefinitelyRejected(
            "RateLimited", "429", TimeSpan.FromMinutes(5),
            new DateTimeOffset(2026, 9, 13, 4, 1, 0, TimeSpan.Zero)));
        var fixture = await AddQueuedSendFixtureAsync(
            db,
            "send-rejected",
            provider: provider);

        var result = await SendService(db, provider).SendAsync(SendInput(fixture));
        var message = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        var attempt = await db.WhatsAppOutboundMessageAttempts.AsNoTracking()
            .SingleAsync(x => x.Id == result.WhatsAppOutboundMessageAttemptId);

        Assert.Equal(WhatsAppSendDisposition.DefinitelyRejected, result.Disposition);
        Assert.Equal(WhatsAppOutboundMessageState.DefinitelyRejected, message.State);
        Assert.Equal("RateLimited", message.LastErrorCategory);
        Assert.Equal("429", message.LastErrorCode);
        Assert.NotNull(message.NextAttemptAt);
        Assert.Equal(message.NextAttemptAt, message.ProviderRetryAfterUntil);
        Assert.Equal("DefinitelyRejected", attempt.Disposition);
        Assert.Equal(DocumentRequestBatchStatus.Queued,
            await db.DocumentRequestBatches.Where(x => x.Id == fixture.Message.DocumentRequestBatchId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestItemStatus.Missing,
            await db.DocumentRequestItems.Where(x => x.Id == fixture.Queue.ItemId)
                .Select(x => x.Status).SingleAsync());

        var current = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, provider).SendAsync(SendInput(fixture, current.Version)));
        Assert.Single(provider.CapturedRequests);
    }

    [PostgresFact]
    public async Task Phase10CExplicitRetryAfterDefiniteRejectionClaimsANewAttempt()
    {
        await using var db = await Fresh();
        var provider = new DeterministicFakeWhatsAppProvider();
        provider.EnqueueBehavior(FakeWhatsAppSendBehavior.DefinitelyRejected(
            "InvalidRequest", "bad-request"));
        var fixture = await AddQueuedSendFixtureAsync(db, "send-retry-after-rejection", provider: provider);

        var first = await SendService(db, provider).SendAsync(SendInput(fixture));
        db.ChangeTracker.Clear();
        var current = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        var second = await SendService(db, provider).SendAsync(
            SendInput(fixture, current.Version));

        Assert.Equal(WhatsAppSendDisposition.DefinitelyRejected, first.Disposition);
        Assert.Equal(WhatsAppSendDisposition.Accepted, second.Disposition);
        Assert.Equal(1, first.AttemptNumber);
        Assert.Equal(2, second.AttemptNumber);
        Assert.Equal(2, provider.CapturedRequests.Count);
        Assert.Equal(2, await db.WhatsAppOutboundMessageAttempts.CountAsync(
            x => x.WhatsAppOutboundMessageId == fixture.Message.Id));
    }

    [PostgresFact]
    public async Task Phase10CAmbiguousResultBlocksOrdinaryResendAndLeavesDocumentsUnchanged()
    {
        await using var db = await Fresh();
        var provider = new DeterministicFakeWhatsAppProvider();
        provider.EnqueueBehavior(FakeWhatsAppSendBehavior.Ambiguous("Network", "timeout"));
        var fixture = await AddQueuedSendFixtureAsync(db, "send-ambiguous", provider: provider);

        var result = await SendService(db, provider).SendAsync(SendInput(fixture));
        db.ChangeTracker.Clear();
        var current = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, provider).SendAsync(SendInput(fixture, current.Version)));

        Assert.Equal(WhatsAppSendDisposition.Ambiguous, result.Disposition);
        Assert.Equal(WhatsAppOutboundMessageState.Ambiguous, current.State);
        Assert.Single(provider.CapturedRequests);
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == fixture.Queue.RequestId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestItemStatus.Missing,
            await db.DocumentRequestItems.Where(x => x.Id == fixture.Queue.ItemId)
                .Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CProviderExceptionPersistsConservativeAmbiguousOutcomeAndBlocksResend()
    {
        await using var db = await Fresh();
        var provider = new SendThrowingWhatsAppProvider();
        var queueFixture = await AddQueueFixtureAsync(db, "send-provider-exception");
        var queue = QueueService(db, new EphemeralDataProtectionProvider(), provider);
        var preview = await queue.PreviewAsync(QueuePreviewInput(queueFixture));
        var queued = await queue.QueueAsync(preview.PreviewToken);
        db.ChangeTracker.Clear();
        var message = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == queued.WhatsAppOutboundMessageId);
        var result = await SendService(db, provider).SendAsync(new(
            message.Id, message.Version, "phase10c-sender", "BillingControl.WhatsApp.Manual"));
        db.ChangeTracker.Clear();
        var current = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == message.Id);
        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, provider).SendAsync(new(
                message.Id, current.Version, "phase10c-sender", "BillingControl.WhatsApp.Manual")));

        Assert.Equal(WhatsAppSendDisposition.Ambiguous, result.Disposition);
        Assert.Equal(WhatsAppOutboundMessageState.Ambiguous, current.State);
        Assert.Equal(1, provider.SendCallCount);
        Assert.Equal("ProviderException", current.LastErrorCategory);
    }

    [PostgresFact]
    public async Task Phase10CExistingOpenAttemptBlocksProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-open-attempt");
        var message = await db.WhatsAppOutboundMessages.SingleAsync(x => x.Id == fixture.Message.Id);
        message.AttemptCount = 1;
        db.WhatsAppOutboundMessageAttempts.Add(new()
        {
            WhatsAppOutboundMessageId = message.Id,
            AttemptNumber = 1,
            StartedAt = ConversationTestTime,
            CorrelationId = message.CorrelationId,
            Actor = "prior-actor",
            Source = "prior-source"
        });
        await db.SaveChangesAsync();
        var expectedVersion = message.Version;

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture, expectedVersion)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        Assert.Equal(1, await db.WhatsAppOutboundMessageAttempts.CountAsync(
            x => x.WhatsAppOutboundMessageId == fixture.Message.Id));
    }

    [PostgresFact]
    public async Task Phase10CStaleExpectedMessageVersionFailsBeforeProviderCall()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-stale-version");
        var message = await db.WhatsAppOutboundMessages.SingleAsync(x => x.Id == fixture.Message.Id);
        message.LastErrorCode = "unrelated-version-bump";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            SendService(db, fixture.Provider).SendAsync(SendInput(fixture)));

        Assert.Empty(fixture.Provider.CapturedRequests);
        var current = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        Assert.Equal(WhatsAppOutboundMessageState.Queued, current.State);
        Assert.Equal(0, await db.WhatsAppOutboundMessageAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task Phase10CRepeatedAcceptedSendIsDeterministicAndDoesNotCreateAttempt()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-repeat-accepted");
        var service = SendService(db, fixture.Provider);
        var first = await service.SendAsync(SendInput(fixture));
        db.ChangeTracker.Clear();
        var current = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Message.Id);
        var second = await service.SendAsync(SendInput(fixture, current.Version));

        Assert.Equal(WhatsAppSendDisposition.Accepted, first.Disposition);
        Assert.Equal(WhatsAppSendDisposition.Accepted, second.Disposition);
        Assert.True(second.AlreadyAccepted);
        Assert.Equal(first.WhatsAppOutboundMessageAttemptId, second.WhatsAppOutboundMessageAttemptId);
        Assert.Equal(first.ProviderMessageId, second.ProviderMessageId);
        Assert.Single(fixture.Provider.CapturedRequests);
        Assert.Equal(1, await db.WhatsAppOutboundMessageAttempts.CountAsync(
            x => x.WhatsAppOutboundMessageId == fixture.Message.Id));
    }

    [PostgresFact]
    public async Task Phase10CConcurrentSendsCallProviderAtMostOnceAndClaimOneAttempt()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-concurrent");
        await using var secondDb = Db();
        var firstService = SendService(db, fixture.Provider);
        var secondService = SendService(secondDb, fixture.Provider);

        async Task<(WhatsAppOutboundSendResult? Result, Exception? Error)> Attempt(
            WhatsAppOutboundSendService service)
        {
            try
            {
                return (await service.SendAsync(SendInput(fixture)), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var outcomes = await Task.WhenAll(Attempt(firstService), Attempt(secondService));

        Assert.Contains(outcomes, x => x.Result is { AlreadyAccepted: false });
        Assert.All(outcomes, x =>
        {
            if (x.Result is null)
                Assert.IsType<BusinessException>(x.Error);
        });
        Assert.Single(fixture.Provider.CapturedRequests);
        await using var verify = Db();
        Assert.Equal(1, await verify.WhatsAppOutboundMessageAttempts.CountAsync());
        Assert.Equal(WhatsAppOutboundMessageState.Accepted,
            await verify.WhatsAppOutboundMessages.Select(x => x.State).SingleAsync());
    }

    [PostgresFact]
    public async Task Phase10CAcceptedSendLeavesUnrelatedFinancialAndWorkflowRowsUntouched()
    {
        await using var db = await Fresh();
        var fixture = await AddQueuedSendFixtureAsync(db, "send-boundary");
        var before = await CaptureQueueBoundaryAsync(db, fixture.Queue);

        _ = await SendService(db, fixture.Provider).SendAsync(SendInput(fixture));

        var after = await CaptureQueueBoundaryAsync(db, fixture.Queue);
        Assert.Equal(before.BillingRecords, after.BillingRecords);
        Assert.Equal(before.WorkItems, after.WorkItems);
        Assert.Equal(before.Invoices, after.Invoices);
        Assert.Equal(before.Receipts, after.Receipts);
        Assert.Equal(before.Assignments, after.Assignments);
        Assert.Equal(DocumentRequestStatus.Requested, after.RequestStatus);
        Assert.Equal(DocumentRequestItemStatus.Requested, after.ItemStatus);
    }

    [PostgresFact]
    public async Task Phase10CAcceptedSendActivatesOnlyRequestsAndItemsInTheImmutableSnapshot()
    {
        await using var db = await Fresh();
        var fixture = await AddQueueFixtureAsync(db, "send-unrelated-members");
        var unrelated = await AddUnrelatedCurrentRequestAsync(db, fixture, "outside-snapshot");
        var provider = new DeterministicFakeWhatsAppProvider();
        var queue = QueueService(db, new EphemeralDataProtectionProvider(), provider);
        var preview = await queue.PreviewAsync(QueuePreviewInput(fixture));
        var queued = await queue.QueueAsync(preview.PreviewToken);
        db.ChangeTracker.Clear();
        var message = await db.WhatsAppOutboundMessages.AsNoTracking()
            .SingleAsync(x => x.Id == queued.WhatsAppOutboundMessageId);

        _ = await SendService(db, provider).SendAsync(new(
            message.Id, message.Version, "phase10c-sender", "BillingControl.WhatsApp.Manual"));

        Assert.Equal(DocumentRequestStatus.Requested,
            await db.DocumentRequests.Where(x => x.Id == fixture.RequestId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestItemStatus.Requested,
            await db.DocumentRequestItems.Where(x => x.Id == fixture.ItemId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            await db.DocumentRequests.Where(x => x.Id == unrelated.RequestId)
                .Select(x => x.Status).SingleAsync());
        Assert.Equal(DocumentRequestItemStatus.Missing,
            await db.DocumentRequestItems.Where(x => x.Id == unrelated.ItemId)
                .Select(x => x.Status).SingleAsync());
    }

    private sealed class SendThrowingWhatsAppProvider : IWhatsAppProvider
    {
        public int SendCallCount { get; private set; }

        public Task<WhatsAppProviderCapabilities> GetCapabilitiesAsync(
            WhatsAppProviderAccountBinding accountBinding,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new WhatsAppProviderCapabilities());
        }

        public Task<WhatsAppSendResult> SendAsync(
            WhatsAppOutboundRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCallCount++;
            throw new TimeoutException("deterministic Phase 10C provider timeout");
        }
    }
}
