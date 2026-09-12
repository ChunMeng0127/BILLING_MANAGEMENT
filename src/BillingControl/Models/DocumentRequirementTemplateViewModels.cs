using System.ComponentModel.DataAnnotations;
using BillingControl.Services;

namespace BillingControl.Models;

public sealed class DocumentRequirementTemplateIndexViewModel
{
    public int? ServiceId { get; set; }
    [StringLength(100)] public string? TemplateKey { get; set; }
    public IReadOnlyList<DocumentRequirementTemplateServiceOption> Services { get; init; } = [];
    public IReadOnlyList<DocumentRequirementTemplateListItemViewModel> Templates { get; init; } = [];
}

public sealed record DocumentRequirementTemplateServiceOption(int Id, string Name, bool IsActive);

public sealed record DocumentRequirementTemplateListItemViewModel(
    int Id,
    int ServiceId,
    string ServiceName,
    string TemplateKey,
    int TemplateVersion,
    string Name,
    bool IsActive,
    bool IsDefault,
    bool IsUsed,
    int ItemCount);

public sealed class DocumentRequirementTemplateCreateViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a service.")]
    public int ServiceId { get; set; }

    [Required, StringLength(100)]
    public string TemplateKey { get; set; } = "";

    [Required, StringLength(160)]
    public string Name { get; set; } = "";

    [StringLength(2000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public List<DocumentRequirementTemplateItemForm> Items { get; set; } = [new()];
}

public sealed class DocumentRequirementTemplateDetailViewModel
{
    public int Id { get; init; }
    public int ServiceId { get; init; }
    public string ServiceName { get; init; } = "";
    public string TemplateKey { get; init; } = "";
    public int TemplateVersion { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }
    public bool IsUsed { get; init; }
    public DocumentRequirementTemplateDefinitionForm Definition { get; init; } = new();
    public DocumentRequirementTemplateItemForm NewItem { get; init; } = new();
    public DocumentRequirementTemplateItemForm? EditingItem { get; init; }
    public IReadOnlyList<DocumentRequirementTemplateItemDisplayViewModel> Items { get; init; } = [];
}

public sealed class DocumentRequirementTemplateDefinitionForm
{
    public int Id { get; set; }

    [Required, StringLength(160)]
    public string Name { get; set; } = "";

    [StringLength(2000)]
    public string? Description { get; set; }
}

public sealed class DocumentRequirementTemplateItemForm
{
    public int Id { get; set; }
    public int TemplateId { get; set; }

    [Required, StringLength(100)]
    public string RequirementKey { get; set; } = "";

    [Required, StringLength(160)]
    public string Name { get; set; } = "";

    [StringLength(2000)]
    public string? Description { get; set; }

    public bool IsRequired { get; set; }
    public DocumentRequirementWave Wave { get; set; } = DocumentRequirementWave.Normal;

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed record DocumentRequirementTemplateItemDisplayViewModel(
    int Id,
    string RequirementKey,
    string Name,
    string? Description,
    bool IsRequired,
    DocumentRequirementWave Wave,
    int DisplayOrder,
    bool IsActive);
