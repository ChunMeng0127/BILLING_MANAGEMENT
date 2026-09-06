using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class BillingController(AppDbContext db, BillingService billing) : AppController
{
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var q = db.BillingRecords.Include(x => x.Shares).Include(x => x.Receipts).Include(x => x.WorkItem).ThenInclude(x => x.Assignments).AsQueryable();
        if (filter.CustomerId != null) q = q.Where(x => x.Engagement.CustomerId == filter.CustomerId);
        if (filter.ServiceId != null) q = q.Where(x => x.Engagement.ServiceId == filter.ServiceId);
        if (filter.Status != null) q = q.Where(x => x.Status == filter.Status);
        if (filter.From != null) q = q.Where(x => x.PeriodEnd >= filter.From);
        if (filter.To != null) q = q.Where(x => x.PeriodStart <= filter.To);
        ViewBag.Filter = filter; return View(await q.AsSplitQuery().OrderByDescending(x => x.PeriodStart).ToListAsync());
    }
    public async Task<IActionResult> Schedule() => View(await db.BillingSchedules.Include(x => x.Engagement).ThenInclude(x => x.Customer).Include(x => x.Engagement).ThenInclude(x => x.Service).Where(x => x.Engagement.Status == EngagementStatus.Active).OrderBy(x => x.NextPeriodStart).ToListAsync());
    [HttpPost]
    public async Task<IActionResult> Generate(int engagementId, DateOnly start, DateOnly end, bool advanceSchedule)
    { ValidForm(); var bill = await billing.Generate(engagementId, start, end, advanceSchedule); TempData["Success"] = "Billing period and work item created."; return RedirectToAction(nameof(Details), new { id = bill.Id }); }
    public async Task<IActionResult> Details(int id)
    {
        var b = await db.BillingRecords.Include(x => x.Shares).Include(x => x.Receipts).Include(x => x.WorkItem).ThenInclude(x => x.Assignments).ThenInclude(x => x.Allocations).ThenInclude(x => x.WorkerPayment).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);
        return b == null ? NotFound() : View(b);
    }
    [HttpPost]
    public async Task<IActionResult> Status(int id, BillingStatus status, DateOnly? date, string? invoice, long version)
    { ValidForm(); await billing.UpdateBillingStatus(id, status, date, invoice, version); TempData["Success"] = "Billing status updated."; return RedirectToAction(nameof(Details), new { id }); }
    [HttpPost]
    public async Task<IActionResult> Receive(int id, DateOnly date, decimal amount, string reference, Guid requestId)
    { ValidForm(); await billing.Receive(id, date, amount, reference, requestId); TempData["Success"] = "Customer receipt recorded."; return RedirectToAction(nameof(Details), new { id }); }
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancel(string kind, int id, string reason)
    { ValidForm(); await billing.Cancel(kind, id, reason); TempData["Success"] = "Record cancelled; history retained."; return RedirectToAction(nameof(Index)); }
}
