using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class BillingController(BillingService billing, AccessScope access) : AppController
{
    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var a = await access.CurrentAsync();
        var showReceipts = a.IsStaff || a.IsAccountingFirm;
        var q = access.BillingRecords(a).Include(x => x.Receipts.Where(_ => showReceipts)).AsQueryable();
        if (filter.CustomerId != null) q = q.Where(x => x.Engagement.CustomerId == filter.CustomerId);
        if (filter.ServiceId != null) q = q.Where(x => x.Engagement.ServiceId == filter.ServiceId);
        if (filter.Status != null) q = q.Where(x => x.Status == filter.Status);
        if (filter.From != null) q = q.Where(x => x.PeriodEnd >= filter.From);
        if (filter.To != null) q = q.Where(x => x.PeriodStart <= filter.To);
        ViewBag.Filter = filter;
        ViewBag.Access = a;
        return View(await q.AsSplitQuery().OrderByDescending(x => x.PeriodStart).ToListAsync());
    }

    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Schedule()
    {
        var a = await access.CurrentAsync();
        ViewBag.Access = a;
        return View(await access.BillingSchedules(a).Include(x => x.Engagement).ThenInclude(x => x.Customer).Include(x => x.Engagement).ThenInclude(x => x.Service).Where(x => x.Engagement.Status == EngagementStatus.Active).OrderBy(x => x.NextPeriodStart).ToListAsync());
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Generate(int engagementId, DateOnly start, DateOnly end, bool advanceSchedule)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.Engagements(a).AnyAsync(x => x.Id == engagementId)) return NotFound();
        var bill = await billing.Generate(engagementId, start, end, advanceSchedule);
        TempData["Success"] = "Billing period and work item created.";
        return RedirectToAction(nameof(Details), new { id = bill.Id });
    }

    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Details(int id)
    {
        var a = await access.CurrentAsync();
        var staff = a.IsStaff;
        var firm = a.IsAccountingFirm;
        var manager = a.IsManager;
        var b = await access.BillingRecords(a)
            .Include(x => x.Shares.Where(s => staff || (firm && s.Kind == ShareKind.Firm) || (manager && s.Kind == ShareKind.Manager)))
            .Include(x => x.Receipts.Where(_ => staff || firm))
            .Include(x => x.WorkItem)
            .ThenInclude(x => x.Assignments.Where(_ => staff))
            .ThenInclude(x => x.Allocations)
            .ThenInclude(x => x.WorkerPayment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        ViewBag.Access = a;
        return View(b);
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Status(int id, BillingStatus status, DateOnly? date, string? invoice, long version)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.BillingRecords(a).AnyAsync(x => x.Id == id)) return NotFound();
        await billing.UpdateBillingStatus(id, status, date, invoice, version);
        TempData["Success"] = "Billing status updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Receive(int id, DateOnly date, decimal amount, string reference, Guid requestId)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.BillingRecords(a).AnyAsync(x => x.Id == id)) return NotFound();
        await billing.Receive(id, date, amount, reference, requestId);
        TempData["Success"] = "Customer receipt recorded.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Cancel(string kind, int id, string reason)
    {
        ValidForm();
        await billing.Cancel(kind, id, reason);
        TempData["Success"] = "Record cancelled; history retained.";
        return RedirectToAction(nameof(Index));
    }
}
