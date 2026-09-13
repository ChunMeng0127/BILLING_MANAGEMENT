using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Services;

/// <summary>
/// Identifies the request revision a caller observed before asking for a
/// pre-conversation batch preview.
/// </summary>
public sealed record DocumentBatchRequestSelection(int DocumentRequestId, int Revision);

/// <summary>
/// An untracked, deterministic candidate for a pre-conversation batch preview.
/// Contact/customer relationship evidence is intentionally not send
/// authorization.
/// </summary>
public sealed record DocumentBatchCandidateReadModel(
    int DocumentRequestId,
    int Revision,
    long RequestVersion,
    int WorkItemId,
    int CustomerId,
    string CustomerName,
    int EngagementId,
    int ServiceId,
    string ServiceName,
    DocumentRequestStatus Status,
    int OutstandingUnresolvedItemCount,
    int TotalItemCount);

/// <summary>
/// Request facts included in a pre-conversation batch preview.
/// </summary>
public sealed record DocumentBatchPreviewRequestReadModel(
    int DocumentRequestId,
    int Revision,
    long RequestVersion,
    int WorkItemId,
    int CustomerId,
    string CustomerName,
    int EngagementId,
    int ServiceId,
    string ServiceName,
    DocumentRequestStatus Status,
    int OutstandingUnresolvedItemCount,
    int TotalItemCount);

/// <summary>
/// A read-only pre-conversation preview. It is not a send-eligible batch and
/// does not snapshot or persist any communication authorization state.
/// </summary>
public sealed record DocumentBatchPreviewReadModel(
    int ContactId,
    string ContactName,
    bool ContactIsActive,
    int ActiveWhatsAppAddressCount,
    int ActiveOptedInWhatsAppAddressCount,
    bool HasActiveDoNotWhatsAppAddress,
    bool RequiresConversationAuthorization,
    IReadOnlyList<DocumentBatchPreviewRequestReadModel> Requests);

/// <summary>
/// Resolves deterministic request candidates for a Contact without creating
/// or changing DocumentRequestBatch or any other domain state.
/// </summary>
public sealed class DocumentRequestBatchService(AppDbContext db)
{
    private const string IneligibleSelectionMessage =
        "One or more selected document requests were not found or are not currently eligible for this Contact.";

    /// <summary>
    /// Returns current ReadyToSend requests whose authoritative request graph
    /// reaches a Customer linked actively to the supplied Contact.
    /// </summary>
    public async Task<IReadOnlyList<DocumentBatchCandidateReadModel>> GetCandidateRequestsAsync(
        int contactId,
        CancellationToken cancellationToken = default)
    {
        await GetActiveContactAsync(contactId, cancellationToken);
        return await CandidateQuery(contactId).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a deterministic, read-only preview from request IDs. The IDs
    /// are revalidated against current Contact-centered eligibility.
    /// </summary>
    public async Task<DocumentBatchPreviewReadModel> PreviewBatchAsync(
        int contactId,
        IEnumerable<int> requestIds,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeRequestIds(requestIds);
        return await PreviewBatchCoreAsync(contactId, ids, expectedRevisions: null, cancellationToken);
    }

    /// <summary>
    /// Creates a deterministic preview while also checking the request
    /// revisions observed by the caller.
    /// </summary>
    public async Task<DocumentBatchPreviewReadModel> PreviewBatchAsync(
        int contactId,
        IEnumerable<DocumentBatchRequestSelection> selections,
        CancellationToken cancellationToken = default)
    {
        var selectionArray = NormalizeSelections(selections);
        var ids = selectionArray.Select(x => x.DocumentRequestId).ToArray();
        var expectedRevisions = selectionArray.ToDictionary(x => x.DocumentRequestId, x => x.Revision);
        return await PreviewBatchCoreAsync(contactId, ids, expectedRevisions, cancellationToken);
    }

    /// <summary>
    /// Creates a deterministic preview from IDs and an explicit revision map.
    /// This overload is useful for callers that already hold selections in a
    /// dictionary-shaped read model.
    /// </summary>
    public async Task<DocumentBatchPreviewReadModel> PreviewBatchAsync(
        int contactId,
        IEnumerable<int> requestIds,
        IReadOnlyDictionary<int, int> expectedRevisions,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeRequestIds(requestIds);
        Finance.Require(expectedRevisions is not null,
            "Expected document request revisions are required.");
        Finance.Require(ids.All(expectedRevisions!.ContainsKey),
            "An expected revision is required for each selected document request.");
        Finance.Require(ids.All(id => expectedRevisions[id] > 0),
            "Expected document request revisions must be positive.");

        return await PreviewBatchCoreAsync(contactId, ids, expectedRevisions, cancellationToken);
    }

    private async Task<DocumentBatchPreviewReadModel> PreviewBatchCoreAsync(
        int contactId,
        IReadOnlyCollection<int> requestIds,
        IReadOnlyDictionary<int, int>? expectedRevisions,
        CancellationToken cancellationToken)
    {
        var contact = await GetActiveContactAsync(contactId, cancellationToken);
        var candidates = await CandidateQuery(contactId, requestIds)
            .ToListAsync(cancellationToken);

        Finance.Require(candidates.Count == requestIds.Count, IneligibleSelectionMessage);

        if (expectedRevisions is not null)
        {
            Finance.Require(candidates.All(x => expectedRevisions[x.DocumentRequestId] == x.Revision),
                "A selected document request revision is stale. Refresh before previewing the batch.");
        }

        return new DocumentBatchPreviewReadModel(
            contact.Id,
            contact.Name,
            contact.IsActive,
            contact.ActiveWhatsAppAddressCount,
            contact.ActiveOptedInWhatsAppAddressCount,
            contact.HasActiveDoNotWhatsAppAddress,
            RequiresConversationAuthorization: true,
            candidates.Select(ToPreviewRequest).ToArray());
    }

    private async Task<ContactPreviewSnapshot> GetActiveContactAsync(
        int contactId,
        CancellationToken cancellationToken)
    {
        Finance.Require(contactId > 0, "A valid Contact is required.");

        var contact = await db.Contacts
            .AsNoTracking()
            .Where(x => x.Id == contactId)
            .Select(x => new ContactPreviewSnapshot(
                x.Id,
                x.Name,
                x.IsActive,
                x.Addresses.Count(a => a.IsActive),
                x.Addresses.Count(a => a.IsActive && a.ConsentState == ContactWhatsAppConsentState.OptedIn),
                x.Addresses.Any(a => a.IsActive && a.ConsentState == ContactWhatsAppConsentState.DoNotWhatsApp)))
            .SingleOrDefaultAsync(cancellationToken);

        Finance.Require(contact is not null, "The Contact was not found.");
        Finance.Require(contact!.IsActive,
            "The Contact is inactive and cannot preview document requests.");
        return contact;
    }

    private IQueryable<DocumentBatchCandidateReadModel> CandidateQuery(
        int contactId,
        IReadOnlyCollection<int>? requestIds = null)
    {
        var query = db.DocumentRequests
            .AsNoTracking()
            .Where(x => x.Status == DocumentRequestStatus.ReadyToSend)
            // Revision is the authoritative current-request ordering. This
            // also keeps malformed or manually imported historical rows out of
            // a preview even if their status was not terminalized correctly.
            .Where(x => !db.DocumentRequests.Any(later =>
                later.WorkItemId == x.WorkItemId && later.Revision > x.Revision))
            .Where(x => x.WorkItem.BillingRecord.Engagement.Customer.ContactCustomerLinks
                .Any(link => link.ContactId == contactId && link.IsActive));

        if (requestIds is not null)
            query = query.Where(x => requestIds.Contains(x.Id));

        return query
            .OrderBy(x => x.WorkItem.BillingRecord.Engagement.Customer.Name)
            .ThenBy(x => x.WorkItem.BillingRecord.EngagementId)
            .ThenBy(x => x.WorkItemId)
            .ThenBy(x => x.Id)
            .Select(x => new DocumentBatchCandidateReadModel(
                x.Id,
                x.Revision,
                x.Version,
                x.WorkItemId,
                x.WorkItem.BillingRecord.Engagement.CustomerId,
                x.WorkItem.BillingRecord.Engagement.Customer.Name,
                x.WorkItem.BillingRecord.EngagementId,
                x.WorkItem.BillingRecord.Engagement.ServiceId,
                x.WorkItem.BillingRecord.Engagement.Service.Name,
                x.Status,
                x.Items.Count(item => item.Status != DocumentRequestItemStatus.Received &&
                                      item.Status != DocumentRequestItemStatus.NotRequired &&
                                      item.Status != DocumentRequestItemStatus.Waived),
                x.Items.Count()));
    }

    private static int[] NormalizeRequestIds(IEnumerable<int> requestIds)
    {
        Finance.Require(requestIds is not null, "Document request IDs are required.");
        var ids = requestIds!.ToArray();
        Finance.Require(ids.Length > 0, "Select at least one document request.");
        Finance.Require(ids.All(x => x > 0), "Document request IDs must be positive.");
        Finance.Require(ids.Distinct().Count() == ids.Length,
            "Duplicate document request IDs are not allowed.");
        return ids;
    }

    private static DocumentBatchRequestSelection[] NormalizeSelections(
        IEnumerable<DocumentBatchRequestSelection> selections)
    {
        Finance.Require(selections is not null, "Document request selections are required.");
        var normalized = selections!.ToArray();
        Finance.Require(normalized.Length > 0, "Select at least one document request.");
        Finance.Require(normalized.All(x => x.DocumentRequestId > 0),
            "Document request IDs must be positive.");
        Finance.Require(normalized.All(x => x.Revision > 0),
            "Document request revisions must be positive.");
        Finance.Require(normalized.Select(x => x.DocumentRequestId).Distinct().Count() == normalized.Length,
            "Duplicate document request IDs are not allowed.");
        return normalized;
    }

    private static DocumentBatchPreviewRequestReadModel ToPreviewRequest(
        DocumentBatchCandidateReadModel candidate) => new(
            candidate.DocumentRequestId,
            candidate.Revision,
            candidate.RequestVersion,
            candidate.WorkItemId,
            candidate.CustomerId,
            candidate.CustomerName,
            candidate.EngagementId,
            candidate.ServiceId,
            candidate.ServiceName,
            candidate.Status,
            candidate.OutstandingUnresolvedItemCount,
            candidate.TotalItemCount);

    private sealed record ContactPreviewSnapshot(
        int Id,
        string Name,
        bool IsActive,
        int ActiveWhatsAppAddressCount,
        int ActiveOptedInWhatsAppAddressCount,
        bool HasActiveDoNotWhatsAppAddress);
}
