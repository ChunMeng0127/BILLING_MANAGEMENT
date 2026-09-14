using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services.WhatsApp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ModelContentKind = BillingControl.Models.WhatsAppOutboundContentKind;
using ProviderDestinationKind = BillingControl.Services.WhatsApp.WhatsAppDestinationKind;

namespace BillingControl.Services;

/// <summary>
/// The complete input to the Phase 10B read-only outbound preview. The
/// selected request revisions and the exact provider-neutral content are
/// captured in the protected preview token; callers must not reconstruct the
/// outbound request from display fields at queue time.
/// </summary>
public sealed record WhatsAppOutboundPreviewInput(
    int ContactId,
    int WhatsAppConversationId,
    IReadOnlyCollection<DocumentBatchRequestSelection> Requests,
    WhatsAppOutboundContent Content,
    string Actor,
    string Source);

/// <summary>
/// Optional client-side echoes accepted by Queue only for equality checking.
/// The protected PreviewToken remains the sole source of authority.
/// </summary>
public sealed record WhatsAppOutboundQueueInput(
    string PreviewToken,
    WhatsAppOutboundContent? Content = null,
    string? Actor = null,
    string? Source = null);

public sealed record WhatsAppOutboundContactReadModel(
    int Id,
    string Name,
    bool IsActive,
    long Version);

public sealed record WhatsAppOutboundConversationReadModel(
    int Id,
    WhatsAppConversationKind Kind,
    WhatsAppConversationStatus Status,
    string ProviderName,
    string BusinessEndpointKey,
    string? ProviderAccountReference,
    string ProviderConversationKey,
    int? DirectContactWhatsAppAddressId,
    int AuthorizationVersion,
    long Version);

public sealed record WhatsAppOutboundDestinationReadModel(
    WhatsAppDestinationKind Kind,
    string ProviderDestinationKey,
    string? NormalizedE164,
    string? ProviderRecipientKey,
    int? ConsentAddressId,
    long? ConsentAddressVersion,
    ContactWhatsAppConsentState? ConsentState);

public sealed record WhatsAppOutboundParticipantReadModel(
    int Id,
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
    long Version);

public sealed record WhatsAppOutboundEngagementScopeReadModel(
    int Id,
    int EngagementId,
    int CustomerId,
    string CustomerName,
    int ServiceId,
    string ServiceName,
    EngagementStatus EngagementStatus,
    bool IsActive,
    int ApprovedAuthorizationVersion,
    DateTime ApprovedAt,
    string ApprovedByActor,
    string? ApprovalReason,
    DateTime? RevokedAt,
    string? RevokedByActor,
    string? RevocationReason,
    long Version);

public sealed record WhatsAppOutboundItemReadModel(
    int DocumentRequestItemId,
    long Version,
    string RequirementKey,
    string RequirementName,
    string? RequirementDescription,
    bool IsRequired,
    DocumentRequirementWave Wave,
    int DisplayOrder,
    DocumentRequestItemStatus Status);

public sealed record WhatsAppOutboundRequestReadModel(
    int DocumentRequestId,
    int Revision,
    long Version,
    int WorkItemId,
    int EngagementId,
    long EngagementVersion,
    int CustomerId,
    long CustomerVersion,
    string CustomerName,
    int ServiceId,
    long ServiceVersion,
    string ServiceName,
    int ContactCustomerLinkId,
    long ContactCustomerLinkVersion,
    IReadOnlyList<WhatsAppOutboundItemReadModel> UnresolvedItems);

public sealed record WhatsAppOutboundPreviewReadModel(
    string PreviewToken,
    Guid PreviewId,
    DateTime PreviewedAt,
    WhatsAppOutboundContactReadModel Contact,
    WhatsAppOutboundConversationReadModel Conversation,
    WhatsAppOutboundDestinationReadModel Destination,
    string ParticipantSetHash,
    IReadOnlyList<WhatsAppOutboundParticipantReadModel> ActiveParticipants,
    IReadOnlyList<WhatsAppOutboundEngagementScopeReadModel> EngagementScopes,
    IReadOnlyList<WhatsAppOutboundRequestReadModel> Requests,
    WhatsAppOutboundContent Content,
    WhatsAppOutboundRequest ProposedRequest,
    string Actor,
    string Source,
    WhatsAppProviderCapabilities? ProviderCapabilities);

public sealed record WhatsAppOutboundQueueResult(
    int DocumentRequestBatchId,
    int WhatsAppOutboundMessageId,
    string LogicalMessageKey,
    string CorrelationId,
    DocumentRequestBatchStatus BatchStatus,
    WhatsAppOutboundMessageState MessageState,
    bool AlreadyQueued);

/// <summary>
/// Owns the Phase 10B Preview -> authoritative revalidation -> Queue
/// orchestration. Preview is read-only. Queue is one serializable PostgreSQL
/// transaction and never calls IWhatsAppProvider.SendAsync.
/// </summary>
public sealed class WhatsAppOutboundQueueService
{
    private const int TokenVersion = 1;
    private const string ProtectorPurpose = "BillingControl.WhatsAppOutboundQueue.Phase10B.Preview.v1";
    private const string InvalidTokenMessage = "The WhatsApp Preview token is invalid. Generate a new Preview.";
    private const string StalePreviewMessage = "The WhatsApp Preview is stale or no longer authorized. Generate a new Preview before queueing.";
    private const string QueueConflictMessage = "Another WhatsApp outbound queue operation completed first. Refresh and retry.";
    private const string ContentChangedMessage = "The outbound content differs from the Preview. Generate a new Preview.";
    private const string ActorChangedMessage = "The authenticated actor differs from the Preview. Generate a new Preview.";

    private static readonly JsonSerializerOptions TokenJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AppDbContext db;
    private readonly DocumentRequestBatchService requestBatchService;
    private readonly BusinessClock clock;
    private readonly IDataProtector previewProtector;
    private readonly IWhatsAppProvider? provider;

    public WhatsAppOutboundQueueService(
        AppDbContext db,
        DocumentRequestBatchService requestBatchService,
        IDataProtectionProvider dataProtectionProvider,
        BusinessClock clock,
        IWhatsAppProvider? provider = null)
    {
        this.db = db ?? throw new ArgumentNullException(nameof(db));
        this.requestBatchService = requestBatchService ?? throw new ArgumentNullException(nameof(requestBatchService));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        previewProtector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        this.provider = provider;
    }

    /// <summary>
    /// Convenience constructor for direct service tests. Production uses the
    /// DI constructor so the application-wide Data Protection key ring and the
    /// configured provider capability reader are shared across requests.
    /// </summary>
    public WhatsAppOutboundQueueService(AppDbContext db, BusinessClock clock)
        : this(db, new DocumentRequestBatchService(db), new EphemeralDataProtectionProvider(), clock)
    {
    }

    public WhatsAppOutboundQueueService(
        AppDbContext db,
        DocumentRequestBatchService requestBatchService,
        BusinessClock clock)
        : this(db, requestBatchService, new EphemeralDataProtectionProvider(), clock)
    {
    }

    public async Task<WhatsAppOutboundPreviewReadModel> PreviewAsync(
        WhatsAppOutboundPreviewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var selections = NormalizeSelections(input.Requests);
        var actor = RequiredText(input.Actor, 254, "Actor");
        var source = RequiredText(input.Source, 80, "Source");
        var content = NormalizeContent(input.Content);

        Finance.Require(input.ContactId > 0, "A valid Contact is required.");
        Finance.Require(input.WhatsAppConversationId > 0, "A valid WhatsApp conversation is required.");

        var facts = await InSerializableReadAsync(async () =>
        {
            // MetaWhatsAppProvider.GetCapabilitiesAsync is a local configuration
            // read. It must remain network-free in this phase; SendAsync is never
            // reachable from the Preview/Queue orchestration.
            var capabilities = await ReadProviderCapabilitiesAsync(
                input.WhatsAppConversationId,
                cancellationToken);
            var eligible = await requestBatchService.PreviewBatchAsync(
                input.ContactId,
                selections,
                cancellationToken);
            return await LoadAndValidateFactsAsync(
                input.ContactId,
                input.WhatsAppConversationId,
                selections,
                eligible.Requests,
                content,
                capabilities,
                cancellationToken);
        }, cancellationToken);

        var previewId = Guid.NewGuid();
        var previewedAt = clock.UtcNow;
        var logicalMessageKey = $"phase10b-preview-{previewId:N}";
        var correlationId = $"phase10b-correlation-{previewId:N}";
        var tokenPayload = CreateTokenPayload(
            previewId,
            previewedAt,
            logicalMessageKey,
            correlationId,
            actor,
            source,
            facts);
        var token = Protect(tokenPayload);

        return ToPreviewReadModel(
            token,
            previewId,
            previewedAt,
            logicalMessageKey,
            correlationId,
            actor,
            source,
            facts);
    }

    public Task<WhatsAppOutboundPreviewReadModel> PreviewAsync(
        int contactId,
        int conversationId,
        IEnumerable<DocumentBatchRequestSelection> selections,
        WhatsAppOutboundContent content,
        string actor,
        string source,
        CancellationToken cancellationToken = default) =>
        PreviewAsync(new(
            contactId,
            conversationId,
            selections?.ToArray() ?? throw new ArgumentNullException(nameof(selections)),
            content,
            actor,
            source), cancellationToken);

    public Task<WhatsAppOutboundPreviewReadModel> Preview(
        WhatsAppOutboundPreviewInput input,
        CancellationToken cancellationToken = default) => PreviewAsync(input, cancellationToken);

    public Task<WhatsAppOutboundQueueResult> QueueAsync(
        string previewToken,
        CancellationToken cancellationToken = default) =>
        QueueAsync(new WhatsAppOutboundQueueInput(previewToken), cancellationToken);

    public async Task<WhatsAppOutboundQueueResult> QueueAsync(
        WhatsAppOutboundQueueInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var payload = Unprotect(input.PreviewToken);

        if (input.Content is not null && !ContentMatches(payload.Content, NormalizeContent(input.Content)))
            throw new BusinessException(ContentChangedMessage);
        if (input.Actor is not null && !string.Equals(payload.Actor, RequiredText(input.Actor, 254, "Actor"), StringComparison.Ordinal))
            throw new BusinessException(ActorChangedMessage);
        if (input.Source is not null && !string.Equals(payload.Source, RequiredText(input.Source, 80, "Source"), StringComparison.Ordinal))
            throw new BusinessException(ActorChangedMessage);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await InSerializableTransactionAsync(
                    () => QueueInsideTransactionAsync(payload, cancellationToken),
                    cancellationToken);
            }
            catch (BusinessException ex) when (attempt < 2 && IsQueueConflict(ex))
            {
                db.ChangeTracker.Clear();
                await Task.Yield();
            }
        }

        throw new BusinessException(QueueConflictMessage);
    }

    /// <summary>
    /// Revalidates a durable Phase 10B message immediately before a manual
    /// provider send and reconstructs the provider request from the persisted
    /// message/snapshot only. The caller owns the surrounding transaction and
    /// row locks; this method deliberately does not begin or commit a
    /// transaction and never calls SendAsync.
    /// </summary>
    internal async Task<WhatsAppOutboundRequest> RevalidateQueuedMessageAndBuildRequestAsync(
        WhatsAppOutboundMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var snapshot = message.WhatsAppOutboundBatchSnapshot;
        Finance.Require(snapshot is not null, StalePreviewMessage);
        Finance.Require(snapshot!.Id == message.WhatsAppOutboundBatchSnapshotId &&
                        snapshot.DocumentRequestBatchId == message.DocumentRequestBatchId &&
                        snapshot.Requests.Count > 0,
            StalePreviewMessage);

        var selections = snapshot.Requests
            .OrderBy(x => x.DocumentRequestId)
            .Select(x => new DocumentBatchRequestSelection(x.DocumentRequestId, x.RequestRevision))
            .ToArray();
        var content = ContentFromQueuedMessage(message);
        var capabilities = await ReadProviderCapabilitiesAsync(
            snapshot.WhatsAppConversationId,
            cancellationToken);
        var eligible = await requestBatchService.PreviewBatchAsync(
            snapshot.ContactId,
            selections,
            cancellationToken);
        var currentFacts = await LoadAndValidateFactsAsync(
            snapshot.ContactId,
            snapshot.WhatsAppConversationId,
            selections,
            eligible.Requests,
            content,
            capabilities,
            cancellationToken);

        EnsureQueuedSnapshotMatches(message, snapshot, currentFacts);

        var account = new WhatsAppProviderAccountBinding(
            message.ProviderName,
            message.BusinessEndpointKey,
            message.ProviderAccountReference);
        var destination = message.DestinationKind == WhatsAppOutboundDestinationKind.Direct
            ? WhatsAppDestination.Direct(
                message.ProviderDestinationKey,
                message.NormalizedE164,
                message.ProviderRecipientKey)
            : WhatsAppDestination.Group(message.ProviderDestinationKey);
        return new WhatsAppOutboundRequest(
            message.LogicalMessageKey,
            account,
            destination,
            ToProviderContent(content),
            message.CorrelationId);
    }

    private async Task<WhatsAppOutboundQueueResult> QueueInsideTransactionAsync(
        PreviewTokenPayload payload,
        CancellationToken cancellationToken)
    {
        await LockRowAsync("WhatsAppConversations", payload.Conversation.Id, cancellationToken,
            StalePreviewMessage);

        var existing = await db.WhatsAppOutboundMessages
            .AsNoTracking()
            .Include(x => x.WhatsAppOutboundBatchSnapshot)
            .Include(x => x.DocumentRequestBatch)
            .SingleOrDefaultAsync(x => x.LogicalMessageKey == payload.LogicalMessageKey, cancellationToken);
        if (existing is not null)
        {
            EnsureExistingMessageMatches(payload, existing);
            return new(
                existing.DocumentRequestBatchId,
                existing.Id,
                existing.LogicalMessageKey,
                existing.CorrelationId,
                existing.DocumentRequestBatch.Status,
                existing.State,
                AlreadyQueued: true);
        }

        var capabilities = await ReadProviderCapabilitiesAsync(payload.Conversation.Id, cancellationToken);
        EnsureCapabilitySnapshot(payload.Capabilities, capabilities);

        await LockRowAsync("Contacts", payload.Contact.Id, cancellationToken, StalePreviewMessage);
        foreach (var request in payload.Requests.OrderBy(x => x.DocumentRequestId))
        {
            await LockRowAsync("DocumentRequests", request.DocumentRequestId, cancellationToken, StalePreviewMessage);
            foreach (var item in request.UnresolvedItems.OrderBy(x => x.DocumentRequestItemId))
                await LockRowAsync("DocumentRequestItems", item.DocumentRequestItemId, cancellationToken, StalePreviewMessage);
        }

        foreach (var request in payload.Requests.OrderBy(x => x.DocumentRequestId))
            await LockRowAsync("ContactCustomerLinks", request.ContactCustomerLinkId, cancellationToken, StalePreviewMessage);
        foreach (var request in payload.Requests.OrderBy(x => x.EngagementId))
        {
            await LockRowAsync("Engagements", request.EngagementId, cancellationToken, StalePreviewMessage);
            await LockRowAsync("Customers", request.CustomerId, cancellationToken, StalePreviewMessage);
            await LockRowAsync("Services", request.ServiceId, cancellationToken, StalePreviewMessage);
        }

        if (payload.Destination.ConsentAddressId is int consentAddressId)
            await LockRowAsync("ContactWhatsAppAddresses", consentAddressId, cancellationToken, StalePreviewMessage);
        foreach (var participant in payload.ActiveParticipants.OrderBy(x => x.Id))
            await LockRowAsync("WhatsAppConversationParticipants", participant.Id, cancellationToken, StalePreviewMessage);
        foreach (var scope in payload.EngagementScopes.OrderBy(x => x.Id))
            await LockRowAsync("WhatsAppConversationEngagementScopes", scope.Id, cancellationToken, StalePreviewMessage);

        var eligible = await requestBatchService.PreviewBatchAsync(
            payload.Contact.Id,
            payload.Requests
                .OrderBy(x => x.DocumentRequestId)
                .Select(x => new DocumentBatchRequestSelection(x.DocumentRequestId, x.Revision))
                .ToArray(),
            cancellationToken);
        var currentFacts = await LoadAndValidateFactsAsync(
            payload.Contact.Id,
            payload.Conversation.Id,
            payload.Requests
                .OrderBy(x => x.DocumentRequestId)
                .Select(x => new DocumentBatchRequestSelection(x.DocumentRequestId, x.Revision))
                .ToArray(),
            eligible.Requests,
            payload.Content,
            capabilities,
            cancellationToken);
        EnsureFactsMatch(payload, currentFacts);

        var now = clock.UtcNow;
        var batch = new DocumentRequestBatch { Status = DocumentRequestBatchStatus.Draft };
        foreach (var request in currentFacts.Requests)
            batch.Members.Add(new DocumentRequestBatchMember { DocumentRequestId = request.DocumentRequestId, IsActive = true });
        db.DocumentRequestBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        batch.Status = DocumentRequestBatchStatus.Ready;
        batch.StatusHistory.Add(new DocumentRequestBatchStatusHistory
        {
            PreviousStatus = DocumentRequestBatchStatus.Draft,
            NewStatus = DocumentRequestBatchStatus.Ready,
            Action = "PreviewValidated",
            Reason = "Phase 10B authoritative preview revalidation passed.",
            Actor = payload.Actor,
            Source = payload.Source,
            CorrelationId = payload.CorrelationId,
            OccurredAt = now
        });

        var snapshot = new WhatsAppOutboundBatchSnapshot
        {
            DocumentRequestBatch = batch,
            ContactId = currentFacts.Contact.Id,
            WhatsAppConversationId = currentFacts.Conversation.Id,
            ConversationAuthorizationVersion = currentFacts.Conversation.AuthorizationVersion,
            ParticipantSetHash = currentFacts.ParticipantSetHash,
            QueuedAt = now,
            QueuedByActor = payload.Actor,
            CorrelationId = payload.CorrelationId
        };
        snapshot.Requests.AddRange(currentFacts.Requests.Select(request => new WhatsAppOutboundRequestSnapshot
        {
            DocumentRequestId = request.DocumentRequestId,
            RequestRevision = request.Revision,
            RequestVersion = request.Version,
            EngagementId = request.EngagementId,
            CustomerId = request.CustomerId,
            CustomerNameSnapshot = request.CustomerName,
            ServiceId = request.ServiceId,
            ServiceNameSnapshot = request.ServiceName
        }));
        snapshot.Items.AddRange(currentFacts.Requests.SelectMany(request => request.UnresolvedItems.Select(item => new WhatsAppOutboundItemSnapshot
        {
            DocumentRequestId = request.DocumentRequestId,
            DocumentRequestItemId = item.DocumentRequestItemId,
            RequestRevision = request.Revision,
            RequirementNameSnapshot = item.RequirementName,
            IsRequired = item.IsRequired,
            DisplayOrder = item.DisplayOrder,
            ItemStatusSnapshot = item.Status
        })));
        snapshot.Participants.AddRange(currentFacts.ActiveParticipants.Select(ToParticipantSnapshot));
        snapshot.EngagementScopes.AddRange(currentFacts.EngagementScopes.Select(scope => new WhatsAppOutboundEngagementScopeSnapshot
        {
            WhatsAppConversationEngagementScopeId = scope.Id,
            EngagementId = scope.EngagementId,
            ApprovedAuthorizationVersion = scope.ApprovedAuthorizationVersion
        }));

        var message = new WhatsAppOutboundMessage
        {
            DocumentRequestBatch = batch,
            WhatsAppOutboundBatchSnapshot = snapshot,
            LogicalMessageKey = payload.LogicalMessageKey,
            CorrelationId = payload.CorrelationId,
            ProviderName = currentFacts.Conversation.ProviderName,
            BusinessEndpointKey = currentFacts.Conversation.BusinessEndpointKey,
            ProviderAccountReference = currentFacts.Conversation.ProviderAccountReference,
            DestinationKind = currentFacts.Destination.Kind == ProviderDestinationKind.Direct
                ? WhatsAppOutboundDestinationKind.Direct
                : WhatsAppOutboundDestinationKind.Group,
            ProviderDestinationKey = currentFacts.Destination.ProviderDestinationKey,
            NormalizedE164 = currentFacts.Destination.NormalizedE164,
            ProviderRecipientKey = currentFacts.Destination.ProviderRecipientKey,
            ContentKind = currentFacts.Content.Kind,
            TextBody = currentFacts.Content.Text,
            TemplateName = currentFacts.Content.TemplateName,
            TemplateLanguage = currentFacts.Content.TemplateLanguage,
            TemplateParametersSnapshot = currentFacts.Content.TemplateParametersSnapshot,
            State = WhatsAppOutboundMessageState.Queued,
            AttemptCount = 0
        };

        batch.Status = DocumentRequestBatchStatus.Queued;
        batch.StatusHistory.Add(new DocumentRequestBatchStatusHistory
        {
            PreviousStatus = DocumentRequestBatchStatus.Ready,
            NewStatus = DocumentRequestBatchStatus.Queued,
            Action = "Queued",
            Reason = "Phase 10B immutable outbound snapshot committed to the durable queue.",
            Actor = payload.Actor,
            Source = payload.Source,
            CorrelationId = payload.CorrelationId,
            OccurredAt = now
        });
        db.Add(snapshot);
        db.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        return new(
            batch.Id,
            message.Id,
            message.LogicalMessageKey,
            message.CorrelationId,
            batch.Status,
            message.State,
            AlreadyQueued: false);
    }

    private async Task<PreviewFacts> LoadAndValidateFactsAsync(
        int contactId,
        int conversationId,
        IReadOnlyCollection<DocumentBatchRequestSelection> selections,
        IReadOnlyList<DocumentBatchPreviewRequestReadModel> eligible,
        ContentFact content,
        ProviderCapabilitiesFact? capabilities,
        CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
            ?? throw new BusinessException("The Contact was not found.");
        Finance.Require(contact.IsActive, "The Contact is inactive and cannot queue document requests.");

        var conversation = await db.WhatsAppConversations
            .AsNoTracking()
            .Include(x => x.DirectContactWhatsAppAddress)
            .Include(x => x.Participants).ThenInclude(x => x.Contact)
            .Include(x => x.Participants).ThenInclude(x => x.ContactWhatsAppAddress)
            .Include(x => x.Participants).ThenInclude(x => x.BusinessParty)
            .Include(x => x.Participants).ThenInclude(x => x.Manager)
            .Include(x => x.Participants).ThenInclude(x => x.AppUser)
            .Include(x => x.EngagementScopes).ThenInclude(x => x.Engagement).ThenInclude(x => x.Customer)
            .Include(x => x.EngagementScopes).ThenInclude(x => x.Engagement).ThenInclude(x => x.Service)
            .SingleOrDefaultAsync(x => x.Id == conversationId, cancellationToken)
            ?? throw new BusinessException("The WhatsApp conversation was not found.");

        Finance.Require(conversation.Status == WhatsAppConversationStatus.Active,
            "The WhatsApp conversation is not active and cannot queue document requests.");
        Finance.Require(conversation.AuthorizationVersion > 0,
            "The WhatsApp conversation authorization version is invalid.");

        var requestIds = selections.Select(x => x.DocumentRequestId).ToArray();
        var requests = await db.DocumentRequests
            .AsNoTracking()
            .Where(x => requestIds.Contains(x.Id))
            .Include(x => x.Items)
            .Include(x => x.DocumentRequirementTemplate)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Engagement).ThenInclude(x => x.Customer)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Engagement).ThenInclude(x => x.Service)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        Finance.Require(requests.Count == selections.Count, StalePreviewMessage);
        Finance.Require(eligible.Count == selections.Count &&
                        eligible.Select(x => x.DocumentRequestId).OrderBy(x => x).SequenceEqual(requestIds.OrderBy(x => x)),
            StalePreviewMessage);
        var eligibleByRequestId = eligible.ToDictionary(x => x.DocumentRequestId);

        var customerIds = requests
            .Select(x => x.WorkItem?.BillingRecord?.Engagement?.CustomerId ?? 0)
            .Distinct()
            .ToArray();
        Finance.Require(customerIds.All(x => x > 0), StalePreviewMessage);
        var links = await db.ContactCustomerLinks
            .AsNoTracking()
            .Where(x => x.ContactId == contactId && customerIds.Contains(x.CustomerId))
            .OrderBy(x => x.CustomerId)
            .ToListAsync(cancellationToken);
        Finance.Require(links.Count == customerIds.Length && links.All(x => x.IsActive), StalePreviewMessage);

        var linkByCustomer = links.ToDictionary(x => x.CustomerId);
        var requestFacts = new List<RequestFact>(requests.Count);
        foreach (var request in requests.OrderBy(x => x.Id))
        {
            var engagement = request.WorkItem?.BillingRecord?.Engagement;
            Finance.Require(engagement is not null, StalePreviewMessage);
            Finance.Require(engagement!.Status == EngagementStatus.Active, StalePreviewMessage);
            Finance.Require(request.DocumentRequirementTemplate?.ServiceId == engagement.ServiceId, StalePreviewMessage);
            Finance.Require(request.Status == DocumentRequestStatus.ReadyToSend, StalePreviewMessage);
            Finance.Require(request.Revision > 0 && request.Version > 0, StalePreviewMessage);

            var laterRevisionExists = await db.DocumentRequests.AsNoTracking().AnyAsync(
                x => x.WorkItemId == request.WorkItemId && x.Revision > request.Revision,
                cancellationToken);
            Finance.Require(!laterRevisionExists, StalePreviewMessage);
            var selected = selections.SingleOrDefault(x => x.DocumentRequestId == request.Id);
            Finance.Require(selected is not null && selected.Revision == request.Revision, StalePreviewMessage);
            Finance.Require(eligibleByRequestId[request.Id].Revision == request.Revision &&
                            eligibleByRequestId[request.Id].RequestVersion == request.Version,
                StalePreviewMessage);

            var unresolvedItems = request.Items
                .Where(IsUnresolved)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.RequirementName, StringComparer.Ordinal)
                .ThenBy(x => x.Id)
                .ToArray();
            Finance.Require(unresolvedItems.Length > 0, StalePreviewMessage);

            var link = linkByCustomer[engagement.CustomerId];
            requestFacts.Add(new RequestFact(
                request.Id,
                request.Revision,
                request.Version,
                request.WorkItemId,
                engagement.Id,
                engagement.Version,
                engagement.CustomerId,
                engagement.Customer.Version,
                engagement.Customer.Name,
                engagement.ServiceId,
                engagement.Service.Version,
                engagement.Service.Name,
                link.Id,
                link.Version,
                unresolvedItems.Select(item => new ItemFact(
                    item.Id,
                    item.Version,
                    item.RequirementKey,
                    item.RequirementName,
                    item.RequirementDescription,
                    item.IsRequired,
                    item.Wave,
                    item.DisplayOrder,
                    item.Status)).ToArray()));
        }

        var activeParticipants = conversation.Participants
            .Where(x => x.IsActive)
            .Select(ToParticipantFact)
            .OrderBy(x => x, ParticipantFactComparer.Instance)
            .ToArray();

        var destination = ResolveDestination(contact, conversation, activeParticipants);
        ValidateProviderCapabilities(destination, content, capabilities);

        if (conversation.Kind == WhatsAppConversationKind.Group)
        {
            // The frozen Phase 9 model authorizes a Group through its known
            // active participant set, current provider capability and explicit
            // Engagement scopes. It has no separate Group-consent record, so do
            // not invent a Contact-address consent policy for the Group key.
            Finance.Require(activeParticipants.Length > 0,
                "A Group WhatsApp conversation must have an active participant set.");
            Finance.Require(!activeParticipants.Any(x => x.ParticipantKind == WhatsAppParticipantKind.UnknownExternal),
                "An active UnknownExternal participant blocks Group outbound queueing.");
            var batchingContact = activeParticipants.SingleOrDefault(x =>
                x.ParticipantKind == WhatsAppParticipantKind.Contact && x.ContactId == contactId);
            Finance.Require(batchingContact is not null,
                "The batching Contact must be explicitly represented by the current Group participant facts.");
        }

        var selectedEngagementIds = requestFacts.Select(x => x.EngagementId).Distinct().OrderBy(x => x).ToArray();
        var scopes = selectedEngagementIds.Select(engagementId =>
        {
            var scope = conversation.EngagementScopes.SingleOrDefault(x => x.EngagementId == engagementId);
            Finance.Require(scope is not null && scope.IsActive, StalePreviewMessage);
            Finance.Require(scope!.ApprovedAuthorizationVersion == conversation.AuthorizationVersion,
                "An Engagement authorization scope is stale. Reapprove the scope before queueing.");
            Finance.Require(scope.Engagement.Status == EngagementStatus.Active, StalePreviewMessage);
            return new ScopeFact(
                scope.Id,
                scope.EngagementId,
                scope.Engagement.CustomerId,
                scope.Engagement.Customer.Name,
                scope.Engagement.ServiceId,
                scope.Engagement.Service.Name,
                scope.Engagement.Status,
                scope.IsActive,
                scope.ApprovedAuthorizationVersion,
                scope.ApprovedAt,
                scope.ApprovedByActor,
                scope.ApprovalReason,
                scope.RevokedAt,
                scope.RevokedByActor,
                scope.RevocationReason,
                scope.Version);
        }).ToArray();

        var conversationFact = new ConversationFact(
            conversation.Id,
            conversation.Kind,
            conversation.Status,
            conversation.ProviderName,
            conversation.BusinessEndpointKey,
            conversation.ProviderAccountReference,
            conversation.ProviderConversationKey,
            conversation.DirectContactWhatsAppAddressId,
            conversation.AuthorizationVersion,
            conversation.Version);
        var contactFact = new ContactFact(contact.Id, contact.Name, contact.IsActive, contact.Version);
        var participantSetHash = ComputeParticipantSetHash(activeParticipants.Select(ToReadModel));

        return new PreviewFacts(
            contactFact,
            conversationFact,
            destination,
            participantSetHash,
            activeParticipants,
            scopes,
            requestFacts.ToArray(),
            content,
            capabilities);
    }

    /// <summary>
    /// Computes SHA-256 over a length-prefixed canonical participant
    /// representation. The order is independent of insertion/query order and
    /// the result is always 64 lowercase hexadecimal characters.
    /// </summary>
    public static string ComputeParticipantSetHash(
        IEnumerable<WhatsAppOutboundParticipantReadModel> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var ordered = participants
            .Where(x => x.IsActive)
            .OrderBy(x => ToParticipantFact(x), ParticipantFactComparer.Instance)
            .ToArray();
        var canonical = new StringBuilder();
        foreach (var participant in ordered)
        {
            var fact = ToParticipantFact(participant);
            AppendInt(canonical, fact.Id);
            AppendInt(canonical, (int)fact.ParticipantKind);
            AppendNullableInt(canonical, fact.ContactId);
            AppendNullableInt(canonical, fact.ContactWhatsAppAddressId);
            AppendNullableInt(canonical, fact.BusinessPartyId);
            AppendNullableInt(canonical, fact.ManagerId);
            AppendNullableString(canonical, fact.AppUserId);
            AppendNullableString(canonical, fact.ProviderParticipantKey);
            AppendNullableString(canonical, fact.NormalizedE164);
            AppendNullableString(canonical, fact.DisplayNameSnapshot);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private async Task<ProviderCapabilitiesFact?> ReadProviderCapabilitiesAsync(
        int conversationId,
        CancellationToken cancellationToken)
    {
        if (provider is null)
            return null;

        var bindingValues = await db.WhatsAppConversations
            .AsNoTracking()
            .Where(x => x.Id == conversationId)
            .Select(x => new
            {
                x.ProviderName,
                x.BusinessEndpointKey,
                x.ProviderAccountReference
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (bindingValues is null)
            throw new BusinessException("The WhatsApp conversation was not found.");
        var binding = new WhatsAppProviderAccountBinding(
            bindingValues.ProviderName,
            bindingValues.BusinessEndpointKey,
            bindingValues.ProviderAccountReference);
        var capabilities = await provider.GetCapabilitiesAsync(binding, cancellationToken);
        ArgumentNullException.ThrowIfNull(capabilities);
        return new ProviderCapabilitiesFact(
            capabilities.SupportsDirectMessaging,
            capabilities.SupportsGroupMessaging,
            capabilities.SupportsText,
            capabilities.SupportsTemplateMessaging,
            capabilities.SupportsMedia,
            capabilities.SupportsDocuments);
    }

    private static void ValidateProviderCapabilities(
        DestinationFact destination,
        ContentFact content,
        ProviderCapabilitiesFact? capabilities)
    {
        if (capabilities is null)
            return;
        if (destination.Kind == ProviderDestinationKind.Direct)
            Finance.Require(capabilities.SupportsDirectMessaging,
                "The configured WhatsApp provider does not currently support Direct messaging.");
        else
            Finance.Require(capabilities.SupportsGroupMessaging,
                "The configured WhatsApp provider does not currently support Group messaging.");
        if (content.Kind == ModelContentKind.Text)
            Finance.Require(capabilities.SupportsText,
                "The configured WhatsApp provider does not currently support Text content.");
        else
            Finance.Require(capabilities.SupportsTemplateMessaging,
                "The configured WhatsApp provider does not currently support Template content.");
    }

    private static void EnsureCapabilitySnapshot(
        ProviderCapabilitiesFact? expected,
        ProviderCapabilitiesFact? actual)
    {
        Finance.Require((expected is null) == (actual is null), StalePreviewMessage);
        if (expected is null)
            return;
        Finance.Require(expected!.Equals(actual), StalePreviewMessage);
    }

    private static DestinationFact ResolveDestination(
        Contact contact,
        WhatsAppConversation conversation,
        IReadOnlyList<ParticipantFact> activeParticipants)
    {
        if (conversation.Kind == WhatsAppConversationKind.Direct)
        {
            Finance.Require(conversation.DirectContactWhatsAppAddress is not null,
                "The Direct WhatsApp conversation address anchor is missing.");
            var address = conversation.DirectContactWhatsAppAddress!;
            Finance.Require(address.ContactId == contact.Id,
                "The Direct WhatsApp address belongs to a different Contact.");
            Finance.Require(address.IsActive,
                "The Direct WhatsApp address anchor is inactive.");
            Finance.Require(address.ConsentState == ContactWhatsAppConsentState.OptedIn,
                "The Direct WhatsApp address does not have active opt-in consent.");
            return new DestinationFact(
                ProviderDestinationKind.Direct,
                address.ProviderWaId ?? address.NormalizedE164,
                address.NormalizedE164,
                address.ProviderWaId,
                address.Id,
                address.Version,
                address.ConsentState);
        }

        var batchingContact = activeParticipants.SingleOrDefault(x =>
            x.ParticipantKind == WhatsAppParticipantKind.Contact && x.ContactId == contact.Id);
        Finance.Require(batchingContact is not null,
            "The batching Contact must be explicitly represented by the current Group participant facts.");
        return new DestinationFact(
            ProviderDestinationKind.Group,
            conversation.ProviderConversationKey,
            null,
            null,
            null,
            null,
            null);
    }

    private static void EnsureFactsMatch(PreviewTokenPayload expected, PreviewFacts actual)
    {
        Finance.Require(expected.Contact.Equals(actual.Contact), StalePreviewMessage);
        Finance.Require(expected.Conversation.Equals(actual.Conversation), StalePreviewMessage);
        Finance.Require(expected.Destination.Equals(actual.Destination), StalePreviewMessage);
        Finance.Require(string.Equals(expected.ParticipantSetHash, actual.ParticipantSetHash, StringComparison.Ordinal), StalePreviewMessage);
        Finance.Require(expected.ActiveParticipants.SequenceEqual(actual.ActiveParticipants, ParticipantFactComparer.Instance), StalePreviewMessage);
        Finance.Require(expected.EngagementScopes.SequenceEqual(actual.EngagementScopes, ScopeFactComparer.Instance), StalePreviewMessage);
        Finance.Require(expected.Requests.SequenceEqual(actual.Requests, RequestFactComparer.Instance), StalePreviewMessage);
        Finance.Require(ContentMatches(expected.Content, actual.Content), StalePreviewMessage);
        EnsureCapabilitySnapshot(expected.Capabilities, actual.Capabilities);
    }

    private static void EnsureExistingMessageMatches(
        PreviewTokenPayload expected,
        WhatsAppOutboundMessage existing)
    {
        var snapshot = existing.WhatsAppOutboundBatchSnapshot;
        Finance.Require(snapshot is not null &&
                        existing.DocumentRequestBatchId == snapshot.DocumentRequestBatchId &&
                        snapshot.ContactId == expected.Contact.Id &&
                        snapshot.WhatsAppConversationId == expected.Conversation.Id &&
                        snapshot.ConversationAuthorizationVersion == expected.Conversation.AuthorizationVersion &&
                        string.Equals(snapshot.ParticipantSetHash, expected.ParticipantSetHash, StringComparison.Ordinal) &&
                        string.Equals(snapshot.QueuedByActor, expected.Actor, StringComparison.Ordinal) &&
                        string.Equals(snapshot.CorrelationId, expected.CorrelationId, StringComparison.Ordinal) &&
                        string.Equals(existing.CorrelationId, expected.CorrelationId, StringComparison.Ordinal) &&
                        existing.State == WhatsAppOutboundMessageState.Queued &&
                        existing.AttemptCount == 0 &&
                        existing.DocumentRequestBatch.Status == DocumentRequestBatchStatus.Queued &&
                        existing.ProviderName == expected.Conversation.ProviderName &&
                        existing.BusinessEndpointKey == expected.Conversation.BusinessEndpointKey &&
                        existing.ProviderAccountReference == expected.Conversation.ProviderAccountReference &&
                        MessageContentMatches(existing, expected.Content) &&
                        MessageDestinationMatches(existing, expected.Destination),
            StalePreviewMessage);
    }

    private static bool MessageContentMatches(WhatsAppOutboundMessage message, ContentFact content) =>
        (content.Kind == ModelContentKind.Text &&
         message.ContentKind == ModelContentKind.Text &&
         message.TextBody == content.Text &&
         message.TemplateName is null &&
         message.TemplateLanguage is null &&
         message.TemplateParametersSnapshot is null) ||
        (content.Kind == ModelContentKind.Template &&
         message.ContentKind == ModelContentKind.Template &&
         message.TextBody is null &&
         message.TemplateName == content.TemplateName &&
         message.TemplateLanguage == content.TemplateLanguage &&
         message.TemplateParametersSnapshot == content.TemplateParametersSnapshot);

    private static bool MessageDestinationMatches(WhatsAppOutboundMessage message, DestinationFact destination) =>
        message.DestinationKind == (destination.Kind == ProviderDestinationKind.Direct
            ? WhatsAppOutboundDestinationKind.Direct
            : WhatsAppOutboundDestinationKind.Group) &&
        message.ProviderDestinationKey == destination.ProviderDestinationKey &&
        message.NormalizedE164 == destination.NormalizedE164 &&
        message.ProviderRecipientKey == destination.ProviderRecipientKey;

    private static ContentFact ContentFromQueuedMessage(WhatsAppOutboundMessage message)
    {
        return message.ContentKind switch
        {
            ModelContentKind.Text => BuildQueuedTextContent(message),
            ModelContentKind.Template => BuildQueuedTemplateContent(message),
            _ => throw new BusinessException(StalePreviewMessage)
        };
    }

    private static ContentFact BuildQueuedTextContent(WhatsAppOutboundMessage message)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(message.TextBody) &&
                        message.TemplateName is null &&
                        message.TemplateLanguage is null &&
                        message.TemplateParametersSnapshot is null,
            StalePreviewMessage);
        return new(ModelContentKind.Text, message.TextBody, null, null, [], null);
    }

    private static ContentFact BuildQueuedTemplateContent(WhatsAppOutboundMessage message)
    {
        Finance.Require(message.TextBody is null &&
                        !string.IsNullOrWhiteSpace(message.TemplateName) &&
                        !string.IsNullOrWhiteSpace(message.TemplateLanguage) &&
                        !string.IsNullOrWhiteSpace(message.TemplateParametersSnapshot),
            StalePreviewMessage);

        string[] parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<string[]>(
                message.TemplateParametersSnapshot!,
                TokenJsonOptions) ?? throw new JsonException("Template parameters were null.");
        }
        catch (JsonException ex)
        {
            throw new BusinessException(StalePreviewMessage, ex);
        }

        Finance.Require(JsonSerializer.Serialize(parameters, TokenJsonOptions) == message.TemplateParametersSnapshot,
            StalePreviewMessage);
        return new(
            ModelContentKind.Template,
            null,
            message.TemplateName,
            message.TemplateLanguage,
            parameters,
            message.TemplateParametersSnapshot);
    }

    private static void EnsureQueuedSnapshotMatches(
        WhatsAppOutboundMessage message,
        WhatsAppOutboundBatchSnapshot snapshot,
        PreviewFacts actual)
    {
        Finance.Require(snapshot.DocumentRequestBatchId == message.DocumentRequestBatchId &&
                        snapshot.ContactId == actual.Contact.Id &&
                        snapshot.WhatsAppConversationId == actual.Conversation.Id &&
                        snapshot.ConversationAuthorizationVersion == actual.Conversation.AuthorizationVersion &&
                        string.Equals(snapshot.ParticipantSetHash, actual.ParticipantSetHash, StringComparison.Ordinal) &&
                        string.Equals(snapshot.CorrelationId, message.CorrelationId, StringComparison.Ordinal) &&
                        message.ProviderName == actual.Conversation.ProviderName &&
                        message.BusinessEndpointKey == actual.Conversation.BusinessEndpointKey &&
                        message.ProviderAccountReference == actual.Conversation.ProviderAccountReference &&
                        MessageDestinationMatches(message, actual.Destination),
            StalePreviewMessage);

        var participants = actual.ActiveParticipants.OrderBy(x => x.Id).ToArray();
        var participantSnapshots = snapshot.Participants.OrderBy(x => x.WhatsAppConversationParticipantId).ToArray();
        Finance.Require(participants.Length == participantSnapshots.Length &&
                        participants.Zip(participantSnapshots).All(x =>
                            x.First.Id == x.Second.WhatsAppConversationParticipantId &&
                            x.First.ParticipantKind == x.Second.ParticipantKind &&
                            x.First.ContactId == x.Second.ContactId &&
                            x.First.ContactWhatsAppAddressId == x.Second.ContactWhatsAppAddressId &&
                            x.First.BusinessPartyId == x.Second.BusinessPartyId &&
                            x.First.ManagerId == x.Second.ManagerId &&
                            x.First.AppUserId == x.Second.AppUserId &&
                            x.First.ProviderParticipantKey == x.Second.ProviderParticipantKey &&
                            x.First.NormalizedE164 == x.Second.NormalizedE164 &&
                            x.First.DisplayNameSnapshot == x.Second.DisplayNameSnapshot),
            StalePreviewMessage);

        var scopes = actual.EngagementScopes.OrderBy(x => x.Id).ToArray();
        var scopeSnapshots = snapshot.EngagementScopes.OrderBy(x => x.WhatsAppConversationEngagementScopeId).ToArray();
        Finance.Require(scopes.Length == scopeSnapshots.Length &&
                        scopes.Zip(scopeSnapshots).All(x =>
                            x.First.Id == x.Second.WhatsAppConversationEngagementScopeId &&
                            x.First.EngagementId == x.Second.EngagementId &&
                            x.First.ApprovedAuthorizationVersion == x.Second.ApprovedAuthorizationVersion),
            StalePreviewMessage);

        var requests = actual.Requests.OrderBy(x => x.DocumentRequestId).ToArray();
        var requestSnapshots = snapshot.Requests.OrderBy(x => x.DocumentRequestId).ToArray();
        Finance.Require(requests.Length == requestSnapshots.Length &&
                        requests.Zip(requestSnapshots).All(x =>
                            x.First.DocumentRequestId == x.Second.DocumentRequestId &&
                            x.First.Revision == x.Second.RequestRevision &&
                            x.First.Version == x.Second.RequestVersion &&
                            x.First.EngagementId == x.Second.EngagementId &&
                            x.First.CustomerId == x.Second.CustomerId &&
                            x.First.CustomerName == x.Second.CustomerNameSnapshot &&
                            x.First.ServiceId == x.Second.ServiceId &&
                            x.First.ServiceName == x.Second.ServiceNameSnapshot &&
                            QueuedItemsMatch(x.First, snapshot)),
            StalePreviewMessage);
    }

    private static bool QueuedItemsMatch(RequestFact request, WhatsAppOutboundBatchSnapshot snapshot)
    {
        var items = request.UnresolvedItems.OrderBy(x => x.DocumentRequestItemId).ToArray();
        var itemSnapshots = snapshot.Items
            .Where(x => x.DocumentRequestId == request.DocumentRequestId)
            .OrderBy(x => x.DocumentRequestItemId)
            .ToArray();
        return items.Length == itemSnapshots.Length &&
               items.Zip(itemSnapshots).All(x =>
                   x.First.DocumentRequestItemId == x.Second.DocumentRequestItemId &&
                   x.Second.RequestRevision == request.Revision &&
                   x.First.RequirementName == x.Second.RequirementNameSnapshot &&
                   x.First.IsRequired == x.Second.IsRequired &&
                   x.First.DisplayOrder == x.Second.DisplayOrder &&
                   x.First.Status == x.Second.ItemStatusSnapshot);
    }

    private static PreviewTokenPayload CreateTokenPayload(
        Guid previewId,
        DateTime previewedAt,
        string logicalMessageKey,
        string correlationId,
        string actor,
        string source,
        PreviewFacts facts)
    {
        var unsigned = new PreviewTokenPayload(
            TokenVersion,
            previewId,
            previewedAt,
            logicalMessageKey,
            correlationId,
            actor,
            source,
            facts.Contact,
            facts.Conversation,
            facts.Destination,
            facts.ParticipantSetHash,
            facts.ActiveParticipants.ToArray(),
            facts.EngagementScopes.ToArray(),
            facts.Requests.ToArray(),
            facts.Content,
            facts.Capabilities,
            "");
        return unsigned with { FactsHash = ComputeFactsHash(unsigned) };
    }

    private string Protect(PreviewTokenPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, TokenJsonOptions);
        return WebEncoders.Base64UrlEncode(previewProtector.Protect(bytes));
    }

    private PreviewTokenPayload Unprotect(string token)
    {
        try
        {
            Finance.Require(!string.IsNullOrWhiteSpace(token), InvalidTokenMessage);
            var bytes = previewProtector.Unprotect(WebEncoders.Base64UrlDecode(token));
            var payload = JsonSerializer.Deserialize<PreviewTokenPayload>(bytes, TokenJsonOptions);
            var validPayload = payload ?? throw new BusinessException(InvalidTokenMessage);
            Finance.Require(validPayload.Version == TokenVersion &&
                            validPayload.Contact is not null &&
                            validPayload.Conversation is not null &&
                            validPayload.Destination is not null &&
                            validPayload.ActiveParticipants is not null &&
                            validPayload.EngagementScopes is not null &&
                            validPayload.Requests is not null &&
                            validPayload.Content is not null &&
                            validPayload.Content.TemplateParameters is not null &&
                            validPayload.ActiveParticipants.All(x => x is not null) &&
                            validPayload.EngagementScopes.All(x => x is not null) &&
                            validPayload.Requests.All(x => x is not null &&
                                                           x.UnresolvedItems is not null &&
                                                           x.UnresolvedItems.All(item => item is not null)),
                InvalidTokenMessage);
            var content = validPayload.Content!;
            var templateParameters = content.TemplateParameters!;
            var engagementScopes = validPayload.EngagementScopes!;
            var requests = validPayload.Requests!;
            Finance.Require(requests.Count > 0 &&
                            engagementScopes.Count > 0 &&
                            ((content.Kind == ModelContentKind.Text &&
                              !string.IsNullOrWhiteSpace(content.Text) &&
                              content.TemplateName is null &&
                              content.TemplateLanguage is null &&
                              templateParameters.Length == 0 &&
                              content.TemplateParametersSnapshot is null) ||
                             (content.Kind == ModelContentKind.Template &&
                              !string.IsNullOrWhiteSpace(content.TemplateName) &&
                              !string.IsNullOrWhiteSpace(content.TemplateLanguage) &&
                              content.Text is null &&
                              content.TemplateParametersSnapshot ==
                              JsonSerializer.Serialize(templateParameters, TokenJsonOptions))),
                InvalidTokenMessage);
            Finance.Require(validPayload.PreviewId != Guid.Empty &&
                            !string.IsNullOrWhiteSpace(validPayload.LogicalMessageKey) &&
                            !string.IsNullOrWhiteSpace(validPayload.CorrelationId) &&
                            !string.IsNullOrWhiteSpace(validPayload.Actor) &&
                            !string.IsNullOrWhiteSpace(validPayload.Source), InvalidTokenMessage);
            Finance.Require(string.Equals(validPayload.FactsHash, ComputeFactsHash(validPayload), StringComparison.Ordinal), InvalidTokenMessage);
            return validPayload;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            throw new BusinessException(InvalidTokenMessage, ex);
        }
    }

    private static string ComputeFactsHash(PreviewTokenPayload payload)
    {
        var unsigned = payload with { FactsHash = "" };
        return Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(unsigned, TokenJsonOptions)))
            .ToLowerInvariant();
    }

    private static WhatsAppOutboundPreviewReadModel ToPreviewReadModel(
        string token,
        Guid previewId,
        DateTime previewedAt,
        string logicalMessageKey,
        string correlationId,
        string actor,
        string source,
        PreviewFacts facts)
    {
        var account = new WhatsAppProviderAccountBinding(
            facts.Conversation.ProviderName,
            facts.Conversation.BusinessEndpointKey,
            facts.Conversation.ProviderAccountReference);
        var destination = facts.Destination.Kind == ProviderDestinationKind.Direct
            ? WhatsAppDestination.Direct(
                facts.Destination.ProviderDestinationKey,
                facts.Destination.NormalizedE164,
                facts.Destination.ProviderRecipientKey)
            : WhatsAppDestination.Group(facts.Destination.ProviderDestinationKey);
        var providerContent = ToProviderContent(facts.Content);
        var proposed = new WhatsAppOutboundRequest(
            logicalMessageKey,
            account,
            destination,
            providerContent,
            correlationId);
        return new(
            token,
            previewId,
            previewedAt,
            new(facts.Contact.Id, facts.Contact.Name, facts.Contact.IsActive, facts.Contact.Version),
            new(
                facts.Conversation.Id,
                facts.Conversation.Kind,
                facts.Conversation.Status,
                facts.Conversation.ProviderName,
                facts.Conversation.BusinessEndpointKey,
                facts.Conversation.ProviderAccountReference,
                facts.Conversation.ProviderConversationKey,
                facts.Conversation.DirectContactWhatsAppAddressId,
                facts.Conversation.AuthorizationVersion,
                facts.Conversation.Version),
            new(
                facts.Destination.Kind,
                facts.Destination.ProviderDestinationKey,
                facts.Destination.NormalizedE164,
                facts.Destination.ProviderRecipientKey,
                facts.Destination.ConsentAddressId,
                facts.Destination.ConsentAddressVersion,
                facts.Destination.ConsentState),
            facts.ParticipantSetHash,
            facts.ActiveParticipants.Select(ToReadModel).ToArray(),
            facts.EngagementScopes.Select(ToReadModel).ToArray(),
            facts.Requests.Select(ToReadModel).ToArray(),
            providerContent,
            proposed,
            actor,
            source,
            facts.Capabilities is null ? null : ToProviderCapabilities(facts.Capabilities));
    }

    private static WhatsAppOutboundParticipantReadModel ToReadModel(ParticipantFact participant) => new(
        participant.Id,
        participant.ParticipantKind,
        participant.ContactId,
        participant.ContactName,
        participant.ContactWhatsAppAddressId,
        participant.ContactWhatsAppAddress?.NormalizedE164,
        participant.BusinessPartyId,
        participant.BusinessPartyName,
        participant.ManagerId,
        participant.ManagerName,
        participant.AppUserId,
        participant.AppUserName,
        participant.ProviderParticipantKey,
        participant.NormalizedE164,
        participant.DisplayNameSnapshot,
        participant.IsActive,
        participant.JoinedAt,
        participant.LeftAt,
        participant.Version);

    private static ParticipantFact ToParticipantFact(WhatsAppOutboundParticipantReadModel participant) => new(
        participant.Id,
        participant.ParticipantKind,
        participant.ContactId,
        participant.ContactName,
        participant.ContactWhatsAppAddressId,
        participant.ContactWhatsAppAddressId is int addressId &&
        participant.ContactId is int contactId &&
        participant.ContactWhatsAppAddressNumber is string addressNumber
            ? new AddressFact(
                addressId,
                contactId,
                addressNumber,
                null,
                true,
                ContactWhatsAppConsentState.Unknown,
                participant.Version)
            : null,
        participant.BusinessPartyId,
        participant.BusinessPartyName,
        participant.ManagerId,
        participant.ManagerName,
        participant.AppUserId,
        participant.AppUserName,
        participant.ProviderParticipantKey,
        participant.NormalizedE164,
        participant.DisplayNameSnapshot,
        participant.IsActive,
        participant.JoinedAt,
        participant.LeftAt,
        participant.Version);

    private static ParticipantFact ToParticipantFact(WhatsAppConversationParticipant participant) => new(
        participant.Id,
        participant.ParticipantKind,
        participant.ContactId,
        participant.Contact?.Name,
        participant.ContactWhatsAppAddressId,
        participant.ContactWhatsAppAddress is null ? null : new AddressFact(
            participant.ContactWhatsAppAddress.Id,
            participant.ContactWhatsAppAddress.ContactId,
            participant.ContactWhatsAppAddress.NormalizedE164,
            participant.ContactWhatsAppAddress.ProviderWaId,
            participant.ContactWhatsAppAddress.IsActive,
            participant.ContactWhatsAppAddress.ConsentState,
            participant.ContactWhatsAppAddress.Version),
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
        participant.Version);

    private static WhatsAppOutboundParticipantSnapshot ToParticipantSnapshot(ParticipantFact participant) => new()
    {
        WhatsAppConversationParticipantId = participant.Id,
        ParticipantKind = participant.ParticipantKind,
        ContactId = participant.ContactId,
        ContactWhatsAppAddressId = participant.ContactWhatsAppAddressId,
        BusinessPartyId = participant.BusinessPartyId,
        ManagerId = participant.ManagerId,
        AppUserId = participant.AppUserId,
        ProviderParticipantKey = participant.ProviderParticipantKey,
        NormalizedE164 = participant.NormalizedE164,
        DisplayNameSnapshot = participant.DisplayNameSnapshot
    };

    private static WhatsAppOutboundEngagementScopeReadModel ToReadModel(ScopeFact scope) => new(
        scope.Id,
        scope.EngagementId,
        scope.CustomerId,
        scope.CustomerName,
        scope.ServiceId,
        scope.ServiceName,
        scope.EngagementStatus,
        scope.IsActive,
        scope.ApprovedAuthorizationVersion,
        scope.ApprovedAt,
        scope.ApprovedByActor,
        scope.ApprovalReason,
        scope.RevokedAt,
        scope.RevokedByActor,
        scope.RevocationReason,
        scope.Version);

    private static WhatsAppOutboundRequestReadModel ToReadModel(RequestFact request) => new(
        request.DocumentRequestId,
        request.Revision,
        request.Version,
        request.WorkItemId,
        request.EngagementId,
        request.EngagementVersion,
        request.CustomerId,
        request.CustomerVersion,
        request.CustomerName,
        request.ServiceId,
        request.ServiceVersion,
        request.ServiceName,
        request.ContactCustomerLinkId,
        request.ContactCustomerLinkVersion,
        request.UnresolvedItems.Select(item => new WhatsAppOutboundItemReadModel(
            item.DocumentRequestItemId,
            item.Version,
            item.RequirementKey,
            item.RequirementName,
            item.RequirementDescription,
            item.IsRequired,
            item.Wave,
            item.DisplayOrder,
            item.Status)).ToArray());

    private static WhatsAppOutboundContent ToProviderContent(ContentFact content) =>
        content.Kind == ModelContentKind.Text
            ? new WhatsAppTextContent(content.Text!)
            : new WhatsAppTemplateContent(content.TemplateName!, content.TemplateLanguage!, content.TemplateParameters);

    private static WhatsAppProviderCapabilities ToProviderCapabilities(ProviderCapabilitiesFact capabilities) =>
        new(
            capabilities.SupportsDirectMessaging,
            capabilities.SupportsGroupMessaging,
            capabilities.SupportsText,
            capabilities.SupportsTemplateMessaging,
            capabilities.SupportsMedia,
            capabilities.SupportsDocuments);

    private static ContentFact NormalizeContent(WhatsAppOutboundContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content switch
        {
            WhatsAppTextContent text => new(ModelContentKind.Text, text.Text, null, null, [], null),
            WhatsAppTemplateContent template => new(
                ModelContentKind.Template,
                null,
                template.TemplateName,
                template.Language,
                template.Parameters.ToArray(),
                JsonSerializer.Serialize(template.Parameters, TokenJsonOptions)),
            _ => throw new BusinessException("The WhatsApp outbound content kind is not supported.")
        };
    }

    private static bool ContentMatches(ContentFact left, ContentFact right) =>
        left.Kind == right.Kind &&
        left.Text == right.Text &&
        left.TemplateName == right.TemplateName &&
        left.TemplateLanguage == right.TemplateLanguage &&
        left.TemplateParameters.SequenceEqual(right.TemplateParameters, StringComparer.Ordinal);

    private static DocumentBatchRequestSelection[] NormalizeSelections(
        IEnumerable<DocumentBatchRequestSelection> selections)
    {
        Finance.Require(selections is not null, "Document request selections are required.");
        var normalized = selections!.ToArray();
        Finance.Require(normalized.Length > 0, "Select at least one document request.");
        Finance.Require(normalized.All(x => x.DocumentRequestId > 0 && x.Revision > 0),
            "Document request IDs and revisions must be positive.");
        Finance.Require(normalized.Select(x => x.DocumentRequestId).Distinct().Count() == normalized.Length,
            "Duplicate document request IDs are not allowed.");
        return normalized.OrderBy(x => x.DocumentRequestId).ToArray();
    }

    private static bool IsUnresolved(DocumentRequestItem item) =>
        item.Status is not DocumentRequestItemStatus.Received and
            not DocumentRequestItemStatus.NotRequired and
            not DocumentRequestItemStatus.Waived;

    private static string RequiredText(string? value, int maxLength, string label)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(value), $"{label} is required.");
        var normalized = value!.Trim();
        Finance.Require(normalized.Length <= maxLength, $"{label} must be {maxLength} characters or fewer.");
        Finance.Require(!normalized.Contains('\r') && !normalized.Contains('\n'), $"{label} cannot contain line breaks.");
        return normalized;
    }

    private static void AppendInt(StringBuilder target, int value)
    {
        target.Append(value).Append(':');
    }

    private static void AppendNullableInt(StringBuilder target, int? value)
    {
        if (value is null)
        {
            target.Append("n:");
            return;
        }
        target.Append("i:").Append(value.Value).Append(':');
    }

    private static void AppendNullableString(StringBuilder target, string? value)
    {
        if (value is null)
        {
            target.Append("n:");
            return;
        }
        target.Append("s:").Append(value.Length).Append(':').Append(value);
    }

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

    private async Task<T> InSerializableReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex) when (FinancialTransaction.IsSerializationConflict(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException("The WhatsApp Preview could not be read consistently. Refresh and retry.", ex);
        }
    }

    private async Task<T> InSerializableTransactionAsync<T>(
        Func<Task<T>> action,
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
            throw new BusinessException(QueueConflictMessage, ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(QueueConflictMessage, ex);
        }
        catch (Exception ex) when (FinancialTransaction.IsSerializationConflict(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(QueueConflictMessage, ex);
        }
    }

    private static bool IsQueueConflict(BusinessException exception) =>
        exception.Message == QueueConflictMessage;

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record PreviewFacts(
        ContactFact Contact,
        ConversationFact Conversation,
        DestinationFact Destination,
        string ParticipantSetHash,
        IReadOnlyList<ParticipantFact> ActiveParticipants,
        IReadOnlyList<ScopeFact> EngagementScopes,
        IReadOnlyList<RequestFact> Requests,
        ContentFact Content,
        ProviderCapabilitiesFact? Capabilities);

    private sealed record ContactFact(int Id, string Name, bool IsActive, long Version);

    private sealed record ConversationFact(
        int Id,
        WhatsAppConversationKind Kind,
        WhatsAppConversationStatus Status,
        string ProviderName,
        string BusinessEndpointKey,
        string? ProviderAccountReference,
        string ProviderConversationKey,
        int? DirectContactWhatsAppAddressId,
        int AuthorizationVersion,
        long Version);

    private sealed record AddressFact(
        int Id,
        int ContactId,
        string NormalizedE164,
        string? ProviderWaId,
        bool IsActive,
        ContactWhatsAppConsentState ConsentState,
        long Version);

    private sealed record DestinationFact(
        ProviderDestinationKind Kind,
        string ProviderDestinationKey,
        string? NormalizedE164,
        string? ProviderRecipientKey,
        int? ConsentAddressId,
        long? ConsentAddressVersion,
        ContactWhatsAppConsentState? ConsentState);

    private sealed record ParticipantFact(
        int Id,
        WhatsAppParticipantKind ParticipantKind,
        int? ContactId,
        string? ContactName,
        int? ContactWhatsAppAddressId,
        AddressFact? ContactWhatsAppAddress,
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
        long Version);

    private sealed record ScopeFact(
        int Id,
        int EngagementId,
        int CustomerId,
        string CustomerName,
        int ServiceId,
        string ServiceName,
        EngagementStatus EngagementStatus,
        bool IsActive,
        int ApprovedAuthorizationVersion,
        DateTime ApprovedAt,
        string ApprovedByActor,
        string? ApprovalReason,
        DateTime? RevokedAt,
        string? RevokedByActor,
        string? RevocationReason,
        long Version);

    private sealed record ItemFact(
        int DocumentRequestItemId,
        long Version,
        string RequirementKey,
        string RequirementName,
        string? RequirementDescription,
        bool IsRequired,
        DocumentRequirementWave Wave,
        int DisplayOrder,
        DocumentRequestItemStatus Status);

    private sealed record RequestFact(
        int DocumentRequestId,
        int Revision,
        long Version,
        int WorkItemId,
        int EngagementId,
        long EngagementVersion,
        int CustomerId,
        long CustomerVersion,
        string CustomerName,
        int ServiceId,
        long ServiceVersion,
        string ServiceName,
        int ContactCustomerLinkId,
        long ContactCustomerLinkVersion,
        IReadOnlyList<ItemFact> UnresolvedItems);

    private sealed record ContentFact(
        ModelContentKind Kind,
        string? Text,
        string? TemplateName,
        string? TemplateLanguage,
        string[] TemplateParameters,
        string? TemplateParametersSnapshot);

    private sealed record ProviderCapabilitiesFact(
        bool SupportsDirectMessaging,
        bool SupportsGroupMessaging,
        bool SupportsText,
        bool SupportsTemplateMessaging,
        bool SupportsMedia,
        bool SupportsDocuments);

    private sealed record PreviewTokenPayload(
        int Version,
        Guid PreviewId,
        DateTime PreviewedAt,
        string LogicalMessageKey,
        string CorrelationId,
        string Actor,
        string Source,
        ContactFact Contact,
        ConversationFact Conversation,
        DestinationFact Destination,
        string ParticipantSetHash,
        IReadOnlyList<ParticipantFact> ActiveParticipants,
        IReadOnlyList<ScopeFact> EngagementScopes,
        IReadOnlyList<RequestFact> Requests,
        ContentFact Content,
        ProviderCapabilitiesFact? Capabilities,
        string FactsHash);

    private sealed class ParticipantFactComparer : IComparer<ParticipantFact>, IEqualityComparer<ParticipantFact>
    {
        public static ParticipantFactComparer Instance { get; } = new();

        public int Compare(ParticipantFact? x, ParticipantFact? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return CompareValues(
                (int)x.ParticipantKind, y.ParticipantKind,
                x.ContactId, y.ContactId,
                x.ContactWhatsAppAddressId, y.ContactWhatsAppAddressId,
                x.BusinessPartyId, y.BusinessPartyId,
                x.ManagerId, y.ManagerId,
                x.AppUserId, y.AppUserId,
                x.ProviderParticipantKey, y.ProviderParticipantKey,
                x.NormalizedE164, y.NormalizedE164,
                x.DisplayNameSnapshot, y.DisplayNameSnapshot,
                x.Id, y.Id);
        }

        public bool Equals(ParticipantFact? x, ParticipantFact? y) => Compare(x, y) == 0 &&
            x?.ContactName == y?.ContactName &&
            x?.BusinessPartyName == y?.BusinessPartyName &&
            x?.ManagerName == y?.ManagerName &&
            x?.AppUserName == y?.AppUserName &&
            x?.Version == y?.Version && x?.IsActive == y?.IsActive &&
            x?.JoinedAt == y?.JoinedAt && x?.LeftAt == y?.LeftAt &&
            Equals(x?.ContactWhatsAppAddress, y?.ContactWhatsAppAddress);

        public int GetHashCode(ParticipantFact obj) => HashCode.Combine(obj.Id, obj.Version, obj.ParticipantKind);
    }

    private sealed class ScopeFactComparer : IEqualityComparer<ScopeFact>
    {
        public static ScopeFactComparer Instance { get; } = new();

        public bool Equals(ScopeFact? x, ScopeFact? y) =>
            x is not null && y is not null && x == y;

        public int GetHashCode(ScopeFact obj) => HashCode.Combine(obj.Id, obj.Version, obj.ApprovedAuthorizationVersion);
    }

    private sealed class RequestFactComparer : IEqualityComparer<RequestFact>
    {
        public static RequestFactComparer Instance { get; } = new();

        public bool Equals(RequestFact? x, RequestFact? y) =>
            x is not null && y is not null &&
            x.DocumentRequestId == y.DocumentRequestId &&
            x.Revision == y.Revision &&
            x.Version == y.Version &&
            x.WorkItemId == y.WorkItemId &&
            x.EngagementId == y.EngagementId &&
            x.EngagementVersion == y.EngagementVersion &&
            x.CustomerId == y.CustomerId &&
            x.CustomerVersion == y.CustomerVersion &&
            x.CustomerName == y.CustomerName &&
            x.ServiceId == y.ServiceId &&
            x.ServiceVersion == y.ServiceVersion &&
            x.ServiceName == y.ServiceName &&
            x.ContactCustomerLinkId == y.ContactCustomerLinkId &&
            x.ContactCustomerLinkVersion == y.ContactCustomerLinkVersion &&
            x.UnresolvedItems.SequenceEqual(y.UnresolvedItems);

        public int GetHashCode(RequestFact obj) => HashCode.Combine(obj.DocumentRequestId, obj.Version, obj.Revision);
    }

    private static int CompareValues(
        int kind,
        WhatsAppParticipantKind otherKind,
        int? contactId,
        int? otherContactId,
        int? addressId,
        int? otherAddressId,
        int? businessPartyId,
        int? otherBusinessPartyId,
        int? managerId,
        int? otherManagerId,
        string? appUserId,
        string? otherAppUserId,
        string? providerKey,
        string? otherProviderKey,
        string? normalized,
        string? otherNormalized,
        string? display,
        string? otherDisplay,
        int id,
        int otherId)
    {
        var comparisons = new[]
        {
            kind.CompareTo((int)otherKind),
            Nullable.Compare(contactId, otherContactId),
            Nullable.Compare(addressId, otherAddressId),
            Nullable.Compare(businessPartyId, otherBusinessPartyId),
            Nullable.Compare(managerId, otherManagerId),
            StringComparer.Ordinal.Compare(appUserId, otherAppUserId),
            StringComparer.Ordinal.Compare(providerKey, otherProviderKey),
            StringComparer.Ordinal.Compare(normalized, otherNormalized),
            StringComparer.Ordinal.Compare(display, otherDisplay),
            id.CompareTo(otherId)
        };
        return comparisons.FirstOrDefault(x => x != 0);
    }
}
