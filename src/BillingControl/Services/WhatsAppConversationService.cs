using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingControl.Services;

public enum WhatsAppScopeAuthorizationState
{
    Current,
    Stale
}

public sealed record WhatsAppConversationCreateInput(
    WhatsAppConversationKind Kind,
    string ProviderName,
    string BusinessEndpointKey,
    string? ProviderAccountReference,
    string ProviderConversationKey,
    int? DirectContactWhatsAppAddressId,
    string Actor,
    string Source,
    string? Reason = null);

public sealed record WhatsAppConversationParticipantCreateInput(
    long ExpectedConversationVersion,
    WhatsAppParticipantKind ParticipantKind,
    int? ContactId = null,
    int? ContactWhatsAppAddressId = null,
    int? BusinessPartyId = null,
    int? ManagerId = null,
    string? AppUserId = null,
    string? ProviderParticipantKey = null,
    string? NormalizedE164 = null,
    string? DisplayNameSnapshot = null,
    string? Reason = null,
    string Actor = "system",
    string Source = "System");

public sealed record WhatsAppConversationParticipantLifecycleInput(
    long ExpectedConversationVersion,
    long ExpectedParticipantVersion,
    string Reason,
    string Actor,
    string Source);

public sealed record WhatsAppConversationUnknownMembershipChangeInput(
    long ExpectedConversationVersion,
    string Reason,
    string Actor,
    string Source);

public sealed record WhatsAppConversationEngagementScopeApprovalInput(
    long ExpectedConversationVersion,
    int EngagementId,
    long? ExpectedScopeVersion,
    string Reason,
    string Actor,
    string Source);

public sealed record WhatsAppConversationEngagementScopeRevocationInput(
    long ExpectedConversationVersion,
    long ExpectedScopeVersion,
    string Reason,
    string Actor,
    string Source);

public sealed record WhatsAppConversationLifecycleInput(
    long ExpectedConversationVersion,
    string Reason,
    string Actor,
    string Source);

public sealed record WhatsAppConversationSummaryReadModel(
    int Id,
    WhatsAppConversationKind Kind,
    WhatsAppConversationStatus Status,
    string ProviderName,
    string BusinessEndpointKey,
    string? ProviderAccountReference,
    string ProviderConversationKey,
    int? DirectContactWhatsAppAddressId,
    int AuthorizationVersion,
    long Version,
    int ActiveParticipantCount,
    int ActiveEngagementScopeCount,
    int StaleActiveEngagementScopeCount);

public sealed record WhatsAppConversationHistoryReadModel(
    int Id,
    WhatsAppConversationStatus? PreviousStatus,
    WhatsAppConversationStatus NewStatus,
    int? PreviousAuthorizationVersion,
    int NewAuthorizationVersion,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record WhatsAppConversationParticipantHistoryReadModel(
    int Id,
    bool? PreviousIsActive,
    bool NewIsActive,
    DateTime? PreviousJoinedAt,
    DateTime NewJoinedAt,
    DateTime? PreviousLeftAt,
    DateTime? NewLeftAt,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record WhatsAppConversationParticipantReadModel(
    int Id,
    int WhatsAppConversationId,
    WhatsAppParticipantKind ParticipantKind,
    int? ContactId,
    string? ContactName,
    int? ContactWhatsAppAddressId,
    string? ContactWhatsAppAddressNumber,
    int? BusinessPartyId,
    string? BusinessPartyName,
    int? ManagerId,
    string? ManagerName,
    string? AppUserId,
    string? AppUserName,
    string? ProviderParticipantKey,
    string? NormalizedE164,
    string? DisplayNameSnapshot,
    bool IsActive,
    DateTime JoinedAt,
    DateTime? LeftAt,
    long Version,
    IReadOnlyList<WhatsAppConversationParticipantHistoryReadModel> History);

public sealed record WhatsAppConversationEngagementScopeHistoryReadModel(
    int Id,
    bool? PreviousIsActive,
    bool NewIsActive,
    int? PreviousApprovedAuthorizationVersion,
    int NewApprovedAuthorizationVersion,
    DateTime? PreviousApprovedAt,
    DateTime NewApprovedAt,
    string? PreviousApprovedByActor,
    string NewApprovedByActor,
    string? PreviousApprovalReason,
    string? NewApprovalReason,
    DateTime? PreviousRevokedAt,
    DateTime? NewRevokedAt,
    string? PreviousRevokedByActor,
    string? NewRevokedByActor,
    string? PreviousRevocationReason,
    string? NewRevocationReason,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record WhatsAppConversationEngagementScopeReadModel(
    int Id,
    int WhatsAppConversationId,
    int EngagementId,
    string? CustomerName,
    string? ServiceName,
    EngagementStatus EngagementStatus,
    bool IsActive,
    int ApprovedAuthorizationVersion,
    int CurrentAuthorizationVersion,
    WhatsAppScopeAuthorizationState AuthorizationState,
    bool IsAuthorizationCurrent,
    DateTime ApprovedAt,
    string ApprovedByActor,
    string? ApprovalReason,
    DateTime? RevokedAt,
    string? RevokedByActor,
    string? RevocationReason,
    long Version,
    IReadOnlyList<WhatsAppConversationEngagementScopeHistoryReadModel> History)
{
    // This is deliberately an authorization-version diagnostic, not a send decision.
    public bool IsAuthorizationStale => !IsAuthorizationCurrent;
    public string AuthorizationVersionState => IsAuthorizationCurrent ? "CURRENT" : "STALE";
}

public sealed record WhatsAppConversationDetailsReadModel(
    int Id,
    WhatsAppConversationKind Kind,
    WhatsAppConversationStatus Status,
    string ProviderName,
    string BusinessEndpointKey,
    string? ProviderAccountReference,
    string ProviderConversationKey,
    int? DirectContactWhatsAppAddressId,
    string? DirectContactWhatsAppAddressNumber,
    bool? DirectContactWhatsAppAddressIsActive,
    int AuthorizationVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version,
    IReadOnlyList<WhatsAppConversationParticipantReadModel> Participants,
    IReadOnlyList<WhatsAppConversationEngagementScopeReadModel> EngagementScopes,
    IReadOnlyList<WhatsAppConversationHistoryReadModel> History);

public sealed record WhatsAppDirectContactWhatsAppAddressOptionReadModel(
    int Id,
    string ContactName,
    string NormalizedE164);

public sealed record WhatsAppContactParticipantOptionReadModel(
    int Id,
    string Name,
    bool IsActive);

public sealed record WhatsAppContactWhatsAppAddressOptionReadModel(
    int Id,
    int ContactId,
    string ContactName,
    string NormalizedE164,
    bool IsActive);

public sealed record WhatsAppBusinessPartyParticipantOptionReadModel(
    int Id,
    string Name,
    bool IsActive);

public sealed record WhatsAppManagerParticipantOptionReadModel(
    int Id,
    string Name,
    bool IsActive);

public sealed record WhatsAppAppUserParticipantOptionReadModel(
    string Id,
    string DisplayName,
    bool IsActive);

public sealed record WhatsAppEngagementApprovalOptionReadModel(
    int Id,
    string CustomerName,
    string ServiceName);

public sealed record WhatsAppConversationManagementOptionsReadModel(
    IReadOnlyList<WhatsAppDirectContactWhatsAppAddressOptionReadModel> DirectContactWhatsAppAddressOptions,
    IReadOnlyList<WhatsAppContactParticipantOptionReadModel> ContactParticipantOptions,
    IReadOnlyList<WhatsAppContactWhatsAppAddressOptionReadModel> ContactWhatsAppAddressOptions,
    IReadOnlyList<WhatsAppBusinessPartyParticipantOptionReadModel> BusinessPartyParticipantOptions,
    IReadOnlyList<WhatsAppManagerParticipantOptionReadModel> ManagerParticipantOptions,
    IReadOnlyList<WhatsAppAppUserParticipantOptionReadModel> AppUserParticipantOptions,
    IReadOnlyList<WhatsAppEngagementApprovalOptionReadModel> ActiveEngagementApprovalOptions);

/// <summary>
/// Provider-neutral application operations for WhatsApp conversation membership
/// and Engagement confidentiality scope. A conversation row is the lock and
/// optimistic-concurrency boundary for every authorization-sensitive mutation.
/// This service never calls a provider and never infers authorization from
/// phone numbers, provider identifiers, or ContactCustomerLink rows.
/// </summary>
public sealed class WhatsAppConversationService(AppDbContext db, BusinessClock clock)
{
    private const string ConversationConflictMessage = "Another WhatsApp conversation change completed first. Refresh and retry.";
    private const string ConversationIdentityConflictMessage = "That WhatsApp provider conversation identity or active Direct address is already in use. Refresh and retry.";
    private const string ParticipantConflictMessage = "Another WhatsApp conversation participant change completed first. Refresh and retry.";
    private const string ScopeConflictMessage = "Another WhatsApp Engagement scope change completed first. Refresh and retry.";

    public async Task<IReadOnlyList<WhatsAppConversationSummaryReadModel>> GetConversationsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.WhatsAppConversations.AsNoTracking();
        if (!includeInactive)
            query = query.Where(x => x.Status != WhatsAppConversationStatus.Inactive);

        return await query
            .OrderBy(x => x.Id)
            .Select(x => new WhatsAppConversationSummaryReadModel(
                x.Id,
                x.Kind,
                x.Status,
                x.ProviderName,
                x.BusinessEndpointKey,
                x.ProviderAccountReference,
                x.ProviderConversationKey,
                x.DirectContactWhatsAppAddressId,
                x.AuthorizationVersion,
                x.Version,
                x.Participants.Count(p => p.IsActive),
                x.EngagementScopes.Count(s => s.IsActive),
                x.EngagementScopes.Count(s => s.IsActive && s.ApprovedAuthorizationVersion != x.AuthorizationVersion)))
            .ToListAsync(cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel?> GetConversationDetailsAsync(
        int conversationId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId <= 0)
            return null;

        var conversation = await ConversationDetailsQuery()
            .SingleOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        return conversation is null ? null : ToReadModel(conversation);
    }

    public async Task<IReadOnlyList<WhatsAppConversationParticipantReadModel>> GetConversationParticipantsAsync(
        int conversationId,
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var details = await GetConversationDetailsAsync(conversationId, cancellationToken);
        if (details is null)
            return [];

        return includeInactive
            ? details.Participants
            : details.Participants.Where(x => x.IsActive).ToArray();
    }

    public async Task<IReadOnlyList<WhatsAppConversationEngagementScopeReadModel>> GetConversationEngagementScopesAsync(
        int conversationId,
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var details = await GetConversationDetailsAsync(conversationId, cancellationToken);
        if (details is null)
            return [];

        return includeInactive
            ? details.EngagementScopes
            : details.EngagementScopes.Where(x => x.IsActive).ToArray();
    }

    public async Task<IReadOnlyList<WhatsAppDirectContactWhatsAppAddressOptionReadModel>>
        GetActiveDirectContactWhatsAppAddressOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.ContactWhatsAppAddresses
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Contact.Name)
            .ThenBy(x => x.NormalizedE164)
            .Select(x => new WhatsAppDirectContactWhatsAppAddressOptionReadModel(
                x.Id,
                x.Contact.Name,
                x.NormalizedE164))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppContactParticipantOptionReadModel>>
        GetContactParticipantOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Contacts
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new WhatsAppContactParticipantOptionReadModel(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppContactWhatsAppAddressOptionReadModel>>
        GetContactWhatsAppAddressOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.ContactWhatsAppAddresses
            .AsNoTracking()
            .OrderBy(x => x.Contact.Name)
            .ThenBy(x => x.NormalizedE164)
            .Select(x => new WhatsAppContactWhatsAppAddressOptionReadModel(
                x.Id,
                x.ContactId,
                x.Contact.Name,
                x.NormalizedE164,
                x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppBusinessPartyParticipantOptionReadModel>>
        GetBusinessPartyParticipantOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.BusinessParties
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new WhatsAppBusinessPartyParticipantOptionReadModel(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppManagerParticipantOptionReadModel>>
        GetManagerParticipantOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Managers
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new WhatsAppManagerParticipantOptionReadModel(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppAppUserParticipantOptionReadModel>>
        GetAppUserParticipantOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Users
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .ThenBy(x => x.Id)
            .Select(x => new WhatsAppAppUserParticipantOptionReadModel(
                x.Id,
                x.UserName ?? x.Id,
                x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppEngagementApprovalOptionReadModel>>
        GetActiveEngagementApprovalOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Engagements
            .AsNoTracking()
            .Where(x => x.Status == EngagementStatus.Active)
            .OrderBy(x => x.Customer.Name)
            .ThenBy(x => x.Service.Name)
            .ThenBy(x => x.Id)
            .Select(x => new WhatsAppEngagementApprovalOptionReadModel(
                x.Id,
                x.Customer.Name,
                x.Service.Name))
            .ToListAsync(cancellationToken);

    public async Task<WhatsAppConversationManagementOptionsReadModel> GetManagementOptionsAsync(
        CancellationToken cancellationToken = default) =>
        new(
            await GetActiveDirectContactWhatsAppAddressOptionsAsync(cancellationToken),
            await GetContactParticipantOptionsAsync(cancellationToken),
            await GetContactWhatsAppAddressOptionsAsync(cancellationToken),
            await GetBusinessPartyParticipantOptionsAsync(cancellationToken),
            await GetManagerParticipantOptionsAsync(cancellationToken),
            await GetAppUserParticipantOptionsAsync(cancellationToken),
            await GetActiveEngagementApprovalOptionsAsync(cancellationToken));

    public async Task<WhatsAppConversationDetailsReadModel> CreateConversationAsync(
        WhatsAppConversationCreateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Finance.Require(Enum.IsDefined(input.Kind), "The WhatsApp conversation kind is invalid.");

        var providerName = RequiredText(input.ProviderName, 80, "Provider name");
        var endpoint = RequiredText(input.BusinessEndpointKey, 254, "Business endpoint key");
        var providerAccount = OptionalText(input.ProviderAccountReference, 254, "Provider account reference");
        var conversationKey = RequiredText(input.ProviderConversationKey, 254, "Provider conversation key");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeOptionalReason(input.Reason);
        Finance.Require(input.Kind != WhatsAppConversationKind.Direct || input.DirectContactWhatsAppAddressId is > 0,
            "A Direct conversation requires a Contact WhatsApp address.");
        Finance.Require(input.Kind != WhatsAppConversationKind.Group || input.DirectContactWhatsAppAddressId is null,
            "A Group conversation cannot have a Direct Contact WhatsApp address.");

        var conversationId = await InSerializableTransactionAsync(async () =>
        {
            if (input.Kind == WhatsAppConversationKind.Direct)
            {
                var addressId = input.DirectContactWhatsAppAddressId!.Value;
                await LockRowAsync("ContactWhatsAppAddresses", addressId, cancellationToken,
                    "The Direct Contact WhatsApp address was not found.");
                var address = await db.ContactWhatsAppAddresses.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == addressId, cancellationToken)
                    ?? throw new BusinessException("The Direct Contact WhatsApp address was not found.");
                Finance.Require(address.IsActive,
                    "The Direct Contact WhatsApp address must be active.");
            }

            var conversation = new WhatsAppConversation
            {
                Kind = input.Kind,
                Status = WhatsAppConversationStatus.Active,
                ProviderName = providerName,
                BusinessEndpointKey = endpoint,
                ProviderAccountReference = providerAccount,
                ProviderConversationKey = conversationKey,
                DirectContactWhatsAppAddressId = input.DirectContactWhatsAppAddressId,
                AuthorizationVersion = 1
            };
            conversation.History.Add(new WhatsAppConversationHistory
            {
                PreviousStatus = null,
                NewStatus = conversation.Status,
                PreviousAuthorizationVersion = null,
                NewAuthorizationVersion = conversation.AuthorizationVersion,
                Action = "Created",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = clock.UtcNow
            });
            db.WhatsAppConversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);
            return conversation.Id;
        }, ConversationIdentityConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> AddParticipantAsync(
        int conversationId,
        WhatsAppConversationParticipantCreateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Finance.Require(conversationId > 0, "A valid WhatsApp conversation is required.");
        Finance.Require(input.ExpectedConversationVersion > 0,
            "The expected WhatsApp conversation version is required.");

        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeRequiredReason(input.Reason, "A reason is required to change conversation participants.");

        var participantId = await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("WhatsAppConversations", conversationId, cancellationToken,
                "The WhatsApp conversation was not found.");
            var conversation = await db.WhatsAppConversations
                .SingleOrDefaultAsync(x => x.Id == conversationId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp conversation was not found.");
            EnsureVersion(conversation.Version, input.ExpectedConversationVersion, ConversationConflictMessage);
            Finance.Require(conversation.Status != WhatsAppConversationStatus.Inactive,
                "Reactivate the WhatsApp conversation before changing participants.");

            var normalized = await ValidateParticipantInputAsync(input, cancellationToken);
            var existing = await FindDurableParticipantAsync(conversationId, normalized, cancellationToken);
            if (existing is not null)
            {
                if (existing.IsActive)
                    return existing.Id;

                throw new BusinessException("That participant already has a durable inactive row. Reactivate it instead of adding another row.");
            }

            var now = clock.UtcNow;
            var participant = new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversationId,
                ParticipantKind = normalized.ParticipantKind,
                ContactId = normalized.ContactId,
                ContactWhatsAppAddressId = normalized.ContactWhatsAppAddressId,
                BusinessPartyId = normalized.BusinessPartyId,
                ManagerId = normalized.ManagerId,
                AppUserId = normalized.AppUserId,
                ProviderParticipantKey = normalized.ProviderParticipantKey,
                NormalizedE164 = normalized.NormalizedE164,
                DisplayNameSnapshot = normalized.DisplayNameSnapshot,
                IsActive = true,
                JoinedAt = now,
                LeftAt = null
            };
            participant.History.Add(new WhatsAppConversationParticipantHistory
            {
                PreviousIsActive = null,
                NewIsActive = true,
                PreviousJoinedAt = null,
                NewJoinedAt = now,
                PreviousLeftAt = null,
                NewLeftAt = null,
                Action = "Joined",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = now
            });
            db.WhatsAppConversationParticipants.Add(participant);
            InvalidateAuthorization(conversation, "ParticipantJoined", reason, actor, source, now);
            await db.SaveChangesAsync(cancellationToken);
            return participant.Id;
        }, ParticipantConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> DeactivateParticipantAsync(
        int conversationId,
        int participantId,
        WhatsAppConversationParticipantLifecycleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateParticipantLifecycleArguments(conversationId, participantId, input);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeRequiredReason(input.Reason, "A reason is required to deactivate a conversation participant.");

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            Finance.Require(conversation.Status != WhatsAppConversationStatus.Inactive,
                "Reactivate the WhatsApp conversation before changing participants.");
            await LockRowAsync("WhatsAppConversationParticipants", participantId, cancellationToken,
                "The WhatsApp conversation participant was not found.");
            var participant = await db.WhatsAppConversationParticipants
                .SingleOrDefaultAsync(x => x.Id == participantId && x.WhatsAppConversationId == conversationId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp conversation participant was not found.");
            EnsureVersion(participant.Version, input.ExpectedParticipantVersion, ParticipantConflictMessage);
            if (!participant.IsActive)
                return true;

            var now = clock.UtcNow;
            var previousJoinedAt = participant.JoinedAt;
            var previousLeftAt = participant.LeftAt;
            participant.IsActive = false;
            participant.LeftAt = now;
            participant.History.Add(new WhatsAppConversationParticipantHistory
            {
                PreviousIsActive = true,
                NewIsActive = false,
                PreviousJoinedAt = previousJoinedAt,
                NewJoinedAt = participant.JoinedAt,
                PreviousLeftAt = previousLeftAt,
                NewLeftAt = now,
                Action = "Left",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = now
            });
            InvalidateAuthorization(conversation, "ParticipantLeft", reason, actor, source, now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ParticipantConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> ReactivateParticipantAsync(
        int conversationId,
        int participantId,
        WhatsAppConversationParticipantLifecycleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateParticipantLifecycleArguments(conversationId, participantId, input);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeRequiredReason(input.Reason, "A reason is required to reactivate a conversation participant.");

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            Finance.Require(conversation.Status != WhatsAppConversationStatus.Inactive,
                "Reactivate the WhatsApp conversation before changing participants.");
            await LockRowAsync("WhatsAppConversationParticipants", participantId, cancellationToken,
                "The WhatsApp conversation participant was not found.");
            var participant = await db.WhatsAppConversationParticipants
                .SingleOrDefaultAsync(x => x.Id == participantId && x.WhatsAppConversationId == conversationId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp conversation participant was not found.");
            EnsureVersion(participant.Version, input.ExpectedParticipantVersion, ParticipantConflictMessage);
            if (participant.IsActive)
                return true;

            var now = clock.UtcNow;
            var previousJoinedAt = participant.JoinedAt;
            var previousLeftAt = participant.LeftAt;
            participant.IsActive = true;
            participant.JoinedAt = now;
            participant.LeftAt = null;
            participant.History.Add(new WhatsAppConversationParticipantHistory
            {
                PreviousIsActive = false,
                NewIsActive = true,
                PreviousJoinedAt = previousJoinedAt,
                NewJoinedAt = now,
                PreviousLeftAt = previousLeftAt,
                NewLeftAt = null,
                Action = "Rejoined",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = now
            });
            InvalidateAuthorization(conversation, "ParticipantRejoined", reason, actor, source, now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ParticipantConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> RecordUnknownMembershipChangeAsync(
        int conversationId,
        WhatsAppConversationUnknownMembershipChangeInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Finance.Require(conversationId > 0, "A valid WhatsApp conversation is required.");
        Finance.Require(input.ExpectedConversationVersion > 0,
            "The expected WhatsApp conversation version is required.");
        var reason = NormalizeRequiredReason(input.Reason, "A reason is required for an unknown membership change.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            Finance.Require(conversation.Status != WhatsAppConversationStatus.Inactive,
                "Reactivate the WhatsApp conversation before recording membership changes.");
            var now = clock.UtcNow;
            InvalidateAuthorization(conversation, "UnknownMembershipChanged", reason, actor, source, now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ConversationConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> ApproveEngagementScopeAsync(
        int conversationId,
        WhatsAppConversationEngagementScopeApprovalInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Finance.Require(conversationId > 0, "A valid WhatsApp conversation is required.");
        Finance.Require(input.ExpectedConversationVersion > 0,
            "The expected WhatsApp conversation version is required.");
        Finance.Require(input.EngagementId > 0, "A valid Engagement is required.");
        var reason = NormalizeRequiredReason(input.Reason, "An audited reason is required to approve an Engagement scope.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            Finance.Require(conversation.Status != WhatsAppConversationStatus.Inactive,
                "An inactive WhatsApp conversation cannot receive an Engagement scope approval.");

            await LockRowAsync("Engagements", input.EngagementId, cancellationToken,
                "The Engagement was not found.");
            var engagement = await db.Engagements.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == input.EngagementId, cancellationToken)
                ?? throw new BusinessException("The Engagement was not found.");
            Finance.Require(engagement.Status == EngagementStatus.Active,
                "Only an active Engagement may be approved in a WhatsApp conversation scope.");

            var scope = await db.WhatsAppConversationEngagementScopes
                .SingleOrDefaultAsync(x => x.WhatsAppConversationId == conversationId && x.EngagementId == input.EngagementId, cancellationToken);
            if (scope is not null)
            {
                await LockRowAsync("WhatsAppConversationEngagementScopes", scope.Id, cancellationToken,
                    "The WhatsApp Engagement scope was not found.");
                db.Entry(scope).State = EntityState.Detached;
                scope = await db.WhatsAppConversationEngagementScopes
                    .SingleOrDefaultAsync(x => x.Id == scope.Id, cancellationToken)
                    ?? throw new BusinessException("The WhatsApp Engagement scope was not found.");
                Finance.Require(input.ExpectedScopeVersion is > 0,
                    "The expected WhatsApp Engagement scope version is required.");
                EnsureVersion(scope.Version, input.ExpectedScopeVersion!.Value, ScopeConflictMessage);
            }

            await EnsureApprovalFactsSafeAsync(conversation, cancellationToken);
            var now = clock.UtcNow;
            if (scope is not null && scope.IsActive && scope.ApprovedAuthorizationVersion == conversation.AuthorizationVersion)
            {
                _ = await TryClearAuthorizationReviewAsync(conversation, actor, source, reason, now, cancellationToken);
                if (db.ChangeTracker.HasChanges())
                    await db.SaveChangesAsync(cancellationToken);
                return scope.Id;
            }

            var previous = scope is null ? null : ScopeSnapshot.From(scope);
            if (scope is null)
            {
                scope = new WhatsAppConversationEngagementScope
                {
                    WhatsAppConversationId = conversationId,
                    EngagementId = input.EngagementId,
                    IsActive = true,
                    ApprovedAuthorizationVersion = conversation.AuthorizationVersion,
                    ApprovedAt = now,
                    ApprovedByActor = actor,
                    ApprovalReason = reason,
                    RevokedAt = null,
                    RevokedByActor = null,
                    RevocationReason = null
                };
                db.WhatsAppConversationEngagementScopes.Add(scope);
            }
            else
            {
                scope.IsActive = true;
                scope.ApprovedAuthorizationVersion = conversation.AuthorizationVersion;
                scope.ApprovedAt = now;
                scope.ApprovedByActor = actor;
                scope.ApprovalReason = reason;
                scope.RevokedAt = null;
                scope.RevokedByActor = null;
                scope.RevocationReason = null;
            }

            scope.History.Add(new WhatsAppConversationEngagementScopeHistory
            {
                PreviousIsActive = previous?.IsActive,
                NewIsActive = scope.IsActive,
                PreviousApprovedAuthorizationVersion = previous?.ApprovedAuthorizationVersion,
                NewApprovedAuthorizationVersion = scope.ApprovedAuthorizationVersion,
                PreviousApprovedAt = previous?.ApprovedAt,
                NewApprovedAt = scope.ApprovedAt,
                PreviousApprovedByActor = previous?.ApprovedByActor,
                NewApprovedByActor = scope.ApprovedByActor,
                PreviousApprovalReason = previous?.ApprovalReason,
                NewApprovalReason = scope.ApprovalReason,
                PreviousRevokedAt = previous?.RevokedAt,
                NewRevokedAt = scope.RevokedAt,
                PreviousRevokedByActor = previous?.RevokedByActor,
                NewRevokedByActor = scope.RevokedByActor,
                PreviousRevocationReason = previous?.RevocationReason,
                NewRevocationReason = scope.RevocationReason,
                Action = previous is null ? "Approved" : previous.IsActive ? "Reapproved" : "Reactivated",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = now
            });

            // Persist the approval before evaluating the aggregate-wide rule so
            // the newly-created durable scope participates in the query. Both
            // saves remain inside the same serializable transaction.
            await db.SaveChangesAsync(cancellationToken);
            _ = await TryClearAuthorizationReviewAsync(conversation, actor, source, reason, now, cancellationToken);
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(cancellationToken);
            return scope.Id;
        }, ScopeConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public Task<WhatsAppConversationDetailsReadModel> ApproveEngagementScopeAsync(
        int conversationId,
        int engagementId,
        long expectedConversationVersion,
        string reason,
        string actor,
        string source,
        CancellationToken cancellationToken = default) =>
        ApproveEngagementScopeAsync(conversationId, new(
            expectedConversationVersion,
            engagementId,
            null,
            reason,
            actor,
            source), cancellationToken);

    public async Task<WhatsAppConversationDetailsReadModel> RevokeEngagementScopeAsync(
        int conversationId,
        int scopeId,
        WhatsAppConversationEngagementScopeRevocationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Finance.Require(conversationId > 0 && scopeId > 0,
            "A valid WhatsApp conversation and Engagement scope are required.");
        Finance.Require(input.ExpectedConversationVersion > 0,
            "The expected WhatsApp conversation version is required.");
        Finance.Require(input.ExpectedScopeVersion > 0,
            "The expected WhatsApp Engagement scope version is required.");
        var reason = NormalizeRequiredReason(input.Reason, "An audited reason is required to revoke an Engagement scope.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            await LockRowAsync("WhatsAppConversationEngagementScopes", scopeId, cancellationToken,
                "The WhatsApp Engagement scope was not found.");
            var scope = await db.WhatsAppConversationEngagementScopes
                .SingleOrDefaultAsync(x => x.Id == scopeId && x.WhatsAppConversationId == conversationId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp Engagement scope was not found.");
            EnsureVersion(scope.Version, input.ExpectedScopeVersion, ScopeConflictMessage);
            if (!scope.IsActive)
                return true;

            var previous = ScopeSnapshot.From(scope);
            var now = clock.UtcNow;
            scope.IsActive = false;
            scope.RevokedAt = now;
            scope.RevokedByActor = actor;
            scope.RevocationReason = reason;
            scope.History.Add(new WhatsAppConversationEngagementScopeHistory
            {
                PreviousIsActive = previous.IsActive,
                NewIsActive = false,
                PreviousApprovedAuthorizationVersion = previous.ApprovedAuthorizationVersion,
                NewApprovedAuthorizationVersion = scope.ApprovedAuthorizationVersion,
                PreviousApprovedAt = previous.ApprovedAt,
                NewApprovedAt = scope.ApprovedAt,
                PreviousApprovedByActor = previous.ApprovedByActor,
                NewApprovedByActor = scope.ApprovedByActor,
                PreviousApprovalReason = previous.ApprovalReason,
                NewApprovalReason = scope.ApprovalReason,
                PreviousRevokedAt = previous.RevokedAt,
                NewRevokedAt = scope.RevokedAt,
                PreviousRevokedByActor = previous.RevokedByActor,
                NewRevokedByActor = scope.RevokedByActor,
                PreviousRevocationReason = previous.RevocationReason,
                NewRevocationReason = scope.RevocationReason,
                Action = "Revoked",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ScopeConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> DeactivateConversationAsync(
        int conversationId,
        WhatsAppConversationLifecycleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateConversationLifecycleArguments(conversationId, input);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeRequiredReason(input.Reason, "A reason is required to deactivate a WhatsApp conversation.");

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            if (conversation.Status == WhatsAppConversationStatus.Inactive)
                return true;

            var previousStatus = conversation.Status;
            var now = clock.UtcNow;
            conversation.Status = WhatsAppConversationStatus.Inactive;
            AddConversationHistory(conversation, previousStatus, conversation.Status,
                conversation.AuthorizationVersion, conversation.AuthorizationVersion,
                "Deactivated", reason, actor, source, now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ConversationConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    public async Task<WhatsAppConversationDetailsReadModel> ReactivateConversationAsync(
        int conversationId,
        WhatsAppConversationLifecycleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateConversationLifecycleArguments(conversationId, input);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeRequiredReason(input.Reason, "A reason is required to reactivate a WhatsApp conversation.");

        await InSerializableTransactionAsync(async () =>
        {
            var conversation = await LockConversationAsync(conversationId, input.ExpectedConversationVersion, cancellationToken);
            if (conversation.Status != WhatsAppConversationStatus.Inactive)
                return true;

            var previousStatus = conversation.Status;
            var now = clock.UtcNow;
            var canBeActive = await CanBeActiveAsync(conversation, cancellationToken);
            conversation.Status = canBeActive
                ? WhatsAppConversationStatus.Active
                : WhatsAppConversationStatus.NeedsAuthorizationReview;
            AddConversationHistory(conversation, previousStatus, conversation.Status,
                conversation.AuthorizationVersion, conversation.AuthorizationVersion,
                "Reactivated", reason, actor, source, now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ConversationConflictMessage, cancellationToken);

        return await ReloadConversationAsync(conversationId, cancellationToken);
    }

    private IQueryable<WhatsAppConversation> ConversationDetailsQuery() => db.WhatsAppConversations
        .AsNoTracking()
        .AsSplitQuery()
        .Include(x => x.DirectContactWhatsAppAddress)
        .Include(x => x.Participants).ThenInclude(x => x.Contact)
        .Include(x => x.Participants).ThenInclude(x => x.ContactWhatsAppAddress)
        .Include(x => x.Participants).ThenInclude(x => x.BusinessParty)
        .Include(x => x.Participants).ThenInclude(x => x.Manager)
        .Include(x => x.Participants).ThenInclude(x => x.AppUser)
        .Include(x => x.Participants).ThenInclude(x => x.History)
        .Include(x => x.EngagementScopes).ThenInclude(x => x.Engagement).ThenInclude(x => x.Customer)
        .Include(x => x.EngagementScopes).ThenInclude(x => x.Engagement).ThenInclude(x => x.Service)
        .Include(x => x.EngagementScopes).ThenInclude(x => x.History)
        .Include(x => x.History);

    private async Task<WhatsAppConversationDetailsReadModel> ReloadConversationAsync(
        int conversationId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await GetConversationDetailsAsync(conversationId, cancellationToken)
            ?? throw new BusinessException("The WhatsApp conversation was not found.");
    }

    private async Task<WhatsAppConversation> LockConversationAsync(
        int conversationId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await LockRowAsync("WhatsAppConversations", conversationId, cancellationToken,
            "The WhatsApp conversation was not found.");
        var conversation = await db.WhatsAppConversations
            .SingleOrDefaultAsync(x => x.Id == conversationId, cancellationToken)
            ?? throw new BusinessException("The WhatsApp conversation was not found.");
        EnsureVersion(conversation.Version, expectedVersion, ConversationConflictMessage);
        return conversation;
    }

    private async Task<NormalizedParticipant> ValidateParticipantInputAsync(
        WhatsAppConversationParticipantCreateInput input,
        CancellationToken cancellationToken)
    {
        Finance.Require(Enum.IsDefined(input.ParticipantKind), "The WhatsApp participant kind is invalid.");
        ValidatePositiveIfPresent(input.ContactId, "Contact");
        ValidatePositiveIfPresent(input.ContactWhatsAppAddressId, "Contact WhatsApp address");
        ValidatePositiveIfPresent(input.BusinessPartyId, "Business Party");
        ValidatePositiveIfPresent(input.ManagerId, "Manager");

        var providerParticipantKey = OptionalText(input.ProviderParticipantKey, 254, "Provider participant key");
        var normalizedE164 = string.IsNullOrWhiteSpace(input.NormalizedE164)
            ? null
            : ContactValueObjects.NormalizeE164(input.NormalizedE164);
        var displayName = OptionalText(input.DisplayNameSnapshot, 254, "Participant display name");
        var mappingCount = (input.ContactId is not null ? 1 : 0)
            + (input.BusinessPartyId is not null ? 1 : 0)
            + (input.ManagerId is not null ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(input.AppUserId) ? 1 : 0);

        switch (input.ParticipantKind)
        {
            case WhatsAppParticipantKind.Contact:
                Finance.Require(input.ContactId is > 0, "A Contact participant requires a Contact.");
                Finance.Require(mappingCount == 1 && input.BusinessPartyId is null && input.ManagerId is null && string.IsNullOrWhiteSpace(input.AppUserId),
                    "A Contact participant cannot carry another typed identity mapping.");
                break;
            case WhatsAppParticipantKind.BusinessParty:
                Finance.Require(input.BusinessPartyId is > 0 && mappingCount == 1 && input.ContactId is null && input.ManagerId is null && string.IsNullOrWhiteSpace(input.AppUserId) && input.ContactWhatsAppAddressId is null,
                    "A Business Party participant requires only a Business Party mapping.");
                break;
            case WhatsAppParticipantKind.Manager:
                Finance.Require(input.ManagerId is > 0 && mappingCount == 1 && input.ContactId is null && input.BusinessPartyId is null && string.IsNullOrWhiteSpace(input.AppUserId) && input.ContactWhatsAppAddressId is null,
                    "A Manager participant requires only a Manager mapping.");
                break;
            case WhatsAppParticipantKind.AppUser:
                Finance.Require(!string.IsNullOrWhiteSpace(input.AppUserId) && mappingCount == 1 && input.ContactId is null && input.BusinessPartyId is null && input.ManagerId is null && input.ContactWhatsAppAddressId is null,
                    "An AppUser participant requires only an AppUser mapping.");
                break;
            case WhatsAppParticipantKind.BusinessSender:
            case WhatsAppParticipantKind.UnknownExternal:
                Finance.Require(mappingCount == 0 && input.ContactWhatsAppAddressId is null,
                    "This participant kind cannot carry a typed identity mapping.");
                break;
        }

        var appUserId = input.AppUserId?.Trim();
        if (input.ParticipantKind == WhatsAppParticipantKind.AppUser)
        {
            Finance.Require(await db.Users.AsNoTracking().AnyAsync(x => x.Id == appUserId, cancellationToken),
                "The AppUser participant was not found.");
        }

        if (input.ParticipantKind == WhatsAppParticipantKind.BusinessParty)
        {
            await LockRowAsync("BusinessParties", input.BusinessPartyId!.Value, cancellationToken,
                "The Business Party participant was not found.");
        }
        else if (input.ParticipantKind == WhatsAppParticipantKind.Manager)
        {
            await LockRowAsync("Managers", input.ManagerId!.Value, cancellationToken,
                "The Manager participant was not found.");
        }
        else if (input.ParticipantKind == WhatsAppParticipantKind.Contact)
        {
            await LockRowAsync("Contacts", input.ContactId!.Value, cancellationToken,
                "The Contact participant was not found.");
            Finance.Require(await db.Contacts.AsNoTracking().AnyAsync(x => x.Id == input.ContactId, cancellationToken),
                "The Contact participant was not found.");

            if (input.ContactWhatsAppAddressId is int addressId)
            {
                await LockRowAsync("ContactWhatsAppAddresses", addressId, cancellationToken,
                    "The Contact WhatsApp address was not found.");
                var address = await db.ContactWhatsAppAddresses.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == addressId, cancellationToken)
                    ?? throw new BusinessException("The Contact WhatsApp address was not found.");
                Finance.Require(address.ContactId == input.ContactId,
                    "The supplied Contact WhatsApp address does not belong to the Contact participant.");
                if (normalizedE164 is not null)
                    Finance.Require(address.NormalizedE164 == normalizedE164,
                        "The participant phone snapshot does not match the authoritative Contact WhatsApp address.");
            }
        }

        if (input.ParticipantKind == WhatsAppParticipantKind.UnknownExternal)
        {
            Finance.Require(providerParticipantKey is not null || normalizedE164 is not null || displayName is not null,
                "An UnknownExternal participant requires provider, phone, or display evidence; record an unknown membership change when it cannot be mapped.");
        }

        return new NormalizedParticipant(
            input.ParticipantKind,
            input.ContactId,
            input.ContactWhatsAppAddressId,
            input.BusinessPartyId,
            input.ManagerId,
            appUserId,
            providerParticipantKey,
            normalizedE164,
            displayName);
    }

    private async Task<WhatsAppConversationParticipant?> FindDurableParticipantAsync(
        int conversationId,
        NormalizedParticipant participant,
        CancellationToken cancellationToken)
    {
        var query = db.WhatsAppConversationParticipants
            .Where(x => x.WhatsAppConversationId == conversationId && x.ParticipantKind == participant.ParticipantKind);
        query = participant.ParticipantKind switch
        {
            WhatsAppParticipantKind.Contact => query.Where(x => x.ContactId == participant.ContactId),
            WhatsAppParticipantKind.BusinessParty => query.Where(x => x.BusinessPartyId == participant.BusinessPartyId),
            WhatsAppParticipantKind.Manager => query.Where(x => x.ManagerId == participant.ManagerId),
            WhatsAppParticipantKind.AppUser => query.Where(x => x.AppUserId == participant.AppUserId),
            _ when participant.ProviderParticipantKey is not null => query.Where(x => x.ProviderParticipantKey == participant.ProviderParticipantKey),
            _ when participant.NormalizedE164 is not null => query.Where(x => x.NormalizedE164 == participant.NormalizedE164),
            _ when participant.DisplayNameSnapshot is not null => query.Where(x => x.DisplayNameSnapshot == participant.DisplayNameSnapshot),
            _ => query.Where(x => false)
        };
        return await query.OrderByDescending(x => x.IsActive).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task EnsureApprovalFactsSafeAsync(
        WhatsAppConversation conversation,
        CancellationToken cancellationToken)
    {
        if (conversation.Kind == WhatsAppConversationKind.Direct)
        {
            Finance.Require(conversation.DirectContactWhatsAppAddressId is > 0,
                "A Direct conversation requires its immutable Contact WhatsApp address anchor.");
            var addressId = conversation.DirectContactWhatsAppAddressId!.Value;
            await LockRowAsync("ContactWhatsAppAddresses", addressId, cancellationToken,
                "The Direct Contact WhatsApp address was not found.");
            var address = await db.ContactWhatsAppAddresses.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == addressId, cancellationToken)
                ?? throw new BusinessException("The Direct Contact WhatsApp address was not found.");
            Finance.Require(address.IsActive,
                "The Direct Contact WhatsApp address must be active before authorization approval.");
            return;
        }

        var activeParticipants = await db.WhatsAppConversationParticipants.AsNoTracking()
            .Where(x => x.WhatsAppConversationId == conversation.Id && x.IsActive)
            .ToListAsync(cancellationToken);
        Finance.Require(activeParticipants.Count > 0,
            "A Group conversation must have at least one active participant before authorization approval.");
        Finance.Require(!activeParticipants.Any(x => x.ParticipantKind == WhatsAppParticipantKind.UnknownExternal),
            "An active UnknownExternal participant must be mapped or removed before authorization approval.");
    }

    private async Task<bool> CanBeActiveAsync(
        WhatsAppConversation conversation,
        CancellationToken cancellationToken)
    {
        var activeScopes = await db.WhatsAppConversationEngagementScopes.AsNoTracking()
            .Where(x => x.WhatsAppConversationId == conversation.Id && x.IsActive)
            .Select(x => new { x.ApprovedAuthorizationVersion, EngagementStatus = x.Engagement.Status })
            .ToListAsync(cancellationToken);
        if (activeScopes.Count == 0 || activeScopes.Any(x => x.ApprovedAuthorizationVersion != conversation.AuthorizationVersion || x.EngagementStatus != EngagementStatus.Active))
            return false;

        if (conversation.Kind == WhatsAppConversationKind.Group)
        {
            var activeParticipants = await db.WhatsAppConversationParticipants.AsNoTracking()
                .Where(x => x.WhatsAppConversationId == conversation.Id && x.IsActive)
                .Select(x => x.ParticipantKind)
                .ToListAsync(cancellationToken);
            return activeParticipants.Count > 0 && !activeParticipants.Contains(WhatsAppParticipantKind.UnknownExternal);
        }

        if (conversation.DirectContactWhatsAppAddressId is not > 0)
            return false;
        var addressId = conversation.DirectContactWhatsAppAddressId.Value;
        await LockRowAsync("ContactWhatsAppAddresses", addressId, cancellationToken,
            "The Direct Contact WhatsApp address was not found.");
        return await db.ContactWhatsAppAddresses.AsNoTracking()
            .Where(x => x.Id == addressId)
            .Select(x => x.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> TryClearAuthorizationReviewAsync(
        WhatsAppConversation conversation,
        string actor,
        string source,
        string reason,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        if (conversation.Status != WhatsAppConversationStatus.NeedsAuthorizationReview ||
            !await CanBeActiveAsync(conversation, cancellationToken))
            return false;

        var previousStatus = conversation.Status;
        conversation.Status = WhatsAppConversationStatus.Active;
        AddConversationHistory(conversation, previousStatus, conversation.Status,
            conversation.AuthorizationVersion, conversation.AuthorizationVersion,
            "AuthorizationReviewCleared", reason, actor, source, occurredAt);
        return true;
    }

    private static void InvalidateAuthorization(
        WhatsAppConversation conversation,
        string action,
        string reason,
        string actor,
        string source,
        DateTime occurredAt)
    {
        Finance.Require(conversation.AuthorizationVersion < int.MaxValue,
            "The WhatsApp conversation authorization version limit has been reached.");
        var previousStatus = conversation.Status;
        var previousAuthorizationVersion = conversation.AuthorizationVersion;
        conversation.AuthorizationVersion++;
        conversation.Status = WhatsAppConversationStatus.NeedsAuthorizationReview;
        AddConversationHistory(conversation, previousStatus, conversation.Status,
            previousAuthorizationVersion, conversation.AuthorizationVersion,
            action, reason, actor, source, occurredAt);
    }

    private static void AddConversationHistory(
        WhatsAppConversation conversation,
        WhatsAppConversationStatus? previousStatus,
        WhatsAppConversationStatus newStatus,
        int? previousAuthorizationVersion,
        int newAuthorizationVersion,
        string action,
        string? reason,
        string actor,
        string source,
        DateTime occurredAt) =>
        conversation.History.Add(new WhatsAppConversationHistory
        {
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            PreviousAuthorizationVersion = previousAuthorizationVersion,
            NewAuthorizationVersion = newAuthorizationVersion,
            Action = action,
            Reason = reason,
            Actor = actor,
            Source = source,
            OccurredAt = occurredAt
        });

    private static void ValidateParticipantLifecycleArguments(
        int conversationId,
        int participantId,
        WhatsAppConversationParticipantLifecycleInput input)
    {
        Finance.Require(conversationId > 0 && participantId > 0,
            "A valid WhatsApp conversation and participant are required.");
        Finance.Require(input.ExpectedConversationVersion > 0,
            "The expected WhatsApp conversation version is required.");
        Finance.Require(input.ExpectedParticipantVersion > 0,
            "The expected WhatsApp conversation participant version is required.");
    }

    private static void ValidateConversationLifecycleArguments(
        int conversationId,
        WhatsAppConversationLifecycleInput input)
    {
        Finance.Require(conversationId > 0, "A valid WhatsApp conversation is required.");
        Finance.Require(input.ExpectedConversationVersion > 0,
            "The expected WhatsApp conversation version is required.");
    }

    private static void ValidatePositiveIfPresent(int? value, string label) =>
        Finance.Require(value is null or > 0, $"The {label} identifier must be positive when supplied.");

    private static string RequiredText(string? value, int maxLength, string label)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(value), $"{label} is required.");
        var normalized = value!.Trim();
        Finance.Require(normalized.Length <= maxLength, $"{label} must be {maxLength} characters or fewer.");
        Finance.Require(!normalized.Contains('\r') && !normalized.Contains('\n'), $"{label} cannot contain line breaks.");
        return normalized;
    }

    private static string? OptionalText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return RequiredText(value, maxLength, label);
    }

    private static string NormalizeActor(string? actor) =>
        RequiredText(actor, 254, "Actor");

    private static string NormalizeSource(string? source) =>
        RequiredText(source, 80, "Source");

    private static string NormalizeRequiredReason(string? reason, string message)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(reason), message);
        return RequiredText(reason, 2000, "Reason");
    }

    private static string? NormalizeOptionalReason(string? reason) =>
        OptionalText(reason, 2000, "Reason");

    private static void EnsureVersion(long actual, long expected, string message) =>
        Finance.Require(actual == expected, message);

    private async Task LockRowAsync(
        string table,
        int id,
        CancellationToken cancellationToken,
        string notFoundMessage)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Id\" FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        Finance.Require(await command.ExecuteScalarAsync(cancellationToken) is not null, notFoundMessage);
    }

    private async Task<T> InSerializableTransactionAsync<T>(
        Func<Task<T>> action,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(conflictMessage, ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(conflictMessage, ex);
        }
        catch (Exception ex) when (FinancialTransaction.IsSerializationConflict(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(conflictMessage, ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record NormalizedParticipant(
        WhatsAppParticipantKind ParticipantKind,
        int? ContactId,
        int? ContactWhatsAppAddressId,
        int? BusinessPartyId,
        int? ManagerId,
        string? AppUserId,
        string? ProviderParticipantKey,
        string? NormalizedE164,
        string? DisplayNameSnapshot);

    private sealed record ScopeSnapshot(
        bool IsActive,
        int ApprovedAuthorizationVersion,
        DateTime ApprovedAt,
        string ApprovedByActor,
        string? ApprovalReason,
        DateTime? RevokedAt,
        string? RevokedByActor,
        string? RevocationReason)
    {
        public static ScopeSnapshot From(WhatsAppConversationEngagementScope scope) => new(
            scope.IsActive,
            scope.ApprovedAuthorizationVersion,
            scope.ApprovedAt,
            scope.ApprovedByActor,
            scope.ApprovalReason,
            scope.RevokedAt,
            scope.RevokedByActor,
            scope.RevocationReason);
    }

    private static WhatsAppConversationDetailsReadModel ToReadModel(WhatsAppConversation conversation) => new(
        conversation.Id,
        conversation.Kind,
        conversation.Status,
        conversation.ProviderName,
        conversation.BusinessEndpointKey,
        conversation.ProviderAccountReference,
        conversation.ProviderConversationKey,
        conversation.DirectContactWhatsAppAddressId,
        conversation.DirectContactWhatsAppAddress?.NormalizedE164,
        conversation.DirectContactWhatsAppAddress?.IsActive,
        conversation.AuthorizationVersion,
        conversation.CreatedAt,
        conversation.UpdatedAt,
        conversation.Version,
        conversation.Participants
            .OrderBy(x => x.Id)
            .Select(ToReadModel)
            .ToArray(),
        conversation.EngagementScopes
            .OrderBy(x => x.Id)
            .Select(x => ToReadModel(x, conversation.AuthorizationVersion))
            .ToArray(),
        conversation.History
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Select(ToReadModel)
            .ToArray());

    private static WhatsAppConversationParticipantReadModel ToReadModel(WhatsAppConversationParticipant participant) => new(
        participant.Id,
        participant.WhatsAppConversationId,
        participant.ParticipantKind,
        participant.ContactId,
        participant.Contact?.Name,
        participant.ContactWhatsAppAddressId,
        participant.ContactWhatsAppAddress?.NormalizedE164,
        participant.BusinessPartyId,
        participant.BusinessParty?.Name,
        participant.ManagerId,
        participant.Manager?.Name,
        participant.AppUserId,
        participant.AppUser?.UserName,
        participant.ProviderParticipantKey,
        participant.NormalizedE164,
        participant.DisplayNameSnapshot,
        participant.IsActive,
        participant.JoinedAt,
        participant.LeftAt,
        participant.Version,
        participant.History
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Select(ToReadModel)
            .ToArray());

    private static WhatsAppConversationEngagementScopeReadModel ToReadModel(
        WhatsAppConversationEngagementScope scope,
        int currentAuthorizationVersion)
    {
        var isCurrent = scope.ApprovedAuthorizationVersion == currentAuthorizationVersion;
        return new(
            scope.Id,
            scope.WhatsAppConversationId,
            scope.EngagementId,
            scope.Engagement?.Customer?.Name,
            scope.Engagement?.Service?.Name,
            scope.Engagement?.Status ?? EngagementStatus.Closed,
            scope.IsActive,
            scope.ApprovedAuthorizationVersion,
            currentAuthorizationVersion,
            isCurrent ? WhatsAppScopeAuthorizationState.Current : WhatsAppScopeAuthorizationState.Stale,
            isCurrent,
            scope.ApprovedAt,
            scope.ApprovedByActor,
            scope.ApprovalReason,
            scope.RevokedAt,
            scope.RevokedByActor,
            scope.RevocationReason,
            scope.Version,
            scope.History
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.Id)
                .Select(ToReadModel)
                .ToArray());
    }

    private static WhatsAppConversationHistoryReadModel ToReadModel(WhatsAppConversationHistory history) => new(
        history.Id,
        history.PreviousStatus,
        history.NewStatus,
        history.PreviousAuthorizationVersion,
        history.NewAuthorizationVersion,
        history.Action,
        history.Reason,
        history.Actor,
        history.Source,
        history.OccurredAt,
        history.Version);

    private static WhatsAppConversationParticipantHistoryReadModel ToReadModel(WhatsAppConversationParticipantHistory history) => new(
        history.Id,
        history.PreviousIsActive,
        history.NewIsActive,
        history.PreviousJoinedAt,
        history.NewJoinedAt,
        history.PreviousLeftAt,
        history.NewLeftAt,
        history.Action,
        history.Reason,
        history.Actor,
        history.Source,
        history.OccurredAt,
        history.Version);

    private static WhatsAppConversationEngagementScopeHistoryReadModel ToReadModel(WhatsAppConversationEngagementScopeHistory history) => new(
        history.Id,
        history.PreviousIsActive,
        history.NewIsActive,
        history.PreviousApprovedAuthorizationVersion,
        history.NewApprovedAuthorizationVersion,
        history.PreviousApprovedAt,
        history.NewApprovedAt,
        history.PreviousApprovedByActor,
        history.NewApprovedByActor,
        history.PreviousApprovalReason,
        history.NewApprovalReason,
        history.PreviousRevokedAt,
        history.NewRevokedAt,
        history.PreviousRevokedByActor,
        history.NewRevokedByActor,
        history.PreviousRevocationReason,
        history.NewRevocationReason,
        history.Action,
        history.Reason,
        history.Actor,
        history.Source,
        history.OccurredAt,
        history.Version);
}
