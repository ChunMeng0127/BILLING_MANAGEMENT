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
    public decimal BillingOutstanding => Active.Sum(x => x.CustomerOutstandingAmount);
    public decimal OwnShare => Access.Role switch
    {
        AppRoles.AccountingFirm => Share(ShareKind.Firm),
        AppRoles.Manager => Share(ShareKind.Manager),
        _ => 0m
    };
    public int ProgressRequired { get; set; }
    public int ProgressSubmitted { get; set; }
    public int ProgressMissing { get; set; }
    public int ProgressLate { get; set; }
}

public class ProgressReportRow
{
    public WorkerAssignment Assignment { get; set; } = null!;
    public WeeklyProgressReport? Report { get; set; }
    public WeeklyProgressReport? LatestReport { get; set; }
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd => WeekStart.AddDays(6);
    public bool RequiresReport { get; set; }
    public bool IsLate { get; set; }
    public string ReportingStatus => Report != null ? IsLate ? "Late" : "Submitted" : RequiresReport ? "Missing" : "Not required";
}

public class ProgressReportModel
{
    public AccessProfile Access { get; set; } = AccessProfile.None;
    public DateOnly WeekStart { get; set; }
    public DateOnly CurrentWeekStart { get; set; }
    public AssignmentListFilter Filter { get; set; } = AssignmentListFilter.Active;
    public List<ProgressReportRow> Rows { get; set; } = [];
    public int Required => Rows.Count(x => x.RequiresReport);
    public int Submitted => Rows.Count(x => x.RequiresReport && x.Report != null && !x.IsLate);
    public int Missing => Rows.Count(x => x.RequiresReport && x.Report == null);
    public int Late => Rows.Count(x => x.RequiresReport && x.Report != null && x.IsLate);
}
