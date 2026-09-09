using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.ProgressReaders)]
public class ProgressController(AccessScope access, ProgressReportService progress) : AppController
{
    private async Task<ProgressReportModel> Build(DateOnly? requestedWeek)
    {
        var a = await access.CurrentAsync();
        var current = ProgressReportService.CurrentWeekStart();
        var week = requestedWeek ?? current;
        if (week == default || week.DayOfWeek != DayOfWeek.Monday) week = current;
        var assignments = await access.ProgressAssignments(a)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.ProgressReports.Where(r => r.WeekStart == week))
            .AsSplitQuery().OrderBy(x => x.WorkItem.BillingRecord.PeriodStart).ThenBy(x => x.WorkerName).ToListAsync();
        return new ProgressReportModel
        {
            Access = a,
            WeekStart = week,
            CurrentWeekStart = current,
            Rows = assignments.Select(x => new ProgressReportRow { Assignment = x, Report = x.ProgressReports.SingleOrDefault(), WeekStart = week }).ToList()
        };
    }

    public async Task<IActionResult> Index(DateOnly? weekStart) => View(await Build(weekStart));

    [Authorize(Roles = AppRoles.ProgressEditors)]
    public async Task<IActionResult> Edit(int? id, int? assignmentId, DateOnly? weekStart)
    {
        var a = await access.CurrentAsync();
        if (id is > 0)
        {
            var report = await access.ProgressReports(a).Include(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleOrDefaultAsync(x => x.Id == id);
            if (report == null) return NotFound();
            if (a.IsWorker && report.WeekStart < ProgressReportService.CurrentWeekStart())
                throw new BusinessException("Submitted reports for past weeks are read-only.");
            ViewBag.Assignment = report.WorkerAssignment;
            return View(new WeeklyProgressForm
            {
                Id = report.Id, Version = report.Version, WorkerAssignmentId = report.WorkerAssignmentId,
                WeekStart = report.WeekStart, ProgressPercent = report.ProgressPercent, ProgressStatus = report.ProgressStatus,
                WorkDone = report.WorkDone, NextAction = report.NextAction, IssuesOrBlockers = report.IssuesOrBlockers
            });
        }
        var targetWeek = weekStart ?? ProgressReportService.CurrentWeekStart();
        if (targetWeek.DayOfWeek != DayOfWeek.Monday) targetWeek = ProgressReportService.CurrentWeekStart();
        if (assignmentId is null) return NotFound();
        var assignment = await access.ProgressAssignments(a).Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleOrDefaultAsync(x => x.Id == assignmentId);
        if (assignment == null) return NotFound();
        ViewBag.Assignment = assignment;
        return View(new WeeklyProgressForm { WorkerAssignmentId = assignment.Id, WeekStart = targetWeek, ProgressStatus = ProgressStatus.InProgress });
    }

    [HttpPost, Authorize(Roles = AppRoles.ProgressEditors)]
    public async Task<IActionResult> Save(WeeklyProgressForm form)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        await progress.SaveAsync(a, form);
        TempData["Success"] = form.Id > 0 ? "Weekly progress report updated." : "Weekly progress report submitted.";
        return RedirectToAction(nameof(Index), new { weekStart = form.WeekStart });
    }
}
