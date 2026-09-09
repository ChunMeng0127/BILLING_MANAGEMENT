using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using static BillingControl.Services.Finance;

namespace BillingControl.Controllers;

public class EngagementsController(AppDbContext db, AccessScope access, BillingScheduleService schedules) : AppController
{
    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Index()
    {
        var a = await access.CurrentAsync();
        ViewBag.Access = a;
        var q = access.Engagements(a).Include(x => x.Customer).Include(x => x.Service).Include(x => x.Schedule).AsQueryable();
        if (a.IsStaff) q = q.Include(x => x.BusinessParty).Include(x => x.Manager);
        return View(await q.OrderBy(x => x.Customer.Name).ToListAsync());
    }
    private async Task Choices(AccessProfile a)
    {
        ViewBag.Customers = new SelectList(await access.Customers(a).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Services = new SelectList(await access.Services(a).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Firms = new SelectList(await access.BusinessParties(a).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Managers = new SelectList(await access.Managers(a).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
    }
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Edit(int? id)
    {
        var a = await access.CurrentAsync();
        await Choices(a);
        if (id == null) return View(new EngagementForm());
        var e = await access.Engagements(a).Include(x => x.Schedule).SingleOrDefaultAsync(x => x.Id == id); if (e == null) return NotFound();
        ViewBag.HasBilling = await db.BillingRecords.AnyAsync(x => x.EngagementId == e.Id);
        return View(new EngagementForm { Id = e.Id, Version = e.Version, ScheduleVersion = e.Schedule.Version, CustomerId = e.CustomerId, ServiceId = e.ServiceId, BusinessPartyId = e.BusinessPartyId, ManagerId = e.ManagerId, StartDate = e.StartDate, EndDate = e.EndDate, BillingAmount = e.BillingAmount, FirmPercent = e.FirmPercent, ManagerPercent = e.ManagerPercent, LcmPercent = e.LcmPercent, Frequency = e.Schedule.Frequency, NextPeriodStart = e.Schedule.NextPeriodStart, AnchorDay = e.Schedule.AnchorDay, Status = e.Status, Notes = e.Notes });
    }
    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Edit(EngagementForm form)
    {
        var a = await access.CurrentAsync();
        try
        {
            ValidForm();
            return await FinancialTransaction.Serializable<IActionResult>(db, async () =>
            {
                Split(form.BillingAmount, form.FirmPercent, form.ManagerPercent, form.LcmPercent);
                Require(Enum.IsDefined(form.Frequency) && Enum.IsDefined(form.Status), "Select a valid frequency and status.");
                Require(form.StartDate != default && (form.EndDate == null || form.EndDate >= form.StartDate), "End date cannot precede start date.");
                Require(await db.Customers.AnyAsync(x => x.Id == form.CustomerId) && await db.Services.AnyAsync(x => x.Id == form.ServiceId) && await db.BusinessParties.AnyAsync(x => x.Id == form.BusinessPartyId) && await db.Managers.AnyAsync(x => x.Id == form.ManagerId), "Select existing customer, service, firm and manager records.");
                var e = form.Id == 0 ? new Engagement { Schedule = new() } : await access.Engagements(a).Include(x => x.Schedule).SingleOrDefaultAsync(x => x.Id == form.Id);
                if (e == null) return NotFound();
                Require(form.Id == 0 || form.Version == e.Version, "This engagement changed. Refresh before saving.");
                Require(form.Id == 0 || form.ScheduleVersion == e.Schedule.Version, "The billing schedule changed. Refresh the engagement before saving.");
                if (form.Id != 0 && await db.BillingRecords.AnyAsync(x => x.EngagementId == form.Id))
                    Require(e.CustomerId == form.CustomerId && e.ServiceId == form.ServiceId && e.BusinessPartyId == form.BusinessPartyId && e.ManagerId == form.ManagerId,
                        "Customer, service, accounting firm and manager cannot be changed after billing has been generated. End this engagement and create a new engagement for the new arrangement.");
                var nextPeriodStart = BillingScheduleService.NormalizeNextPeriodStart(form.Frequency, form.NextPeriodStart, form.Id == 0, form.StartDate);
                var scheduleChanged = form.Id == 0 || BillingScheduleService.IsChanged(e.Schedule, form.Frequency, nextPeriodStart, form.AnchorDay);
                e.CustomerId = form.CustomerId; e.ServiceId = form.ServiceId; e.BusinessPartyId = form.BusinessPartyId; e.ManagerId = form.ManagerId;
                e.StartDate = form.StartDate; e.EndDate = form.EndDate; e.BillingAmount = form.BillingAmount; e.FirmPercent = form.FirmPercent; e.ManagerPercent = form.ManagerPercent; e.LcmPercent = form.LcmPercent; e.Status = form.Status; e.Notes = form.Notes;
                e.Schedule.Frequency = form.Frequency; e.Schedule.AnchorDay = form.AnchorDay;
                await schedules.ValidateAsync(e, form.Frequency, nextPeriodStart, form.AnchorDay, scheduleChanged);
                e.Schedule.NextPeriodStart = nextPeriodStart;
                if (form.Id == 0) db.Engagements.Add(e);
                await db.SaveChangesAsync();
                TempData["Success"] = "Engagement saved. Existing billing snapshots are unchanged.";
                return RedirectToAction(nameof(Index));
            });
        }
        catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); await Choices(a); ViewBag.HasBilling = form.Id != 0 && await db.BillingRecords.AnyAsync(x => x.EngagementId == form.Id); return View(form); }
    }
}
