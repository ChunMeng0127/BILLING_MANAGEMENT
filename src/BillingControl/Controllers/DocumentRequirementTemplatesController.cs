using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.Staff)]
public sealed class DocumentRequirementTemplatesController(
    AppDbContext db,
    DocumentRequirementTemplateService templates) : AppController
{
    public async Task<IActionResult> Index(int? serviceId)
    {
        var readModels = await templates.GetTemplatesAsync(serviceId);
        var serviceIds = readModels.Select(x => x.ServiceId).Distinct().ToArray();
        var serviceNames = await db.Services.AsNoTracking()
            .Where(x => serviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        return View(new DocumentRequirementTemplateIndexViewModel
        {
            ServiceId = serviceId,
            Services = await ServiceOptionsAsync(),
            Templates = readModels.Select(x => new DocumentRequirementTemplateListItemViewModel(
                x.Id,
                x.ServiceId,
                serviceNames.TryGetValue(x.ServiceId, out var serviceName) ? serviceName : $"Service #{x.ServiceId}",
                x.TemplateKey,
                x.TemplateVersion,
                x.Name,
                x.IsActive,
                x.IsDefault,
                x.IsUsed,
                x.Items.Count)).ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.Services = await ServiceOptionsAsync();
        return View(new DocumentRequirementTemplateCreateViewModel
        {
            Items = [new() { Wave = DocumentRequirementWave.Normal, IsActive = true }]
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentRequirementTemplateCreateViewModel form)
    {
        form.Items ??= [];
        ValidateWaves(form.Items, "Items");
        if (!ModelState.IsValid)
        {
            ViewBag.Services = await ServiceOptionsAsync();
            return View(form);
        }

        try
        {
            var created = await templates.CreateFirstVersionAsync(new(
                form.ServiceId,
                null,
                form.Name,
                form.Description,
                form.Items.Select(ToServiceInput).ToArray(),
                form.IsActive,
                form.IsDefault));
            TempData["Success"] = $"Document checklist created. Version {created.TemplateVersion} is ready.";
            return RedirectToAction(nameof(Edit), new { id = created.Id });
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Services = await ServiceOptionsAsync();
            return View(form);
        }
    }

    [HttpGet]
    public Task<IActionResult> Edit(int id) => DetailViewAsync(id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDefinition(DocumentRequirementTemplateDefinitionForm form)
    {
        var template = await templates.GetAsync(form.Id);
        if (template is null) return NotFound();
        if (!ModelState.IsValid) return await DetailViewAsync(form.Id, definition: form);

        try
        {
            await templates.UpdateUnusedVersionAsync(form.Id, new(form.Name, form.Description));
            TempData["Success"] = "Checklist details saved.";
            return RedirectToAction(nameof(Edit), new { id = form.Id });
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await DetailViewAsync(form.Id, definition: form);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(DocumentRequirementTemplateItemForm form)
    {
        var template = await templates.GetAsync(form.TemplateId);
        if (template is null) return NotFound();
        ValidateWave(form, "NewItem");
        if (!ModelState.IsValid) return await DetailViewAsync(form.TemplateId, newItem: form);

        try
        {
            await templates.AddItemAsync(form.TemplateId, ToServiceInput(form));
            TempData["Success"] = "Document / requirement added.";
            return RedirectToAction(nameof(Edit), new { id = form.TemplateId });
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await DetailViewAsync(form.TemplateId, newItem: form);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditItem(DocumentRequirementTemplateItemForm form)
    {
        var template = await templates.GetAsync(form.TemplateId);
        if (template is null || !template.Items.Any(x => x.Id == form.Id)) return NotFound();
        ValidateWave(form, "EditingItem");
        if (!ModelState.IsValid) return await DetailViewAsync(form.TemplateId, editingItem: form);

        try
        {
            await templates.UpdateItemAsync(form.Id, new(form.Name, form.Description, form.IsRequired, form.Wave, 0, form.IsActive));
            TempData["Success"] = "Document / requirement saved.";
            return RedirectToAction(nameof(Edit), new { id = form.TemplateId });
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await DetailViewAsync(form.TemplateId, editingItem: form);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetItemActive(int templateId, int itemId, bool isActive)
    {
        var template = await templates.GetAsync(templateId);
        if (template is null || !template.Items.Any(x => x.Id == itemId)) return NotFound();
        try
        {
            await templates.SetItemActiveAsync(itemId, isActive);
            TempData["Success"] = isActive ? "Document / requirement activated." : "Document / requirement deactivated.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Edit), new { id = templateId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveItem(int templateId, int itemId, string direction)
    {
        var template = await templates.GetAsync(templateId);
        if (template is null) return NotFound();
        if (template.IsUsed) { TempData["Error"] = "This checklist version has already been used and its details are immutable."; return RedirectToAction(nameof(Edit), new { id = templateId }); }

        var items = template.Items.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => x.Id).ToList();
        var index = items.IndexOf(itemId);
        if (index < 0) return NotFound();
        var targetIndex = direction.Equals("up", StringComparison.OrdinalIgnoreCase) ? index - 1 : index + 1;
        if (targetIndex >= 0 && targetIndex < items.Count)
            (items[index], items[targetIndex]) = (items[targetIndex], items[index]);

        try
        {
            await templates.ReorderItemsAsync(templateId, items);
            TempData["Success"] = "Checklist order updated.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Edit), new { id = templateId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVersion(int id)
    {
        try
        {
            var created = await templates.CreateNewVersionAsync(id);
            TempData["Success"] = $"Checklist version {created.TemplateVersion} created. The new version was copied from the previous checklist. The previous version was not changed.";
            return RedirectToAction(nameof(Edit), new { id = created.Id });
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Edit), new { id });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        try
        {
            await templates.SetTemplateActiveAsync(id, isActive);
            TempData["Success"] = isActive ? "Checklist activated." : "Checklist deactivated.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int id)
    {
        try
        {
            await templates.SetDefaultAsync(id);
            TempData["Success"] = "Default checklist updated for this service.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<IActionResult> DetailViewAsync(
        int id,
        DocumentRequirementTemplateDefinitionForm? definition = null,
        DocumentRequirementTemplateItemForm? newItem = null,
        DocumentRequirementTemplateItemForm? editingItem = null)
    {
        var template = await templates.GetAsync(id);
        if (template is null) return NotFound();
        var serviceName = await db.Services.AsNoTracking()
            .Where(x => x.Id == template.ServiceId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync() ?? $"Service #{template.ServiceId}";
        var defaultDefinition = new DocumentRequirementTemplateDefinitionForm
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description
        };
        var defaultNewItem = new DocumentRequirementTemplateItemForm
        {
            TemplateId = template.Id,
            Wave = DocumentRequirementWave.Normal,
            IsActive = true
        };
        return View("Edit", new DocumentRequirementTemplateDetailViewModel
        {
            Id = template.Id,
            ServiceId = template.ServiceId,
            ServiceName = serviceName,
            TemplateKey = template.TemplateKey,
            TemplateVersion = template.TemplateVersion,
            IsActive = template.IsActive,
            IsDefault = template.IsDefault,
            IsUsed = template.IsUsed,
            Definition = definition ?? defaultDefinition,
            NewItem = newItem ?? defaultNewItem,
            EditingItem = editingItem,
            Items = template.Items.Select(x => new DocumentRequirementTemplateItemDisplayViewModel(
                x.Id, x.RequirementKey, x.Name, x.Description, x.IsRequired, x.Wave, x.DisplayOrder, x.IsActive)).ToArray()
        });
    }

    private async Task<IReadOnlyList<DocumentRequirementTemplateServiceOption>> ServiceOptionsAsync() =>
        await db.Services.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new DocumentRequirementTemplateServiceOption(x.Id, x.Name, x.IsActive))
            .ToListAsync();

    private void ValidateWaves(IEnumerable<DocumentRequirementTemplateItemForm> items, string prefix)
    {
        var index = 0;
        foreach (var item in items)
        {
            ValidateWave(item, $"{prefix}[{index}]");
            index++;
        }
    }

    private void ValidateWave(DocumentRequirementTemplateItemForm item, string prefix)
    {
        if (!Enum.IsDefined(item.Wave)) ModelState.AddModelError($"{prefix}.Wave", "Select a valid request priority.");
    }

    private static DocumentRequirementTemplateItemInput ToServiceInput(DocumentRequirementTemplateItemForm item) => new(
        null,
        item.Name,
        item.Description,
        item.IsRequired,
        item.Wave,
        0,
        item.IsActive);

}
