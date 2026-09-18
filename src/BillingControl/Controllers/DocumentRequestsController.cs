using System.ComponentModel.DataAnnotations;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.Staff)]
public sealed class DocumentRequestsController(
    AppDbContext db,
    AccessScope access,
    DocumentRequestService documentRequests) : AppController
{
    private const string Source = "BillingControl.DocumentRequests";

    [HttpGet]
    public async Task<IActionResult> Create(int workItemId)
    {
        if (workItemId <= 0) return NotFound();

        var actor = await access.CurrentAsync();
        var workItem = await GetWorkItemContextAsync(actor, workItemId);
        if (workItem is null) return NotFound();

        var current = await documentRequests.GetCurrentForWorkItemAsync(workItemId);
        if (current is not null)
            return RedirectToAction(nameof(Details), new { id = current.Id });

        return View(await BuildCreateModelAsync(workItem));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentRequestCreateViewModel form)
    {
        if (form.WorkItemId <= 0) return NotFound();

        var actor = await access.CurrentAsync();
        var workItem = await GetWorkItemContextAsync(actor, form.WorkItemId);
        if (workItem is null) return NotFound();

        var current = await documentRequests.GetCurrentForWorkItemAsync(form.WorkItemId);
        if (current is not null)
            return RedirectToAction(nameof(Details), new { id = current.Id });

        var model = await BuildCreateModelAsync(workItem, form);
        if (!ModelState.IsValid) return View(model);

        try
        {
            var created = await documentRequests.CreateDraftAsync(new(
                form.WorkItemId,
                form.DocumentRequirementTemplateId,
                actor.UserId,
                Source));
            TempData["Success"] = $"Document request revision {created.Revision} created as Draft.";
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }
        catch (BusinessException ex)
        {
            current = await documentRequests.GetCurrentForWorkItemAsync(form.WorkItemId);
            if (current is not null)
                return RedirectToAction(nameof(Details), new { id = current.Id });

            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildCreateModelAsync(workItem, form));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (id <= 0) return NotFound();

        var actor = await access.CurrentAsync();
        var workItemId = await db.DocumentRequests.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (int?)x.WorkItemId)
            .SingleOrDefaultAsync();
        if (workItemId is null) return NotFound();

        var workItem = await GetWorkItemContextAsync(actor, workItemId.Value);
        if (workItem is null) return NotFound();

        var request = await documentRequests.GetByIdAsync(id);
        if (request is null) return NotFound();

        var revisions = await documentRequests.GetRevisionsForWorkItemAsync(workItem.WorkItemId);
        return View(new DocumentRequestDetailsViewModel(workItem, request, revisions));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Transition(
        int requestId,
        long expectedVersion,
        DocumentRequestStatus targetStatus,
        [Required, StringLength(2000)] string reason)
    {
        var actor = await access.CurrentAsync();
        var workItemId = await GetRequestWorkItemIdAsync(requestId);
        if (workItemId is null) return NotFound();
        if (await GetWorkItemContextAsync(actor, workItemId.Value) is null) return NotFound();

        ValidForm();
        await documentRequests.TransitionAsync(requestId, new(
            targetStatus,
            expectedVersion,
            actor.UserId,
            Source,
            reason));

        TempData["Success"] = "Document request status updated.";
        return RedirectToAction(nameof(Details), new { id = requestId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TransitionItem(
        int requestId,
        int itemId,
        long expectedVersion,
        DocumentRequestItemStatus targetStatus,
        [Required, StringLength(2000)] string reason)
    {
        var actor = await access.CurrentAsync();
        var belongsToRequest = await db.DocumentRequestItems.AsNoTracking()
            .AnyAsync(x => x.Id == itemId && x.DocumentRequestId == requestId);
        if (!belongsToRequest) return NotFound();

        var workItemId = await GetRequestWorkItemIdAsync(requestId);
        if (workItemId is null) return NotFound();
        if (await GetWorkItemContextAsync(actor, workItemId.Value) is null) return NotFound();

        ValidForm();
        await documentRequests.TransitionItemAsync(itemId, new(
            targetStatus,
            expectedVersion,
            actor.UserId,
            Source,
            reason));

        TempData["Success"] = "Checklist item status updated.";
        return RedirectToAction(nameof(Details), new { id = requestId });
    }

    private async Task<DocumentRequestCreateViewModel> BuildCreateModelAsync(
        DocumentRequestWorkItemContextViewModel workItem,
        DocumentRequestCreateViewModel? form = null)
    {
        var templates = await documentRequests.GetAvailableTemplatesForWorkItemAsync(workItem.WorkItemId);
        return new DocumentRequestCreateViewModel
        {
            WorkItemId = workItem.WorkItemId,
            DocumentRequirementTemplateId = form?.DocumentRequirementTemplateId
                ?? templates.FirstOrDefault(x => x.IsDefault)?.Id
                ?? 0,
            WorkItem = workItem,
            Templates = templates
        };
    }

    private async Task<DocumentRequestWorkItemContextViewModel?> GetWorkItemContextAsync(
        AccessProfile actor,
        int workItemId) =>
        await access.WorkItems(actor).AsNoTracking()
            .Where(x => x.Id == workItemId)
            .Select(x => new DocumentRequestWorkItemContextViewModel(
                x.Id,
                x.BillingRecord.Engagement.ServiceId,
                x.BillingRecord.CustomerName,
                x.BillingRecord.Engagement.Service.Name,
                x.BillingRecord.PeriodStart,
                x.BillingRecord.PeriodEnd))
            .SingleOrDefaultAsync();

    private Task<int?> GetRequestWorkItemIdAsync(int requestId) =>
        requestId <= 0
            ? Task.FromResult<int?>(null)
            : db.DocumentRequests.AsNoTracking()
                .Where(x => x.Id == requestId)
                .Select(x => (int?)x.WorkItemId)
                .SingleOrDefaultAsync();
}
