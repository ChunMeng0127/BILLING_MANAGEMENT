namespace BillingControl.Models;

public enum DocumentRequirementWave
{
    StartWork,
    Normal,
    Later,
    Optional
}

public enum DocumentRequestStatus
{
    Draft,
    ReadyToSend,
    Requested,
    PartiallyReceived,
    Complete,
    Paused,
    Cancelled,
    Superseded
}

public enum DocumentRequestItemStatus
{
    Missing,
    Requested,
    PartiallyReceived,
    Received,
    NotRequired,
    Waived
}

public enum ReceivedDocumentStatus
{
    PendingReview,
    Quarantined,
    Accepted,
    Rejected,
    Duplicate,
    Superseded
}

public class DocumentRequirementTemplate : Record
{
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;
    public string TemplateKey { get; set; } = "";
    public int TemplateVersion { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public List<DocumentRequirementTemplateItem> Items { get; set; } = [];
    public List<DocumentRequest> Requests { get; set; } = [];
}

public class DocumentRequirementTemplateItem : Record
{
    public int DocumentRequirementTemplateId { get; set; }
    public DocumentRequirementTemplate DocumentRequirementTemplate { get; set; } = null!;
    public string RequirementKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public DocumentRequirementWave Wave { get; set; } = DocumentRequirementWave.Normal;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DocumentRequestItem> RequestItems { get; set; } = [];
}

public class DocumentRequest : Record
{
    public int WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;
    public int DocumentRequirementTemplateId { get; set; }
    public DocumentRequirementTemplate DocumentRequirementTemplate { get; set; } = null!;
    public int Revision { get; set; }
    public DocumentRequestStatus Status { get; set; } = DocumentRequestStatus.Draft;
    public int? SupersedesRequestId { get; set; }
    public DocumentRequest? SupersedesRequest { get; set; }
    public List<DocumentRequest> SupersedingRequests { get; set; } = [];
    public List<DocumentRequestItem> Items { get; set; } = [];
    public List<DocumentRequestBatchMember> BatchMemberships { get; set; } = [];
    public List<DocumentRequestStatusHistory> StatusHistory { get; set; } = [];
}

public class DocumentRequestItem : Record
{
    public int DocumentRequestId { get; set; }
    public DocumentRequest DocumentRequest { get; set; } = null!;
    public int? DocumentRequirementTemplateItemId { get; set; }
    public DocumentRequirementTemplateItem? DocumentRequirementTemplateItem { get; set; }

    // Immutable requirement snapshot copied from the selected template item.
    public string RequirementKey { get; set; } = "";
    public string RequirementName { get; set; } = "";
    public string? RequirementDescription { get; set; }
    public bool IsRequired { get; set; }
    public DocumentRequirementWave Wave { get; set; } = DocumentRequirementWave.Normal;
    public int DisplayOrder { get; set; }

    public DocumentRequestItemStatus Status { get; set; } = DocumentRequestItemStatus.Missing;
    public List<DocumentRequestItemEvidence> EvidenceLinks { get; set; } = [];
    public List<DocumentRequestItemStatusHistory> StatusHistory { get; set; } = [];
}

public class ReceivedDocument : Record
{
    public ReceivedDocumentStatus Status { get; set; } = ReceivedDocumentStatus.PendingReview;
    public DateTime ReceivedAt { get; set; }
    public string? SenderSnapshot { get; set; }
    public string? SourceSnapshot { get; set; }
    public string? OriginalFileName { get; set; }
    public string? MimeType { get; set; }
    public long? ByteLength { get; set; }
    public string? Sha256Hash { get; set; }

    // A replacement is a new artifact row pointing to the artifact it replaces.
    public int? SupersedesReceivedDocumentId { get; set; }
    public ReceivedDocument? SupersedesReceivedDocument { get; set; }
    public List<ReceivedDocument> SupersedingDocuments { get; set; } = [];

    // A duplicate remains an auditable artifact pointing to its canonical artifact.
    public int? DuplicateOfReceivedDocumentId { get; set; }
    public ReceivedDocument? DuplicateOfReceivedDocument { get; set; }
    public List<ReceivedDocument> DuplicateDocuments { get; set; } = [];

    public List<DocumentRequestItemEvidence> EvidenceLinks { get; set; } = [];
    public List<ReceivedDocumentStatusHistory> StatusHistory { get; set; } = [];
}

public class DocumentRequestItemEvidence : Record
{
    public int DocumentRequestItemId { get; set; }
    public DocumentRequestItem DocumentRequestItem { get; set; } = null!;
    public int ReceivedDocumentId { get; set; }
    public ReceivedDocument ReceivedDocument { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime? InactivatedAt { get; set; }
    public string? InactivationReason { get; set; }
    public List<DocumentRequestItemEvidenceHistory> History { get; set; } = [];
}

public class DocumentRequestBatch : Record
{
    public List<DocumentRequestBatchMember> Members { get; set; } = [];
}

public class DocumentRequestBatchMember : Record
{
    public int DocumentRequestBatchId { get; set; }
    public DocumentRequestBatch DocumentRequestBatch { get; set; } = null!;
    public int DocumentRequestId { get; set; }
    public DocumentRequest DocumentRequest { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public class DocumentRequestStatusHistory : Record
{
    public int DocumentRequestId { get; set; }
    public DocumentRequest DocumentRequest { get; set; } = null!;
    public DocumentRequestStatus? PreviousStatus { get; set; }
    public DocumentRequestStatus NewStatus { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class DocumentRequestItemStatusHistory : Record
{
    public int DocumentRequestItemId { get; set; }
    public DocumentRequestItem DocumentRequestItem { get; set; } = null!;
    public DocumentRequestItemStatus? PreviousStatus { get; set; }
    public DocumentRequestItemStatus NewStatus { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class ReceivedDocumentStatusHistory : Record
{
    public int ReceivedDocumentId { get; set; }
    public ReceivedDocument ReceivedDocument { get; set; } = null!;
    public ReceivedDocumentStatus? PreviousStatus { get; set; }
    public ReceivedDocumentStatus NewStatus { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class DocumentRequestItemEvidenceHistory : Record
{
    public int DocumentRequestItemEvidenceId { get; set; }
    public DocumentRequestItemEvidence DocumentRequestItemEvidence { get; set; } = null!;
    public bool? PreviousIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
}
