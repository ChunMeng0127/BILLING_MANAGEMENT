using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Services;

/// <summary>
/// Owns the current worker-delivery workflow and its immutable change history.
/// It deliberately does not change WorkItem, BillingRecord, invoice or payment state.
/// </summary>
public sealed class AssignmentWorkflowService(AppDbContext db, BusinessClock clock)
{
    public static bool IsActive(WorkerAssignment assignment) =>
        !assignment.IsCancelled && assignment.WorkItem.BillingRecord.Status != BillingStatus.Cancelled
        && !assignment.IsHidden && assignment.CurrentWorkflowStatus != WorkflowStatus.Completed;

    /// <summary>
    /// Returns whether this assignment was responsible for a report in the selected
    /// historical week. BillingRecord dates are deliberately not consulted here.
    ///
    /// Completion remains responsible for the week in which it happened; the next
    /// week is the first week that is no longer required. Hiding takes effect in
    /// its transition week, while an unhide/reopen transition resumes that week.
    /// </summary>
    public static bool RequiresWeeklyReport(WorkerAssignment assignment, DateOnly weekStart, BusinessClock clock)
    {
        if (weekStart > clock.CurrentWeekStart || assignment.CreatedAt == default)
            return false;

        var assignmentWeek = BusinessClock.WeekStart(DateOnly.FromDateTime(clock.Local(assignment.CreatedAt)));
        if (weekStart < assignmentWeek)
            return false;

        var responsible = true;
        var hasWorkflowHistory = assignment.WorkflowHistory.Count > 0;
        foreach (var change in assignment.WorkflowHistory.OrderBy(x => x.ChangedAt))
        {
            var changeWeek = BusinessClock.WeekStart(DateOnly.FromDateTime(clock.Local(change.ChangedAt)));
            if (changeWeek > weekStart)
                break;

            if (change.Action == WorkflowHistoryAction.Hidden)
            {
                responsible = false;
                continue;
            }

            if (change.Action == WorkflowHistoryAction.Unhidden)
            {
                responsible = true;
                continue;
            }

            if (change.NewWorkflowStatus == WorkflowStatus.Completed)
            {
                // The completion week still has a reporting obligation.
                if (changeWeek < weekStart)
                    responsible = false;
            }
            else if (change.PreviousWorkflowStatus == WorkflowStatus.Completed)
            {
                // Reopening resumes responsibility in the transition week.
                responsible = true;
            }
        }

        // Legacy rows may have current cancellation/completion/hidden state but
        // no workflow history. Use their last audit timestamp as the transition.
        if (!hasWorkflowHistory)
        {
            if (assignment.IsHidden)
                responsible = weekStart < BusinessClock.WeekStart(DateOnly.FromDateTime(clock.Local(assignment.HiddenAt ?? assignment.UpdatedAt)));
            if (assignment.CurrentWorkflowStatus == WorkflowStatus.Completed)
                responsible = weekStart <= BusinessClock.WeekStart(DateOnly.FromDateTime(clock.Local(assignment.UpdatedAt)));
        }

        if (assignment.IsCancelled)
            responsible &= weekStart < BusinessClock.WeekStart(DateOnly.FromDateTime(clock.Local(assignment.UpdatedAt)));

        var billing = assignment.WorkItem?.BillingRecord;
        if (billing?.Status == BillingStatus.Cancelled)
            responsible &= weekStart < BusinessClock.WeekStart(DateOnly.FromDateTime(clock.Local(billing.UpdatedAt)));

        return responsible;
    }

    public static void ValidateWorkflow(WorkflowStatus status, int? version)
    {
        Finance.Require(Enum.IsDefined(status), "Select a valid workflow status.");
        if (Finance.RequiresWorkflowVersion(status))
            Finance.Require(version is > 0, "Enter a positive version for Queries Sent or Draft Management Report Sent.");
        else
            Finance.Require(version is null, "Only Queries Sent and Draft Management Report Sent may have a version.");
    }

    public async Task<int> NextVersionAsync(int assignmentId, WorkflowStatus status)
    {
        if (!Finance.RequiresWorkflowVersion(status)) return 0;
        var history = await db.WorkerAssignmentWorkflowHistories
            .Where(x => x.WorkerAssignmentId == assignmentId && x.NewWorkflowStatus == status && x.NewWorkflowVersion != null)
            .Select(x => x.NewWorkflowVersion!.Value).ToListAsync();
        var reports = await db.WeeklyProgressReports
            .Where(x => x.WorkerAssignmentId == assignmentId && x.WorkflowStatusAtSubmission == status && x.WorkflowVersionAtSubmission != null)
            .Select(x => x.WorkflowVersionAtSubmission!.Value).ToListAsync();
        return Math.Max(history.Concat(reports).DefaultIfEmpty(0).Max() + 1, 1);
    }

    public async Task ApplyWorkflowAsync(WorkerAssignment assignment, WorkflowStatus status, int? version, WorkflowHistoryAction action)
    {
        ValidateWorkflow(status, version);
        var changed = assignment.CurrentWorkflowStatus != status || assignment.CurrentWorkflowVersion != version;
        if (!changed) return;
        if (Finance.RequiresWorkflowVersion(status))
        {
            var latest = await db.WorkerAssignmentWorkflowHistories
                .Where(x => x.WorkerAssignmentId == assignment.Id && x.NewWorkflowStatus == status && x.NewWorkflowVersion != null)
                .Select(x => (int?)x.NewWorkflowVersion).MaxAsync() ?? 0;
            // Repeating the current stage/version is allowed for a weekly update. A new stage change must advance it.
            Finance.Require(version > latest || (assignment.CurrentWorkflowStatus == status && assignment.CurrentWorkflowVersion == version),
                "Workflow version must be greater than the latest recorded version for this assignment.");
        }
        var wasCompleted = assignment.CurrentWorkflowStatus == WorkflowStatus.Completed;
        db.WorkerAssignmentWorkflowHistories.Add(new WorkerAssignmentWorkflowHistory
        {
            WorkerAssignmentId = assignment.Id,
            PreviousWorkflowStatus = assignment.CurrentWorkflowStatus,
            PreviousWorkflowVersion = assignment.CurrentWorkflowVersion,
            NewWorkflowStatus = status,
            NewWorkflowVersion = version,
            Action = action,
            ChangedAt = clock.UtcNow
        });
        assignment.CurrentWorkflowStatus = status;
        assignment.CurrentWorkflowVersion = version;
        if (wasCompleted && status != WorkflowStatus.Completed)
            assignment.ReportingResumedFromWeek = clock.CurrentWeekStart;
    }

    public void SetHidden(WorkerAssignment assignment, bool hidden, string actor)
    {
        Finance.Require(!assignment.IsCancelled && assignment.WorkItem.BillingRecord.Status != BillingStatus.Cancelled,
            "Cancelled assignments and billing records cannot be changed.");
        Finance.Require(assignment.IsHidden != hidden, hidden ? "This assignment is already hidden." : "This assignment is not hidden.");
        db.WorkerAssignmentWorkflowHistories.Add(new WorkerAssignmentWorkflowHistory
        {
            WorkerAssignmentId = assignment.Id,
            PreviousWorkflowStatus = assignment.CurrentWorkflowStatus,
            PreviousWorkflowVersion = assignment.CurrentWorkflowVersion,
            NewWorkflowStatus = assignment.CurrentWorkflowStatus,
            NewWorkflowVersion = assignment.CurrentWorkflowVersion,
            Action = hidden ? WorkflowHistoryAction.Hidden : WorkflowHistoryAction.Unhidden,
            ChangedAt = clock.UtcNow
        });
        assignment.IsHidden = hidden;
        assignment.HiddenAt = hidden ? clock.UtcNow : null;
        assignment.HiddenBy = hidden ? actor : null;
        if (!hidden) assignment.ReportingResumedFromWeek = clock.CurrentWeekStart;
    }

    public async Task BatchAsync(AccessProfile access, BatchWorkflowForm form)
    {
        await FinancialTransaction.Serializable(db, async () =>
        {
            Finance.Require(access.IsStaff || access.IsWorker, "Only staff or the assigned worker can update workflow status.");
            var ids = form.AssignmentIds.Distinct().ToArray();
            Finance.Require(ids.Length is > 0 and <= 100, "Select between 1 and 100 assignments to update.");
            Finance.Require(ids.All(id => form.Versions.TryGetValue(id, out var version) && version > 0), "One or more selected assignments are missing a current version. Refresh and try again.");
            Finance.Require(Enum.IsDefined(form.Action), "Select a valid batch action.");
            if (form.Action == BatchWorkflowAction.UpdateWorkflow) ValidateWorkflow(form.WorkflowStatus, form.WorkflowVersion);
            if (form.Action is BatchWorkflowAction.Hide or BatchWorkflowAction.Unhide)
                Finance.Require(access.IsStaff, "Only Admin and InternalUser can hide or unhide assignments.");

            var assignments = await db.WorkerAssignments
                .Where(x => ids.Contains(x.Id))
                .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
                .ToListAsync();
            Finance.Require(assignments.Count == ids.Length, "One or more selected assignments are no longer available.");
            foreach (var assignment in assignments)
            {
                var workerOwns = access.IsWorker && assignment.WorkerId == access.WorkerId;
                Finance.Require(access.IsStaff || workerOwns, "One or more selected assignments are not in your scope.");
                Finance.Require(assignment.Version == form.Versions[assignment.Id], "One or more assignments changed. Refresh and try again.");
                Finance.Require(!assignment.IsCancelled && assignment.WorkItem.BillingRecord.Status != BillingStatus.Cancelled,
                    "Cancelled assignments and billing records cannot be changed.");
                if (access.IsWorker)
                    Finance.Require(IsActive(assignment), "Workers may update only their own active assignments.");

                switch (form.Action)
                {
                    case BatchWorkflowAction.UpdateWorkflow:
                        if (assignment.CurrentWorkflowStatus == WorkflowStatus.Completed && form.WorkflowStatus != WorkflowStatus.Completed)
                            Finance.Require(access.IsStaff, "Workers cannot reopen completed work.");
                        await ApplyWorkflowAsync(assignment, form.WorkflowStatus, form.WorkflowVersion, WorkflowHistoryAction.BatchUpdate);
                        break;
                    case BatchWorkflowAction.Hide:
                        SetHidden(assignment, true, access.UserId);
                        break;
                    case BatchWorkflowAction.Unhide:
                        SetHidden(assignment, false, access.UserId);
                        break;
                }
            }
            await db.SaveChangesAsync();
        });
    }
}
