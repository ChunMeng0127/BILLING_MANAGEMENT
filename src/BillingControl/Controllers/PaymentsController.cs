using BillingControl.Data;
using BillingControl.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

public class PaymentsController(AppDbContext db, BillingService billing) : AppController
{
    public async Task<IActionResult> Index() => View(await db.WorkerPayments.Include(x => x.Worker).Include(x => x.Allocations).ThenInclude(x => x.WorkerAssignment).ThenInclude(x => x.WorkItem).ThenInclude(x => x.BillingRecord).AsSplitQuery().OrderByDescending(x => x.PaymentDate).ToListAsync());
    public async Task<IActionResult> Create(int? workerId)
    {
        ViewBag.Workers = await db.Workers.OrderBy(x => x.Name).ToListAsync(); ViewBag.WorkerId = workerId;
        return View(await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment).Where(x => !x.IsCancelled && x.WorkerId == workerId).AsSplitQuery().ToListAsync());
    }
    [HttpPost]
    public async Task<IActionResult> Create(int workerId, DateOnly date, string reference, Guid requestId, Dictionary<int, decimal> amounts)
    {
        ValidForm(); Finance.Require(amounts.Values.All(x => x >= 0), "Payment allocations cannot be negative.");
        await billing.Pay(workerId, date, reference, requestId, amounts.Where(x => x.Value != 0).ToDictionary(x => x.Key, x => x.Value));
        TempData["Success"] = "Worker payment recorded and allocated."; return RedirectToAction(nameof(Index));
    }
}
