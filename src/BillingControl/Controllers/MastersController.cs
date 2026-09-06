using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class MastersController(AppDbContext db) : AppController
{
    private Type Kind(string kind) => kind switch { "Customers" => typeof(Customer), "Services" => typeof(Service), "Firms" => typeof(BusinessParty), "Managers" => typeof(Manager), "Workers" => typeof(Worker), _ => throw new BusinessException("Unknown master register.") };
    private async Task<List<Master>> List(string kind) => kind switch
    {
        "Customers" => (await db.Customers.OrderBy(x => x.Name).ToListAsync()).Cast<Master>().ToList(),
        "Services" => (await db.Services.OrderBy(x => x.Name).ToListAsync()).Cast<Master>().ToList(),
        "Firms" => (await db.BusinessParties.OrderBy(x => x.Name).ToListAsync()).Cast<Master>().ToList(),
        "Managers" => (await db.Managers.OrderBy(x => x.Name).ToListAsync()).Cast<Master>().ToList(),
        "Workers" => (await db.Workers.OrderBy(x => x.Name).ToListAsync()).Cast<Master>().ToList(),
        _ => throw new BusinessException("Unknown register.")
    };
    public async Task<IActionResult> Index(string kind = "Customers") { ViewBag.Kind = kind; return View(await List(kind)); }
    public async Task<IActionResult> Edit(string kind, int? id)
    {
        ViewBag.Kind = kind; var type = Kind(kind);
        if (id == null) return View(new MasterForm());
        var m = (Master?)await db.FindAsync(type, id.Value); if (m == null) return NotFound();
        return View(new MasterForm { Id = m.Id, Version = m.Version, Name = m.Name, IsActive = m.IsActive, Notes = m.Notes, Email = m is Customer c ? c.Email : (m as Worker)?.Email, RegistrationNumber = (m as Customer)?.RegistrationNumber, Type = (m as Worker)?.Type ?? WorkerType.Self });
    }
    [HttpPost]
    public async Task<IActionResult> Edit(string kind, MasterForm form)
    {
        ViewBag.Kind = kind; var type = Kind(kind);
        if (!Enum.IsDefined(form.Type)) ModelState.AddModelError("Type", "Select a valid worker type.");
        if (!ModelState.IsValid) return View(form);
        var m = form.Id == 0 ? (Master)Activator.CreateInstance(type)! : (Master?)await db.FindAsync(type, form.Id);
        if (m == null) return NotFound();
        Finance.Require(form.Id == 0 || m.Version == form.Version, "This record changed. Refresh before saving.");
        m.Name = form.Name.Trim(); Finance.Require(m.Name.Length > 0, "Name is required."); m.Notes = form.Notes; m.IsActive = form.IsActive;
        if (m is Customer c) { c.Email = form.Email; c.RegistrationNumber = form.RegistrationNumber; }
        if (m is Worker w) { w.Email = form.Email; w.Type = form.Type; }
        if (form.Id == 0) db.Add(m);
        await db.SaveChangesAsync(); TempData["Success"] = "Record saved."; return RedirectToAction(nameof(Index), new { kind });
    }
}
