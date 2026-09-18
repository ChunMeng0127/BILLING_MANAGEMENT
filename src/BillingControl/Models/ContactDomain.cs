using System.ComponentModel.DataAnnotations;

namespace BillingControl.Models;

public enum ContactWhatsAppConsentState
{
    Unknown,
    OptedIn,
    DoNotWhatsApp
}

public class Contact : Record
{
    [Required, StringLength(160)]
    public string Name { get; set; } = "";

    [StringLength(35)]
    public string? PreferredLanguage { get; set; }

    public bool IsActive { get; set; } = true;
    public List<ContactWhatsAppAddress> Addresses { get; set; } = [];
    public List<ContactCustomerLink> CustomerLinks { get; set; } = [];
    public List<ContactStatusHistory> StatusHistory { get; set; } = [];
}

public class ContactWhatsAppAddress : Record
{
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    [Required, StringLength(16)]
    public string NormalizedE164 { get; set; } = "";

    [StringLength(254)]
    public string? ProviderWaId { get; set; }

    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public ContactWhatsAppConsentState ConsentState { get; set; } = ContactWhatsAppConsentState.Unknown;
    public DateTime? ConsentRecordedAt { get; set; }

    [StringLength(254)]
    public string? ConsentSource { get; set; }

    [StringLength(254)]
    public string? ConsentEvidenceReference { get; set; }

    public DateTime? LastOptOutAt { get; set; }

    [StringLength(2000)]
    public string? LastOptOutReason { get; set; }

    public List<ContactWhatsAppAddressHistory> History { get; set; } = [];
}

public class ContactCustomerLink : Record
{
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    [StringLength(120)]
    public string? Role { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }

    public List<ContactCustomerLinkHistory> History { get; set; } = [];
}

public class ContactStatusHistory : Record
{
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    public bool? PreviousIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public DateTime OccurredAt { get; set; }
}

public class ContactWhatsAppAddressHistory : Record
{
    public int ContactWhatsAppAddressId { get; set; }
    public ContactWhatsAppAddress ContactWhatsAppAddress { get; set; } = null!;
    public ContactWhatsAppConsentState? PreviousConsentState { get; set; }
    public ContactWhatsAppConsentState NewConsentState { get; set; }
    public DateTime? PreviousConsentRecordedAt { get; set; }
    public DateTime? NewConsentRecordedAt { get; set; }
    public string? PreviousConsentSource { get; set; }
    public string? NewConsentSource { get; set; }
    public string? PreviousConsentEvidenceReference { get; set; }
    public string? NewConsentEvidenceReference { get; set; }
    public DateTime? PreviousLastOptOutAt { get; set; }
    public DateTime? NewLastOptOutAt { get; set; }
    public string? PreviousLastOptOutReason { get; set; }
    public string? NewLastOptOutReason { get; set; }
    public bool? PreviousIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public bool? PreviousIsPrimary { get; set; }
    public bool NewIsPrimary { get; set; }
    public string? PreviousProviderWaId { get; set; }
    public string? NewProviderWaId { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public DateTime OccurredAt { get; set; }
}

public class ContactCustomerLinkHistory : Record
{
    public int ContactCustomerLinkId { get; set; }
    public ContactCustomerLink ContactCustomerLink { get; set; } = null!;
    public bool? PreviousIsActive { get; set; }
    public bool NewIsActive { get; set; }
    public DateOnly? PreviousEffectiveFrom { get; set; }
    public DateOnly NewEffectiveFrom { get; set; }
    public DateOnly? PreviousEffectiveTo { get; set; }
    public DateOnly? NewEffectiveTo { get; set; }
    public string? PreviousRole { get; set; }
    public string? NewRole { get; set; }
    public string? PreviousNote { get; set; }
    public string? NewNote { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string Actor { get; set; } = "system";
    public string Source { get; set; } = "System";
    public DateTime OccurredAt { get; set; }
}
