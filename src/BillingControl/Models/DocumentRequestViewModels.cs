using System.ComponentModel.DataAnnotations;
using BillingControl.Services;

namespace BillingControl.Models;

public sealed record DocumentRequestWorkItemContextViewModel(
    int WorkItemId,
    int ServiceId,
    string CustomerName,
    string ServiceName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

public sealed class DocumentRequestCreateViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid WorkItem is required.")]
    public int WorkItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select an eligible document template.")]
    public int DocumentRequirementTemplateId { get; set; }

    public DocumentRequestWorkItemContextViewModel WorkItem { get; init; } = new(0, 0, "", "", default, default);
    public IReadOnlyList<DocumentRequestTemplateOptionReadModel> Templates { get; init; } = [];
}

public sealed record DocumentRequestPanelViewModel(
    int WorkItemId,
    int ServiceId,
    string ServiceName,
    DocumentRequestReadModel? CurrentRequest,
    IReadOnlyList<DocumentRequestReadModel> Revisions,
    IReadOnlyList<DocumentRequestTemplateOptionReadModel> AvailableTemplates);

public sealed record DocumentRequestDetailsViewModel(
    DocumentRequestWorkItemContextViewModel WorkItem,
    DocumentRequestReadModel Request,
    IReadOnlyList<DocumentRequestReadModel> Revisions);
