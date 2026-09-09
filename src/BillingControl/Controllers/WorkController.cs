using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class WorkController(AppDbContext db, BillingService billing, AccessScope access) : AppController
{
    [Authorize(Roles = AppRoles.WorkReaders)]
    public async Task<IActionResult> Index()
    {
        var a = await access.CurrentAsync();
        var staff = a.IsStaff;
        var worker = a.IsWorker;
        ViewBag.Access = a;
        return View(await access.WorkItems(a).Include(x => x.BillingRecord).Include(x => x.Assignments.Where(y => staff || (worker && !y.IsCancelled && y.WorkerId == a.WorkerId))).OrderByDescending(x => x.Id).ToListAsync());
    }

    [Authorize(Roles = AppRoles.WorkReaders)]
    public async Task<IActionResult> Details(int id)
    {
        var a = await access.CurrentAsync();
        var staff = a.IsStaff;
        var worker = a.IsWorker;
        var item = await access.WorkItems(a)
            .Include(x => x.BillingRecord).ThenInclude(x => x.Shares.Where(_ => staff))
            .Include(x => x.Assignments.Where(y => staff || (worker && !y.IsCancelled && y.WorkerId == a.WorkerId)))
            .ThenInclude(x => x.Allocations)
            .ThenInclude(x => x.WorkerPayment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        ViewBag.Access = a;
        ViewBag.Workers = staff ? await db.Workers.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync() : [];
        return View(item);
    }

    [HttpPost, Authorize(Roles = AppRoles.WorkEditors)]
    public async Task<IActionResult> Update(int id, WorkStatus status, string? notes, long version)
    {
        ValidForm();
        Finance.Require(Enum.IsDefined(status) && (notes?.Length ?? 0) <= 2000, "Select a valid status and notes up to 2,000 characters.");
        var a = await access.CurrentAsync();
        var item = await access.WorkItems(a).Include(x => x.BillingRecord).SingleOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        Finance.Require(a.IsStaff, "Only Admin and InternalUser can update shared work progress. Submit a weekly worker progress report instead.");
        Finance.Require(item.Version == version && item.BillingRecord.Status != BillingStatus.Cancelled, "Record changed or billing was cancelled. Refresh before saving.");
        item.Status = status;
        item.Notes = notes;
        await db.SaveChangesAsync();
        TempData["Success"] = "Work item updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Assign(int id, int workerId, decimal percent)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.WorkItems(a).AnyAsync(x => x.Id == id)) return NotFound();
        await billing.Assign(id, workerId, percent);
        TempData["Success"] = "Worker assigned with a fixed entitlement snapshot.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = AppRoles.PaymentReaders)]
    public async Task<IActionResult> Assignments()
    {
        var a = await access.CurrentAsync();
        ViewBag.Access = a;
        return View(await access.WorkerAssignments(a).Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment).AsSplitQuery().OrderByDescending(x => x.Id).ToListAsync());
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> EditAssignment(int id)
    {
        var a = await access.CurrentAsync();
        var assignment = await access.WorkerAssignments(a)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (assignment == null) return NotFound();
        ViewBag.Assignment = assignment;
        ViewBag.Workers = await db.Workers.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        return View(new WorkerAssignmentEditForm { Id = id, Version = assignment.Version, WorkerId = assignment.WorkerId, Percent = assignment.Percent });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> EditAssignment(WorkerAssignmentEditForm form)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.WorkerAssignments(a).AnyAsync(x => x.Id == form.Id)) return NotFound();
        await billing.EditAssignment(form.Id, form.WorkerId, form.Percent, form.Version);
        TempData["Success"] = "Worker assignment corrected and entitlement recalculated from the immutable LCM gross snapshot.";
        return RedirectToAction(nameof(Assignments));
    }
}
