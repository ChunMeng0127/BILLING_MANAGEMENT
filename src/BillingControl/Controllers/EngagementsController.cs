using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using static BillingControl.Services.Finance;

namespace BillingControl.Controllers;

public class EngagementsController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index() => View(await db.Engagements.Include(x => x.Customer).Include(x => x.Service).Include(x => x.BusinessParty).Include(x => x.Manager).Include(x => x.Schedule).OrderBy(x => x.Customer.Name).ToListAsync());
    private async Task Choices()
    {
        ViewBag.Customers = new SelectList(await db.Customers.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Services = new SelectList(await db.Services.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Firms = new SelectList(await db.BusinessParties.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Managers = new SelectList(await db.Managers.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
    }
    public async Task<IActionResult> Edit(int? id)
    {
        await Choices();
        if (id == null) return View(new EngagementForm());
        var e = await db.Engagements.Include(x => x.Schedule).SingleOrDefaultAsync(x => x.Id == id); if (e == null) return NotFound();
        return View(new EngagementForm { Id = e.Id, Version = e.Version, CustomerId = e.CustomerId, ServiceId = e.ServiceId, BusinessPartyId = e.BusinessPartyId, ManagerId = e.ManagerId, StartDate = e.StartDate, EndDate = e.EndDate, BillingAmount = e.BillingAmount, FirmPercent = e.FirmPercent, ManagerPercent = e.ManagerPercent, LcmPercent = e.LcmPercent, Frequency = e.Schedule.Frequency, NextPeriodStart = e.Schedule.NextPeriodStart, AnchorDay = e.Schedule.AnchorDay, Status = e.Status, Notes = e.Notes });
    }
    [HttpPost]
    public async Task<IActionResult> Edit(EngagementForm form)
    {
        try
        {
            ValidForm(); Split(form.BillingAmount, form.FirmPercent, form.ManagerPercent, form.LcmPercent);
            Require(Enum.IsDefined(form.Frequency) && Enum.IsDefined(form.Status), "Select a valid frequency and status.");
            Require(form.StartDate != default && (form.EndDate == null || form.EndDate >= form.StartDate), "End date cannot precede start date.");
            Require(form.NextPeriodStart == null || (form.NextPeriodStart >= form.StartDate && (form.EndDate == null || form.NextPeriodStart <= form.EndDate)), "Next service period must be inside the engagement dates.");
            Require(await db.Customers.AnyAsync(x => x.Id == form.CustomerId) && await db.Services.AnyAsync(x => x.Id == form.ServiceId) && await db.BusinessParties.AnyAsync(x => x.Id == form.BusinessPartyId) && await db.Managers.AnyAsync(x => x.Id == form.ManagerId), "Select existing customer, service, firm and manager records.");
            var e = form.Id == 0 ? new Engagement { Schedule = new() } : await db.Engagements.Include(x => x.Schedule).SingleAsync(x => x.Id == form.Id);
            Require(form.Id == 0 || form.Version == e.Version, "This engagement changed. Refresh before saving.");
            if (form.Id != 0 && await db.BillingRecords.AnyAsync(x => x.EngagementId == form.Id)) Require(e.CustomerId == form.CustomerId && e.ServiceId == form.ServiceId, "Customer and service cannot change after billing has been generated. Create another engagement.");
            e.CustomerId = form.CustomerId; e.ServiceId = form.ServiceId; e.BusinessPartyId = form.BusinessPartyId; e.ManagerId = form.ManagerId;
            e.StartDate = form.StartDate; e.EndDate = form.EndDate; e.BillingAmount = form.BillingAmount; e.FirmPercent = form.FirmPercent; e.ManagerPercent = form.ManagerPercent; e.LcmPercent = form.LcmPercent; e.Status = form.Status; e.Notes = form.Notes;
            e.Schedule.Frequency = form.Frequency; e.Schedule.AnchorDay = form.AnchorDay;
            e.Schedule.NextPeriodStart = form.Frequency == Frequency.AdHoc ? null : form.NextPeriodStart ?? (form.Id == 0 ? form.StartDate : null);
            if (form.Id == 0) db.Engagements.Add(e);
            await db.SaveChangesAsync(); TempData["Success"] = "Engagement saved. Existing billing snapshots are unchanged."; return RedirectToAction(nameof(Index));
        }
        catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); await Choices(); return View(form); }
    }
}
