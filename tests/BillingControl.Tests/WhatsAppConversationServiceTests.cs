using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static BusinessClock WhatsAppTestClock() => new(
        new FixedProgressTime
        {
            Now = new DateTimeOffset(2026, 9, 13, 4, 0, 0, TimeSpan.Zero)
        },
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BusinessTimeZone"] = "UTC" })
            .Build());

    private static WhatsAppConversationService WhatsAppService(AppDbContext db) =>
        new(db, WhatsAppTestClock());

    private static WhatsAppConversationCreateInput GroupConversationInput(string key) =>
        new(WhatsAppConversationKind.Group, "Meta", "phase9b-endpoint", "phase9b-account", key,
            null, "phase9b-staff", "BillingControl.WhatsApp", "Phase 9B test creation");

    private static WhatsAppConversationCreateInput DirectConversationInput(int addressId, string key) =>
        new(WhatsAppConversationKind.Direct, "Meta", "phase9b-endpoint", "phase9b-account", key,
            addressId, "phase9b-staff", "BillingControl.WhatsApp", "Phase 9B test creation");

    private static WhatsAppConversationParticipantCreateInput ContactParticipantInput(
        long expectedConversationVersion,
        int contactId,
        int? addressId,
        string? phone,
        string key) =>
        new(expectedConversationVersion, WhatsAppParticipantKind.Contact, contactId,
            addressId, null, null, null, key, phone, "Phase 9B contact", "Phase 9B participant", "Phase 9B actor", "BillingControl.WhatsApp");

    private static WhatsAppConversationParticipantCreateInput BusinessSenderInput(
        long expectedConversationVersion,
        string key) =>
        new(expectedConversationVersion, WhatsAppParticipantKind.BusinessSender,
            ProviderParticipantKey: key, DisplayNameSnapshot: "Phase 9B sender",
            Reason: "Phase 9B participant", Actor: "Phase 9B participant", Source: "BillingControl.WhatsApp");

    private static WhatsAppConversationParticipantCreateInput UnknownExternalInput(
        long expectedConversationVersion,
        string key) =>
        new(expectedConversationVersion, WhatsAppParticipantKind.UnknownExternal,
            ProviderParticipantKey: key, DisplayNameSnapshot: "Unmapped member",
            Reason: "Phase 9B unknown member", Actor: "Phase 9B participant", Source: "BillingControl.WhatsApp");

    private static WhatsAppConversationEngagementScopeApprovalInput ApprovalInput(
        long expectedConversationVersion,
        int engagementId,
        long? expectedScopeVersion = null,
        string reason = "Phase 9B explicit staff approval") =>
        new(expectedConversationVersion, engagementId, expectedScopeVersion, reason,
            "Phase 9B approver", "BillingControl.WhatsApp");

    private static WhatsAppConversationParticipantLifecycleInput ParticipantLifecycleInput(
        long expectedConversationVersion,
        long expectedParticipantVersion,
        string reason) =>
        new(expectedConversationVersion, expectedParticipantVersion, reason,
            "Phase 9B staff", "BillingControl.WhatsApp");

    private static WhatsAppConversationLifecycleInput ConversationLifecycleInput(
        long expectedConversationVersion,
        string reason) =>
        new(expectedConversationVersion, reason, "Phase 9B staff", "BillingControl.WhatsApp");

    private static async Task<Exception?> CaptureFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<(int BillingRecords, int WorkItems, int Invoices, int Receipts, int Assignments, int Batches)>
        FinancialWorkflowCountsAsync(AppDbContext db) =>
        (
            await db.BillingRecords.CountAsync(),
            await db.WorkItems.CountAsync(),
            await db.Invoices.CountAsync(),
            await db.CustomerReceipts.CountAsync(),
            await db.WorkerAssignments.CountAsync(),
            await db.DocumentRequestBatches.CountAsync());

    [PostgresFact]
    public async Task WhatsAppConversationServiceCreatesDirectAndGroupAndTranslatesIdentityConflicts()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "phase9b-create-contact");
        var activeAddress = await AddContactAddressAsync(db, contact.Id, "+60139001001");
        var inactiveAddress = await AddContactAddressAsync(db, contact.Id, "+60139001002", isActive: false);
        var service = WhatsAppService(db);

        var direct = await service.CreateConversationAsync(DirectConversationInput(activeAddress.Id, "phase9b-direct"));
        Assert.Equal(WhatsAppConversationKind.Direct, direct.Kind);
        Assert.Equal(WhatsAppConversationStatus.Active, direct.Status);
        Assert.Equal(1, direct.AuthorizationVersion);
        Assert.Equal(activeAddress.Id, direct.DirectContactWhatsAppAddressId);
        var directCreated = Assert.Single(direct.History);
        Assert.Null(directCreated.PreviousStatus);
        Assert.Equal(WhatsAppConversationStatus.Active, directCreated.NewStatus);
        Assert.Null(directCreated.PreviousAuthorizationVersion);
        Assert.Equal(1, directCreated.NewAuthorizationVersion);
        Assert.Equal("Created", directCreated.Action);
        Assert.Equal("phase9b-staff", directCreated.Actor);
        Assert.Equal("BillingControl.WhatsApp", directCreated.Source);
        Assert.Equal(ConversationTestTime, directCreated.OccurredAt);

        var group = await service.CreateConversationAsync(GroupConversationInput("phase9b-group"));
        Assert.Equal(WhatsAppConversationKind.Group, group.Kind);
        Assert.Null(group.DirectContactWhatsAppAddressId);
        Assert.Equal(WhatsAppConversationStatus.Active, group.Status);
        Assert.Equal(1, group.AuthorizationVersion);

        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateConversationAsync(DirectConversationInput(inactiveAddress.Id, "phase9b-inactive-address")));
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateConversationAsync(Direct_conversation_missing_address(), CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateConversationAsync(new(
                WhatsAppConversationKind.Group, "Meta", "phase9b-endpoint", "phase9b-account", "phase9b-group",
                null, "phase9b-staff", "BillingControl.WhatsApp", "duplicate provider identity")));

        WhatsAppConversationCreateInput Direct_conversation_missing_address() =>
            DirectConversationInput(int.MaxValue, "phase9b-missing-address");
    }

    [PostgresFact]
    public async Task WhatsAppConversationServiceVersionsParticipantLifecycleAndSerializesConcurrentChanges()
    {
        await using var db = await Fresh();
        var service = WhatsAppService(db);
        var conversation = await service.CreateConversationAsync(GroupConversationInput("phase9b-participants"));
        var contact = await AddContactAsync(db, "phase9b-participant-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139001003");

        var joined = await service.AddParticipantAsync(conversation.Id,
            ContactParticipantInput(conversation.Version, contact.Id, address.Id, "+60 13-900 1003", "contact-participant"));
        var contactParticipant = Assert.Single(joined.Participants);
        Assert.Equal(2, joined.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, joined.Status);
        var joinedHistory = Assert.Single(contactParticipant.History);
        Assert.Equal("Joined", joinedHistory.Action);
        Assert.True(joinedHistory.NewIsActive);
        Assert.Equal("Phase 9B actor", joinedHistory.Actor);
        Assert.Equal("BillingControl.WhatsApp", joinedHistory.Source);
        Assert.Equal("Phase 9B participant", joinedHistory.Reason);
        Assert.Equal(ConversationTestTime, joinedHistory.OccurredAt);
        var membershipAudit = joined.History.Single(x => x.Action == "ParticipantJoined");
        Assert.Equal(1, membershipAudit.PreviousAuthorizationVersion);
        Assert.Equal(2, membershipAudit.NewAuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.Active, membershipAudit.PreviousStatus);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, membershipAudit.NewStatus);

        var participantHistoryCount = contactParticipant.History.Count;
        var noOpAdd = await service.AddParticipantAsync(joined.Id,
            ContactParticipantInput(joined.Version, contact.Id, address.Id, "+60139001003", "ignored-on-no-op"));
        Assert.Equal(2, noOpAdd.AuthorizationVersion);
        Assert.Equal(joined.Version, noOpAdd.Version);
        Assert.Equal(contactParticipant.Id, Assert.Single(noOpAdd.Participants).Id);
        Assert.Equal(participantHistoryCount, Assert.Single(noOpAdd.Participants).History.Count);

        await Assert.ThrowsAsync<BusinessException>(() => service.AddParticipantAsync(joined.Id,
            BusinessSenderInput(conversation.Version, "stale-concurrent-input")));

        var secondParticipant = await service.AddParticipantAsync(joined.Id,
            BusinessSenderInput(noOpAdd.Version, "phase9b-business-sender"));
        Assert.Equal(3, secondParticipant.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, secondParticipant.Status);
        Assert.Equal(2, secondParticipant.Participants.Count);

        var left = await service.DeactivateParticipantAsync(joined.Id, contactParticipant.Id,
            ParticipantLifecycleInput(secondParticipant.Version, contactParticipant.Version, "Member left"));
        var leftParticipant = left.Participants.Single(x => x.Id == contactParticipant.Id);
        Assert.Equal(4, left.AuthorizationVersion);
        Assert.False(leftParticipant.IsActive);
        Assert.Equal("Left", leftParticipant.History.Last().Action);
        Assert.Equal("Member left", leftParticipant.History.Last().Reason);

        var leftNoOp = await service.DeactivateParticipantAsync(left.Id, leftParticipant.Id,
            ParticipantLifecycleInput(left.Version, leftParticipant.Version, "Repeated member leave"));
        Assert.Equal(4, leftNoOp.AuthorizationVersion);
        Assert.Equal(left.Version, leftNoOp.Version);
        Assert.Equal(leftParticipant.History.Count, leftNoOp.Participants.Single(x => x.Id == leftParticipant.Id).History.Count);

        var rejoined = await service.ReactivateParticipantAsync(left.Id, leftParticipant.Id,
            ParticipantLifecycleInput(leftNoOp.Version, leftNoOp.Participants.Single(x => x.Id == leftParticipant.Id).Version, "Member rejoined"));
        var rejoinedParticipant = rejoined.Participants.Single(x => x.Id == leftParticipant.Id);
        Assert.Equal(5, rejoined.AuthorizationVersion);
        Assert.True(rejoinedParticipant.IsActive);
        Assert.Null(rejoinedParticipant.LeftAt);
        Assert.Equal("Rejoined", rejoinedParticipant.History.Last().Action);

        var rejoinedNoOp = await service.ReactivateParticipantAsync(rejoined.Id, rejoinedParticipant.Id,
            ParticipantLifecycleInput(rejoined.Version, rejoinedParticipant.Version, "Repeated member rejoin"));
        Assert.Equal(5, rejoinedNoOp.AuthorizationVersion);
        Assert.Equal(rejoined.Version, rejoinedNoOp.Version);
        Assert.Equal(rejoinedParticipant.History.Count, rejoinedNoOp.Participants.Single(x => x.Id == rejoinedParticipant.Id).History.Count);

        var beforeUnknownParticipantCount = rejoinedNoOp.Participants.Count;
        var unknownChanged = await service.RecordUnknownMembershipChangeAsync(rejoined.Id,
            new(rejoinedNoOp.Version, "Provider membership changed without a safe mapping", "Phase 9B provider",
                "BillingControl.WhatsApp"));
        Assert.Equal(6, unknownChanged.AuthorizationVersion);
        Assert.Equal(beforeUnknownParticipantCount, unknownChanged.Participants.Count);
        Assert.Contains(unknownChanged.History, x => x.Action == "UnknownMembershipChanged" && x.Reason == "Provider membership changed without a safe mapping");

        await using var concurrencySeed = Db();
        var concurrentConversation = await WhatsAppService(concurrencySeed)
            .CreateConversationAsync(GroupConversationInput("phase9b-concurrent"));
        var contextA = Db();
        var contextB = Db();
        await using (contextA)
        await using (contextB)
        {
            var serviceA = WhatsAppService(contextA);
            var serviceB = WhatsAppService(contextB);
            var result = await Task.WhenAll(
                CaptureFailureAsync(() => serviceA.AddParticipantAsync(concurrentConversation.Id,
                    BusinessSenderInput(concurrentConversation.Version, "phase9b-concurrent-a"))),
                CaptureFailureAsync(() => serviceB.AddParticipantAsync(concurrentConversation.Id,
                    BusinessSenderInput(concurrentConversation.Version, "phase9b-concurrent-b"))));

            Assert.Equal(1, result.Count(x => x is null));
            Assert.Single(result, x => x is BusinessException);
        }

        await using var verify = Db();
        var concurrentState = await verify.WhatsAppConversations.SingleAsync(x => x.Id == concurrentConversation.Id);
        Assert.Equal(2, concurrentState.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, concurrentState.Status);
        Assert.Equal(1, await verify.WhatsAppConversationParticipants.CountAsync(x => x.WhatsAppConversationId == concurrentConversation.Id));
        Assert.Equal(2, await verify.WhatsAppConversationHistories.CountAsync(x => x.WhatsAppConversationId == concurrentConversation.Id));
    }

    [PostgresFact]
    public async Task WhatsAppConversationServiceApprovesReapprovesRevokesAndReportsCurrentOrStaleScopes()
    {
        await using var db = await Fresh();
        var service = WhatsAppService(db);
        var conversation = await service.CreateConversationAsync(GroupConversationInput("phase9b-scopes"));
        var contact = await AddContactAsync(db, "phase9b-scope-contact");
        var joined = await service.AddParticipantAsync(conversation.Id,
            ContactParticipantInput(conversation.Version, contact.Id, null, null, "scope-contact"));
        var engagementOne = await Engagement(db);
        var engagementTwo = await Engagement(db);

        var approvedOne = await service.ApproveEngagementScopeAsync(joined.Id,
            ApprovalInput(joined.Version, engagementOne));
        var scopeOne = Assert.Single(approvedOne.EngagementScopes);
        Assert.Equal(2, approvedOne.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.Active, approvedOne.Status);
        Assert.Equal(2, scopeOne.ApprovedAuthorizationVersion);
        Assert.Equal(WhatsAppScopeAuthorizationState.Current, scopeOne.AuthorizationState);
        Assert.True(scopeOne.IsAuthorizationCurrent);
        Assert.False(scopeOne.IsAuthorizationStale);
        Assert.Equal("Approved", Assert.Single(scopeOne.History).Action);

        var approvedTwo = await service.ApproveEngagementScopeAsync(approvedOne.Id,
            ApprovalInput(approvedOne.Version, engagementTwo));
        Assert.Equal(2, approvedTwo.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.Active, approvedTwo.Status);
        Assert.Equal(2, approvedTwo.EngagementScopes.Count);

        var afterMembershipChange = await service.AddParticipantAsync(approvedTwo.Id,
            BusinessSenderInput(approvedTwo.Version, "scope-second-participant"));
        Assert.Equal(3, afterMembershipChange.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, afterMembershipChange.Status);
        Assert.All(afterMembershipChange.EngagementScopes, scope =>
        {
            Assert.Equal(2, scope.ApprovedAuthorizationVersion);
            Assert.Equal(WhatsAppScopeAuthorizationState.Stale, scope.AuthorizationState);
            Assert.False(scope.IsAuthorizationCurrent);
            Assert.True(scope.IsAuthorizationStale);
        });

        var staleOne = afterMembershipChange.EngagementScopes.Single(x => x.EngagementId == engagementOne);
        var staleTwo = afterMembershipChange.EngagementScopes.Single(x => x.EngagementId == engagementTwo);
        var oneReapproved = await service.ApproveEngagementScopeAsync(afterMembershipChange.Id,
            ApprovalInput(afterMembershipChange.Version, engagementOne, staleOne.Version, "Reapprove only one scope"));
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, oneReapproved.Status);
        Assert.Equal(3, oneReapproved.AuthorizationVersion);
        Assert.Equal(staleOne.Id, oneReapproved.EngagementScopes.Single(x => x.EngagementId == engagementOne).Id);
        Assert.Equal(3, oneReapproved.EngagementScopes.Single(x => x.EngagementId == engagementOne).ApprovedAuthorizationVersion);
        Assert.Equal("Reapproved", oneReapproved.EngagementScopes.Single(x => x.EngagementId == engagementOne).History.Last().Action);

        var allCurrent = await service.ApproveEngagementScopeAsync(oneReapproved.Id,
            ApprovalInput(oneReapproved.Version, engagementTwo, staleTwo.Version, "Reapprove the final scope"));
        Assert.Equal(WhatsAppConversationStatus.Active, allCurrent.Status);
        Assert.Equal(3, allCurrent.AuthorizationVersion);
        Assert.Contains(allCurrent.History, x => x.Action == "AuthorizationReviewCleared" &&
            x.PreviousAuthorizationVersion == 3 && x.NewAuthorizationVersion == 3);
        Assert.All(allCurrent.EngagementScopes, scope => Assert.Equal(WhatsAppScopeAuthorizationState.Current, scope.AuthorizationState));

        var currentScope = allCurrent.EngagementScopes.Single(x => x.EngagementId == engagementTwo);
        var historyCount = currentScope.History.Count;
        var noOp = await service.ApproveEngagementScopeAsync(allCurrent.Id,
            ApprovalInput(allCurrent.Version, engagementTwo, currentScope.Version, "Current scope no-op"));
        var noOpScope = noOp.EngagementScopes.Single(x => x.EngagementId == engagementTwo);
        Assert.Equal(allCurrent.Version, noOp.Version);
        Assert.Equal(historyCount, noOpScope.History.Count);
        Assert.Equal(3, noOp.AuthorizationVersion);

        var revoked = await service.RevokeEngagementScopeAsync(noOp.Id, noOpScope.Id,
            new(noOp.Version, noOpScope.Version, "Scope revoked by staff", "Phase 9B revoker", "BillingControl.WhatsApp"));
        var revokedScope = revoked.EngagementScopes.Single(x => x.EngagementId == engagementTwo);
        Assert.False(revokedScope.IsActive);
        Assert.Equal("Revoked", revokedScope.History.Last().Action);
        Assert.Equal("Scope revoked by staff", revokedScope.RevocationReason);
        Assert.Equal(3, revoked.AuthorizationVersion);

        var revokeNoOp = await service.RevokeEngagementScopeAsync(revoked.Id, revokedScope.Id,
            new(revoked.Version, revokedScope.Version, "Repeated revoke", "Phase 9B revoker", "BillingControl.WhatsApp"));
        Assert.Equal(revoked.Version, revokeNoOp.Version);
        Assert.Equal(revokedScope.History.Count, revokeNoOp.EngagementScopes.Single(x => x.Id == revokedScope.Id).History.Count);
        Assert.Equal(3, revokeNoOp.AuthorizationVersion);

        var reactivatedScope = await service.ApproveEngagementScopeAsync(revokeNoOp.Id,
            ApprovalInput(revokeNoOp.Version, engagementTwo, revokeNoOp.EngagementScopes.Single(x => x.Id == revokedScope.Id).Version, "Reactivate revoked scope"));
        var activeAgain = reactivatedScope.EngagementScopes.Single(x => x.Id == revokedScope.Id);
        Assert.True(activeAgain.IsActive);
        Assert.Null(activeAgain.RevokedAt);
        Assert.Null(activeAgain.RevokedByActor);
        Assert.Null(activeAgain.RevocationReason);
        Assert.Equal("Reactivated", activeAgain.History.Last().Action);
        Assert.Equal(3, reactivatedScope.AuthorizationVersion);

        var blockedGroup = await service.CreateConversationAsync(GroupConversationInput("phase9b-blocked-group"));
        var blockingEngagement = await Engagement(db);
        await Assert.ThrowsAsync<BusinessException>(() => service.ApproveEngagementScopeAsync(blockedGroup.Id,
            ApprovalInput(blockedGroup.Version, blockingEngagement)));
        var unknown = await service.AddParticipantAsync(blockedGroup.Id,
            UnknownExternalInput(blockedGroup.Version, "phase9b-unknown-member"));
        await Assert.ThrowsAsync<BusinessException>(() => service.ApproveEngagementScopeAsync(unknown.Id,
            ApprovalInput(unknown.Version, blockingEngagement)));
        var noActiveMembers = await service.DeactivateParticipantAsync(unknown.Id,
            Assert.Single(unknown.Participants).Id,
            ParticipantLifecycleInput(unknown.Version, Assert.Single(unknown.Participants).Version, "Unknown member left"));
        await Assert.ThrowsAsync<BusinessException>(() => service.ApproveEngagementScopeAsync(noActiveMembers.Id,
            ApprovalInput(noActiveMembers.Version, blockingEngagement)));
    }

    [PostgresFact]
    public async Task WhatsAppConversationServiceRequiresDirectAnchorAndPreservesLifecycleAndFinancialBoundaries()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "phase9b-direct-boundary-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139001004");
        var linkedCustomer = await AddContactCustomerAsync(db, "phase9b-linked-customer");
        await AddContactLinkAsync(db, contact.Id, linkedCustomer.Id);
        var service = WhatsAppService(db);
        var direct = await service.CreateConversationAsync(DirectConversationInput(address.Id, "phase9b-direct-scope"));
        var engagementId = await Engagement(db);
        var beforeScope = await service.GetConversationEngagementScopesAsync(direct.Id);
        Assert.Empty(beforeScope);

        var approved = await service.ApproveEngagementScopeAsync(direct.Id,
            ApprovalInput(direct.Version, engagementId, reason: "Explicit direct staff approval"));
        var directScope = Assert.Single(approved.EngagementScopes);
        Assert.Equal(WhatsAppConversationStatus.Active, approved.Status);
        Assert.Equal(1, approved.AuthorizationVersion);

        var beforeFinancialWorkflow = await FinancialWorkflowCountsAsync(db);
        var deactivated = await service.DeactivateConversationAsync(approved.Id,
            ConversationLifecycleInput(approved.Version, "Conversation temporarily inactive"));
        Assert.Equal(WhatsAppConversationStatus.Inactive, deactivated.Status);
        Assert.Equal(1, deactivated.AuthorizationVersion);
        Assert.Empty(deactivated.Participants);
        Assert.Single(deactivated.EngagementScopes);
        Assert.Contains(deactivated.History, x => x.Action == "Deactivated");

        await Assert.ThrowsAsync<BusinessException>(() => service.AddParticipantAsync(deactivated.Id,
            BusinessSenderInput(deactivated.Version, "blocked-while-inactive")));

        var reactivated = await service.ReactivateConversationAsync(deactivated.Id,
            ConversationLifecycleInput(deactivated.Version, "Conversation restored"));
        Assert.Equal(WhatsAppConversationStatus.Active, reactivated.Status);
        Assert.Equal(1, reactivated.AuthorizationVersion);
        Assert.Empty(reactivated.Participants);
        Assert.Single(reactivated.EngagementScopes);

        var addressService = new ContactService(db, ContactTestClock());
        var currentAddress = await db.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == address.Id);
        await addressService.SetAddressActiveAsync(currentAddress.Id,
            new(currentAddress.Version, false, false, "Direct address retired", "Phase 9B staff", "BillingControl.Contacts"));
        var revoked = await service.RevokeEngagementScopeAsync(reactivated.Id, directScope.Id,
            new(reactivated.Version, directScope.Version, "Prepare anchor validation", "Phase 9B staff", "BillingControl.WhatsApp"));
        await Assert.ThrowsAsync<BusinessException>(() => service.ApproveEngagementScopeAsync(revoked.Id,
            ApprovalInput(revoked.Version, engagementId, revoked.EngagementScopes.Single().Version, "Inactive direct anchor must block approval")));

        var currentBeforeStale = await service.GetConversationDetailsAsync(revoked.Id);
        Assert.NotNull(currentBeforeStale);
        var activeAddressAgain = await addressService.SetAddressActiveAsync(currentAddress.Id,
            new((await db.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == address.Id)).Version,
                true, false, "Direct address restored", "Phase 9B staff", "BillingControl.Contacts"));
        Assert.True(activeAddressAgain.IsActive);

        var safeAgain = await service.ApproveEngagementScopeAsync(revoked.Id,
            ApprovalInput(currentBeforeStale!.Version, engagementId, revoked.EngagementScopes.Single().Version, "Restore direct approval"));
        var participantContact = await AddContactAsync(db, "phase9b-stale-lifecycle-contact");
        var afterParticipantChange = await service.AddParticipantAsync(safeAgain.Id,
            ContactParticipantInput(safeAgain.Version, participantContact.Id, null, null, "stale-lifecycle-participant"));
        var inactiveAgain = await service.DeactivateConversationAsync(afterParticipantChange.Id,
            ConversationLifecycleInput(afterParticipantChange.Version, "Deactivate with stale scope"));
        var conservative = await service.ReactivateConversationAsync(inactiveAgain.Id,
            ConversationLifecycleInput(inactiveAgain.Version, "Conservative reactivation"));
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, conservative.Status);
        Assert.Equal(afterParticipantChange.AuthorizationVersion, conservative.AuthorizationVersion);
        Assert.Single(conservative.EngagementScopes);
        Assert.Equal(WhatsAppScopeAuthorizationState.Stale, conservative.EngagementScopes.Single().AuthorizationState);

        var reapproved = await service.ApproveEngagementScopeAsync(conservative.Id,
            ApprovalInput(conservative.Version, engagementId, conservative.EngagementScopes.Single().Version, "Reapprove after participant review"));
        Assert.Equal(WhatsAppConversationStatus.Active, reapproved.Status);
        Assert.Equal(conservative.AuthorizationVersion, reapproved.AuthorizationVersion);
        Assert.Contains(reapproved.History, x => x.Action == "AuthorizationReviewCleared");

        var afterFinancialWorkflow = await FinancialWorkflowCountsAsync(db);
        Assert.Equal(beforeFinancialWorkflow, afterFinancialWorkflow);
    }
}
