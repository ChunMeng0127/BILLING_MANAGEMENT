using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class BillingController(AppDbContext db, BillingService billing, BillingScheduleService schedules, AccessScope access) : AppController
{
    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var a = await access.CurrentAsync();
        var showReceipts = a.IsStaff || a.IsAccountingFirm;
        var q = access.BillingRecords(a)
            .Include(x => x.InvoiceLines)
            .ThenInclude(x => x.Invoice)
            .ThenInclude(x => x.ReceiptAllocations)
            .ThenInclude(x => x.CustomerReceipt)
            .AsQueryable();
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

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> EditSchedule(int id)
    {
        var a = await access.CurrentAsync();
        var engagement = await access.Engagements(a)
            .Include(x => x.Schedule)
            .Include(x => x.Customer)
            .Include(x => x.Service)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (engagement == null) return NotFound();
        ViewBag.Engagement = engagement;
        return View(new BillingScheduleForm
        {
            EngagementId = engagement.Id,
            EngagementVersion = engagement.Version,
            Version = engagement.Schedule.Version,
            Frequency = engagement.Schedule.Frequency,
            NextPeriodStart = engagement.Schedule.NextPeriodStart,
            AnchorDay = engagement.Schedule.AnchorDay
        });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> EditSchedule(BillingScheduleForm form)
    {
        var a = await access.CurrentAsync();
        try
        {
            ValidForm();
            return await FinancialTransaction.Serializable<IActionResult>(db, async () =>
            {
                var engagement = await access.Engagements(a)
                    .Include(x => x.Schedule)
                    .Include(x => x.Customer)
                    .Include(x => x.Service)
                    .SingleOrDefaultAsync(x => x.Id == form.EngagementId);
                if (engagement == null) return NotFound();
                Finance.Require(form.EngagementVersion == engagement.Version && form.Version == engagement.Schedule.Version, "This schedule changed. Refresh before saving.");
                var nextPeriodStart = BillingScheduleService.NormalizeNextPeriodStart(form.Frequency, form.NextPeriodStart, false, engagement.StartDate);
                var scheduleChanged = BillingScheduleService.IsChanged(engagement.Schedule, form.Frequency, nextPeriodStart, form.AnchorDay);
                await schedules.ValidateAsync(engagement, form.Frequency, nextPeriodStart, form.AnchorDay, scheduleChanged);
                engagement.Schedule.Frequency = form.Frequency;
                engagement.Schedule.NextPeriodStart = nextPeriodStart;
                engagement.Schedule.AnchorDay = form.AnchorDay;
                await db.SaveChangesAsync();
                TempData["Success"] = "Billing schedule saved. Existing billing records are unchanged.";
                return RedirectToAction(nameof(Schedule));
            });
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError("", ex.Message);
            var engagement = await access.Engagements(a)
                .Include(x => x.Schedule)
                .Include(x => x.Customer)
                .Include(x => x.Service)
                .SingleOrDefaultAsync(x => x.Id == form.EngagementId);
            ViewBag.Engagement = engagement;
            return View(form);
        }
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Generate(int engagementId, DateOnly start, DateOnly end, bool advanceSchedule, bool manualReplacement)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.Engagements(a).AnyAsync(x => x.Id == engagementId)) return NotFound();
        var bill = await billing.Generate(engagementId, start, end, advanceSchedule, manualReplacement);
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
            .Include(x => x.InvoiceLines)
            .ThenInclude(x => x.Invoice)
            .ThenInclude(x => x.ReceiptAllocations)
            .ThenInclude(x => x.CustomerReceipt)
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
    public async Task<IActionResult> Status(int id, BillingStatus status, long version)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.BillingRecords(a).AnyAsync(x => x.Id == id)) return NotFound();
        await billing.UpdateBillingStatus(id, status, version);
        TempData["Success"] = "Billing status updated.";
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
