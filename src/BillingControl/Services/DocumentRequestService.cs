using System.Data;
using System.Linq.Expressions;
using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingControl.Services;

public sealed record DocumentRequestCreateInput(
    int WorkItemId,
    int DocumentRequirementTemplateId,
    string Actor,
    string Source);

public sealed record DocumentRequestStatusTransitionInput(
    DocumentRequestStatus NewStatus,
    long ExpectedVersion,
    string Actor,
    string Source,
    string Reason);

public sealed record DocumentRequestItemStatusTransitionInput(
    DocumentRequestItemStatus NewStatus,
    long ExpectedVersion,
    string Actor,
    string Source,
    string Reason);

public sealed record DocumentRequestStatusHistoryReadModel(
    int Id,
    DocumentRequestStatus? PreviousStatus,
    DocumentRequestStatus NewStatus,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record DocumentRequestItemStatusHistoryReadModel(
    int Id,
    DocumentRequestItemStatus? PreviousStatus,
    DocumentRequestItemStatus NewStatus,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record DocumentRequestItemReadModel(
    int Id,
    int? TemplateItemId,
    string RequirementKey,
    string RequirementName,
    string? RequirementDescription,
    bool IsRequired,
    DocumentRequirementWave Wave,
    int DisplayOrder,
    DocumentRequestItemStatus Status,
    long Version,
    IReadOnlyList<DocumentRequestItemStatusHistoryReadModel> StatusHistory);

public sealed record DocumentRequestReadModel(
    int Id,
    int WorkItemId,
    int DocumentRequirementTemplateId,
    string TemplateKey,
    int TemplateVersion,
    string TemplateName,
    int Revision,
    DocumentRequestStatus Status,
    long Version,
    IReadOnlyList<DocumentRequestItemReadModel> Items,
    IReadOnlyList<DocumentRequestStatusHistoryReadModel> StatusHistory);

public sealed record DocumentRequestTemplateOptionReadModel(
    int Id,
    int ServiceId,
    string TemplateKey,
    int TemplateVersion,
    string Name,
    bool IsActive,
    bool IsDefault);

/// <summary>
/// Deterministic internal document-request operations before provider delivery exists.
/// This service deliberately does not create batches, provider messages, evidence, or
/// workflow/financial side effects.
/// </summary>
public sealed class DocumentRequestService(AppDbContext db, BusinessClock clock)
{
    private const string CreateConflictMessage = "Another document request operation for this WorkItem completed first. Refresh and retry.";
    private const string TransitionConflictMessage = "Another document request change completed first. Refresh and retry.";

    public async Task<DocumentRequestReadModel> CreateDraftAsync(
        DocumentRequestCreateInput input,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(input.WorkItemId > 0, "A valid WorkItem is required.");
        Finance.Require(input.DocumentRequirementTemplateId > 0, "A valid document requirement template is required.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);

        var requestId = await InSerializableTransactionAsync(async () =>
        {
            var serviceId = await GetAuthoritativeServiceIdAsync(input.WorkItemId, cancellationToken);
            await LockServiceAsync(serviceId, cancellationToken);
            await LockWorkItemAsync(input.WorkItemId, cancellationToken);

            var authoritativeServiceId = await GetAuthoritativeServiceIdAsync(input.WorkItemId, cancellationToken);
            Finance.Require(authoritativeServiceId == serviceId,
                "The WorkItem relationship changed while the request was being created. Refresh and retry.");

            var template = await db.DocumentRequirementTemplates
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == input.DocumentRequirementTemplateId, cancellationToken)
                ?? throw new BusinessException("The selected document requirement template was not found.");

            Finance.Require(template.ServiceId == serviceId,
                "The selected document requirement template belongs to another service.");
            Finance.Require(template.IsActive, "Only an active document requirement template can be used for a request.");

            var activeItems = template.Items
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Id)
                .ToArray();
            Finance.Require(activeItems.Length > 0,
                "The selected active template must contain at least one active requirement item.");

            var hasCurrentRequest = await db.DocumentRequests.AnyAsync(x =>
                x.WorkItemId == input.WorkItemId &&
                x.Status != DocumentRequestStatus.Cancelled &&
                x.Status != DocumentRequestStatus.Superseded, cancellationToken);
            Finance.Require(!hasCurrentRequest,
                "This WorkItem already has a current document request. Cancel or supersede it before creating another.");

            var maximumRevision = await db.DocumentRequests
                .Where(x => x.WorkItemId == input.WorkItemId)
                .MaxAsync(x => (int?)x.Revision, cancellationToken) ?? 0;
            Finance.Require(maximumRevision < int.MaxValue,
                "No further document request revision can be allocated for this WorkItem.");

            var request = new DocumentRequest
            {
                WorkItemId = input.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = maximumRevision + 1,
                Status = DocumentRequestStatus.Draft
            };

            var occurredAt = clock.UtcNow;
            var templateReason = $"Created from active template {template.TemplateKey} v{template.TemplateVersion}.";
            request.StatusHistory.Add(new DocumentRequestStatusHistory
            {
                PreviousStatus = null,
                NewStatus = DocumentRequestStatus.Draft,
                Action = "CreatedFromTemplate",
                Reason = templateReason,
                Actor = actor,
                Source = source,
                OccurredAt = occurredAt,
                DocumentRequest = request
            });

            foreach (var templateItem in activeItems)
            {
                var item = new DocumentRequestItem
                {
                    DocumentRequirementTemplateItemId = templateItem.Id,
                    RequirementKey = templateItem.RequirementKey,
                    RequirementName = templateItem.Name,
                    RequirementDescription = templateItem.Description,
                    IsRequired = templateItem.IsRequired,
                    Wave = templateItem.Wave,
                    DisplayOrder = templateItem.DisplayOrder,
                    Status = DocumentRequestItemStatus.Missing
                };
                item.StatusHistory.Add(new DocumentRequestItemStatusHistory
                {
                    PreviousStatus = null,
                    NewStatus = DocumentRequestItemStatus.Missing,
                    Action = "CreatedFromTemplate",
                    Reason = "Requirement snapshot created from the active template item.",
                    Actor = actor,
                    Source = source,
                    OccurredAt = occurredAt,
                    DocumentRequestItem = item
                });
                request.Items.Add(item);
            }

            db.DocumentRequests.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            return request.Id;
        }, CreateConflictMessage, cancellationToken);

        return await GetByIdAsync(requestId, cancellationToken)
            ?? throw new BusinessException("The document request was created but could not be reloaded.");
    }

    public async Task<DocumentRequestReadModel> TransitionAsync(
        int requestId,
        DocumentRequestStatusTransitionInput input,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(requestId > 0, "A valid document request is required.");
        Finance.Require(input.ExpectedVersion > 0, "A valid document request version is required.");
        Finance.Require(Enum.IsDefined(input.NewStatus), "Select a valid document request status.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeReason(input.Reason);

        await InSerializableTransactionAsync(async () =>
        {
            await LockRequestAsync(requestId, cancellationToken);
            var request = await db.DocumentRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken)
                ?? throw new BusinessException("The document request was not found.");

            Finance.Require(request.Version == input.ExpectedVersion,
                "The document request changed. Refresh before applying this transition.");
            EnsureRequestTransitionAllowed(request.Status, input.NewStatus);

            var previousStatus = request.Status;
            request.Status = input.NewStatus;
            db.DocumentRequestStatusHistories.Add(new DocumentRequestStatusHistory
            {
                DocumentRequestId = request.Id,
                PreviousStatus = previousStatus,
                NewStatus = input.NewStatus,
                Action = RequestTransitionAction(previousStatus, input.NewStatus),
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = clock.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, TransitionConflictMessage, cancellationToken);

        return await GetByIdAsync(requestId, cancellationToken)
            ?? throw new BusinessException("The document request could not be reloaded after its transition.");
    }

    public async Task<DocumentRequestItemReadModel> TransitionItemAsync(
        int itemId,
        DocumentRequestItemStatusTransitionInput input,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(itemId > 0, "A valid document request item is required.");
        Finance.Require(input.ExpectedVersion > 0, "A valid document request item version is required.");
        Finance.Require(Enum.IsDefined(input.NewStatus), "Select a valid document request item status.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var reason = NormalizeReason(input.Reason);

        var requestItemId = await InSerializableTransactionAsync(async () =>
        {
            var requestId = await db.DocumentRequestItems.AsNoTracking()
                .Where(x => x.Id == itemId)
                .Select(x => (int?)x.DocumentRequestId)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new BusinessException("The document request item was not found.");

            await LockRequestAsync(requestId, cancellationToken);
            await LockRequestItemAsync(itemId, cancellationToken);
            var item = await db.DocumentRequestItems
                .Include(x => x.DocumentRequest)
                .SingleOrDefaultAsync(x => x.Id == itemId, cancellationToken)
                ?? throw new BusinessException("The document request item was not found.");

            Finance.Require(item.Version == input.ExpectedVersion,
                "The document request item changed. Refresh before applying this transition.");
            Finance.Require(item.DocumentRequest.Status is DocumentRequestStatus.Draft
                or DocumentRequestStatus.ReadyToSend
                or DocumentRequestStatus.Paused,
                "Checklist status changes are unavailable after provider/evidence processing or on a terminal request.");
            EnsureItemTransitionAllowed(item.Status, input.NewStatus);

            var previousStatus = item.Status;
            item.Status = input.NewStatus;
            db.DocumentRequestItemStatusHistories.Add(new DocumentRequestItemStatusHistory
            {
                DocumentRequestItemId = item.Id,
                PreviousStatus = previousStatus,
                NewStatus = input.NewStatus,
                Action = ItemTransitionAction(previousStatus, input.NewStatus),
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = clock.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            return item.Id;
        }, TransitionConflictMessage, cancellationToken);

        var request = await GetByIdAsync(
            await db.DocumentRequestItems.AsNoTracking()
                .Where(x => x.Id == requestItemId)
                .Select(x => x.DocumentRequestId)
                .SingleAsync(cancellationToken), cancellationToken);
        return request?.Items.Single(x => x.Id == requestItemId)
            ?? throw new BusinessException("The document request item could not be reloaded after its transition.");
    }

    public Task<DocumentRequestReadModel?> GetCurrentForWorkItemAsync(
        int workItemId,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(workItemId > 0, "A valid WorkItem is required.");
        return GetRequestReadModelAsync(
            x => x.WorkItemId == workItemId
                && x.Status != DocumentRequestStatus.Cancelled
                && x.Status != DocumentRequestStatus.Superseded,
            cancellationToken);
    }

    public Task<DocumentRequestReadModel?> GetByIdAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        if (requestId <= 0) return Task.FromResult<DocumentRequestReadModel?>(null);
        return GetRequestReadModelAsync(
            x => x.Id == requestId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentRequestReadModel>> GetRevisionsForWorkItemAsync(
        int workItemId,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(workItemId > 0, "A valid WorkItem is required.");
        Finance.Require(await db.WorkItems.AsNoTracking().AnyAsync(x => x.Id == workItemId, cancellationToken),
            "The WorkItem was not found.");

        var requests = await RequestQuery()
            .Where(x => x.WorkItemId == workItemId)
            .OrderBy(x => x.Revision)
            .ToListAsync(cancellationToken);
        return requests.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<DocumentRequestTemplateOptionReadModel>> GetAvailableTemplatesForWorkItemAsync(
        int workItemId,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(workItemId > 0, "A valid WorkItem is required.");
        var serviceId = await GetAuthoritativeServiceIdAsync(workItemId, cancellationToken);
        return await db.DocumentRequirementTemplates.AsNoTracking()
            .Where(x => x.ServiceId == serviceId && x.IsActive && x.Items.Any(i => i.IsActive))
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.TemplateKey)
            .ThenBy(x => x.TemplateVersion)
            .Select(x => new DocumentRequestTemplateOptionReadModel(
                x.Id,
                x.ServiceId,
                x.TemplateKey,
                x.TemplateVersion,
                x.Name,
                x.IsActive,
                x.IsDefault))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<DocumentRequest> RequestQuery() =>
        db.DocumentRequests.AsNoTracking().AsSplitQuery()
            .Include(x => x.DocumentRequirementTemplate)
            .Include(x => x.Items).ThenInclude(x => x.StatusHistory)
            .Include(x => x.StatusHistory);

    private async Task<DocumentRequestReadModel?> GetRequestReadModelAsync(
        Expression<Func<DocumentRequest, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var request = await RequestQuery()
            .Where(predicate)
            .SingleOrDefaultAsync(cancellationToken);
        return request is null ? null : ToReadModel(request);
    }

    private async Task<int> GetAuthoritativeServiceIdAsync(int workItemId, CancellationToken cancellationToken) =>
        await db.WorkItems.AsNoTracking()
            .Where(x => x.Id == workItemId)
            .Select(x => (int?)x.BillingRecord.Engagement.ServiceId)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new BusinessException("The WorkItem was not found.");

    private async Task LockServiceAsync(int serviceId, CancellationToken cancellationToken)
    {
        await LockRowAsync("Services", serviceId, cancellationToken,
            "The authoritative Service was not found.");
    }

    private async Task LockWorkItemAsync(int workItemId, CancellationToken cancellationToken)
    {
        await LockRowAsync("WorkItems", workItemId, cancellationToken,
            "The WorkItem was not found.");
    }

    private async Task LockRequestAsync(int requestId, CancellationToken cancellationToken)
    {
        await LockRowAsync("DocumentRequests", requestId, cancellationToken,
            "The document request was not found.");
    }

    private async Task LockRequestItemAsync(int itemId, CancellationToken cancellationToken)
    {
        await LockRowAsync("DocumentRequestItems", itemId, cancellationToken,
            "The document request item was not found.");
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

    private static void EnsureRequestTransitionAllowed(
        DocumentRequestStatus current,
        DocumentRequestStatus target)
    {
        var allowed = current switch
        {
            DocumentRequestStatus.Draft => target is DocumentRequestStatus.ReadyToSend
                or DocumentRequestStatus.Paused
                or DocumentRequestStatus.Cancelled,
            DocumentRequestStatus.ReadyToSend => target is DocumentRequestStatus.Paused
                or DocumentRequestStatus.Cancelled,
            DocumentRequestStatus.Paused => target is DocumentRequestStatus.Draft
                or DocumentRequestStatus.ReadyToSend
                or DocumentRequestStatus.Cancelled,
            _ => false
        };
        Finance.Require(allowed,
            "That request transition is not available in Phase 3A. Provider/evidence-driven states remain deferred.");
    }

    private static void EnsureItemTransitionAllowed(
        DocumentRequestItemStatus current,
        DocumentRequestItemStatus target)
    {
        var allowed = current switch
        {
            DocumentRequestItemStatus.Missing => target is DocumentRequestItemStatus.NotRequired
                or DocumentRequestItemStatus.Waived,
            DocumentRequestItemStatus.NotRequired or DocumentRequestItemStatus.Waived =>
                target == DocumentRequestItemStatus.Missing,
            _ => false
        };
        Finance.Require(allowed,
            "That checklist transition is not available in Phase 3A. Requested/received states require later provider and evidence operations.");
    }

    private static string RequestTransitionAction(DocumentRequestStatus previous, DocumentRequestStatus next) =>
        $"{previous}To{next}";

    private static string ItemTransitionAction(DocumentRequestItemStatus previous, DocumentRequestItemStatus next) =>
        $"{previous}To{next}";

    private static string NormalizeActor(string? actor)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(actor), "An actor is required for document request operations.");
        var normalized = actor!.Trim();
        Finance.Require(normalized.Length <= 254, "Actor must be 254 characters or fewer.");
        return normalized;
    }

    private static string NormalizeSource(string? source)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(source), "A source is required for document request operations.");
        var normalized = source!.Trim();
        Finance.Require(normalized.Length <= 80, "Source must be 80 characters or fewer.");
        return normalized;
    }

    private static string NormalizeReason(string? reason)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(reason), "A reason is required for document request transitions.");
        var normalized = reason!.Trim();
        Finance.Require(normalized.Length <= 2000, "Reason must be 2,000 characters or fewer.");
        return normalized;
    }

    private static DocumentRequestReadModel ToReadModel(DocumentRequest request) => new(
        request.Id,
        request.WorkItemId,
        request.DocumentRequirementTemplateId,
        request.DocumentRequirementTemplate.TemplateKey,
        request.DocumentRequirementTemplate.TemplateVersion,
        request.DocumentRequirementTemplate.Name,
        request.Revision,
        request.Status,
        request.Version,
        request.Items
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => new DocumentRequestItemReadModel(
                x.Id,
                x.DocumentRequirementTemplateItemId,
                x.RequirementKey,
                x.RequirementName,
                x.RequirementDescription,
                x.IsRequired,
                x.Wave,
                x.DisplayOrder,
                x.Status,
                x.Version,
                x.StatusHistory
                    .OrderBy(h => h.OccurredAt)
                    .ThenBy(h => h.Id)
                    .Select(h => new DocumentRequestItemStatusHistoryReadModel(
                        h.Id,
                        h.PreviousStatus,
                        h.NewStatus,
                        h.Action,
                        h.Reason,
                        h.Actor,
                        h.Source,
                        h.OccurredAt,
                        h.Version))
                    .ToArray()))
            .ToArray(),
        request.StatusHistory
            .OrderBy(h => h.OccurredAt)
            .ThenBy(h => h.Id)
            .Select(h => new DocumentRequestStatusHistoryReadModel(
                h.Id,
                h.PreviousStatus,
                h.NewStatus,
                h.Action,
                h.Reason,
                h.Actor,
                h.Source,
                h.OccurredAt,
                h.Version))
            .ToArray());
}
