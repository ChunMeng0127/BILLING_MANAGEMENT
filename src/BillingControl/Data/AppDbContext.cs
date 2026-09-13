using BillingControl.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactWhatsAppAddress> ContactWhatsAppAddresses => Set<ContactWhatsAppAddress>();
    public DbSet<ContactCustomerLink> ContactCustomerLinks => Set<ContactCustomerLink>();
    public DbSet<ContactStatusHistory> ContactStatusHistories => Set<ContactStatusHistory>();
    public DbSet<ContactWhatsAppAddressHistory> ContactWhatsAppAddressHistories => Set<ContactWhatsAppAddressHistory>();
    public DbSet<ContactCustomerLinkHistory> ContactCustomerLinkHistories => Set<ContactCustomerLinkHistory>();
    public DbSet<WhatsAppConversation> WhatsAppConversations => Set<WhatsAppConversation>();
    public DbSet<WhatsAppConversationParticipant> WhatsAppConversationParticipants => Set<WhatsAppConversationParticipant>();
    public DbSet<WhatsAppConversationEngagementScope> WhatsAppConversationEngagementScopes => Set<WhatsAppConversationEngagementScope>();
    public DbSet<WhatsAppConversationHistory> WhatsAppConversationHistories => Set<WhatsAppConversationHistory>();
    public DbSet<WhatsAppConversationParticipantHistory> WhatsAppConversationParticipantHistories => Set<WhatsAppConversationParticipantHistory>();
    public DbSet<WhatsAppConversationEngagementScopeHistory> WhatsAppConversationEngagementScopeHistories => Set<WhatsAppConversationEngagementScopeHistory>();

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
            t.HasCheckConstraint("CK_DocumentRequirementTemplate_Text", "length(btrim(\"TemplateKey\")) > 0 AND length(btrim(\"Name\")) > 0");
        });

        b.Entity<DocumentRequirementTemplateItem>().Property(x => x.RequirementKey).HasMaxLength(100);
        b.Entity<DocumentRequirementTemplateItem>().Property(x => x.Name).HasMaxLength(160);
        b.Entity<DocumentRequirementTemplateItem>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<DocumentRequirementTemplateItem>().HasIndex(x => new { x.DocumentRequirementTemplateId, x.RequirementKey }).IsUnique();
        b.Entity<DocumentRequirementTemplateItem>().HasOne(x => x.DocumentRequirementTemplate)
            .WithMany(x => x.Items).HasForeignKey(x => x.DocumentRequirementTemplateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequirementTemplateItem>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_DocumentRequirementTemplateItem_DisplayOrder", "\"DisplayOrder\" >= 0");
            t.HasCheckConstraint("CK_DocumentRequirementTemplateItem_Wave", "\"Wave\" IN (0, 1, 2, 3)");
            t.HasCheckConstraint("CK_DocumentRequirementTemplateItem_Text", "length(btrim(\"RequirementKey\")) > 0 AND length(btrim(\"Name\")) > 0");
        });

        b.Entity<DocumentRequest>().HasIndex(x => new { x.WorkItemId, x.Revision }).IsUnique();
        b.Entity<DocumentRequest>().HasIndex(x => x.WorkItemId).IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2, 3, 4, 5)");
        b.Entity<DocumentRequest>().HasIndex(x => x.SupersedesRequestId).IsUnique()
            .HasFilter("\"SupersedesRequestId\" IS NOT NULL");
        b.Entity<DocumentRequest>().HasOne(x => x.WorkItem).WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequest>().HasOne(x => x.DocumentRequirementTemplate).WithMany(x => x.Requests)
            .HasForeignKey(x => x.DocumentRequirementTemplateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequest>().HasOne(x => x.SupersedesRequest).WithMany(x => x.SupersedingRequests)
            .HasForeignKey(x => x.SupersedesRequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequest>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_DocumentRequest_Revision", "\"Revision\" > 0");
            t.HasCheckConstraint("CK_DocumentRequest_NoSelfSupersession", "\"SupersedesRequestId\" IS NULL OR \"SupersedesRequestId\" <> \"Id\"");
            t.HasCheckConstraint("CK_DocumentRequest_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
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
        {
            t.HasCheckConstraint("CK_DocumentRequestItem_DisplayOrder", "\"DisplayOrder\" >= 0");
            t.HasCheckConstraint("CK_DocumentRequestItem_Wave", "\"Wave\" IN (0, 1, 2, 3)");
            t.HasCheckConstraint("CK_DocumentRequestItem_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_DocumentRequestItem_SnapshotText", "length(btrim(\"RequirementKey\")) > 0 AND length(btrim(\"RequirementName\")) > 0");
        });

        b.Entity<ReceivedDocument>().Property(x => x.SenderSnapshot).HasMaxLength(254);
        b.Entity<ReceivedDocument>().Property(x => x.SourceSnapshot).HasMaxLength(254);
        b.Entity<ReceivedDocument>().Property(x => x.OriginalFileName).HasMaxLength(512);
        b.Entity<ReceivedDocument>().Property(x => x.MimeType).HasMaxLength(254);
        b.Entity<ReceivedDocument>().Property(x => x.Sha256Hash).HasMaxLength(64);
        b.Entity<ReceivedDocument>().HasIndex(x => x.Sha256Hash);
        b.Entity<ReceivedDocument>().HasIndex(x => x.SupersedesReceivedDocumentId).IsUnique()
            .HasFilter("\"SupersedesReceivedDocumentId\" IS NOT NULL");
        b.Entity<ReceivedDocument>().HasOne(x => x.SupersedesReceivedDocument).WithMany(x => x.SupersedingDocuments)
            .HasForeignKey(x => x.SupersedesReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReceivedDocument>().HasOne(x => x.DuplicateOfReceivedDocument).WithMany(x => x.DuplicateDocuments)
            .HasForeignKey(x => x.DuplicateOfReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReceivedDocument>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ReceivedDocument_ByteLength", "\"ByteLength\" IS NULL OR \"ByteLength\" > 0");
            t.HasCheckConstraint("CK_ReceivedDocument_Sha256Hash", "\"Sha256Hash\" IS NULL OR \"Sha256Hash\" ~ '^[0-9A-Fa-f]{64}$'");
            t.HasCheckConstraint("CK_ReceivedDocument_NoSelfSupersession", "\"SupersedesReceivedDocumentId\" IS NULL OR \"SupersedesReceivedDocumentId\" <> \"Id\"");
            t.HasCheckConstraint("CK_ReceivedDocument_NoSelfDuplicate", "\"DuplicateOfReceivedDocumentId\" IS NULL OR \"DuplicateOfReceivedDocumentId\" <> \"Id\"");
            t.HasCheckConstraint("CK_ReceivedDocument_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_ReceivedDocument_DuplicateRequiresCanonical", "\"Status\" <> 4 OR \"DuplicateOfReceivedDocumentId\" IS NOT NULL");
            t.HasCheckConstraint("CK_ReceivedDocument_CanonicalRequiresDuplicate", "\"DuplicateOfReceivedDocumentId\" IS NULL OR \"Status\" = 4");
        });

        b.Entity<DocumentRequestItemEvidence>().Property(x => x.InactivationReason).HasMaxLength(2000);
        b.Entity<DocumentRequestItemEvidence>().HasIndex(x => new { x.DocumentRequestItemId, x.ReceivedDocumentId }).IsUnique();
        b.Entity<DocumentRequestItemEvidence>().HasOne(x => x.DocumentRequestItem).WithMany(x => x.EvidenceLinks)
            .HasForeignKey(x => x.DocumentRequestItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestItemEvidence>().HasOne(x => x.ReceivedDocument).WithMany(x => x.EvidenceLinks)
            .HasForeignKey(x => x.ReceivedDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentRequestItemEvidence>().ToTable(t =>
            t.HasCheckConstraint("CK_DocumentRequestItemEvidence_Lifecycle", "(\"IsActive\" AND \"InactivatedAt\" IS NULL AND \"InactivationReason\" IS NULL) OR (NOT \"IsActive\" AND \"InactivatedAt\" IS NOT NULL AND \"InactivationReason\" IS NOT NULL AND length(btrim(\"InactivationReason\")) > 0)"));

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

        b.Entity<DocumentRequestStatusHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_DocumentRequestStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
            t.HasCheckConstraint("CK_DocumentRequestStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });
        b.Entity<DocumentRequestItemStatusHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_DocumentRequestItemStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_DocumentRequestItemStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });
        b.Entity<ReceivedDocumentStatusHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ReceivedDocumentStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_ReceivedDocumentStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });
        b.Entity<DocumentRequestItemEvidenceHistory>().ToTable(t =>
            t.HasCheckConstraint("CK_DocumentRequestItemEvidenceHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0"));

        b.Entity<Contact>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Entity<Contact>().Property(x => x.PreferredLanguage).HasMaxLength(35);
        b.Entity<Contact>().HasIndex(x => x.Name);
        b.Entity<Contact>().ToTable(t => t.HasCheckConstraint(
            "CK_Contact_Text",
            "length(btrim(\"Name\")) > 0 AND (\"PreferredLanguage\" IS NULL OR length(btrim(\"PreferredLanguage\")) > 0)"));

        b.Entity<ContactWhatsAppAddress>().Property(x => x.NormalizedE164).HasMaxLength(16).IsRequired();
        b.Entity<ContactWhatsAppAddress>().Property(x => x.ProviderWaId).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddress>().Property(x => x.ConsentSource).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddress>().Property(x => x.ConsentEvidenceReference).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddress>().Property(x => x.LastOptOutReason).HasMaxLength(2000);
        b.Entity<ContactWhatsAppAddress>().HasIndex(x => new { x.ContactId, x.NormalizedE164 }).IsUnique();
        b.Entity<ContactWhatsAppAddress>().HasIndex(x => x.NormalizedE164).IsUnique().HasFilter("\"IsActive\" = TRUE");
        b.Entity<ContactWhatsAppAddress>().HasIndex(x => x.ContactId).IsUnique().HasFilter("\"IsActive\" = TRUE AND \"IsPrimary\" = TRUE");
        b.Entity<ContactWhatsAppAddress>().HasIndex(x => x.ProviderWaId);
        b.Entity<ContactWhatsAppAddress>().HasOne(x => x.Contact).WithMany(x => x.Addresses)
            .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContactWhatsAppAddress>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ContactWhatsAppAddress_NormalizedE164", "\"NormalizedE164\" ~ '^\\+[1-9][0-9]{0,14}$'");
            t.HasCheckConstraint("CK_ContactWhatsAppAddress_PrimaryActive", "NOT \"IsPrimary\" OR \"IsActive\"");
            t.HasCheckConstraint("CK_ContactWhatsAppAddress_ConsentState", "\"ConsentState\" IN (0, 1, 2)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddress_UnknownConsent", "\"ConsentState\" <> 0 OR (\"ConsentRecordedAt\" IS NULL AND \"ConsentSource\" IS NULL AND \"ConsentEvidenceReference\" IS NULL)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddress_OptedInConsent", "\"ConsentState\" <> 1 OR (\"ConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"ConsentSource\", ''))) > 0 AND length(btrim(COALESCE(\"ConsentEvidenceReference\", ''))) > 0)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddress_DoNotWhatsAppConsent", "\"ConsentState\" <> 2 OR (\"ConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"ConsentSource\", ''))) > 0 AND \"LastOptOutAt\" IS NOT NULL AND length(btrim(COALESCE(\"LastOptOutReason\", ''))) > 0)");
        });

        b.Entity<ContactCustomerLink>().Property(x => x.Role).HasMaxLength(120);
        b.Entity<ContactCustomerLink>().Property(x => x.Note).HasMaxLength(2000);
        b.Entity<ContactCustomerLink>().HasIndex(x => new { x.ContactId, x.CustomerId }).IsUnique();
        b.Entity<ContactCustomerLink>().HasIndex(x => x.ContactId).HasFilter("\"IsActive\" = TRUE");
        b.Entity<ContactCustomerLink>().HasIndex(x => x.CustomerId).HasFilter("\"IsActive\" = TRUE");
        b.Entity<ContactCustomerLink>().HasOne(x => x.Contact).WithMany(x => x.CustomerLinks)
            .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContactCustomerLink>().HasOne(x => x.Customer).WithMany(x => x.ContactCustomerLinks)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContactCustomerLink>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ContactCustomerLink_EffectiveState", "(\"IsActive\" AND \"EffectiveTo\" IS NULL) OR (NOT \"IsActive\" AND \"EffectiveTo\" IS NOT NULL)");
            t.HasCheckConstraint("CK_ContactCustomerLink_EffectiveDates", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
            t.HasCheckConstraint("CK_ContactCustomerLink_Role", "\"Role\" IS NULL OR length(btrim(\"Role\")) > 0");
        });

        b.Entity<ContactStatusHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<ContactStatusHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<ContactStatusHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<ContactStatusHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<ContactStatusHistory>().HasIndex(x => new { x.ContactId, x.OccurredAt, x.Id });
        b.Entity<ContactStatusHistory>().HasOne(x => x.Contact).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContactStatusHistory>().ToTable(t => t.HasCheckConstraint(
            "CK_ContactStatusHistory_Text",
            "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0"));

        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.PreviousProviderWaId).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.NewProviderWaId).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.PreviousConsentSource).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.NewConsentSource).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.PreviousConsentEvidenceReference).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.NewConsentEvidenceReference).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.PreviousLastOptOutReason).HasMaxLength(2000);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.NewLastOptOutReason).HasMaxLength(2000);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<ContactWhatsAppAddressHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<ContactWhatsAppAddressHistory>().HasIndex(x => new { x.ContactWhatsAppAddressId, x.OccurredAt, x.Id });
        b.Entity<ContactWhatsAppAddressHistory>().HasOne(x => x.ContactWhatsAppAddress).WithMany(x => x.History)
            .HasForeignKey(x => x.ContactWhatsAppAddressId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContactWhatsAppAddressHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_ConsentState", "(\"PreviousConsentState\" IS NULL OR \"PreviousConsentState\" IN (0, 1, 2)) AND \"NewConsentState\" IN (0, 1, 2)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_NewUnknownConsent", "\"NewConsentState\" <> 0 OR (\"NewConsentRecordedAt\" IS NULL AND \"NewConsentSource\" IS NULL AND \"NewConsentEvidenceReference\" IS NULL)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_NewOptedInConsent", "\"NewConsentState\" <> 1 OR (\"NewConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewConsentSource\", ''))) > 0 AND length(btrim(COALESCE(\"NewConsentEvidenceReference\", ''))) > 0)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_NewDoNotWhatsAppConsent", "\"NewConsentState\" <> 2 OR (\"NewConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewConsentSource\", ''))) > 0 AND \"NewLastOptOutAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewLastOptOutReason\", ''))) > 0)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousUnknownConsent", "\"PreviousConsentState\" <> 0 OR (\"PreviousConsentRecordedAt\" IS NULL AND \"PreviousConsentSource\" IS NULL AND \"PreviousConsentEvidenceReference\" IS NULL)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousOptedInConsent", "\"PreviousConsentState\" <> 1 OR (\"PreviousConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousConsentSource\", ''))) > 0 AND length(btrim(COALESCE(\"PreviousConsentEvidenceReference\", ''))) > 0)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousDoNotWhatsAppConsent", "\"PreviousConsentState\" <> 2 OR (\"PreviousConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousConsentSource\", ''))) > 0 AND \"PreviousLastOptOutAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousLastOptOutReason\", ''))) > 0)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousCreationSnapshot", "\"PreviousConsentState\" IS NOT NULL OR (\"PreviousConsentRecordedAt\" IS NULL AND \"PreviousConsentSource\" IS NULL AND \"PreviousConsentEvidenceReference\" IS NULL AND \"PreviousLastOptOutAt\" IS NULL AND \"PreviousLastOptOutReason\" IS NULL)");
            t.HasCheckConstraint("CK_ContactWhatsAppAddressHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });

        b.Entity<ContactCustomerLinkHistory>().Property(x => x.PreviousRole).HasMaxLength(120);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.NewRole).HasMaxLength(120);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.PreviousNote).HasMaxLength(2000);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.NewNote).HasMaxLength(2000);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<ContactCustomerLinkHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<ContactCustomerLinkHistory>().HasIndex(x => new { x.ContactCustomerLinkId, x.OccurredAt, x.Id });
        b.Entity<ContactCustomerLinkHistory>().HasOne(x => x.ContactCustomerLink).WithMany(x => x.History)
            .HasForeignKey(x => x.ContactCustomerLinkId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ContactCustomerLinkHistory>().ToTable(t => t.HasCheckConstraint(
            "CK_ContactCustomerLinkHistory_Text",
            "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0"));

        b.Entity<WhatsAppConversation>().Property(x => x.ProviderName).HasMaxLength(80).IsRequired();
        b.Entity<WhatsAppConversation>().Property(x => x.BusinessEndpointKey).HasMaxLength(254).IsRequired();
        b.Entity<WhatsAppConversation>().Property(x => x.ProviderAccountReference).HasMaxLength(254);
        b.Entity<WhatsAppConversation>().Property(x => x.ProviderConversationKey).HasMaxLength(254).IsRequired();
        b.Entity<WhatsAppConversation>().HasIndex(x => new { x.ProviderName, x.BusinessEndpointKey, x.ProviderConversationKey }).IsUnique();
        b.Entity<WhatsAppConversation>().HasIndex(x => new { x.BusinessEndpointKey, x.DirectContactWhatsAppAddressId }).IsUnique()
            .HasFilter("\"Kind\" = 0 AND \"Status\" <> 1 AND \"DirectContactWhatsAppAddressId\" IS NOT NULL");
        b.Entity<WhatsAppConversation>().HasOne(x => x.DirectContactWhatsAppAddress).WithMany()
            .HasForeignKey(x => x.DirectContactWhatsAppAddressId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversation>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WhatsAppConversation_Kind", "\"Kind\" IN (0, 1)");
            t.HasCheckConstraint("CK_WhatsAppConversation_Status", "\"Status\" IN (0, 1, 2)");
            t.HasCheckConstraint("CK_WhatsAppConversation_References", "length(btrim(\"ProviderName\")) > 0 AND length(btrim(\"BusinessEndpointKey\")) > 0 AND length(btrim(\"ProviderConversationKey\")) > 0 AND (\"ProviderAccountReference\" IS NULL OR length(btrim(\"ProviderAccountReference\")) > 0)");
            t.HasCheckConstraint("CK_WhatsAppConversation_AuthorizationVersion", "\"AuthorizationVersion\" > 0");
            t.HasCheckConstraint("CK_WhatsAppConversation_DirectAddress", "\"Kind\" <> 0 OR \"DirectContactWhatsAppAddressId\" IS NOT NULL");
            t.HasCheckConstraint("CK_WhatsAppConversation_GroupNoDirectAddress", "\"Kind\" <> 1 OR \"DirectContactWhatsAppAddressId\" IS NULL");
        });

        b.Entity<WhatsAppConversationParticipant>().Property(x => x.ProviderParticipantKey).HasMaxLength(254);
        b.Entity<WhatsAppConversationParticipant>().Property(x => x.NormalizedE164).HasMaxLength(16);
        b.Entity<WhatsAppConversationParticipant>().Property(x => x.DisplayNameSnapshot).HasMaxLength(254);
        b.Entity<WhatsAppConversationParticipant>().HasIndex(x => new { x.WhatsAppConversationId, x.ContactId }).IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"ContactId\" IS NOT NULL");
        b.Entity<WhatsAppConversationParticipant>().HasIndex(x => new { x.WhatsAppConversationId, x.BusinessPartyId }).IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"BusinessPartyId\" IS NOT NULL");
        b.Entity<WhatsAppConversationParticipant>().HasIndex(x => new { x.WhatsAppConversationId, x.ManagerId }).IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"ManagerId\" IS NOT NULL");
        b.Entity<WhatsAppConversationParticipant>().HasIndex(x => new { x.WhatsAppConversationId, x.AppUserId }).IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"AppUserId\" IS NOT NULL");
        b.Entity<WhatsAppConversationParticipant>().HasIndex(x => new { x.WhatsAppConversationId, x.ProviderParticipantKey }).IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"ProviderParticipantKey\" IS NOT NULL");
        b.Entity<WhatsAppConversationParticipant>().HasOne(x => x.WhatsAppConversation).WithMany(x => x.Participants)
            .HasForeignKey(x => x.WhatsAppConversationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipant>().HasOne(x => x.Contact).WithMany()
            .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipant>().HasOne(x => x.ContactWhatsAppAddress).WithMany()
            .HasForeignKey(x => x.ContactWhatsAppAddressId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipant>().HasOne(x => x.BusinessParty).WithMany()
            .HasForeignKey(x => x.BusinessPartyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipant>().HasOne(x => x.Manager).WithMany()
            .HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipant>().HasOne(x => x.AppUser).WithMany()
            .HasForeignKey(x => x.AppUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipant>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_Kind", "\"ParticipantKind\" IN (0, 1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_Identity", "(\"ParticipantKind\" = 0 AND \"ContactId\" IS NOT NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 1 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 2 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NOT NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 3 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NOT NULL) OR ((\"ParticipantKind\" = 4 OR \"ParticipantKind\" = 5) AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL)");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_References", "\"ProviderParticipantKey\" IS NULL OR length(btrim(\"ProviderParticipantKey\")) > 0");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_NormalizedE164", "\"NormalizedE164\" IS NULL OR \"NormalizedE164\" ~ '^\\+[1-9][0-9]{0,14}$'");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_DisplayName", "\"DisplayNameSnapshot\" IS NULL OR length(btrim(\"DisplayNameSnapshot\")) > 0");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_Lifecycle", "(\"IsActive\" AND \"LeftAt\" IS NULL) OR (NOT \"IsActive\" AND \"LeftAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipant_Dates", "\"LeftAt\" IS NULL OR \"LeftAt\" >= \"JoinedAt\"");
        });

        b.Entity<WhatsAppConversationEngagementScope>().Property(x => x.ApprovedByActor).HasMaxLength(254).IsRequired();
        b.Entity<WhatsAppConversationEngagementScope>().Property(x => x.ApprovalReason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScope>().Property(x => x.RevokedByActor).HasMaxLength(254);
        b.Entity<WhatsAppConversationEngagementScope>().Property(x => x.RevocationReason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScope>().HasIndex(x => new { x.WhatsAppConversationId, x.EngagementId }).IsUnique();
        b.Entity<WhatsAppConversationEngagementScope>().HasOne(x => x.WhatsAppConversation).WithMany(x => x.EngagementScopes)
            .HasForeignKey(x => x.WhatsAppConversationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationEngagementScope>().HasOne(x => x.Engagement).WithMany()
            .HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationEngagementScope>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScope_ApprovedVersion", "\"ApprovedAuthorizationVersion\" > 0");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScope_ApprovalFacts", "length(btrim(\"ApprovedByActor\")) > 0 AND (\"ApprovalReason\" IS NULL OR length(btrim(\"ApprovalReason\")) > 0)");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScope_RevocationFacts", "(\"IsActive\" AND \"RevokedAt\" IS NULL AND \"RevokedByActor\" IS NULL AND \"RevocationReason\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAt\" IS NOT NULL AND \"RevokedByActor\" IS NOT NULL AND length(btrim(\"RevokedByActor\")) > 0 AND (\"RevocationReason\" IS NULL OR length(btrim(\"RevocationReason\")) > 0))");
        });

        b.Entity<WhatsAppConversationHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<WhatsAppConversationHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<WhatsAppConversationHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<WhatsAppConversationHistory>().HasIndex(x => new { x.WhatsAppConversationId, x.OccurredAt, x.Id });
        b.Entity<WhatsAppConversationHistory>().HasOne(x => x.WhatsAppConversation).WithMany(x => x.History)
            .HasForeignKey(x => x.WhatsAppConversationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WhatsAppConversationHistory_Status", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2)) AND \"NewStatus\" IN (0, 1, 2)");
            t.HasCheckConstraint("CK_WhatsAppConversationHistory_Versions", "(\"PreviousAuthorizationVersion\" IS NULL OR \"PreviousAuthorizationVersion\" > 0) AND \"NewAuthorizationVersion\" > 0 AND ((\"PreviousStatus\" IS NULL AND \"PreviousAuthorizationVersion\" IS NULL) OR (\"PreviousStatus\" IS NOT NULL AND \"PreviousAuthorizationVersion\" IS NOT NULL))");
            t.HasCheckConstraint("CK_WhatsAppConversationHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });

        b.Entity<WhatsAppConversationParticipantHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<WhatsAppConversationParticipantHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationParticipantHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<WhatsAppConversationParticipantHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<WhatsAppConversationParticipantHistory>().HasIndex(x => new { x.WhatsAppConversationParticipantId, x.OccurredAt, x.Id });
        b.Entity<WhatsAppConversationParticipantHistory>().HasOne(x => x.WhatsAppConversationParticipant).WithMany(x => x.History)
            .HasForeignKey(x => x.WhatsAppConversationParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationParticipantHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WhatsAppConversationParticipantHistory_PreviousLifecycle", "(\"PreviousIsActive\" IS NULL AND \"PreviousJoinedAt\" IS NULL AND \"PreviousLeftAt\" IS NULL) OR (\"PreviousIsActive\" AND \"PreviousJoinedAt\" IS NOT NULL AND \"PreviousLeftAt\" IS NULL) OR (NOT \"PreviousIsActive\" AND \"PreviousJoinedAt\" IS NOT NULL AND \"PreviousLeftAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipantHistory_NewLifecycle", "(\"NewIsActive\" AND \"NewLeftAt\" IS NULL) OR (NOT \"NewIsActive\" AND \"NewLeftAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipantHistory_Dates", "\"NewLeftAt\" IS NULL OR \"NewLeftAt\" >= \"NewJoinedAt\"");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipantHistory_PreviousDates", "\"PreviousLeftAt\" IS NULL OR \"PreviousJoinedAt\" IS NOT NULL AND \"PreviousLeftAt\" >= \"PreviousJoinedAt\"");
            t.HasCheckConstraint("CK_WhatsAppConversationParticipantHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });

        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.PreviousApprovedByActor).HasMaxLength(254);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.NewApprovedByActor).HasMaxLength(254);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.PreviousApprovalReason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.NewApprovalReason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.PreviousRevokedByActor).HasMaxLength(254);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.NewRevokedByActor).HasMaxLength(254);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.PreviousRevocationReason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.NewRevocationReason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.Actor).HasMaxLength(254);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().Property(x => x.Source).HasMaxLength(80);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().HasIndex(x => new { x.WhatsAppConversationEngagementScopeId, x.OccurredAt, x.Id });
        b.Entity<WhatsAppConversationEngagementScopeHistory>().HasOne(x => x.WhatsAppConversationEngagementScope).WithMany(x => x.History)
            .HasForeignKey(x => x.WhatsAppConversationEngagementScopeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WhatsAppConversationEngagementScopeHistory>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_PreviousVersion", "\"PreviousApprovedAuthorizationVersion\" IS NULL OR \"PreviousApprovedAuthorizationVersion\" > 0");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_NewVersion", "\"NewApprovedAuthorizationVersion\" > 0");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_PreviousLifecycle", "(\"PreviousIsActive\" IS NULL AND \"PreviousApprovedAuthorizationVersion\" IS NULL AND \"PreviousApprovedAt\" IS NULL AND \"PreviousApprovedByActor\" IS NULL AND \"PreviousApprovalReason\" IS NULL AND \"PreviousRevokedAt\" IS NULL AND \"PreviousRevokedByActor\" IS NULL AND \"PreviousRevocationReason\" IS NULL) OR (\"PreviousIsActive\" AND \"PreviousApprovedAuthorizationVersion\" IS NOT NULL AND \"PreviousApprovedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousApprovedByActor\", ''))) > 0 AND \"PreviousRevokedAt\" IS NULL AND \"PreviousRevokedByActor\" IS NULL AND \"PreviousRevocationReason\" IS NULL) OR (NOT \"PreviousIsActive\" AND \"PreviousApprovedAuthorizationVersion\" IS NOT NULL AND \"PreviousApprovedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousApprovedByActor\", ''))) > 0 AND \"PreviousRevokedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousRevokedByActor\", ''))) > 0)");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_NewLifecycle", "(\"NewIsActive\" AND \"NewRevokedAt\" IS NULL AND \"NewRevokedByActor\" IS NULL AND \"NewRevocationReason\" IS NULL) OR (NOT \"NewIsActive\" AND \"NewRevokedAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewRevokedByActor\", ''))) > 0)");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_ApprovalFacts", "length(btrim(\"NewApprovedByActor\")) > 0 AND (\"NewApprovalReason\" IS NULL OR length(btrim(\"NewApprovalReason\")) > 0) AND (\"NewRevocationReason\" IS NULL OR length(btrim(\"NewRevocationReason\")) > 0)");
            t.HasCheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
        });

        foreach (var fk in b.Model.GetEntityTypes().Where(t => typeof(Record).IsAssignableFrom(t.ClrType)).SelectMany(t => t.GetForeignKeys())) fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var usedTemplateIds = await LoadUsedTemplateIdsAsync(cancellationToken);
        foreach (var e in ChangeTracker.Entries<Record>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (e.State == EntityState.Deleted)
            {
                if (e.Entity is InvoiceLine && allowInvoiceLineDeletion) continue;
                throw new InvalidOperationException("Use cancellation or deactivate records instead of deleting them.");
            }
            var duplicateClassificationLinkChanged = false;
            if (e.Entity is ReceivedDocument && (e.State == EntityState.Added || e.State == EntityState.Modified))
                duplicateClassificationLinkChanged = await ValidateReceivedDocumentChangeAsync(e, cancellationToken);
            if (e.State == EntityState.Added && e.Entity is DocumentRequirementTemplateItem addedTemplateItem &&
                usedTemplateIds.Contains(TemplateIdFor(addedTemplateItem)))
                throw new InvalidOperationException("A used document requirement template cannot receive new checklist items; create a new template version.");
            if (e.State == EntityState.Modified)
            {
                string[] allowed;
                if (e.Entity is DocumentRequirementTemplate template && usedTemplateIds.Contains(template.Id))
                    allowed = ["IsActive", "IsDefault"];
                else if (e.Entity is DocumentRequirementTemplateItem && IsUsedTemplateItem(e, usedTemplateIds))
                    allowed = [];
                else
                {
                    allowed = e.Entity switch
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
                        DocumentRequest => ["Status"],
                        DocumentRequestItem => ["Status"],
                        ReceivedDocument => duplicateClassificationLinkChanged
                            ? ["Status", "DuplicateOfReceivedDocumentId"]
                            : ["Status"],
                        DocumentRequestItemEvidence => ["IsActive", "InactivatedAt", "InactivationReason"],
                        DocumentRequestBatchMember => ["IsActive"],
                        DocumentRequestStatusHistory => [],
                        DocumentRequestItemStatusHistory => [],
                        ReceivedDocumentStatusHistory => [],
                        DocumentRequestItemEvidenceHistory => [],
                        Contact => ["Name", "PreferredLanguage", "IsActive"],
                        ContactWhatsAppAddress => ["ProviderWaId", "IsPrimary", "IsActive", "ConsentState", "ConsentRecordedAt", "ConsentSource", "ConsentEvidenceReference", "LastOptOutAt", "LastOptOutReason"],
                        ContactCustomerLink => ["IsActive", "EffectiveFrom", "EffectiveTo", "Role", "Note"],
                        ContactStatusHistory => [],
                        ContactWhatsAppAddressHistory => [],
                        ContactCustomerLinkHistory => [],
                        WhatsAppConversation => ["Status", "AuthorizationVersion"],
                        WhatsAppConversationParticipant => ["IsActive", "JoinedAt", "LeftAt"],
                        WhatsAppConversationEngagementScope => ["IsActive", "ApprovedAuthorizationVersion", "ApprovedAt", "ApprovedByActor", "ApprovalReason", "RevokedAt", "RevokedByActor", "RevocationReason"],
                        WhatsAppConversationHistory => [],
                        WhatsAppConversationParticipantHistory => [],
                        WhatsAppConversationEngagementScopeHistory => [],
                        WorkerAssignment => ["WorkerId", "WorkerName", "Percent", "Entitlement", "IsCancelled", "CancellationReason", "CurrentWorkflowStatus", "CurrentWorkflowVersion", "CurrentProgressPercent", "IsHidden", "HiddenAt", "HiddenBy", "ReportingResumedFromWeek"],
                        WorkerPayment => ["PaymentDate", "Reference", "IsCancelled", "CancellationReason"],
                        CustomerReceipt => allowReceiptAllocationCorrection
                            ? ["ReceiptDate", "Reference", "Amount", "IsCancelled", "CancellationReason"]
                            : ["ReceiptDate", "Reference", "IsCancelled", "CancellationReason"],
                        _ => e.Properties.Select(p => p.Metadata.Name).Except(["CreatedAt", "CreatedBy", "Id"]).ToArray()
                    };
                }
                if (e.Properties.Any(p => p.IsModified && !allowed.Contains(p.Metadata.Name)))
                    throw new InvalidOperationException("Historical snapshots and audit origins cannot be edited.");
            }
            var actor = http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (e.State == EntityState.Added) { e.Entity.CreatedAt = DateTime.UtcNow; e.Entity.CreatedBy = actor; }
            e.Entity.UpdatedAt = DateTime.UtcNow; e.Entity.UpdatedBy = actor; e.Entity.Version++;
        }
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ValidateReceivedDocumentChangeAsync(EntityEntry<Record> entry, CancellationToken cancellationToken)
    {
        var document = (ReceivedDocument)entry.Entity;
        var currentCanonicalId = document.DuplicateOfReceivedDocumentId;

        if (entry.State == EntityState.Added)
        {
            if (currentCanonicalId is not null || document.Status == ReceivedDocumentStatus.Duplicate)
                throw new InvalidOperationException("A duplicate document must be classified from a persisted PendingReview artifact; save the raw intake artifact first.");
            return false;
        }

        var originalStatus = (ReceivedDocumentStatus)entry.Property(nameof(ReceivedDocument.Status)).OriginalValue!;
        var originalCanonicalId = entry.Property(nameof(ReceivedDocument.DuplicateOfReceivedDocumentId)).OriginalValue is int id
            ? id
            : (int?)null;
        var canonicalLinkChanged = originalCanonicalId != currentCanonicalId;

        if (originalStatus == ReceivedDocumentStatus.Duplicate && document.Status != ReceivedDocumentStatus.Duplicate)
            throw new InvalidOperationException("A duplicate received document is terminal and cannot leave Duplicate status.");

        if (document.Status == ReceivedDocumentStatus.Duplicate && currentCanonicalId is null)
            throw new InvalidOperationException("A duplicate received document must identify its canonical received document.");

        if (currentCanonicalId is not null && document.Status != ReceivedDocumentStatus.Duplicate)
            throw new InvalidOperationException("DuplicateOfReceivedDocumentId may only be assigned to a Duplicate document.");

        if (canonicalLinkChanged &&
            (originalStatus != ReceivedDocumentStatus.PendingReview ||
             document.Status != ReceivedDocumentStatus.Duplicate ||
             originalCanonicalId is not null ||
             currentCanonicalId is null))
            throw new InvalidOperationException("DuplicateOfReceivedDocumentId may be assigned only once during PendingReview to Duplicate classification.");

        if (currentCanonicalId is not int canonicalId) return canonicalLinkChanged;
        if (canonicalId == document.Id)
            throw new InvalidOperationException("A received document cannot be its own duplicate canonical document.");

        var trackedCanonical = ChangeTracker.Entries<ReceivedDocument>()
            .FirstOrDefault(x => x.Entity.Id == canonicalId);
        if (trackedCanonical is not null)
        {
            if (trackedCanonical.State == EntityState.Deleted || trackedCanonical.Entity.DuplicateOfReceivedDocumentId is not null)
                throw new InvalidOperationException("A duplicate must point directly to a canonical received document, not to another duplicate.");
        }
        else if (await ReceivedDocuments.AsNoTracking()
                     .Where(x => x.Id == canonicalId)
                     .Select(x => x.DuplicateOfReceivedDocumentId)
                     .SingleOrDefaultAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException("A duplicate must point directly to a canonical received document, not to another duplicate.");
        }

        return canonicalLinkChanged;
    }

    private async Task<HashSet<int>> LoadUsedTemplateIdsAsync(CancellationToken cancellationToken)
    {
        var candidateTemplateIds = new HashSet<int>(
            ChangeTracker.Entries<DocumentRequirementTemplate>()
                .Where(e => e.State == EntityState.Modified)
                .Select(e => e.Entity.Id)
                .Where(id => id > 0));

        foreach (var entry in ChangeTracker.Entries<DocumentRequirementTemplateItem>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var currentTemplateId = TemplateIdFor(entry.Entity);
            if (currentTemplateId > 0) candidateTemplateIds.Add(currentTemplateId);
            if (entry.State == EntityState.Modified &&
                entry.Property(nameof(DocumentRequirementTemplateItem.DocumentRequirementTemplateId)).OriginalValue is int originalTemplateId &&
                originalTemplateId > 0)
                candidateTemplateIds.Add(originalTemplateId);
        }

        if (candidateTemplateIds.Count == 0) return [];

        var usedTemplateIds = (await DocumentRequests.AsNoTracking()
            .Where(x => candidateTemplateIds.Contains(x.DocumentRequirementTemplateId))
            .Select(x => x.DocumentRequirementTemplateId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var request in ChangeTracker.Entries<DocumentRequest>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified)
                     .Select(e => e.Entity))
        {
            var templateId = request.DocumentRequirementTemplateId != 0
                ? request.DocumentRequirementTemplateId
                : request.DocumentRequirementTemplate?.Id ?? 0;
            if (candidateTemplateIds.Contains(templateId)) usedTemplateIds.Add(templateId);
        }

        return usedTemplateIds;
    }

    private static int TemplateIdFor(DocumentRequirementTemplateItem item) =>
        item.DocumentRequirementTemplateId != 0 ? item.DocumentRequirementTemplateId : item.DocumentRequirementTemplate?.Id ?? 0;

    private static bool IsUsedTemplateItem(EntityEntry<Record> entry, HashSet<int> usedTemplateIds)
    {
        if (entry.Entity is not DocumentRequirementTemplateItem item) return false;
        if (usedTemplateIds.Contains(TemplateIdFor(item))) return true;
        return entry.State == EntityState.Modified &&
               entry.Property(nameof(DocumentRequirementTemplateItem.DocumentRequirementTemplateId)).OriginalValue is int originalTemplateId &&
               usedTemplateIds.Contains(originalTemplateId);
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
