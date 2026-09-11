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
    public DbSet<DocumentRequirementTemplate> DocumentRequirementTemplates => Set<DocumentRequirementTemplate>();
    public DbSet<DocumentRequirementTemplateItem> DocumentRequirementTemplateItems => Set<DocumentRequirementTemplateItem>();
    public DbSet<DocumentRequest> DocumentRequests => Set<DocumentRequest>();
    public DbSet<DocumentRequestItem> DocumentRequestItems => Set<DocumentRequestItem>();
    public DbSet<ReceivedDocument> ReceivedDocuments => Set<ReceivedDocument>();
    public DbSet<DocumentRequestItemEvidence> DocumentRequestItemEvidences => Set<DocumentRequestItemEvidence>();
    public DbSet<DocumentRequestBatch> DocumentRequestBatches => Set<DocumentRequestBatch>();
    public DbSet<DocumentRequestBatchMember> DocumentRequestBatchMembers => Set<DocumentRequestBatchMember>();
    public DbSet<DocumentRequestStatusHistory> DocumentRequestStatusHistories => Set<DocumentRequestStatusHistory>();
    public DbSet<DocumentRequestItemStatusHistory> DocumentRequestItemStatusHistories => Set<DocumentRequestItemStatusHistory>();
    public DbSet<ReceivedDocumentStatusHistory> ReceivedDocumentStatusHistories => Set<ReceivedDocumentStatusHistory>();
    public DbSet<DocumentRequestItemEvidenceHistory> DocumentRequestItemEvidenceHistories => Set<DocumentRequestItemEvidenceHistory>();

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

        b.Entity<DocumentRequirementTemplate>().Property(x => x.TemplateKey).HasMaxLength(100);
        b.Entity<DocumentRequirementTemplate>().Property(x => x.Name).HasMaxLength(160);
        b.Entity<DocumentRequirementTemplate>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<DocumentRequirementTemplate>().HasIndex(x => new { x.ServiceId, x.TemplateKey, x.TemplateVersion }).IsUnique();
        b.Entity<DocumentRequirementTemplate>().HasIndex(x => x.ServiceId).IsUnique().HasFilter("\"IsActive\" = TRUE AND \"IsDefault\" = TRUE");
        b.Entity<DocumentRequirementTemplate>().HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequirementTemplate>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_DocumentRequirementTemplate_TemplateVersion", "\"TemplateVersion\" > 0");
            t.HasCheckConstraint("CK_DocumentRequirementTemplate_DefaultRequiresActive", "NOT \"IsDefault\" OR \"IsActive\"");
        });

        b.Entity<DocumentRequirementTemplateItem>().Property(x => x.RequirementKey).HasMaxLength(100);
        b.Entity<DocumentRequirementTemplateItem>().Property(x => x.Name).HasMaxLength(160);
        b.Entity<DocumentRequirementTemplateItem>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<DocumentRequirementTemplateItem>().HasIndex(x => new { x.DocumentRequirementTemplateId, x.RequirementKey }).IsUnique();
        b.Entity<DocumentRequirementTemplateItem>().HasOne(x => x.DocumentRequirementTemplate)
            .WithMany(x => x.Items).HasForeignKey(x => x.DocumentRequirementTemplateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequirementTemplateItem>().ToTable(t =>
            t.HasCheckConstraint("CK_DocumentRequirementTemplateItem_DisplayOrder", "\"DisplayOrder\" >= 0"));

        b.Entity<DocumentRequest>().HasIndex(x => new { x.WorkItemId, x.Revision }).IsUnique();
        b.Entity<DocumentRequest>().HasIndex(x => x.WorkItemId).IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2, 3, 4, 5)");
        b.Entity<DocumentRequest>().HasOne(x => x.WorkItem).WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequest>().HasOne(x => x.DocumentRequirementTemplate).WithMany(x => x.Requests)
            .HasForeignKey(x => x.DocumentRequirementTemplateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequest>().HasOne(x => x.SupersedesRequest).WithMany(x => x.SupersedingRequests)
            .HasForeignKey(x => x.SupersedesRequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequest>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_DocumentRequest_Revision", "\"Revision\" > 0");
            t.HasCheckConstraint("CK_DocumentRequest_NoSelfSupersession", "\"SupersedesRequestId\" IS NULL OR \"SupersedesRequestId\" <> \"Id\"");
        });

        b.Entity<DocumentRequestItem>().Property(x => x.RequirementKey).HasMaxLength(100);
        b.Entity<DocumentRequestItem>().Property(x => x.RequirementName).HasMaxLength(160);
        b.Entity<DocumentRequestItem>().Property(x => x.RequirementDescription).HasMaxLength(2000);
        b.Entity<DocumentRequestItem>().HasIndex(x => new { x.DocumentRequestId, x.RequirementKey }).IsUnique();
        b.Entity<DocumentRequestItem>().HasOne(x => x.DocumentRequest).WithMany(x => x.Items)
            .HasForeignKey(x => x.DocumentRequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestItem>().HasOne(x => x.DocumentRequirementTemplateItem).WithMany(x => x.RequestItems)
            .HasForeignKey(x => x.DocumentRequirementTemplateItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestItem>().ToTable(t =>
            t.HasCheckConstraint("CK_DocumentRequestItem_DisplayOrder", "\"DisplayOrder\" >= 0"));

        b.Entity<ReceivedDocument>().Property(x => x.SenderSnapshot).HasMaxLength(254);
        b.Entity<ReceivedDocument>().Property(x => x.SourceSnapshot).HasMaxLength(254);
        b.Entity<ReceivedDocument>().Property(x => x.OriginalFileName).HasMaxLength(512);
        b.Entity<ReceivedDocument>().Property(x => x.MimeType).HasMaxLength(254);
        b.Entity<ReceivedDocument>().Property(x => x.Sha256Hash).HasMaxLength(64);
        b.Entity<ReceivedDocument>().HasIndex(x => x.Sha256Hash);
        b.Entity<ReceivedDocument>().HasOne(x => x.SupersedesReceivedDocument).WithMany(x => x.SupersedingDocuments)
            .HasForeignKey(x => x.SupersedesReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReceivedDocument>().HasOne(x => x.DuplicateOfReceivedDocument).WithMany(x => x.DuplicateDocuments)
            .HasForeignKey(x => x.DuplicateOfReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReceivedDocument>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ReceivedDocument_ByteLength", "\"ByteLength\" IS NULL OR \"ByteLength\" >= 0");
            t.HasCheckConstraint("CK_ReceivedDocument_Sha256Hash", "\"Sha256Hash\" IS NULL OR \"Sha256Hash\" ~ '^[0-9A-Fa-f]{64}$'");
            t.HasCheckConstraint("CK_ReceivedDocument_NoSelfSupersession", "\"SupersedesReceivedDocumentId\" IS NULL OR \"SupersedesReceivedDocumentId\" <> \"Id\"");
            t.HasCheckConstraint("CK_ReceivedDocument_NoSelfDuplicate", "\"DuplicateOfReceivedDocumentId\" IS NULL OR \"DuplicateOfReceivedDocumentId\" <> \"Id\"");
        });

        b.Entity<DocumentRequestItemEvidence>().Property(x => x.InactivationReason).HasMaxLength(2000);
        b.Entity<DocumentRequestItemEvidence>().HasIndex(x => new { x.DocumentRequestItemId, x.ReceivedDocumentId }).IsUnique();
        b.Entity<DocumentRequestItemEvidence>().HasOne(x => x.DocumentRequestItem).WithMany(x => x.EvidenceLinks)
            .HasForeignKey(x => x.DocumentRequestItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestItemEvidence>().HasOne(x => x.ReceivedDocument).WithMany(x => x.EvidenceLinks)
            .HasForeignKey(x => x.ReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestItemEvidence>().ToTable(t =>
            t.HasCheckConstraint("CK_DocumentRequestItemEvidence_Lifecycle", "(\"IsActive\" AND \"InactivatedAt\" IS NULL AND \"InactivationReason\" IS NULL) OR (NOT \"IsActive\" AND \"InactivatedAt\" IS NOT NULL AND \"InactivationReason\" IS NOT NULL)"));

        b.Entity<DocumentRequestBatchMember>().HasIndex(x => new { x.DocumentRequestBatchId, x.DocumentRequestId }).IsUnique();
        b.Entity<DocumentRequestBatchMember>().HasOne(x => x.DocumentRequestBatch).WithMany(x => x.Members)
            .HasForeignKey(x => x.DocumentRequestBatchId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestBatchMember>().HasOne(x => x.DocumentRequest).WithMany(x => x.BatchMemberships)
            .HasForeignKey(x => x.DocumentRequestId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<DocumentRequestStatusHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<DocumentRequestStatusHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<DocumentRequestStatusHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<DocumentRequestStatusHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<DocumentRequestStatusHistory>().Property(x => x.CorrelationId).HasMaxLength(254);
        b.Entity<DocumentRequestStatusHistory>().HasIndex(x => new { x.DocumentRequestId, x.OccurredAt });
        b.Entity<DocumentRequestStatusHistory>().HasOne(x => x.DocumentRequest).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.DocumentRequestId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<DocumentRequestItemStatusHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<DocumentRequestItemStatusHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<DocumentRequestItemStatusHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<DocumentRequestItemStatusHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<DocumentRequestItemStatusHistory>().Property(x => x.CorrelationId).HasMaxLength(254);
        b.Entity<DocumentRequestItemStatusHistory>().HasIndex(x => new { x.DocumentRequestItemId, x.OccurredAt });
        b.Entity<DocumentRequestItemStatusHistory>().HasOne(x => x.DocumentRequestItem).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.DocumentRequestItemId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ReceivedDocumentStatusHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<ReceivedDocumentStatusHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<ReceivedDocumentStatusHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<ReceivedDocumentStatusHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<ReceivedDocumentStatusHistory>().Property(x => x.CorrelationId).HasMaxLength(254);
        b.Entity<ReceivedDocumentStatusHistory>().HasIndex(x => new { x.ReceivedDocumentId, x.OccurredAt });
        b.Entity<ReceivedDocumentStatusHistory>().HasOne(x => x.ReceivedDocument).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.ReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<DocumentRequestItemEvidenceHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<DocumentRequestItemEvidenceHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<DocumentRequestItemEvidenceHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<DocumentRequestItemEvidenceHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<DocumentRequestItemEvidenceHistory>().Property(x => x.CorrelationId).HasMaxLength(254);
        b.Entity<DocumentRequestItemEvidenceHistory>().HasIndex(x => new { x.DocumentRequestItemEvidenceId, x.OccurredAt });
        b.Entity<DocumentRequestItemEvidenceHistory>().HasOne(x => x.DocumentRequestItemEvidence).WithMany(x => x.History)
            .HasForeignKey(x => x.DocumentRequestItemEvidenceId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<DocumentRequestStatusHistory>().ToTable(t => t.HasCheckConstraint("CK_DocumentRequestStatusHistory_Actor", "length(btrim(\"Actor\")) > 0"));
        b.Entity<DocumentRequestItemStatusHistory>().ToTable(t => t.HasCheckConstraint("CK_DocumentRequestItemStatusHistory_Actor", "length(btrim(\"Actor\")) > 0"));
        b.Entity<ReceivedDocumentStatusHistory>().ToTable(t => t.HasCheckConstraint("CK_ReceivedDocumentStatusHistory_Actor", "length(btrim(\"Actor\")) > 0"));
        b.Entity<DocumentRequestItemEvidenceHistory>().ToTable(t => t.HasCheckConstraint("CK_DocumentRequestItemEvidenceHistory_Actor", "length(btrim(\"Actor\")) > 0"));
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
                    DocumentRequestStatusHistory => [],
                    DocumentRequestItemStatusHistory => [],
                    ReceivedDocumentStatusHistory => [],
                    DocumentRequestItemEvidenceHistory => [],
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
