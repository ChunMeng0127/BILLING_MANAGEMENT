using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingControl.Services;

/// <summary>
/// The deliberately minimal caller input for a manual send. Content,
/// destination, account binding and request membership are never accepted
/// from this boundary; they are reconstructed from the durable queue record.
/// </summary>
public sealed record WhatsAppOutboundSendInput(
    int WhatsAppOutboundMessageId,
    long ExpectedMessageVersion,
    string Actor,
    string Source);

/// <summary>
/// The durable outcome of one manual send operation. The existing Phase 10A
/// model has no AcceptedAt or LastAttemptAt columns: the completed attempt's
/// CompletedAt and the message's ProviderTimestamp/UpdatedAt carry those
/// existing audit facts without changing the schema.
/// </summary>
public sealed record WhatsAppOutboundSendResult(
    int WhatsAppOutboundMessageId,
    int WhatsAppOutboundMessageAttemptId,
    int AttemptNumber,
    long MessageVersion,
    WhatsAppSendDisposition Disposition,
    WhatsAppOutboundMessageState MessageState,
    DocumentRequestBatchStatus BatchStatus,
    string? ProviderMessageId,
    bool AlreadyAccepted);

/// <summary>
/// Owns the Phase 10C manual queued-message send boundary. A short
/// serializable claim transaction ends before IWhatsAppProvider.SendAsync is
/// called; a second short serializable transaction durably records the result
/// and, only for Accepted, activates the immutable document-request members.
/// </summary>
public sealed class WhatsAppOutboundSendService
{
    private const string MessageChangedMessage =
        "The queued WhatsApp message changed. Refresh before sending.";
    private const string MessageNotFoundMessage =
        "The queued WhatsApp message was not found.";
    private const string SendConflictMessage =
        "Another WhatsApp send operation is already processing this message. Refresh and retry.";
    private const string OpenAttemptMessage =
        "This WhatsApp message has an incomplete send attempt and cannot be sent again until it is reconciled.";
    private const string AmbiguousMessage =
        "This WhatsApp message has an ambiguous provider outcome and cannot be sent again until it is reconciled.";
    private const string RetryNotReadyMessage =
        "This WhatsApp message is not yet eligible for the requested manual retry.";
    private const string StaleSendMessage =
        "The queued WhatsApp message is stale and has been invalidated. No provider send was attempted.";
    private const string ResultConflictMessage =
        "The provider result could not be persisted consistently. The durable attempt remains open for reconciliation.";

    private readonly AppDbContext db;
    private readonly WhatsAppOutboundQueueService queueService;
    private readonly BusinessClock clock;
    private readonly IWhatsAppProvider provider;

    public WhatsAppOutboundSendService(
        AppDbContext db,
        WhatsAppOutboundQueueService queueService,
        BusinessClock clock,
        IWhatsAppProvider provider)
    {
        this.db = db ?? throw new ArgumentNullException(nameof(db));
        this.queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Convenience constructor for direct service tests. Production uses the
    /// DI constructor so Queue and Send share the configured provider and
    /// application Data Protection/runtime services.
    /// </summary>
    public WhatsAppOutboundSendService(
        AppDbContext db,
        BusinessClock clock,
        IWhatsAppProvider provider)
        : this(
            db,
            new WhatsAppOutboundQueueService(db, clock),
            clock,
            provider)
    {
    }

    public async Task<WhatsAppOutboundSendResult> SendAsync(
        WhatsAppOutboundSendInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Finance.Require(input.WhatsAppOutboundMessageId > 0,
            "A valid WhatsApp outbound message is required.");
        Finance.Require(input.ExpectedMessageVersion > 0,
            "A valid WhatsApp outbound message version is required.");
        var actor = RequiredText(input.Actor, 254, "Actor");
        var source = RequiredText(input.Source, 80, "Source");

        var claim = await ClaimAsync(
            input.WhatsAppOutboundMessageId,
            input.ExpectedMessageVersion,
            actor,
            source,
            cancellationToken);
        if (claim.FailureMessage is not null)
            throw new BusinessException(claim.FailureMessage);
        if (claim.AlreadyAccepted is not null)
            return claim.AlreadyAccepted;

        var claimed = claim.Claim
            ?? throw new InvalidOperationException("The WhatsApp send claim was not created.");

        WhatsAppSendResult providerResult;
        try
        {
            providerResult = await provider.SendAsync(claimed.Request, cancellationToken)
                ?? throw new InvalidOperationException("The WhatsApp provider returned no result.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A provider-side timeout is an unknown external outcome. Persist
            // the conservative disposition and never issue a second call.
            providerResult = WhatsAppSendResult.Ambiguous(
                "ProviderException",
                "OperationCanceled");
        }
        catch (Exception exception)
        {
            // The durable claim proves that the external side-effect boundary
            // was entered. An unexpected exception therefore cannot become a
            // definite rejection or trigger an automatic resend.
            providerResult = WhatsAppSendResult.Ambiguous(
                "ProviderException",
                exception.GetType().Name);
        }

        return await PersistResultWithRetryAsync(
            claimed,
            providerResult,
            actor,
            source);
    }

    public Task<WhatsAppOutboundSendResult> SendAsync(
        int messageId,
        long expectedMessageVersion,
        string actor,
        string source,
        CancellationToken cancellationToken = default) =>
        SendAsync(new(
            messageId,
            expectedMessageVersion,
            actor,
            source), cancellationToken);

    public Task<WhatsAppOutboundSendResult> Send(
        WhatsAppOutboundSendInput input,
        CancellationToken cancellationToken = default) =>
        SendAsync(input, cancellationToken);

    private async Task<ClaimOutcome> ClaimAsync(
        int messageId,
        long expectedMessageVersion,
        string actor,
        string source,
        CancellationToken cancellationToken)
    {
        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                return await InSerializableTransactionAsync(
                    () => ClaimInsideTransactionAsync(
                        messageId,
                        expectedMessageVersion,
                        actor,
                        source,
                        cancellationToken),
                    cancellationToken);
            }
            catch (BusinessException exception) when (
                retry < 2 && exception.Message == SendConflictMessage)
            {
                db.ChangeTracker.Clear();
                await Task.Yield();
            }
        }

        throw new BusinessException(SendConflictMessage);
    }

    private async Task<ClaimOutcome> ClaimInsideTransactionAsync(
        int messageId,
        long expectedMessageVersion,
        string actor,
        string source,
        CancellationToken cancellationToken)
    {
        await LockRowAsync("WhatsAppOutboundMessages", messageId, cancellationToken,
            MessageNotFoundMessage);
        var message = await LoadMessageAsync(messageId, cancellationToken)
            ?? throw new BusinessException(MessageNotFoundMessage);

        if (message.State == WhatsAppOutboundMessageState.Accepted)
        {
            return new(ToAlreadyAcceptedResult(message));
        }

        Finance.Require(message.Version == expectedMessageVersion,
            MessageChangedMessage);

        var openAttempt = message.Attempts
            .Where(x => x.CompletedAt is null)
            .OrderByDescending(x => x.AttemptNumber)
            .FirstOrDefault();
        if (openAttempt is not null)
        {
            if (message.State == WhatsAppOutboundMessageState.Ambiguous)
                throw new BusinessException(AmbiguousMessage);
            throw new BusinessException(OpenAttemptMessage);
        }

        if (message.State == WhatsAppOutboundMessageState.Ambiguous)
            throw new BusinessException(AmbiguousMessage);
        if (message.State == WhatsAppOutboundMessageState.Cancelled)
            throw new BusinessException("This WhatsApp message has been cancelled and cannot be sent.");
        Finance.Require(message.State is WhatsAppOutboundMessageState.Queued or
                        WhatsAppOutboundMessageState.DefinitelyRejected,
            "This WhatsApp message is not eligible for a manual send.");

        var now = clock.UtcNow;
        if (message.State == WhatsAppOutboundMessageState.DefinitelyRejected)
        {
            Finance.Require(message.NextAttemptAt is null || message.NextAttemptAt <= now,
                RetryNotReadyMessage);
        }

        Finance.Require(message.DocumentRequestBatch is not null &&
                        message.DocumentRequestBatch.Status == DocumentRequestBatchStatus.Queued,
            "The queued WhatsApp batch is no longer eligible for a manual send.");

        await LockSnapshotRowsAsync(message, cancellationToken);

        WhatsAppOutboundRequest request;
        try
        {
            // This is the last authoritative check before the durable claim.
            // It reuses Phase 10B's validator and compares all persisted
            // routing/content/member facts against the immutable queue.
            request = await queueService.RevalidateQueuedMessageAndBuildRequestAsync(
                message,
                cancellationToken);
        }
        catch (BusinessException exception)
        {
            await InvalidateQueuedMessageAsync(message, actor, source, exception.Message, cancellationToken);
            // Return a committed failure outcome from the transaction. The
            // caller throws only after the invalidation transaction commits.
            return new(null, null, StaleSendMessage);
        }

        var attemptNumber = Math.Max(
            message.AttemptCount,
            message.Attempts.Count == 0 ? 0 : message.Attempts.Max(x => x.AttemptNumber)) + 1;
        var attempt = new WhatsAppOutboundMessageAttempt
        {
            WhatsAppOutboundMessageId = message.Id,
            AttemptNumber = attemptNumber,
            StartedAt = now,
            CorrelationId = message.CorrelationId,
            Actor = actor,
            Source = source
        };

        // A retry after an explicit DefinitelyRejected result claims a new
        // attempt but does not retry automatically. The claim itself remains
        // Queued until the single provider result is durably recorded.
        message.State = WhatsAppOutboundMessageState.Queued;
        message.AttemptCount = attemptNumber;
        message.NextAttemptAt = null;
        message.ProviderMessageId = null;
        message.LastErrorCategory = null;
        message.LastErrorCode = null;
        message.ProviderRetryAfterUntil = null;
        message.ProviderTimestamp = null;
        db.WhatsAppOutboundMessageAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        return new(null, new SendClaim(message.Id, attempt.Id, attemptNumber, request));
    }

    private async Task<WhatsAppOutboundSendResult> PersistResultWithRetryAsync(
        SendClaim claim,
        WhatsAppSendResult providerResult,
        string actor,
        string source)
    {
        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                // The provider result is already known. A caller cancellation
                // must not prevent the durable result from being recorded.
                return await InSerializableTransactionAsync(
                    () => PersistResultInsideTransactionAsync(
                        claim,
                        providerResult,
                        actor,
                        source),
                    CancellationToken.None);
            }
            catch (BusinessException exception) when (
                retry < 2 &&
                (exception.Message == ResultConflictMessage || exception.Message == SendConflictMessage))
            {
                db.ChangeTracker.Clear();
                await Task.Yield();
            }
        }

        throw new BusinessException(ResultConflictMessage);
    }

    private async Task<WhatsAppOutboundSendResult> PersistResultInsideTransactionAsync(
        SendClaim claim,
        WhatsAppSendResult providerResult,
        string actor,
        string source)
    {
        await LockRowAsync("WhatsAppOutboundMessages", claim.MessageId, CancellationToken.None,
            MessageNotFoundMessage);
        var message = await LoadMessageAsync(claim.MessageId, CancellationToken.None)
            ?? throw new BusinessException(MessageNotFoundMessage);
        var attempt = message.Attempts.SingleOrDefault(x => x.Id == claim.AttemptId)
            ?? throw new BusinessException(ResultConflictMessage);
        await LockRowAsync("WhatsAppOutboundMessageAttempts", attempt.Id, CancellationToken.None,
            ResultConflictMessage);

        // A database/result persistence retry or an application-level replay
        // must return the durable prior outcome without calling the provider.
        if (attempt.CompletedAt is not null)
            return ToResult(message, attempt, AlreadyAccepted: message.State == WhatsAppOutboundMessageState.Accepted);

        Finance.Require(message.State == WhatsAppOutboundMessageState.Queued,
            ResultConflictMessage);
        Finance.Require(message.AttemptCount == claim.AttemptNumber,
            ResultConflictMessage);
        await LockSnapshotRowsAsync(message, CancellationToken.None);

        var completedAt = clock.UtcNow;
        var retryAfterUntil = RetryAfterUntil(completedAt, providerResult.RetryAfter);
        var errorCategory = BoundedOptional(providerResult.ErrorCategory, 80, nameof(providerResult.ErrorCategory));
        var errorCode = BoundedOptional(providerResult.ErrorCode, 254, nameof(providerResult.ErrorCode));
        var providerMessageId = BoundedOptional(providerResult.ProviderMessageId, 254, nameof(providerResult.ProviderMessageId));
        var providerTimestamp = providerResult.ProviderTimestamp?.UtcDateTime;

        switch (providerResult.Disposition)
        {
            case WhatsAppSendDisposition.Accepted:
                message.State = WhatsAppOutboundMessageState.Accepted;
                message.ProviderMessageId = providerMessageId;
                message.LastErrorCategory = null;
                message.LastErrorCode = null;
                message.ProviderRetryAfterUntil = null;
                message.NextAttemptAt = null;
                message.ProviderTimestamp = providerTimestamp;
                ActivateAcceptedDocumentRequests(message, actor, source, completedAt);
                break;

            case WhatsAppSendDisposition.DefinitelyRejected:
                message.State = WhatsAppOutboundMessageState.DefinitelyRejected;
                message.ProviderMessageId = null;
                message.LastErrorCategory = errorCategory;
                message.LastErrorCode = errorCode;
                message.ProviderRetryAfterUntil = retryAfterUntil;
                message.NextAttemptAt = retryAfterUntil;
                message.ProviderTimestamp = providerTimestamp;
                break;

            case WhatsAppSendDisposition.Ambiguous:
                message.State = WhatsAppOutboundMessageState.Ambiguous;
                message.ProviderMessageId = providerMessageId;
                message.LastErrorCategory = errorCategory;
                message.LastErrorCode = errorCode;
                message.ProviderRetryAfterUntil = retryAfterUntil;
                message.NextAttemptAt = retryAfterUntil;
                message.ProviderTimestamp = providerTimestamp;
                break;

            default:
                throw new BusinessException(ResultConflictMessage);
        }

        await CompleteAttemptAsync(
            attempt,
            completedAt,
            providerResult.Disposition.ToString(),
            providerMessageId,
            errorCategory,
            errorCode,
            retryAfterUntil,
            providerTimestamp,
            actor);
        await db.SaveChangesAsync(CancellationToken.None);
        return ToResult(
            message,
            attempt,
            providerResult.Disposition,
            AlreadyAccepted: false);
    }

    private async Task CompleteAttemptAsync(
        WhatsAppOutboundMessageAttempt attempt,
        DateTime completedAt,
        string disposition,
        string? providerMessageId,
        string? errorCategory,
        string? errorCode,
        DateTime? retryAfterUntil,
        DateTime? providerTimestamp,
        string actor)
    {
        // Phase 10A intentionally rejects ordinary edits to an attempt row.
        // Phase 10C's approved lifecycle is the sole controlled completion
        // path: the row is locked, still open, and updated in this same
        // serializable result transaction. This keeps the Phase 10A
        // append-only guard intact for all other callers without changing the
        // existing schema/model.
        var nextVersion = checked(attempt.Version + 1);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "WhatsAppOutboundMessageAttempts"
            SET "CompletedAt" = {completedAt},
                "Disposition" = {disposition},
                "ProviderMessageId" = {providerMessageId},
                "ErrorCategory" = {errorCategory},
                "ErrorCode" = {errorCode},
                "RetryAfterUntil" = {retryAfterUntil},
                "ProviderTimestamp" = {providerTimestamp},
                "UpdatedAt" = {completedAt},
                "UpdatedBy" = {actor},
                "Version" = {nextVersion}
            WHERE "Id" = {attempt.Id}
              AND "WhatsAppOutboundMessageId" = {attempt.WhatsAppOutboundMessageId}
              AND "CompletedAt" IS NULL
              AND "Version" = {attempt.Version}
            """,
            CancellationToken.None);
        Finance.Require(affected == 1, ResultConflictMessage);

        attempt.CompletedAt = completedAt;
        attempt.Disposition = disposition;
        attempt.ProviderMessageId = providerMessageId;
        attempt.ErrorCategory = errorCategory;
        attempt.ErrorCode = errorCode;
        attempt.RetryAfterUntil = retryAfterUntil;
        attempt.ProviderTimestamp = providerTimestamp;
        attempt.UpdatedAt = completedAt;
        attempt.UpdatedBy = actor;
        attempt.Version = nextVersion;
        db.Entry(attempt).State = EntityState.Unchanged;
    }

    /// <summary>
    /// Phase 10C owns this narrow post-provider acceptance operation. It does
    /// not broaden DocumentRequestService's ordinary staff transition API:
    /// only the current snapshot member may move ReadyToSend -> Requested,
    /// and only its still-Missing snapshot items may move to Requested.
    /// </summary>
    private void ActivateAcceptedDocumentRequests(
        WhatsAppOutboundMessage message,
        string actor,
        string source,
        DateTime occurredAt)
    {
        var snapshot = message.WhatsAppOutboundBatchSnapshot
            ?? throw new BusinessException(ResultConflictMessage);
        var batch = message.DocumentRequestBatch
            ?? throw new BusinessException(ResultConflictMessage);
        Finance.Require(batch.Status == DocumentRequestBatchStatus.Queued,
            ResultConflictMessage);

        var snapshotRequestIds = snapshot.Requests
            .Select(x => x.DocumentRequestId)
            .OrderBy(x => x)
            .ToArray();
        var activeMemberIds = batch.Members
            .Where(x => x.IsActive)
            .Select(x => x.DocumentRequestId)
            .OrderBy(x => x)
            .ToArray();
        Finance.Require(snapshotRequestIds.SequenceEqual(activeMemberIds),
            ResultConflictMessage);

        var requestIds = snapshotRequestIds;
        var requests = db.DocumentRequests
            .Include(x => x.Items)
            .Where(x => requestIds.Contains(x.Id))
            .ToList();
        Finance.Require(requests.Count == requestIds.Length, ResultConflictMessage);

        foreach (var requestSnapshot in snapshot.Requests.OrderBy(x => x.DocumentRequestId))
        {
            var request = requests.SingleOrDefault(x => x.Id == requestSnapshot.DocumentRequestId)
                ?? throw new BusinessException(ResultConflictMessage);
            Finance.Require(request.Revision == requestSnapshot.RequestRevision &&
                            request.Version == requestSnapshot.RequestVersion &&
                            request.Status == DocumentRequestStatus.ReadyToSend &&
                            !db.DocumentRequests.Any(x =>
                                x.WorkItemId == request.WorkItemId &&
                                x.Revision > request.Revision),
                ResultConflictMessage);

            var previousStatus = request.Status;
            request.Status = DocumentRequestStatus.Requested;
            db.DocumentRequestStatusHistories.Add(new DocumentRequestStatusHistory
            {
                DocumentRequestId = request.Id,
                PreviousStatus = previousStatus,
                NewStatus = DocumentRequestStatus.Requested,
                Action = "WhatsAppProviderAccepted",
                Reason = "The immutable WhatsApp outbound message was accepted by the provider.",
                Actor = actor,
                Source = source,
                CorrelationId = message.CorrelationId,
                OccurredAt = occurredAt
            });

            foreach (var itemSnapshot in snapshot.Items
                         .Where(x => x.DocumentRequestId == request.Id)
                         .OrderBy(x => x.DocumentRequestItemId))
            {
                var item = request.Items.SingleOrDefault(x => x.Id == itemSnapshot.DocumentRequestItemId)
                    ?? throw new BusinessException(ResultConflictMessage);
                Finance.Require(item.RequirementName == itemSnapshot.RequirementNameSnapshot &&
                                item.IsRequired == itemSnapshot.IsRequired &&
                                item.DisplayOrder == itemSnapshot.DisplayOrder &&
                                itemSnapshot.RequestRevision == request.Revision,
                    ResultConflictMessage);

                if (item.Status != DocumentRequestItemStatus.Missing)
                    continue;

                item.Status = DocumentRequestItemStatus.Requested;
                db.DocumentRequestItemStatusHistories.Add(new DocumentRequestItemStatusHistory
                {
                    DocumentRequestItemId = item.Id,
                    PreviousStatus = DocumentRequestItemStatus.Missing,
                    NewStatus = DocumentRequestItemStatus.Requested,
                    Action = "WhatsAppProviderAccepted",
                    Reason = "The immutable WhatsApp outbound message was accepted by the provider.",
                    Actor = actor,
                    Source = source,
                    CorrelationId = message.CorrelationId,
                    OccurredAt = occurredAt
                });
            }
        }

        var previousBatchStatus = batch.Status;
        batch.Status = DocumentRequestBatchStatus.Sent;
        db.DocumentRequestBatchStatusHistories.Add(new DocumentRequestBatchStatusHistory
        {
            DocumentRequestBatchId = batch.Id,
            PreviousStatus = previousBatchStatus,
            NewStatus = DocumentRequestBatchStatus.Sent,
            Action = "WhatsAppProviderAccepted",
            Reason = "The WhatsApp provider accepted the immutable outbound message.",
            Actor = actor,
            Source = source,
            CorrelationId = message.CorrelationId,
            OccurredAt = occurredAt
        });
    }

    private async Task InvalidateQueuedMessageAsync(
        WhatsAppOutboundMessage message,
        string actor,
        string source,
        string reason,
        CancellationToken cancellationToken)
    {
        var batch = message.DocumentRequestBatch
            ?? throw new BusinessException(StaleSendMessage);
        Finance.Require(batch.Status == DocumentRequestBatchStatus.Queued,
            StaleSendMessage);

        message.State = WhatsAppOutboundMessageState.Cancelled;
        message.LastErrorCategory = "StaleAtSend";
        message.LastErrorCode = "SendRevalidationFailed";
        message.NextAttemptAt = null;
        message.ProviderRetryAfterUntil = null;
        batch.Status = DocumentRequestBatchStatus.Invalidated;
        db.DocumentRequestBatchStatusHistories.Add(new DocumentRequestBatchStatusHistory
        {
            DocumentRequestBatchId = batch.Id,
            PreviousStatus = DocumentRequestBatchStatus.Queued,
            NewStatus = DocumentRequestBatchStatus.Invalidated,
            Action = "SendRevalidationFailed",
            Reason = BoundedReason(reason),
            Actor = actor,
            Source = source,
            CorrelationId = message.CorrelationId,
            OccurredAt = clock.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WhatsAppOutboundMessage?> LoadMessageAsync(
        int messageId,
        CancellationToken cancellationToken)
    {
        return await db.WhatsAppOutboundMessages
            .Include(x => x.DocumentRequestBatch).ThenInclude(x => x.Members)
            .Include(x => x.WhatsAppOutboundBatchSnapshot).ThenInclude(x => x.Requests)
            .Include(x => x.WhatsAppOutboundBatchSnapshot).ThenInclude(x => x.Items)
            .Include(x => x.WhatsAppOutboundBatchSnapshot).ThenInclude(x => x.Participants)
            .Include(x => x.WhatsAppOutboundBatchSnapshot).ThenInclude(x => x.EngagementScopes)
            .Include(x => x.Attempts)
            .SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
    }

    private async Task LockSnapshotRowsAsync(
        WhatsAppOutboundMessage message,
        CancellationToken cancellationToken)
    {
        var snapshot = message.WhatsAppOutboundBatchSnapshot
            ?? throw new BusinessException(StaleSendMessage);

        await LockRowAsync("DocumentRequestBatches", message.DocumentRequestBatchId,
            cancellationToken, StaleSendMessage);
        await LockRowAsync("WhatsAppOutboundBatchSnapshots", message.WhatsAppOutboundBatchSnapshotId,
            cancellationToken, StaleSendMessage);
        await LockRowAsync("WhatsAppConversations", snapshot.WhatsAppConversationId,
            cancellationToken, StaleSendMessage);
        await LockRowAsync("Contacts", snapshot.ContactId,
            cancellationToken, StaleSendMessage);

        var requestIds = snapshot.Requests
            .Select(x => x.DocumentRequestId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        Finance.Require(requestIds.Length > 0, StaleSendMessage);
        var requestRows = await db.DocumentRequests.AsNoTracking()
            .Where(x => requestIds.Contains(x.Id))
            .Select(x => new { x.Id, x.WorkItemId })
            .ToListAsync(cancellationToken);
        foreach (var requestId in requestRows.Select(x => x.Id).Concat(requestIds).Distinct().OrderBy(x => x))
            await LockRowAsync("DocumentRequests", requestId, cancellationToken, StaleSendMessage);

        var workItemIds = requestRows.Select(x => x.WorkItemId).Distinct().ToArray();
        if (workItemIds.Length > 0)
        {
            var allRequestIds = await db.DocumentRequests.AsNoTracking()
                .Where(x => workItemIds.Contains(x.WorkItemId))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var requestId in allRequestIds.OrderBy(x => x))
                await LockRowAsync("DocumentRequests", requestId, cancellationToken, StaleSendMessage);
        }

        var itemIds = await db.DocumentRequestItems.AsNoTracking()
            .Where(x => requestIds.Contains(x.DocumentRequestId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var itemId in itemIds.OrderBy(x => x))
            await LockRowAsync("DocumentRequestItems", itemId, cancellationToken, StaleSendMessage);

        var customerIds = snapshot.Requests.Select(x => x.CustomerId).Distinct().OrderBy(x => x).ToArray();
        var linkIds = await db.ContactCustomerLinks.AsNoTracking()
            .Where(x => x.ContactId == snapshot.ContactId && customerIds.Contains(x.CustomerId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var linkId in linkIds.OrderBy(x => x))
            await LockRowAsync("ContactCustomerLinks", linkId, cancellationToken, StaleSendMessage);

        foreach (var engagementId in snapshot.Requests.Select(x => x.EngagementId).Distinct().OrderBy(x => x))
            await LockRowAsync("Engagements", engagementId, cancellationToken, StaleSendMessage);
        foreach (var customerId in customerIds)
            await LockRowAsync("Customers", customerId, cancellationToken, StaleSendMessage);
        foreach (var serviceId in snapshot.Requests.Select(x => x.ServiceId).Distinct().OrderBy(x => x))
            await LockRowAsync("Services", serviceId, cancellationToken, StaleSendMessage);

        var directAddressId = await db.WhatsAppConversations.AsNoTracking()
            .Where(x => x.Id == snapshot.WhatsAppConversationId)
            .Select(x => x.DirectContactWhatsAppAddressId)
            .SingleOrDefaultAsync(cancellationToken);
        if (directAddressId is int addressId)
            await LockRowAsync("ContactWhatsAppAddresses", addressId, cancellationToken, StaleSendMessage);

        foreach (var participantId in snapshot.Participants
                     .Select(x => x.WhatsAppConversationParticipantId)
                     .Distinct().OrderBy(x => x))
            await LockRowAsync("WhatsAppConversationParticipants", participantId, cancellationToken, StaleSendMessage);
        foreach (var scopeId in snapshot.EngagementScopes
                     .Select(x => x.WhatsAppConversationEngagementScopeId)
                     .Distinct().OrderBy(x => x))
            await LockRowAsync("WhatsAppConversationEngagementScopes", scopeId, cancellationToken, StaleSendMessage);
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
        Finance.Require(await command.ExecuteScalarAsync(cancellationToken) is not null,
            notFoundMessage);
    }

    private async Task<T> InSerializableTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(SendConflictMessage, exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(SendConflictMessage, exception);
        }
        catch (Exception exception) when (FinancialTransaction.IsSerializationConflict(exception))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(SendConflictMessage, exception);
        }
    }

    private static WhatsAppOutboundSendResult ToAlreadyAcceptedResult(
        WhatsAppOutboundMessage message)
    {
        var attempt = message.Attempts
            .Where(x => x.CompletedAt is not null &&
                        string.Equals(x.Disposition,
                            WhatsAppSendDisposition.Accepted.ToString(),
                            StringComparison.Ordinal))
            .OrderByDescending(x => x.AttemptNumber)
            .FirstOrDefault()
            ?? throw new BusinessException(ResultConflictMessage);
        Finance.Require(message.DocumentRequestBatch?.Status == DocumentRequestBatchStatus.Sent,
            ResultConflictMessage);
        return ToResult(message, attempt, AlreadyAccepted: true);
    }

    private static WhatsAppOutboundSendResult ToResult(
        WhatsAppOutboundMessage message,
        WhatsAppOutboundMessageAttempt attempt,
        bool AlreadyAccepted) =>
        ToResult(message, attempt, ParseDisposition(attempt.Disposition), AlreadyAccepted);

    private static WhatsAppOutboundSendResult ToResult(
        WhatsAppOutboundMessage message,
        WhatsAppOutboundMessageAttempt attempt,
        WhatsAppSendDisposition disposition,
        bool AlreadyAccepted) =>
        new(
            message.Id,
            attempt.Id,
            attempt.AttemptNumber,
            message.Version,
            disposition,
            message.State,
            message.DocumentRequestBatch?.Status ??
                throw new BusinessException(ResultConflictMessage),
            attempt.ProviderMessageId,
            AlreadyAccepted);

    private static WhatsAppSendDisposition ParseDisposition(string? disposition)
    {
        Finance.Require(Enum.TryParse<WhatsAppSendDisposition>(disposition, out var parsed) &&
                        Enum.IsDefined(parsed),
            ResultConflictMessage);
        return parsed;
    }

    private static DateTime? RetryAfterUntil(DateTime completedAt, TimeSpan? retryAfter)
    {
        if (!retryAfter.HasValue)
            return null;
        try
        {
            return completedAt.Add(retryAfter.Value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new BusinessException(ResultConflictMessage, exception);
        }
    }

    private static string? BoundedOptional(string? value, int maxLength, string label)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        Finance.Require(normalized.Length > 0 && normalized.Length <= maxLength,
            $"The provider {label} is invalid.");
        Finance.Require(!normalized.Contains('\r') && !normalized.Contains('\n'),
            $"The provider {label} is invalid.");
        return normalized;
    }

    private static string BoundedReason(string reason)
    {
        var normalized = reason.Trim();
        if (normalized.Length > 2000)
            normalized = normalized[..2000];
        return normalized.Length == 0 ? "Send-time revalidation failed." : normalized;
    }

    private static string RequiredText(string? value, int maxLength, string label)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(value), $"{label} is required.");
        var normalized = value!.Trim();
        Finance.Require(normalized.Length <= maxLength, $"{label} must be {maxLength} characters or fewer.");
        Finance.Require(!normalized.Contains('\r') && !normalized.Contains('\n'), $"{label} cannot contain line breaks.");
        return normalized;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record SendClaim(
        int MessageId,
        int AttemptId,
        int AttemptNumber,
        WhatsAppOutboundRequest Request);

    private sealed record ClaimOutcome(
        WhatsAppOutboundSendResult? AlreadyAccepted,
        SendClaim? Claim = null,
        string? FailureMessage = null);
}
