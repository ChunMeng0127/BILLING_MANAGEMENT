using System.ComponentModel.DataAnnotations;
using BillingControl.Services;

namespace BillingControl.Models;

public sealed class WhatsAppConversationIndexViewModel
{
    public bool IncludeInactive { get; set; }
    public IReadOnlyList<WhatsAppConversationSummaryReadModel> Conversations { get; init; } = [];
}

public sealed class WhatsAppConversationCreateViewModel
{
    [EnumDataType(typeof(WhatsAppConversationKind))]
    public WhatsAppConversationKind Kind { get; set; } = WhatsAppConversationKind.Direct;

    [Required, StringLength(80)]
    public string ProviderName { get; set; } = "";

    [Required, StringLength(254)]
    public string BusinessEndpointKey { get; set; } = "";

    [StringLength(254)]
    public string? ProviderAccountReference { get; set; }

    [Required, StringLength(254)]
    public string ProviderConversationKey { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int? DirectContactWhatsAppAddressId { get; set; }

    [StringLength(2000)]
    public string? Reason { get; set; }

    public IReadOnlyList<WhatsAppDirectContactWhatsAppAddressOptionReadModel> DirectAddressOptions { get; init; } = [];
}

public sealed class WhatsAppConversationDetailsViewModel
{
    public WhatsAppConversationDetailsReadModel Conversation { get; init; } = null!;
    public WhatsAppConversationManagementOptionsReadModel Options { get; init; } = null!;
}

public sealed class WhatsAppConversationParticipantForm
{
    [Range(1, int.MaxValue)]
    public int ConversationId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedConversationVersion { get; set; }

    [EnumDataType(typeof(WhatsAppParticipantKind))]
    public WhatsAppParticipantKind ParticipantKind { get; set; }

    [Range(1, int.MaxValue)]
    public int? ContactId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ContactWhatsAppAddressId { get; set; }

    [Range(1, int.MaxValue)]
    public int? BusinessPartyId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ManagerId { get; set; }

    [StringLength(450)]
    public string? AppUserId { get; set; }

    [StringLength(254)]
    public string? ProviderParticipantKey { get; set; }

    [StringLength(80)]
    public string? NormalizedE164 { get; set; }

    [StringLength(254)]
    public string? DisplayNameSnapshot { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class WhatsAppConversationParticipantLifecycleForm
{
    [Range(1, int.MaxValue)]
    public int ConversationId { get; set; }

    [Range(1, int.MaxValue)]
    public int ParticipantId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedConversationVersion { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedParticipantVersion { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class WhatsAppConversationUnknownMembershipChangeForm
{
    [Range(1, int.MaxValue)]
    public int ConversationId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedConversationVersion { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class WhatsAppConversationScopeApprovalForm
{
    [Range(1, int.MaxValue)]
    public int ConversationId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedConversationVersion { get; set; }

    [Range(1, int.MaxValue)]
    public int EngagementId { get; set; }

    [Range(1, long.MaxValue)]
    public long? ExpectedScopeVersion { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class WhatsAppConversationScopeRevocationForm
{
    [Range(1, int.MaxValue)]
    public int ConversationId { get; set; }

    [Range(1, int.MaxValue)]
    public int ScopeId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedConversationVersion { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedScopeVersion { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class WhatsAppConversationLifecycleForm
{
    [Range(1, int.MaxValue)]
    public int ConversationId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedConversationVersion { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}
