using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using static BillingControl.Services.Finance;

namespace BillingControl.Services;

public class BillingService
{
    private readonly AppDbContext db;
    public BillingService(AppDbContext db) { this.db = db; }

    public async Task<BillingRecord> Generate(int engagementId, DateOnly start, DateOnly end, bool advanceSchedule = false, bool manualReplacement = false)
    {
        return await FinancialTransaction.Serializable(db, async () =>
        {
        var e = await db.Engagements.Include(x => x.Customer).Include(x => x.Service).Include(x => x.BusinessParty).Include(x => x.Manager).Include(x => x.Schedule).SingleAsync(x => x.Id == engagementId);
        Require(e.Status == EngagementStatus.Active, "Engagement must be active.");
        Require(e.Customer.IsActive && e.Service.IsActive && e.BusinessParty.IsActive && e.Manager.IsActive, "Reactivate this engagement's master records before generating billing.");
        Require(start >= e.StartDate && end >= start && (e.EndDate == null || end <= e.EndDate), "Service period must fall within the engagement dates.");
        Require(!await db.BillingRecords.AnyAsync(x => x.EngagementId == engagementId && x.Status != BillingStatus.Cancelled && x.PeriodStart <= end && x.PeriodEnd >= start), "An active billing record already covers all or part of this service period.");
        if (manualReplacement)
        {
            Require(!advanceSchedule, "Choose either the scheduled period or manual replacement, not both.");
            if (Months(e.Schedule.Frequency) > 0 || e.Schedule.Frequency == Frequency.OneOff)
                Require(e.Schedule.NextPeriodStart != null && start == e.Schedule.NextPeriodStart, "A manual replacement must start at the current scheduled period start.");
        }
        if (advanceSchedule)
        {
            Require(e.Schedule.NextPeriodStart == start, "Schedule changed. Refresh before generating.");
            Require(Months(e.Schedule.Frequency) > 0 || e.Schedule.Frequency == Frequency.OneOff, "Ad-Hoc billing requires a manually selected period.");
            var expectedEnd = Months(e.Schedule.Frequency) > 0 ? Next(start, e.Schedule.Frequency, e.Schedule.AnchorDay).AddDays(-1) : (e.EndDate ?? start);
            if (e.EndDate is { } stop && expectedEnd > stop) expectedEnd = stop;
            Require(end == expectedEnd, "Scheduled period no longer matches. Refresh before generating.");
        }
        var amounts = Split(e.BillingAmount, e.FirmPercent, e.ManagerPercent, e.LcmPercent);
        var bill = new BillingRecord { EngagementId = e.Id, PeriodStart = start, PeriodEnd = end, Amount = e.BillingAmount, CustomerName = e.Customer.Name, ServiceName = e.Service.Name, WorkItem = new WorkItem() };
        bill.Shares = [new() { Kind = ShareKind.Firm, PartyName = e.BusinessParty.Name, Percent = e.FirmPercent, Amount = amounts[0] }, new() { Kind = ShareKind.Manager, PartyName = e.Manager.Name, Percent = e.ManagerPercent, Amount = amounts[1] }, new() { Kind = ShareKind.Lcm, PartyName = "LCM MGT Sdn Bhd", Percent = e.LcmPercent, Amount = amounts[2] }];
        db.BillingRecords.Add(bill);
        if (advanceSchedule) { var next = Next(start, e.Schedule.Frequency, e.Schedule.AnchorDay); e.Schedule.NextPeriodStart = Months(e.Schedule.Frequency) == 0 || (e.EndDate != null && next > e.EndDate) ? null : next; }
        else if (manualReplacement)
        {
            if (Months(e.Schedule.Frequency) == 0)
                e.Schedule.NextPeriodStart = null;
            else
            {
                var next = end.AddDays(1);
                e.Schedule.NextPeriodStart = e.EndDate is { } stop && next > stop ? null : next;
                if (e.Schedule.NextPeriodStart != null) e.Schedule.AnchorDay = e.Schedule.NextPeriodStart.Value.Day;
            }
        }
        await db.SaveChangesAsync(); return bill;
        });
    }
    public async Task Assign(int workItemId, int workerId, decimal percent)
    {
        await FinancialTransaction.Serializable(db, async () =>
        {
        Percentage(percent); Require(percent > 0, "Worker percentage must be greater than zero.");
        var work = await db.WorkItems.Include(x => x.BillingRecord).ThenInclude(x => x.Shares).Include(x => x.Assignments).SingleAsync(x => x.Id == workItemId);
        Require(work.BillingRecord.Status != BillingStatus.Cancelled, "Cannot assign cancelled billing.");
        var worker = await db.Workers.SingleAsync(x => x.Id == workerId); Require(worker.IsActive, "Worker is inactive.");
        var active = work.Assignments.Where(x => !x.IsCancelled).ToList();
        Require(!active.Any(x => x.WorkerId == workerId), "This worker already has an active assignment for this work item.");
        Require(active.Sum(x => x.Percent) + percent <= 100, "Combined worker percentages cannot exceed 100% of the LCM gross share.");
        var gross = work.BillingRecord.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount;
        var amount = WorkerEntitlement(gross, percent);
        Require(active.Sum(x => x.Entitlement) + amount <= gross, "Rounded worker entitlements would exceed the LCM gross share. Adjust the percentage.");
        db.WorkerAssignments.Add(new() { WorkItemId = workItemId, WorkerId = workerId, WorkerName = worker.Name, Percent = percent, LcmGrossSnapshot = gross, Entitlement = amount });
        await db.SaveChangesAsync();
        });
    }
    public async Task Pay(int workerId, DateOnly date, string reference, Guid requestId, Dictionary<int, decimal> allocations)
    {
        await FinancialTransaction.Serializable(db, async () =>
        {
        Require(requestId != Guid.Empty, "Payment request identifier is required.");
        Require(!await db.WorkerPayments.AnyAsync(x => x.RequestId == requestId), "This payment was already submitted. Refresh the register.");
        Require(!string.IsNullOrWhiteSpace(reference) && reference.Length <= 160, "Payment reference is required (maximum 160 characters).");
        Require(date != default && date <= DateOnly.FromDateTime(DateTime.Today), "Payment date must be today or earlier.");
        Require(allocations.Count > 0 && allocations.Count <= 100, "Allocate the payment to between 1 and 100 assignments.");
        var assignments = await db.WorkerAssignments.Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment).Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Where(x => allocations.Keys.Contains(x.Id)).ToListAsync();
        Require(assignments.Count == allocations.Count && assignments.All(x => x.WorkerId == workerId && !x.IsCancelled && x.WorkItem.BillingRecord.Status != BillingStatus.Cancelled), "All allocations must belong to active assignments for the selected worker.");
        foreach (var a in assignments) ValidateAllocation(allocations[a.Id], a.Entitlement, a.Allocations.Where(x => !x.WorkerPayment.IsCancelled).Sum(x => x.Amount));
        var total = allocations.Values.Sum(); PositiveMoney(total);
        db.WorkerPayments.Add(new() { WorkerId = workerId, PaymentDate = date, Reference = reference.Trim(), RequestId = requestId, Amount = total, Allocations = allocations.Select(x => new WorkerPaymentAllocation { WorkerAssignmentId = x.Key, Amount = x.Value }).ToList() });
        await db.SaveChangesAsync();
        });
    }
    public async Task UpdateBillingStatus(int id, BillingStatus status, long version)
    {
        await FinancialTransaction.Serializable(db, async () =>
        {
        Require(Enum.IsDefined(status) && status <= BillingStatus.ReadyToBill, "Financial billing states are derived from invoice and receipt allocations.");
        var bill = await db.BillingRecords.SingleAsync(x => x.Id == id);
        Require(bill.Version == version, "This record changed. Refresh before saving.");
        Require(bill.Status <= BillingStatus.ReadyToBill, "Financial billing states are controlled by invoices and receipts.");
        bill.Status = status; await db.SaveChangesAsync();
        });
    }
    public async Task Cancel(string kind, int id, string reason)
    {
        Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 2000, "A cancellation reason is required (up to 2,000 characters).");
        if (kind == "invoice") { await new InvoiceService(db).CancelInvoice(id, reason); return; }
        if (kind == "receipt") { await new InvoiceService(db).CancelReceipt(id, reason); return; }
        await FinancialTransaction.Serializable(db, async () =>
        {
        if (kind == "payment") { var p = await db.WorkerPayments.SingleAsync(x => x.Id == id); Require(!p.IsCancelled, "Already cancelled."); p.IsCancelled = true; p.CancellationReason = reason; }
        else if (kind == "assignment")
        {
            var a = await db.WorkerAssignments.Include(x => x.Allocations).ThenInclude(x => x.WorkerPayment).SingleAsync(x => x.Id == id);
            Require(!a.IsCancelled && !a.Allocations.Any(x => !x.WorkerPayment.IsCancelled), "Cancel active payments first, or assignment is already cancelled."); a.IsCancelled = true; a.CancellationReason = reason;
        }
        else if (kind == "billing")
        {
            var b = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).ThenInclude(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).Include(x => x.WorkItem).ThenInclude(x => x.Assignments).SingleAsync(x => x.Id == id);
            Require(b.Status != BillingStatus.Cancelled && !b.InvoiceLines.Any(x => x.Invoice.Status != InvoiceStatus.Cancelled) && !b.WorkItem.Assignments.Any(x => !x.IsCancelled), "Cancel active invoices, receipts and assignments first, or billing is already cancelled."); b.Status = BillingStatus.Cancelled; b.CancellationReason = reason;
        }
        else throw new BusinessException("Unknown cancellation type.");
        await db.SaveChangesAsync();
        });
    }
}
