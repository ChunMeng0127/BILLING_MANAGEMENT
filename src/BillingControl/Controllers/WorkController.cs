using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class WorkController(AppDbContext db, BillingService billing) : AppController
{
    public async Task<IActionResult> Index() => View(await db.WorkItems.Include(x => x.BillingRecord).Include(x => x.Assignments).OrderByDescending(x => x.Id).ToListAsync());
    public async Task<IActionResult> Details(int id)
    {
        var item = await db.WorkItems.Include(x => x.BillingRecord).ThenInclude(x => x.Shares).Include(x => x.Assignments).ThenInclude(x => x.Allocations).ThenInclude(x => x.WorkerPayment).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);
        ViewBag.Workers = await db.Workers.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        return item == null ? NotFound() : View(item);
    }
    [HttpPost]
    public async Task<IActionResult> Update(int id, WorkStatus status, string? notes, long version)
    {
        ValidForm(); Finance.Require(Enum.IsDefined(status) && (notes?.Length ?? 0) <= 2000, "Select a valid status and notes up to 2,000 characters.");
        var item = await db.WorkItems.Include(x => x.BillingRecord).SingleAsync(x => x.Id == id);
        Finance.Require(item.Version == version && item.BillingRecord.Status != BillingStatus.Cancelled, "Record changed or billing was cancelled. Refresh before saving.");
        item.Status = status; item.Notes = notes; await db.SaveChangesAsync(); TempData["Success"] = "Work item updated."; return RedirectToAction(nameof(Details), new { id });
    }
    [HttpPost]
    public async Task<IActionResult> Assign(int id, int workerId, decimal percent)
    { ValidForm(); await billing.Assign(id, workerId, percent); TempData["Success"] = "Worker assigned with a fixed entitlement snapshot."; return RedirectToAction(nameof(Details), new { id }); }
    public async Task<IActionResult> Assignments() => View(await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment).AsSplitQuery().OrderByDescending(x => x.Id).ToListAsync());
}
