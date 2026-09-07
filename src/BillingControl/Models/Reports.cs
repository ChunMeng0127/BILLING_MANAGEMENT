using BillingControl.Services;

namespace BillingControl.Models;

public class ReportFilter
{
    public int? CustomerId { get; set; }
    public int? ServiceId { get; set; }
    public int? WorkerId { get; set; }
    public BillingStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
public class ReportModel
{
    public AccessProfile Access { get; set; } = AccessProfile.None;
    public ReportFilter Filter { get; set; } = new();
    public List<BillingRecord> Bills { get; set; } = [];
    public List<BillingSchedule> Upcoming { get; set; } = [];
    public IEnumerable<BillingRecord> Active => Bills.Where(x => x.Status != BillingStatus.Cancelled);
    public decimal Total => Active.Sum(x => x.Amount);
    public decimal Share(ShareKind kind) => Active.Sum(x => x.Shares.Where(s => s.Kind == kind).Sum(s => s.Amount));
    public IEnumerable<WorkerAssignment> Assignments => Active.SelectMany(x => x.WorkItem.Assignments).Where(x => !x.IsCancelled);
    public decimal WorkerCost => Assignments.Sum(x => x.Entitlement);
    public decimal WorkerPaid => Assignments.Sum(x => x.Allocations.Where(a => !a.WorkerPayment.IsCancelled).Sum(a => a.Amount));
    public decimal BillingOutstanding => Active.Where(x => x.Status is BillingStatus.Billed or BillingStatus.PartiallyPaid or BillingStatus.Paid).Sum(x => x.Amount - x.Receipts.Where(r => !r.IsCancelled).Sum(r => r.Amount));
    public decimal OwnShare => Access.Role switch
    {
        AppRoles.AccountingFirm => Share(ShareKind.Firm),
        AppRoles.Manager => Share(ShareKind.Manager),
        _ => 0m
    };
}
