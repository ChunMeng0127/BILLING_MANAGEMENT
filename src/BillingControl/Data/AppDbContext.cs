using BillingControl.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BillingControl.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http = null) : IdentityDbContext<AppUser>(options)
{
    private bool allowInvoiceLineDeletion;
    private bool allowBillingSnapshotCorrection;
    private bool allowReceiptAllocationCorrection;
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<BusinessParty> BusinessParties => Set<BusinessParty>();
    public DbSet<Manager> Managers => Set<Manager>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<Engagement> Engagements => Set<Engagement>();
    public DbSet<BillingSchedule> BillingSchedules => Set<BillingSchedule>();
    public DbSet<BillingRecord> BillingRecords => Set<BillingRecord>();
    public DbSet<RevenueShareAllocation> RevenueShareAllocations => Set<RevenueShareAllocation>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkerAssignment> WorkerAssignments => Set<WorkerAssignment>();
    public DbSet<WeeklyProgressReport> WeeklyProgressReports => Set<WeeklyProgressReport>();
    public DbSet<WeeklyProgressUpdateHistory> WeeklyProgressUpdateHistories => Set<WeeklyProgressUpdateHistory>();
    public DbSet<WorkerAssignmentWorkflowHistory> WorkerAssignmentWorkflowHistories => Set<WorkerAssignmentWorkflowHistory>();
    public DbSet<WorkerPayment> WorkerPayments => Set<WorkerPayment>();
    public DbSet<WorkerPaymentAllocation> WorkerPaymentAllocations => Set<WorkerPaymentAllocation>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<CustomerReceiptAllocation> CustomerReceiptAllocations => Set<CustomerReceiptAllocation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().HasOne(x => x.BusinessParty).WithMany().HasForeignKey(x => x.BusinessPartyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().HasOne(x => x.Worker).WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().ToTable(t => t.HasCheckConstraint("CK_User_AtMostOneLink", "(CASE WHEN \"BusinessPartyId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"ManagerId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"WorkerId\" IS NULL THEN 0 ELSE 1 END) <= 1"));
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Record).IsAssignableFrom(t.ClrType)).ToList())
        {
            b.Entity(type.ClrType).HasKey("Id");
            b.Entity(type.ClrType).Property("Version").IsConcurrencyToken();
            foreach (var p in type.GetProperties().Where(p => p.ClrType == typeof(decimal)))
                b.Entity(type.ClrType).Property(p.Name).HasPrecision(18, p.Name.Contains("Percent") ? 4 : 2);
            foreach (var p in type.GetProperties().Where(p => p.ClrType == typeof(string)))
                b.Entity(type.ClrType).Property(p.Name).HasMaxLength(p.Name.Contains("Notes") || p.Name.Contains("Reason") ? 2000 : 254);
        }
        b.Entity<Customer>().HasIndex(x => x.Name);
        b.Entity<Worker>().HasIndex(x => x.Name);
        b.Entity<AppUser>().HasIndex(x => x.BusinessPartyId);
        b.Entity<AppUser>().HasIndex(x => x.ManagerId);
        b.Entity<AppUser>().HasIndex(x => x.WorkerId);
        b.Entity<Engagement>().HasIndex(x => new { x.CustomerId, x.ServiceId, x.Status });
        b.Entity<Engagement>().HasOne(x => x.Schedule).WithOne(x => x.Engagement).HasForeignKey<BillingSchedule>(x => x.EngagementId);
        b.Entity<Engagement>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Engagement_Percent", "\"FirmPercent\" >= 0 AND \"ManagerPercent\" >= 0 AND \"LcmPercent\" >= 0 AND \"FirmPercent\" + \"ManagerPercent\" + \"LcmPercent\" = 100");
            t.HasCheckConstraint("CK_Engagement_Amount", "\"BillingAmount\" > 0");
            t.HasCheckConstraint("CK_Engagement_Dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
        });
        b.Entity<BillingSchedule>().ToTable(t => t.HasCheckConstraint("CK_Schedule_Anchor", "\"AnchorDay\" BETWEEN 1 AND 31"));
        b.Entity<BillingRecord>().HasIndex(x => new { x.EngagementId, x.PeriodStart, x.PeriodEnd }).IsUnique().HasFilter("\"Status\" <> 7");
        b.Entity<BillingRecord>().HasIndex(x => new { x.PeriodStart, x.Status });
        b.Entity<BillingRecord>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Billing_Dates", "\"PeriodEnd\" >= \"PeriodStart\"");
            t.HasCheckConstraint("CK_Billing_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_Billing_RevenueShareBaseAmount", "\"RevenueShareBaseAmount\" > 0");
        });
        b.Entity<RevenueShareAllocation>().HasIndex(x => new { x.BillingRecordId, x.Kind }).IsUnique();
        b.Entity<RevenueShareAllocation>().ToTable(t => t.HasCheckConstraint("CK_Share", "\"Amount\" >= 0 AND \"Percent\" BETWEEN 0 AND 100"));
        b.Entity<Invoice>().HasIndex(x => new { x.BusinessPartyId, x.InvoiceNumber }).IsUnique().HasFilter("\"Flow\" = 0");
        b.Entity<Invoice>().HasIndex(x => new { x.ManagerId, x.InvoiceNumber }).IsUnique().HasFilter("\"Flow\" = 1");
        b.Entity<Invoice>().HasIndex(x => x.InvoiceNumber).IsUnique().HasFilter("\"Flow\" = 2");
        b.Entity<Invoice>().Property(x => x.InvoiceNumber).HasMaxLength(100);
        b.Entity<Invoice>().HasOne(x => x.BusinessParty).WithMany().HasForeignKey(x => x.BusinessPartyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Invoice>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Invoice>().HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Invoice>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Invoice_Total", "\"Total\" > 0");
            t.HasCheckConstraint("CK_Invoice_Party", "(\"Flow\" = 0 AND \"BusinessPartyId\" IS NOT NULL AND \"CustomerId\" IS NOT NULL AND \"ManagerId\" IS NULL) OR (\"Flow\" = 1 AND \"BusinessPartyId\" IS NOT NULL AND \"CustomerId\" IS NULL AND \"ManagerId\" IS NOT NULL) OR (\"Flow\" = 2 AND \"BusinessPartyId\" IS NULL AND \"CustomerId\" IS NULL AND \"ManagerId\" IS NOT NULL)");
        });
        b.Entity<InvoiceLine>().HasIndex(x => new { x.InvoiceId, x.BillingRecordId }).IsUnique();
        b.Entity<InvoiceLine>().ToTable(t => t.HasCheckConstraint("CK_InvoiceLine_Amount", "\"AllocatedAmount\" > 0"));
        b.Entity<BillingRecord>().HasOne(x => x.WorkItem).WithOne(x => x.BillingRecord).HasForeignKey<WorkItem>(x => x.BillingRecordId);
        b.Entity<WorkerAssignment>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Assignment", "\"Percent\" >= 0 AND \"Percent\" <= 100 AND \"LcmGrossSnapshot\" >= 0 AND \"Entitlement\" >= 0");
            t.HasCheckConstraint("CK_Assignment_CurrentProgress", "\"CurrentProgressPercent\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_Assignment_CurrentWorkflowVersion", "\"CurrentWorkflowVersion\" IS NULL OR \"CurrentWorkflowVersion\" > 0");
        });
        b.Entity<WorkerAssignmentWorkflowHistory>().HasIndex(x => new { x.WorkerAssignmentId, x.ChangedAt });
        b.Entity<WorkerAssignmentWorkflowHistory>().ToTable(t => t.HasCheckConstraint("CK_WorkflowHistory_NewVersion", "\"NewWorkflowVersion\" IS NULL OR \"NewWorkflowVersion\" > 0"));
        b.Entity<WeeklyProgressReport>().HasIndex(x => new { x.WorkerAssignmentId, x.WeekStart }).IsUnique();
        b.Entity<WeeklyProgressReport>().Property(x => x.WorkDone).HasMaxLength(4000);
        b.Entity<WeeklyProgressReport>().Property(x => x.NextAction).HasMaxLength(2000);
        b.Entity<WeeklyProgressReport>().Property(x => x.IssuesOrBlockers).HasMaxLength(2000);
        b.Entity<WeeklyProgressReport>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WeeklyProgress_Dates", "\"WeekEnd\" >= \"WeekStart\"");
            t.HasCheckConstraint("CK_WeeklyProgress_Percent", "\"ProgressPercent\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_WeeklyProgress_WorkflowVersion", "\"WorkflowVersionAtSubmission\" IS NULL OR \"WorkflowVersionAtSubmission\" > 0");
        });
        b.Entity<WeeklyProgressUpdateHistory>().HasIndex(x => new { x.WeeklyProgressReportId, x.OccurredAt });
        b.Entity<WeeklyProgressUpdateHistory>().HasIndex(x => new { x.WorkerAssignmentId, x.OccurredAt });
        b.Entity<WeeklyProgressUpdateHistory>().HasOne(x => x.WeeklyProgressReport).WithMany(x => x.UpdateHistory).HasForeignKey(x => x.WeeklyProgressReportId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WeeklyProgressUpdateHistory>().HasOne(x => x.WorkerAssignment).WithMany(x => x.ProgressUpdateHistory).HasForeignKey(x => x.WorkerAssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WeeklyProgressUpdateHistory>().Property(x => x.WorkDone).HasMaxLength(4000);
        b.Entity<WeeklyProgressUpdateHistory>().Property(x => x.NextAction).HasMaxLength(2000);
        b.Entity<WeeklyProgressUpdateHistory>().Property(x => x.IssuesOrBlockers).HasMaxLength(2000);
        b.Entity<WeeklyProgressUpdateHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<WeeklyProgressUpdateHistory>().Property(x => x.Source).HasMaxLength(40);
        b.Entity<WeeklyProgressUpdateHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WeeklyProgressHistory_Percent", "\"ProgressPercent\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_WeeklyProgressHistory_WorkflowVersion", "\"WorkflowVersion\" IS NULL OR \"WorkflowVersion\" > 0");
        });
        b.Entity<WorkerPayment>().HasIndex(x => x.RequestId).IsUnique();
        b.Entity<CustomerReceipt>().HasIndex(x => x.RequestId).IsUnique();
        b.Entity<WorkerPayment>().ToTable(t => t.HasCheckConstraint("CK_Payment", "\"Amount\" > 0"));
        b.Entity<CustomerReceipt>().ToTable(t => t.HasCheckConstraint("CK_Receipt", "\"Amount\" > 0"));
        b.Entity<CustomerReceiptAllocation>().HasIndex(x => new { x.CustomerReceiptId, x.InvoiceId }).IsUnique();
        b.Entity<CustomerReceiptAllocation>().ToTable(t => t.HasCheckConstraint("CK_ReceiptAllocation_Amount", "\"Amount\" > 0"));
        b.Entity<WorkerPaymentAllocation>().HasIndex(x => new { x.WorkerPaymentId, x.WorkerAssignmentId }).IsUnique();
        b.Entity<WorkerPaymentAllocation>().ToTable(t => t.HasCheckConstraint("CK_PaymentAllocation", "\"Amount\" > 0"));
        foreach (var fk in b.Model.GetEntityTypes().Where(t => typeof(Record).IsAssignableFrom(t.ClrType)).SelectMany(t => t.GetForeignKeys())) fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var e in ChangeTracker.Entries<Record>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (e.State == EntityState.Deleted)
            {
                if (e.Entity is InvoiceLine && allowInvoiceLineDeletion) continue;
                throw new InvalidOperationException("Use cancellation or deactivate records instead of deleting them.");
            }
            if (e.State == EntityState.Modified)
            {
                string[] allowed = e.Entity switch
                {
                    BillingRecord => allowBillingSnapshotCorrection
                        ? ["PeriodStart", "PeriodEnd", "Status", "CancellationReason", "Amount", "RevenueShareBaseAmount"]
                        : ["PeriodStart", "PeriodEnd", "Status", "CancellationReason"],
                    Invoice => ["InvoiceNumber", "InvoiceDate", "Total", "Status", "CancellationReason"],
                    RevenueShareAllocation => allowBillingSnapshotCorrection ? ["Amount"] : [],
                    WorkerPaymentAllocation => [],
                    CustomerReceiptAllocation => allowReceiptAllocationCorrection ? ["Amount"] : [],
                    InvoiceLine => ["BillingRecordId", "AllocatedAmount"],
                    WeeklyProgressReport => ["ProgressPercent", "ProgressStatus", "WorkflowStatusAtSubmission", "WorkflowVersionAtSubmission", "WorkDone", "NextAction", "IssuesOrBlockers"],
                    WeeklyProgressUpdateHistory => [],
                    WorkerAssignment => ["WorkerId", "WorkerName", "Percent", "Entitlement", "IsCancelled", "CancellationReason", "CurrentWorkflowStatus", "CurrentWorkflowVersion", "CurrentProgressPercent", "IsHidden", "HiddenAt", "HiddenBy", "ReportingResumedFromWeek"],
                    WorkerPayment => ["PaymentDate", "Reference", "IsCancelled", "CancellationReason"],
                    CustomerReceipt => allowReceiptAllocationCorrection
                        ? ["ReceiptDate", "Reference", "Amount", "IsCancelled", "CancellationReason"]
                        : ["ReceiptDate", "Reference", "IsCancelled", "CancellationReason"],
                    _ => e.Properties.Select(p => p.Metadata.Name).Except(["CreatedAt", "CreatedBy", "Id"]).ToArray()
                };
                if (e.Properties.Any(p => p.IsModified && !allowed.Contains(p.Metadata.Name)))
                    throw new InvalidOperationException("Historical snapshots and audit origins cannot be edited.");
            }
            var actor = http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (e.State == EntityState.Added) { e.Entity.CreatedAt = DateTime.UtcNow; e.Entity.CreatedBy = actor; }
            e.Entity.UpdatedAt = DateTime.UtcNow; e.Entity.UpdatedBy = actor; e.Entity.Version++;
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    internal IDisposable PermitInvoiceLineDeletion()
    {
        var previous = allowInvoiceLineDeletion;
        allowInvoiceLineDeletion = true;
        return new DeletionScope(this, previous);
    }

    internal IDisposable PermitBillingSnapshotCorrection()
    {
        var previous = allowBillingSnapshotCorrection;
        allowBillingSnapshotCorrection = true;
        return new SnapshotScope(this, previous);
    }

    internal IDisposable PermitReceiptAllocationCorrection()
    {
        var previous = allowReceiptAllocationCorrection;
        allowReceiptAllocationCorrection = true;
        return new ReceiptAllocationScope(this, previous);
    }

    private sealed class DeletionScope(AppDbContext context, bool previous) : IDisposable
    {
        public void Dispose() => context.allowInvoiceLineDeletion = previous;
    }

    private sealed class SnapshotScope(AppDbContext context, bool previous) : IDisposable
    {
        public void Dispose() => context.allowBillingSnapshotCorrection = previous;
    }

    private sealed class ReceiptAllocationScope(AppDbContext context, bool previous) : IDisposable
    {
        public void Dispose() => context.allowReceiptAllocationCorrection = previous;
    }
}
