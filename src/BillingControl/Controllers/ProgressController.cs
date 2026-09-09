using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.ProgressReaders)]
public class ProgressController(AccessScope access, ProgressReportService progress, BusinessClock clock) : AppController
{
    private async Task<ProgressReportModel> Build(DateOnly? requestedWeek)
    {
        var a = await access.CurrentAsync();
        var current = clock.CurrentWeekStart;
        var week = requestedWeek ?? current;
        if (week == default || week.DayOfWeek != DayOfWeek.Monday) week = current;
        var weekEnd = week.AddDays(6);
        var assignments = await access.ProgressAssignments(a)
            .Where(x => x.WorkItem.BillingRecord.PeriodStart <= weekEnd && x.WorkItem.BillingRecord.PeriodEnd >= week
                && ((!x.IsCancelled && x.WorkItem.BillingRecord.Status != BillingStatus.Cancelled)
                    || x.ProgressReports.Any(r => r.WeekStart == week)))
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.ProgressReports.Where(r => r.WeekStart == week))
            .AsSplitQuery().OrderBy(x => x.WorkItem.BillingRecord.PeriodStart).ThenBy(x => x.WorkerName).ToListAsync();
        return new ProgressReportModel
        {
            Access = a,
            WeekStart = week,
            CurrentWeekStart = current,
            Rows = assignments.Select(x => new ProgressReportRow { Assignment = x, Report = x.ProgressReports.SingleOrDefault(), WeekStart = week, IsLate = x.ProgressReports.Any(r => clock.IsLate(r.SubmittedAt, r.WeekEnd)) }).ToList()
        };
    }

    public async Task<IActionResult> Index(DateOnly? weekStart) => View(await Build(weekStart));

    public async Task<IActionResult> Details(int id)
    {
        var a = await access.CurrentAsync();
        var report = await access.ProgressReports(a).Include(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleOrDefaultAsync(x => x.Id == id);
        if (report == null) return NotFound();
        return View(report);
    }

    [Authorize(Roles = AppRoles.ProgressEditors)]
    public async Task<IActionResult> Edit(int? id, int? assignmentId, DateOnly? weekStart)
    {
        var a = await access.CurrentAsync();
        if (id is > 0)
        {
            var report = await access.ProgressReports(a).Include(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleOrDefaultAsync(x => x.Id == id);
            if (report == null) return NotFound();
            if (a.IsWorker && report.WeekStart < clock.CurrentWeekStart)
                throw new BusinessException("Submitted reports for past weeks are read-only.");
            ViewBag.Assignment = report.WorkerAssignment;
            return View(new WeeklyProgressForm
            {
                Id = report.Id, Version = report.Version, WorkerAssignmentId = report.WorkerAssignmentId,
                WeekStart = report.WeekStart, ProgressPercent = report.ProgressPercent, ProgressStatus = report.ProgressStatus,
                WorkDone = report.WorkDone, NextAction = report.NextAction, IssuesOrBlockers = report.IssuesOrBlockers
            });
        }
        if (!a.IsWorker) return Forbid();
        var targetWeek = weekStart ?? clock.CurrentWeekStart;
        if (targetWeek.DayOfWeek != DayOfWeek.Monday) targetWeek = clock.CurrentWeekStart;
        if (assignmentId is null) return NotFound();
        var assignment = await access.EligibleProgressAssignments(a, targetWeek).Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleOrDefaultAsync(x => x.Id == assignmentId);
        if (assignment == null) return NotFound();
        ViewBag.Assignment = assignment;
        return View(new WeeklyProgressForm { WorkerAssignmentId = assignment.Id, WeekStart = targetWeek, ProgressStatus = ProgressStatus.InProgress });
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
        TempData["Success"] = form.Id > 0 ? "Weekly progress report updated." : "Weekly progress report submitted.";
        return RedirectToAction(nameof(Index), new { weekStart = form.WeekStart });
    }
}
