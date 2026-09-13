using System.ComponentModel.DataAnnotations;

namespace BillingControl.Models;

public enum WhatsAppConversationKind
{
    Direct,
    Group
}

public enum WhatsAppConversationStatus
{
    Active,
    Inactive,
    NeedsAuthorizationReview
}

public enum WhatsAppParticipantKind
{
    Contact,
    BusinessParty,
    Manager,
    AppUser,
    BusinessSender,
    UnknownExternal
}

public class WhatsAppConversation : Record
{
    public WhatsAppConversationKind Kind { get; set; }
    public WhatsAppConversationStatus Status { get; set; } = WhatsAppConversationStatus.Active;

    [Required, StringLength(80)]
    public string ProviderName { get; set; } = "";

    [Required, StringLength(254)]
    public string BusinessEndpointKey { get; set; } = "";

    [StringLength(254)]
    public string? ProviderAccountReference { get; set; }

    [Required, StringLength(254)]
    public string ProviderConversationKey { get; set; } = "";

    public int? DirectContactWhatsAppAddressId { get; set; }
    public ContactWhatsAppAddress? DirectContactWhatsAppAddress { get; set; }

    public int AuthorizationVersion { get; set; } = 1;

    public List<WhatsAppConversationParticipant> Participants { get; set; } = [];
    public List<WhatsAppConversationEngagementScope> EngagementScopes { get; set; } = [];
    public List<WhatsAppConversationHistory> History { get; set; } = [];
}

public class WhatsAppConversationParticipant : Record
{
    public int WhatsAppConversationId { get; set; }
    public WhatsAppConversation WhatsAppConversation { get; set; } = null!;

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

    public bool IsActive { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public List<WhatsAppConversationParticipantHistory> History { get; set; } = [];
}

public class WhatsAppConversationEngagementScope : Record
{
    public int WhatsAppConversationId { get; set; }
    public WhatsAppConversation WhatsAppConversation { get; set; } = null!;

    public int EngagementId { get; set; }
    public Engagement Engagement { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public int ApprovedAuthorizationVersion { get; set; } = 1;
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    [Required, StringLength(254)]
    public string ApprovedByActor { get; set; } = "";

    [StringLength(2000)]
    public string? ApprovalReason { get; set; }

    public DateTime? RevokedAt { get; set; }

    [StringLength(254)]
    public string? RevokedByActor { get; set; }

    [StringLength(2000)]
    public string? RevocationReason { get; set; }

    public List<WhatsAppConversationEngagementScopeHistory> History { get; set; } = [];
}

public class WhatsAppConversationHistory : Record
{
    public int WhatsAppConversationId { get; set; }
    public WhatsAppConversation WhatsAppConversation { get; set; } = null!;

    public WhatsAppConversationStatus? PreviousStatus { get; set; }
    public WhatsAppConversationStatus NewStatus { get; set; }
    public int? PreviousAuthorizationVersion { get; set; }
    public int NewAuthorizationVersion { get; set; } = 1;

    [Required, StringLength(80)]
    public string Action { get; set; } = "";

    [StringLength(2000)]
    public string? Reason { get; set; }

    [Required, StringLength(254)]
    public string Actor { get; set; } = "system";

    [Required, StringLength(80)]
    public string Source { get; set; } = "System";

    public DateTime OccurredAt { get; set; }
}

public class WhatsAppConversationParticipantHistory : Record
{
    public int WhatsAppConversationParticipantId { get; set; }
    public WhatsAppConversationParticipant WhatsAppConversationParticipant { get; set; } = null!;

    public bool? PreviousIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public DateTime? PreviousJoinedAt { get; set; }
    public DateTime NewJoinedAt { get; set; }
    public DateTime? PreviousLeftAt { get; set; }
    public DateTime? NewLeftAt { get; set; }

    [Required, StringLength(80)]
    public string Action { get; set; } = "";

    [StringLength(2000)]
    public string? Reason { get; set; }

    [Required, StringLength(254)]
    public string Actor { get; set; } = "system";

    [Required, StringLength(80)]
    public string Source { get; set; } = "System";

    public DateTime OccurredAt { get; set; }
}

public class WhatsAppConversationEngagementScopeHistory : Record
{
    public int WhatsAppConversationEngagementScopeId { get; set; }
    public WhatsAppConversationEngagementScope WhatsAppConversationEngagementScope { get; set; } = null!;

    public bool? PreviousIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public int? PreviousApprovedAuthorizationVersion { get; set; }
    public int NewApprovedAuthorizationVersion { get; set; } = 1;
    public DateTime? PreviousApprovedAt { get; set; }
    public DateTime NewApprovedAt { get; set; }
    public string? PreviousApprovedByActor { get; set; }
    public string NewApprovedByActor { get; set; } = "";
    public string? PreviousApprovalReason { get; set; }
    public string? NewApprovalReason { get; set; }
    public DateTime? PreviousRevokedAt { get; set; }
    public DateTime? NewRevokedAt { get; set; }
    public string? PreviousRevokedByActor { get; set; }
    public string? NewRevokedByActor { get; set; }
    public string? PreviousRevocationReason { get; set; }
    public string? NewRevocationReason { get; set; }

    [Required, StringLength(80)]
    public string Action { get; set; } = "";

    [StringLength(2000)]
    public string? Reason { get; set; }

    [Required, StringLength(254)]
    public string Actor { get; set; } = "system";

    [Required, StringLength(80)]
    public string Source { get; set; } = "System";

    public DateTime OccurredAt { get; set; }
}
