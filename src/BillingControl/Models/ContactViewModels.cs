using System.ComponentModel.DataAnnotations;
using BillingControl.Services;

namespace BillingControl.Models;

public sealed class ContactListViewModel
{
    [StringLength(160)]
    public string? Search { get; set; }

    public bool IncludeInactive { get; set; }
    public IReadOnlyList<ContactSummaryReadModel> Contacts { get; init; } = [];
}

public sealed class ContactCreateViewModel
{
    [Required, StringLength(160)]
    public string Name { get; set; } = "";

    [StringLength(35)]
    public string? PreferredLanguage { get; set; }
}

public sealed class ContactEditViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }

    [Required, StringLength(160)]
    public string Name { get; set; } = "";

    [StringLength(35)]
    public string? PreferredLanguage { get; set; }
}

public sealed class ContactDetailsViewModel
{
    public ContactReadModel Contact { get; init; } = null!;
    public IReadOnlyList<ActiveCustomerOptionReadModel> CustomerOptions { get; init; } = [];
}

public sealed class ContactActiveForm
{
    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }

    public bool IsActive { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class AddAddressForm
{
    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Required, StringLength(80)]
    public string PhoneNumber { get; set; } = "";
}

public sealed class AddressPrimaryForm
{
    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, int.MaxValue)]
    public int AddressId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public sealed class AddressActiveForm
{
    [Range(1, int.MaxValue)]
    public int AddressId { get; set; }

    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }

    public bool IsActive { get; set; }
    public bool MakePrimary { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class ConsentForm
{
    [Range(1, int.MaxValue)]
    public int AddressId { get; set; }

    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }

    public ContactWhatsAppConsentState ConsentState { get; set; }

    [StringLength(254)]
    public string? ConsentSource { get; set; }

    [StringLength(254)]
    public string? ConsentEvidenceReference { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class AddCustomerLinkForm
{
    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, int.MaxValue)]
    public int CustomerId { get; set; }

    [StringLength(120)]
    public string? Role { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }
}

public sealed class EditCustomerLinkForm
{
    [Range(1, int.MaxValue)]
    public int LinkId { get; set; }

    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }

    [StringLength(120)]
    public string? Role { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }
}

public sealed class CustomerLinkActiveForm
{
    [Range(1, int.MaxValue)]
    public int LinkId { get; set; }

    [Range(1, int.MaxValue)]
    public int ContactId { get; set; }

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }

    public bool IsActive { get; set; }

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";
}
