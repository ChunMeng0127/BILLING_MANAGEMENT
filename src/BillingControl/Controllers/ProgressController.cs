using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.ProgressReaders)]
public class ProgressController(AccessScope access, ProgressReportService progress, AssignmentWorkflowService workflow, BusinessClock clock) : AppController
{
    private static bool MatchesFilter(WorkerAssignment assignment, AssignmentListFilter filter) => filter switch
    {
        AssignmentListFilter.Active => AssignmentWorkflowService.IsActive(assignment),
        AssignmentListFilter.Completed => !assignment.IsCancelled && assignment.CurrentWorkflowStatus == WorkflowStatus.Completed,
        AssignmentListFilter.Hidden => assignment.IsHidden,
        AssignmentListFilter.All => true,
        _ => false
    };

    private async Task SetVersionChoices(WorkerAssignment assignment)
    {
        ViewBag.NextQueriesVersion = await workflow.NextVersionAsync(assignment.Id, WorkflowStatus.QueriesSent);
        ViewBag.NextDraftVersion = await workflow.NextVersionAsync(assignment.Id, WorkflowStatus.DraftManagementReportSent);
    }

    private async Task<ProgressReportModel> Build(DateOnly? requestedWeek, AssignmentListFilter filter)
    {
        var a = await access.CurrentAsync();
        var current = clock.CurrentWeekStart;
        var week = requestedWeek ?? current;
        if (week == default || week.DayOfWeek != DayOfWeek.Monday) week = current;
        if (a.IsWorker && filter == AssignmentListFilter.Hidden) filter = AssignmentListFilter.Active;
        var assignments = await access.ProgressAssignments(a)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.ProgressReports)
            .Include(x => x.WorkflowHistory)
            .AsSplitQuery()
            .OrderBy(x => x.WorkerName).ThenBy(x => x.Id)
            .ToListAsync();
        var rows = assignments.Where(x => MatchesFilter(x, filter)).Select(x =>
        {
            var report = x.ProgressReports.SingleOrDefault(r => r.WeekStart == week);
            var latest = x.ProgressReports.OrderByDescending(r => r.WeekStart).ThenByDescending(r => r.SubmittedAt).FirstOrDefault();
            return new ProgressReportRow
            {
                Assignment = x,
                Report = report,
                LatestReport = latest,
                WeekStart = week,
                RequiresReport = AssignmentWorkflowService.RequiresWeeklyReport(x, week, clock),
                IsLate = report != null && clock.IsLate(report.SubmittedAt, report.WeekEnd)
            };
        }).ToList();
        return new ProgressReportModel { Access = a, WeekStart = week, CurrentWeekStart = current, Filter = filter, Rows = rows };
    }

    public async Task<IActionResult> Index(DateOnly? weekStart, AssignmentListFilter filter = AssignmentListFilter.Active) => View(await Build(weekStart, filter));

    public async Task<IActionResult> Details(int id)
    {
        var a = await access.CurrentAsync();
        var report = await access.ProgressReports(a)
            .Include(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.WorkerAssignment).ThenInclude(x => x.WorkflowHistory)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (report == null) return NotFound();
        return View(report);
    }

    [Authorize(Roles = AppRoles.ProgressEditors)]
    public async Task<IActionResult> Edit(int? id, int? assignmentId, DateOnly? weekStart)
    {
        var a = await access.CurrentAsync();
        if (id is > 0)
        {
            var report = await access.ProgressReports(a)
                .Include(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
                .SingleOrDefaultAsync(x => x.Id == id);
            if (report == null) return NotFound();
            if (a.IsWorker && report.WeekStart < clock.CurrentWeekStart)
                throw new BusinessException("Submitted reports for past weeks are read-only.");
            ViewBag.Assignment = report.WorkerAssignment;
            var workerCurrentWeekEdit = a.IsWorker && report.WeekStart == clock.CurrentWeekStart;
            await SetVersionChoices(report.WorkerAssignment);
            return View(new WeeklyProgressForm
            {
                Id = report.Id,
                Version = report.Version,
                WorkerAssignmentId = report.WorkerAssignmentId,
                AssignmentVersion = report.WorkerAssignment.Version,
                WeekStart = report.WeekStart,
                ProgressPercent = workerCurrentWeekEdit ? report.WorkerAssignment.CurrentProgressPercent : report.ProgressPercent,
                WorkflowStatus = workerCurrentWeekEdit ? report.WorkerAssignment.CurrentWorkflowStatus : report.WorkflowStatusAtSubmission ?? report.WorkerAssignment.CurrentWorkflowStatus,
                WorkflowVersion = workerCurrentWeekEdit ? report.WorkerAssignment.CurrentWorkflowVersion : report.WorkflowVersionAtSubmission,
                WorkDone = report.WorkDone,
                NextAction = report.NextAction,
                IssuesOrBlockers = report.IssuesOrBlockers
            });
        }
        if (!a.IsWorker) return Forbid();
        var targetWeek = weekStart ?? clock.CurrentWeekStart;
        if (targetWeek.DayOfWeek != DayOfWeek.Monday) targetWeek = clock.CurrentWeekStart;
        if (assignmentId is null) return NotFound();
        var assignment = await access.ProgressAssignments(a)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.WorkflowHistory)
            .SingleOrDefaultAsync(x => x.Id == assignmentId);
        var hasCurrentReport = assignment != null && await access.ProgressReports(a)
            .AnyAsync(x => x.WorkerAssignmentId == assignment.Id && x.WeekStart == targetWeek);
        var finalCompletionWeek = assignment != null && a.IsWorker && targetWeek == clock.CurrentWeekStart
            && !hasCurrentReport && AssignmentWorkflowService.AllowsFinalCurrentWeekReport(assignment, clock);
        if (assignment == null || (!AssignmentWorkflowService.IsActive(assignment) && !finalCompletionWeek)
            || (assignment.ReportingResumedFromWeek.HasValue && targetWeek < assignment.ReportingResumedFromWeek.Value)) return NotFound();
        ViewBag.Assignment = assignment;
        ViewBag.FinalCompletionWeek = finalCompletionWeek;
        await SetVersionChoices(assignment);
        var status = assignment.CurrentWorkflowStatus;
        return View(new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id,
            AssignmentVersion = assignment.Version,
            WeekStart = targetWeek,
            ProgressPercent = assignment.CurrentProgressPercent,
            WorkflowStatus = status,
            WorkflowVersion = Finance.RequiresWorkflowVersion(status) ? await workflow.NextVersionAsync(assignment.Id, status) : null
        });
    }

    [HttpPost, Authorize(Roles = AppRoles.ProgressEditors)]
    public async Task<IActionResult> Save(WeeklyProgressForm form)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.ProgressAssignments(a).AnyAsync(x => x.Id == form.WorkerAssignmentId)) return NotFound();
        if (form.Id > 0 && !await access.ProgressReports(a).AnyAsync(x => x.Id == form.Id && x.WorkerAssignmentId == form.WorkerAssignmentId)) return NotFound();
        if (form.Id == 0 && !a.IsWorker) return Forbid();
        await progress.SaveAsync(a, form);
        TempData["Success"] = form.Id > 0
            ? "Weekly progress report updated."
            : form.WeekStart == clock.CurrentWeekStart ? "Weekly update submitted and current workflow state updated." : "Historical weekly report submitted.";
        return RedirectToAction(nameof(Index), new { weekStart = form.WeekStart });
    }

    [HttpPost, Authorize(Roles = AppRoles.ProgressEditors)]
    public async Task<IActionResult> Batch(BatchWorkflowForm form)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        var ids = form.AssignmentIds.Distinct().ToArray();
        if (ids.Length > 0 && await access.ProgressAssignments(a).Where(x => ids.Contains(x.Id)).CountAsync() != ids.Length) return NotFound();
        await workflow.BatchAsync(a, form);
        TempData["Success"] = form.Action == BatchWorkflowAction.UpdateWorkflow
            ? "Workflow status updated. No weekly reports were created."
            : form.Action == BatchWorkflowAction.Hide ? "Selected assignments are hidden from the worker active list." : "Selected assignments are visible again from the current week.";
        return RedirectToAction(nameof(Index), new { weekStart = form.ReturnWeekStart, filter = form.ReturnFilter });
    }
}
