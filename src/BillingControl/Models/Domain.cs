using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace BillingControl.Models;

public class AppUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
    public int? BusinessPartyId { get; set; }
    public BusinessParty? BusinessParty { get; set; }
    public int? ManagerId { get; set; }
    public Manager? Manager { get; set; }
    public int? WorkerId { get; set; }
    public Worker? Worker { get; set; }
}
public abstract class Record
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "system";
    public long Version { get; set; }
}
public abstract class Master : Record
{
    [Required, StringLength(160)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [StringLength(2000)] public string? Notes { get; set; }
}
public class Customer : Master { [EmailAddress, StringLength(254)] public string? Email { get; set; } [StringLength(60)] public string? RegistrationNumber { get; set; } }
public class Service : Master { }
public class BusinessParty : Master { }
public class Manager : Master { }
public enum WorkerType { Self, Family, Friend, Employee, Freelancer, Contractor }
public class Worker : Master { public WorkerType Type { get; set; } [EmailAddress, StringLength(254)] public string? Email { get; set; } }
public enum Frequency { Monthly, Every2Months, Quarterly, HalfYearly, Yearly, OneOff, AdHoc }
public enum BillingGenerationMode { Scheduled, Replacement, AdHocManual }
public enum EngagementStatus { Active, Paused, Closed }
public enum BillingStatus { Upcoming, WorkInProgress, Completed, ReadyToBill, Billed, PartiallyPaid, Paid, Cancelled }
public enum WorkStatus { Upcoming, InProgress, Completed }
public class Engagement : Record
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;
    public int BusinessPartyId { get; set; }
    public BusinessParty BusinessParty { get; set; } = null!;
    public int ManagerId { get; set; }
    public Manager Manager { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal BillingAmount { get; set; }
    public decimal FirmPercent { get; set; } = 35m;
    public decimal ManagerPercent { get; set; } = 25m;
    public decimal LcmPercent { get; set; } = 40m;
    public EngagementStatus Status { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public BillingSchedule Schedule { get; set; } = null!;
}
public class BillingSchedule : Record
{
    public int EngagementId { get; set; }
    public Engagement Engagement { get; set; } = null!;
    public Frequency Frequency { get; set; }
    public DateOnly? NextPeriodStart { get; set; }
    public int AnchorDay { get; set; }
}
public class BillingRecord : Record
{
    public int EngagementId { get; set; }
    public Engagement Engagement { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public BillingStatus Status { get; set; }
    /// <summary>Immutable historical base used to calculate the revenue-share snapshots.</summary>
    public decimal RevenueShareBaseAmount { get; set; }
    public decimal Amount { get; set; }
    public string CustomerName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string? CancellationReason { get; set; }
    public List<RevenueShareAllocation> Shares { get; set; } = [];
    public WorkItem WorkItem { get; set; } = null!;
    public List<InvoiceLine> InvoiceLines { get; set; } = [];
    [NotMapped] public decimal CustomerInvoicedAmount => InvoiceLines.Where(x => x.Invoice.Status != InvoiceStatus.Cancelled && x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer).Sum(x => x.AllocatedAmount);
    [NotMapped] public BillingInvoiceState CustomerInvoiceState => CustomerInvoicedAmount <= 0 ? BillingInvoiceState.Unbilled : CustomerInvoicedAmount < Amount ? BillingInvoiceState.PartiallyInvoiced : BillingInvoiceState.FullyInvoiced;
    [NotMapped] public decimal CustomerReceivedAmount => InvoiceLines.Where(x => x.Invoice.Status != InvoiceStatus.Cancelled && x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Invoice.Total > 0).Sum(x => x.Invoice.ReceiptAllocations.Where(y => !y.CustomerReceipt.IsCancelled).Sum(y => y.Amount) * x.AllocatedAmount / x.Invoice.Total);
    [NotMapped] public decimal CustomerOutstandingAmount => Math.Max(0m, CustomerInvoicedAmount - CustomerReceivedAmount);
}
public enum BillingInvoiceState { Unbilled, PartiallyInvoiced, FullyInvoiced }
public enum ShareKind { Firm, Manager, Lcm }
public class RevenueShareAllocation : Record
{
    public int BillingRecordId { get; set; }
    public BillingRecord BillingRecord { get; set; } = null!;
    public ShareKind Kind { get; set; }
    public string PartyName { get; set; } = "";
    public decimal Percent { get; set; }
    public decimal Amount { get; set; }
}
public class WorkItem : Record
{
    public int BillingRecordId { get; set; }
    public BillingRecord BillingRecord { get; set; } = null!;
    public WorkStatus Status { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public List<WorkerAssignment> Assignments { get; set; } = [];
}
public class WorkerAssignment : Record
{
    public int WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;
    public int WorkerId { get; set; }
    public Worker Worker { get; set; } = null!;
    public string WorkerName { get; set; } = "";
    public decimal Percent { get; set; }
    public decimal LcmGrossSnapshot { get; set; }
    public decimal Entitlement { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
    public List<WorkerPaymentAllocation> Allocations { get; set; } = [];
}
public class WorkerPayment : Record
{
    public int WorkerId { get; set; }
    public Worker Worker { get; set; } = null!;
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
    public Guid RequestId { get; set; }
    public List<WorkerPaymentAllocation> Allocations { get; set; } = [];
}
public class WorkerPaymentAllocation : Record
{
    public int WorkerPaymentId { get; set; }
    public WorkerPayment WorkerPayment { get; set; } = null!;
    public int WorkerAssignmentId { get; set; }
    public WorkerAssignment WorkerAssignment { get; set; } = null!;
    public decimal Amount { get; set; }
}
public enum InvoiceFlow { AccountingFirmToCustomer, ManagerToAccountingFirm, LcmToManager }
public enum InvoiceStatus { Issued, PartiallyPaid, Paid, Cancelled }
public class Invoice : Record
{
    [Required, StringLength(100)] public string InvoiceNumber { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public InvoiceFlow Flow { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public decimal Total { get; set; }
    public string IssuerName { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public int? BusinessPartyId { get; set; }
    public BusinessParty? BusinessParty { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? ManagerId { get; set; }
    public Manager? Manager { get; set; }
    public string? CancellationReason { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    public List<CustomerReceiptAllocation> ReceiptAllocations { get; set; } = [];
}
public class InvoiceLine : Record
{
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public int BillingRecordId { get; set; }
    public BillingRecord BillingRecord { get; set; } = null!;
    public decimal AllocatedAmount { get; set; }
}
public class CustomerReceipt : Record
{
    public DateOnly ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public Guid RequestId { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
    public List<CustomerReceiptAllocation> Allocations { get; set; } = [];
}
public class CustomerReceiptAllocation : Record
{
    public int CustomerReceiptId { get; set; }
    public CustomerReceipt CustomerReceipt { get; set; } = null!;
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public decimal Amount { get; set; }
}
