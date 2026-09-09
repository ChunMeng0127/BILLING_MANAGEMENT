using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Services;

public sealed class ProgressReportService(AppDbContext db, BusinessClock clock, AssignmentWorkflowService? assignedWorkflow = null)
{
    private readonly AssignmentWorkflowService workflow = assignedWorkflow ?? new AssignmentWorkflowService(db, clock);
    private static ProgressStatus LegacyStatus(WorkflowStatus status) => status switch
    {
        WorkflowStatus.AssignedNotStarted or WorkflowStatus.DocumentRequested or WorkflowStatus.DocumentReceived => ProgressStatus.NotStarted,
        WorkflowStatus.Completed => ProgressStatus.Completed,
        WorkflowStatus.AmendmentRevision => ProgressStatus.Blocked,
        _ => ProgressStatus.InProgress
    };

    public async Task<WeeklyProgressReport> SaveAsync(AccessProfile access, WeeklyProgressForm form)
    {
        return await FinancialTransaction.Serializable(db, async () =>
        {
            Finance.Require(access.IsStaff || access.IsWorker, "Only staff and the assigned worker can submit progress reports.");
            AssignmentWorkflowService.ValidateWorkflow(form.WorkflowStatus, form.WorkflowVersion);
            Finance.Percentage(form.ProgressPercent);
            var weekStart = form.WeekStart;
            Finance.Require(weekStart != default && weekStart.DayOfWeek == DayOfWeek.Monday, "Progress reports must start on a Monday.");
            Finance.Require(weekStart <= clock.CurrentWeekStart, "A future week cannot be submitted.");
            var workDone = form.WorkDone?.Trim() ?? "";
            var nextAction = form.NextAction?.Trim();
            Finance.Require(workDone.Length > 0 && workDone.Length <= 4000, "Describe the work completed before submitting the report.");
            if (form.WorkflowStatus != WorkflowStatus.Completed)
                Finance.Require(!string.IsNullOrWhiteSpace(nextAction), "Enter the next action unless the workflow status is Completed.");
            Finance.Require((nextAction?.Length ?? 0) <= 2000, "Next action must be 2,000 characters or fewer.");
            Finance.Require((form.IssuesOrBlockers?.Length ?? 0) <= 2000, "Issues or blockers must be 2,000 characters or fewer.");

            var assignment = await db.WorkerAssignments
                .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
                .SingleOrDefaultAsync(x => x.Id == form.WorkerAssignmentId);
            if (assignment == null) throw new BusinessException("The selected assignment was not found.");
            var inScope = access.IsStaff || (access.IsWorker && !assignment.IsCancelled && !assignment.IsHidden && assignment.WorkerId == access.WorkerId);
            Finance.Require(inScope, "You can submit progress only for your own active assignment.");
            Finance.Require(form.Id > 0 || access.IsWorker, "Workers submit new reports. Staff may correct existing submitted reports.");
            Finance.Require(!assignment.IsCancelled || (access.IsStaff && form.Id > 0), "Cancelled assignments cannot receive progress reports.");
            Finance.Require(form.Id > 0 || assignment.WorkItem.BillingRecord.Status != BillingStatus.Cancelled, "Cancelled billing cannot receive progress reports.");
            Finance.Require(form.Id > 0 || AssignmentWorkflowService.IsActive(assignment), "Completed or hidden assignments cannot receive a new weekly update.");
            Finance.Require(form.Id > 0 || !assignment.ReportingResumedFromWeek.HasValue || weekStart >= assignment.ReportingResumedFromWeek.Value,
                "Weekly reporting resumes from the current week after an assignment is reopened or unhidden.");

            WeeklyProgressReport report;
            var isNew = form.Id == 0;
            if (!isNew)
            {
                report = await db.WeeklyProgressReports.SingleOrDefaultAsync(x => x.Id == form.Id && x.WorkerAssignmentId == assignment.Id)
                    ?? throw new BusinessException("The progress report was not found.");
                Finance.Require(report.Version == form.Version, "This progress report changed. Refresh before saving.");
                if (access.IsWorker && report.WeekStart < clock.CurrentWeekStart)
                    throw new BusinessException("Submitted reports for past weeks are read-only.");
                Finance.Require(report.WeekStart == weekStart, "The reporting week cannot be changed after submission.");
            }
            else
            {
                Finance.Require(!await db.WeeklyProgressReports.AnyAsync(x => x.WorkerAssignmentId == assignment.Id && x.WeekStart == weekStart), "A progress report already exists for this assignment and week.");
                report = new WeeklyProgressReport { WorkerAssignmentId = assignment.Id, WeekStart = weekStart, WeekEnd = weekStart.AddDays(6), SubmittedAt = clock.UtcNow };
                db.WeeklyProgressReports.Add(report);
            }

            report.WeekEnd = weekStart.AddDays(6);
            report.ProgressPercent = form.ProgressPercent;
            report.ProgressStatus = LegacyStatus(form.WorkflowStatus);
            report.WorkflowStatusAtSubmission = form.WorkflowStatus;
            report.WorkflowVersionAtSubmission = form.WorkflowVersion;
            report.WorkDone = workDone;
            report.NextAction = string.IsNullOrWhiteSpace(nextAction) ? null : nextAction;
            report.IssuesOrBlockers = string.IsNullOrWhiteSpace(form.IssuesOrBlockers) ? null : form.IssuesOrBlockers.Trim();

            if (isNew)
            {
                await workflow.ApplyWorkflowAsync(assignment, form.WorkflowStatus, form.WorkflowVersion, WorkflowHistoryAction.WeeklyUpdate);
                assignment.CurrentProgressPercent = form.ProgressPercent;
            }
            await db.SaveChangesAsync();
            return report;
        });
    }
}
