using System.ComponentModel.DataAnnotations;

namespace BillingControl.Models;

public enum WhatsAppOutboundDestinationKind
{
    Direct,
    Group
}

public enum WhatsAppOutboundContentKind
{
    Text,
    Template
}

public enum WhatsAppOutboundMessageState
{
    Queued,
    Accepted,
    DefinitelyRejected,
    Ambiguous,
    Cancelled
}

public class WhatsAppOutboundBatchSnapshot : Record
{
    public int DocumentRequestBatchId { get; set; }
    public DocumentRequestBatch DocumentRequestBatch { get; set; } = null!;

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public int WhatsAppConversationId { get; set; }
    public WhatsAppConversation WhatsAppConversation { get; set; } = null!;

    public int ConversationAuthorizationVersion { get; set; }

    [Required, StringLength(64)]
    public string ParticipantSetHash { get; set; } = "";

    public DateTime QueuedAt { get; set; }

    [Required, StringLength(254)]
    public string QueuedByActor { get; set; } = "";

    [Required, StringLength(254)]
    public string CorrelationId { get; set; } = "";

    public WhatsAppOutboundMessage? WhatsAppOutboundMessage { get; set; }
    public List<WhatsAppOutboundRequestSnapshot> Requests { get; set; } = [];
    public List<WhatsAppOutboundItemSnapshot> Items { get; set; } = [];
    public List<WhatsAppOutboundParticipantSnapshot> Participants { get; set; } = [];
    public List<WhatsAppOutboundEngagementScopeSnapshot> EngagementScopes { get; set; } = [];
}

public class WhatsAppOutboundRequestSnapshot : Record
{
    public int WhatsAppOutboundBatchSnapshotId { get; set; }
    public WhatsAppOutboundBatchSnapshot WhatsAppOutboundBatchSnapshot { get; set; } = null!;

    public int DocumentRequestId { get; set; }
    public DocumentRequest DocumentRequest { get; set; } = null!;

    public int RequestRevision { get; set; }
    public long RequestVersion { get; set; }

    public int EngagementId { get; set; }
    public Engagement Engagement { get; set; } = null!;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [Required, StringLength(160)]
    public string CustomerNameSnapshot { get; set; } = "";

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    [Required, StringLength(160)]
    public string ServiceNameSnapshot { get; set; } = "";
}

public class WhatsAppOutboundItemSnapshot : Record
{
    public int WhatsAppOutboundBatchSnapshotId { get; set; }
    public WhatsAppOutboundBatchSnapshot WhatsAppOutboundBatchSnapshot { get; set; } = null!;

    public int DocumentRequestId { get; set; }
    public DocumentRequest DocumentRequest { get; set; } = null!;

    public int DocumentRequestItemId { get; set; }
    public DocumentRequestItem DocumentRequestItem { get; set; } = null!;

    public int RequestRevision { get; set; }

    [Required, StringLength(160)]
    public string RequirementNameSnapshot { get; set; } = "";

    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public DocumentRequestItemStatus ItemStatusSnapshot { get; set; }
}

public class WhatsAppOutboundParticipantSnapshot : Record
{
    public int WhatsAppOutboundBatchSnapshotId { get; set; }
    public WhatsAppOutboundBatchSnapshot WhatsAppOutboundBatchSnapshot { get; set; } = null!;

    public int WhatsAppConversationParticipantId { get; set; }
    public WhatsAppConversationParticipant WhatsAppConversationParticipant { get; set; } = null!;

    public WhatsAppParticipantKind ParticipantKind { get; set; }

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public int? ContactWhatsAppAddressId { get; set; }
    public ContactWhatsAppAddress? ContactWhatsAppAddress { get; set; }

    public int? BusinessPartyId { get; set; }
    public BusinessParty? BusinessParty { get; set; }

    public int? ManagerId { get; set; }
    public Manager? Manager { get; set; }

    public string? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    [StringLength(254)]
    public string? ProviderParticipantKey { get; set; }

    [StringLength(16)]
    public string? NormalizedE164 { get; set; }

    [StringLength(254)]
    public string? DisplayNameSnapshot { get; set; }
}

public class WhatsAppOutboundEngagementScopeSnapshot : Record
{
    public int WhatsAppOutboundBatchSnapshotId { get; set; }
    public WhatsAppOutboundBatchSnapshot WhatsAppOutboundBatchSnapshot { get; set; } = null!;

    public int WhatsAppConversationEngagementScopeId { get; set; }
    public WhatsAppConversationEngagementScope WhatsAppConversationEngagementScope { get; set; } = null!;

    public int EngagementId { get; set; }
    public Engagement Engagement { get; set; } = null!;

    public int ApprovedAuthorizationVersion { get; set; }
}

public class WhatsAppOutboundMessage : Record
{
    public int DocumentRequestBatchId { get; set; }
    public DocumentRequestBatch DocumentRequestBatch { get; set; } = null!;

    public int WhatsAppOutboundBatchSnapshotId { get; set; }
    public WhatsAppOutboundBatchSnapshot WhatsAppOutboundBatchSnapshot { get; set; } = null!;

    [Required, StringLength(254)]
    public string LogicalMessageKey { get; set; } = "";

    [Required, StringLength(254)]
    public string CorrelationId { get; set; } = "";

    [Required, StringLength(80)]
    public string ProviderName { get; set; } = "";

    [Required, StringLength(254)]
    public string BusinessEndpointKey { get; set; } = "";

    [StringLength(254)]
    public string? ProviderAccountReference { get; set; }

    public WhatsAppOutboundDestinationKind DestinationKind { get; set; }

    [Required, StringLength(254)]
    public string ProviderDestinationKey { get; set; } = "";

    [StringLength(16)]
    public string? NormalizedE164 { get; set; }

    [StringLength(254)]
    public string? ProviderRecipientKey { get; set; }

    public WhatsAppOutboundContentKind ContentKind { get; set; }

    public string? TextBody { get; set; }

    [StringLength(512)]
    public string? TemplateName { get; set; }

    [StringLength(35)]
    public string? TemplateLanguage { get; set; }

    public string? TemplateParametersSnapshot { get; set; }

    public WhatsAppOutboundMessageState State { get; set; } = WhatsAppOutboundMessageState.Queued;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    [StringLength(254)]
    public string? ProviderMessageId { get; set; }

    [StringLength(80)]
    public string? LastErrorCategory { get; set; }

    [StringLength(254)]
    public string? LastErrorCode { get; set; }

    public DateTime? ProviderRetryAfterUntil { get; set; }
    public DateTime? ProviderTimestamp { get; set; }

    public List<WhatsAppOutboundMessageAttempt> Attempts { get; set; } = [];
}

public class WhatsAppOutboundMessageAttempt : Record
{
    public int WhatsAppOutboundMessageId { get; set; }
    public WhatsAppOutboundMessage WhatsAppOutboundMessage { get; set; } = null!;

    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [StringLength(80)]
    public string? Disposition { get; set; }

    [StringLength(254)]
    public string? ProviderMessageId { get; set; }

    [StringLength(80)]
    public string? ErrorCategory { get; set; }

    [StringLength(254)]
    public string? ErrorCode { get; set; }

    public DateTime? RetryAfterUntil { get; set; }
    public DateTime? ProviderTimestamp { get; set; }

    [Required, StringLength(254)]
    public string CorrelationId { get; set; } = "";

    [Required, StringLength(254)]
    public string Actor { get; set; } = "system";

    [Required, StringLength(80)]
    public string Source { get; set; } = "System";
}
