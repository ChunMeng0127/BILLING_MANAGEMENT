using BillingControl.Services;
using BillingControl.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class PaymentsController(BillingService billing, AccessScope access) : AppController
{
    [Authorize(Roles = AppRoles.PaymentReaders)]
    public async Task<IActionResult> Index()
    {
        var a = await access.CurrentAsync();
        ViewBag.Access = a;
        return View(await access.WorkerPayments(a).Include(x => x.Worker).Include(x => x.Allocations).ThenInclude(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord).AsSplitQuery().OrderByDescending(x => x.PaymentDate).ToListAsync());
    }
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Create(int? workerId)
    {
        var a = await access.CurrentAsync();
        ViewBag.Workers = await access.Workers(a).OrderBy(x => x.Name).ToListAsync(); ViewBag.WorkerId = workerId;
        if (workerId != null && !await access.Workers(a).AnyAsync(x => x.Id == workerId)) return NotFound();
        return View(await access.WorkerAssignments(a).Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment).Where(x => !x.IsCancelled && x.WorkerId == workerId).AsSplitQuery().ToListAsync());
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Edit(int id)
    {
        var a = await access.CurrentAsync();
        var payment = await access.WorkerPayments(a).SingleOrDefaultAsync(x => x.Id == id);
        if (payment == null) return NotFound();
        return View(new WorkerPaymentEditForm { Id = payment.Id, Version = payment.Version, PaymentDate = payment.PaymentDate, Reference = payment.Reference });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Edit(WorkerPaymentEditForm form)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.WorkerPayments(a).AnyAsync(x => x.Id == form.Id)) return NotFound();
        await billing.EditPayment(form.Id, form.PaymentDate, form.Reference, form.Version);
        TempData["Success"] = "Worker payment date and reference corrected. Allocation amounts remain immutable; cancel and re-enter the payment to change allocations.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Create(int workerId, DateOnly date, string reference, Guid requestId, Dictionary<int, decimal?>? amounts)
    {
        ValidForm();
        var allocations = NormalizeAllocations(amounts, "Enter an amount for at least one worker assignment.");
        Finance.Require(allocations.Values.All(x => x >= 0), "Payment allocations cannot be negative.");
        var a = await access.CurrentAsync();
        if (!await access.Workers(a).AnyAsync(x => x.Id == workerId)) return NotFound();
        await billing.Pay(workerId, date, reference, requestId, allocations);
        TempData["Success"] = "Worker payment recorded and allocated."; return RedirectToAction(nameof(Index));
    }
}
