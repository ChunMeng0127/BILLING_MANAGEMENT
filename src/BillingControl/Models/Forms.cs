using System.ComponentModel.DataAnnotations;
using BillingControl.Services;

namespace BillingControl.Models;

public class LoginForm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
public class MasterForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
    [StringLength(60)] public string? RegistrationNumber { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public WorkerType Type { get; set; }
}
public class EngagementForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    public long ScheduleVersion { get; set; }
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    [Range(1, int.MaxValue)] public int ServiceId { get; set; }
    [Range(1, int.MaxValue)] public int BusinessPartyId { get; set; }
    [Range(1, int.MaxValue)] public int ManagerId { get; set; }
    [DataType(DataType.Date)] public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
    public decimal BillingAmount { get; set; }
    public decimal FirmPercent { get; set; } = 35;
    public decimal ManagerPercent { get; set; } = 25;
    public decimal LcmPercent { get; set; } = 40;
    public Frequency Frequency { get; set; }
    [DataType(DataType.Date)] public DateOnly? NextPeriodStart { get; set; }
    [Range(1, 31)] public int AnchorDay { get; set; } = 1;
    public EngagementStatus Status { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}
public class BillingScheduleForm
{
    public int EngagementId { get; set; }
    public long EngagementVersion { get; set; }
    public long Version { get; set; }
    public Frequency Frequency { get; set; }
    [DataType(DataType.Date)] public DateOnly? NextPeriodStart { get; set; }
    [Range(1, 31)] public int AnchorDay { get; set; } = 1;
}
public class BillingRecordEditForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    public long WorkItemVersion { get; set; }
    [DataType(DataType.Date)] public DateOnly PeriodStart { get; set; }
    [DataType(DataType.Date)] public DateOnly PeriodEnd { get; set; }
    [Range(typeof(decimal), "0.01", "9999999999.99")] public decimal CustomerBillingAmount { get; set; }
    [Range(typeof(decimal), "0.01", "9999999999.99")] public decimal RevenueShareBaseAmount { get; set; }
    public BillingStatus Status { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}
public class UserForm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, MinLength(12), DataType(DataType.Password)] public string Password { get; set; } = "";
    public string Role { get; set; } = AppRoles.InternalUser;
    public int? BusinessPartyId { get; set; }
    public int? ManagerId { get; set; }
    public int? WorkerId { get; set; }
}
public class InvoiceForm
{
    [Required, StringLength(100)] public string InvoiceNumber { get; set; } = "";
    [DataType(DataType.Date)] public DateOnly InvoiceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public InvoiceFlow Flow { get; set; }
    public Dictionary<int, decimal?> Allocations { get; set; } = [];
}
public class InvoiceEditForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    [Required, StringLength(100)] public string InvoiceNumber { get; set; } = "";
    [DataType(DataType.Date)] public DateOnly InvoiceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public InvoiceFlow Flow { get; set; }
    public Dictionary<int, decimal?> Allocations { get; set; } = [];
}
public class ReceiptForm
{
    [DataType(DataType.Date)] public DateOnly ReceiptDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Required, StringLength(160)] public string Reference { get; set; } = "";
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public Dictionary<int, decimal?> Allocations { get; set; } = [];
}

public class ReceiptEditForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    [DataType(DataType.Date)] public DateOnly ReceiptDate { get; set; }
    [Required, StringLength(160)] public string Reference { get; set; } = "";
    public Dictionary<int, decimal?> Allocations { get; set; } = [];
}

public class WorkerAssignmentEditForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    [Range(1, int.MaxValue)] public int WorkerId { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal Percent { get; set; }
}

public class WorkerPaymentEditForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    [DataType(DataType.Date)] public DateOnly PaymentDate { get; set; }
    [Required, StringLength(160)] public string Reference { get; set; } = "";
}

public class WeeklyProgressForm
{
    public int Id { get; set; }
    public long Version { get; set; }
    [Range(1, int.MaxValue)] public int WorkerAssignmentId { get; set; }
    public long AssignmentVersion { get; set; }
    [DataType(DataType.Date)] public DateOnly WeekStart { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal ProgressPercent { get; set; }
    public WorkflowStatus WorkflowStatus { get; set; } = WorkflowStatus.AssignedNotStarted;
    public int? WorkflowVersion { get; set; }
    [Required, StringLength(4000)] public string WorkDone { get; set; } = "";
    [StringLength(2000)] public string? NextAction { get; set; }
    [StringLength(2000)] public string? IssuesOrBlockers { get; set; }
}

public class BatchWorkflowForm
{
    public List<int> AssignmentIds { get; set; } = [];
    public Dictionary<int, long> Versions { get; set; } = [];
    public BatchWorkflowAction Action { get; set; } = BatchWorkflowAction.UpdateWorkflow;
    public WorkflowStatus WorkflowStatus { get; set; } = WorkflowStatus.AssignedNotStarted;
    public int? WorkflowVersion { get; set; }
    public DateOnly? ReturnWeekStart { get; set; }
    public AssignmentListFilter ReturnFilter { get; set; } = AssignmentListFilter.Active;
}
