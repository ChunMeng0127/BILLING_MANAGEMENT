using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingControl.Services;

public sealed record DocumentRequirementTemplateItemInput(
    string RequirementKey,
    string Name,
    string? Description,
    bool IsRequired,
    DocumentRequirementWave Wave,
    int DisplayOrder,
    bool IsActive = true);

public sealed record DocumentRequirementTemplateCreateInput(
    int ServiceId,
    string TemplateKey,
    string Name,
    string? Description,
    IReadOnlyList<DocumentRequirementTemplateItemInput> Items,
    bool IsActive = true,
    bool IsDefault = false);

public sealed record DocumentRequirementTemplateUpdateInput(string Name, string? Description);

public sealed record DocumentRequirementTemplateItemUpdateInput(
    string Name,
    string? Description,
    bool IsRequired,
    DocumentRequirementWave Wave,
    int DisplayOrder,
    bool IsActive = true);

public sealed record DocumentRequirementTemplateItemReadModel(
    int Id,
    string RequirementKey,
    string Name,
    string? Description,
    bool IsRequired,
    DocumentRequirementWave Wave,
    int DisplayOrder,
    bool IsActive);

public sealed record DocumentRequirementTemplateReadModel(
    int Id,
    int ServiceId,
    string TemplateKey,
    int TemplateVersion,
    string Name,
    string? Description,
    bool IsActive,
    bool IsDefault,
    bool IsUsed,
    IReadOnlyList<DocumentRequirementTemplateItemReadModel> Items);

/// <summary>
/// Staff-facing application operations for reusable document requirement templates.
/// Endpoint authorization is intentionally deferred to the Phase 2B internal UI/API;
/// those endpoints must use the existing <see cref="AppRoles.Staff"/> boundary.
/// </summary>
public sealed class DocumentRequirementTemplateService(AppDbContext db)
{
    private const string TemplateConflictMessage = "Another template change completed first. Refresh and retry the template operation.";

    public async Task<DocumentRequirementTemplateReadModel> CreateFirstVersionAsync(
        DocumentRequirementTemplateCreateInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCreateInput(input);
        ValidateItems(normalized.Items);
        ValidateOperationalState(normalized.IsActive, normalized.IsDefault, normalized.Items);

        return await InSerializableTransactionAsync(async () =>
        {
            await LockServiceAsync(normalized.ServiceId, cancellationToken);
            var alreadyExists = await db.DocumentRequirementTemplates.AnyAsync(
                x => x.ServiceId == normalized.ServiceId && x.TemplateKey == normalized.TemplateKey,
                cancellationToken);
            Finance.Require(!alreadyExists,
                "A template with this key already exists for the selected service. Create a new template version instead.");

            var template = new DocumentRequirementTemplate
            {
                ServiceId = normalized.ServiceId,
                TemplateKey = normalized.TemplateKey,
                TemplateVersion = 1,
                Name = normalized.Name,
                Description = normalized.Description,
                IsActive = normalized.IsActive,
                IsDefault = false
            };
            template.Items = normalized.Items.Select(ToEntity).ToList();
            db.DocumentRequirementTemplates.Add(template);
            await db.SaveChangesAsync(cancellationToken);

            if (normalized.IsDefault)
                await SwitchDefaultWithinTransactionAsync(template, cancellationToken);

            return ToReadModel(template, isUsed: false);
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateReadModel> CreateNewVersionAsync(
        int sourceTemplateId,
        CancellationToken cancellationToken = default)
    {
        Finance.Require(sourceTemplateId > 0, "A valid source template is required.");

        return await InSerializableTransactionAsync(async () =>
        {
            var sourceIdentity = await db.DocumentRequirementTemplates.AsNoTracking()
                .Where(x => x.Id == sourceTemplateId)
                .Select(x => new { x.ServiceId, x.TemplateKey })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new BusinessException("The source document requirement template was not found.");

            await LockServiceAsync(sourceIdentity.ServiceId, cancellationToken);
            var source = await db.DocumentRequirementTemplates
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == sourceTemplateId, cancellationToken)
                ?? throw new BusinessException("The source document requirement template was not found.");

            if (source.IsActive)
                ValidateOperationalItems(source.Items);

            var maximumVersion = await db.DocumentRequirementTemplates
                .Where(x => x.ServiceId == source.ServiceId && x.TemplateKey == source.TemplateKey)
                .MaxAsync(x => (int?)x.TemplateVersion, cancellationToken) ?? 0;
            Finance.Require(maximumVersion < int.MaxValue, "No further template versions can be allocated for this template key.");

            var clone = new DocumentRequirementTemplate
            {
                ServiceId = source.ServiceId,
                TemplateKey = source.TemplateKey,
                TemplateVersion = maximumVersion + 1,
                Name = source.Name,
                Description = source.Description,
                IsActive = source.IsActive,
                IsDefault = false,
                Items = source.Items
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => new DocumentRequirementTemplateItem
                    {
                        RequirementKey = x.RequirementKey,
                        Name = x.Name,
                        Description = x.Description,
                        IsRequired = x.IsRequired,
                        Wave = x.Wave,
                        DisplayOrder = x.DisplayOrder,
                        IsActive = x.IsActive
                    })
                    .ToList()
            };
            db.DocumentRequirementTemplates.Add(clone);
            await db.SaveChangesAsync(cancellationToken);
            return ToReadModel(clone, isUsed: false);
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateReadModel> UpdateUnusedVersionAsync(
        int templateId,
        DocumentRequirementTemplateUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeRequired(input.Name, "Template name", 160);
        var description = NormalizeOptional(input.Description, "Template description", 2000);

        var template = await GetTrackedTemplateAsync(templateId, cancellationToken);
        await EnsureUnusedAsync(template.Id, cancellationToken);
        template.Name = name;
        template.Description = description;
        await SaveSingleAsync(cancellationToken);
        return await GetRequiredAsync(template.Id, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateItemReadModel> AddItemAsync(
        int templateId,
        DocumentRequirementTemplateItemInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeItemInput(input);
        ValidateItem(normalized);

        return await InSerializableTransactionAsync(async () =>
        {
            var template = await GetTrackedTemplateWithItemsAsync(templateId, cancellationToken);
            await EnsureUnusedAsync(template.Id, cancellationToken);
            Finance.Require(!template.Items.Any(x => string.Equals(x.RequirementKey, normalized.RequirementKey, StringComparison.OrdinalIgnoreCase)),
                "The requirement key is already used by this template version.");

            var item = ToEntity(normalized);
            template.Items.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return ToReadModel(item);
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateItemReadModel> UpdateItemAsync(
        int itemId,
        DocumentRequirementTemplateItemUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeItemUpdateInput(input);
        ValidateItem(normalized);

        return await InSerializableTransactionAsync(async () =>
        {
            var item = await db.DocumentRequirementTemplateItems
                .Include(x => x.DocumentRequirementTemplate)
                .ThenInclude(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == itemId, cancellationToken)
                ?? throw new BusinessException("The document requirement template item was not found.");
            await EnsureUnusedAsync(item.DocumentRequirementTemplateId, cancellationToken);
            EnsureItemActiveChangeAllowed(item.DocumentRequirementTemplate, item, normalized.IsActive);

            item.Name = normalized.Name;
            item.Description = normalized.Description;
            item.IsRequired = normalized.IsRequired;
            item.Wave = normalized.Wave;
            item.DisplayOrder = normalized.DisplayOrder;
            item.IsActive = normalized.IsActive;
            await db.SaveChangesAsync(cancellationToken);
            return ToReadModel(item);
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateItemReadModel> SetItemActiveAsync(
        int itemId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        return await InSerializableTransactionAsync(async () =>
        {
            var item = await db.DocumentRequirementTemplateItems
                .Include(x => x.DocumentRequirementTemplate)
                .ThenInclude(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == itemId, cancellationToken)
                ?? throw new BusinessException("The document requirement template item was not found.");
            await EnsureUnusedAsync(item.DocumentRequirementTemplateId, cancellationToken);
            EnsureItemActiveChangeAllowed(item.DocumentRequirementTemplate, item, isActive);
            item.IsActive = isActive;
            await db.SaveChangesAsync(cancellationToken);
            return ToReadModel(item);
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateReadModel> ReorderItemsAsync(
        int templateId,
        IReadOnlyList<int> orderedItemIds,
        CancellationToken cancellationToken = default)
    {
        if (orderedItemIds is null) throw new BusinessException("An item order is required.");

        return await InSerializableTransactionAsync(async () =>
        {
            var template = await GetTrackedTemplateWithItemsAsync(templateId, cancellationToken);
            await EnsureUnusedAsync(template.Id, cancellationToken);
            Finance.Require(orderedItemIds.Count == template.Items.Count && orderedItemIds.Distinct().Count() == orderedItemIds.Count,
                "The reorder list must contain each template item exactly once.");
            var itemsById = template.Items.ToDictionary(x => x.Id);
            Finance.Require(orderedItemIds.All(itemsById.ContainsKey), "The reorder list contains an item from another template.");
            for (var index = 0; index < orderedItemIds.Count; index++)
                itemsById[orderedItemIds[index]].DisplayOrder = index;

            await db.SaveChangesAsync(cancellationToken);
            return ToReadModel(template, await IsUsedAsync(template.Id, cancellationToken));
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateReadModel> SetTemplateActiveAsync(
        int templateId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        return await InSerializableTransactionAsync(async () =>
        {
            var template = await GetTrackedTemplateWithItemsAsync(templateId, cancellationToken);
            if (!isActive && template.IsDefault)
                throw new BusinessException("The current default template cannot be deactivated. Set another active default first.");
            if (isActive)
                ValidateOperationalItems(template.Items);

            template.IsActive = isActive;
            await db.SaveChangesAsync(cancellationToken);
            return ToReadModel(template, await IsUsedAsync(template.Id, cancellationToken));
        }, cancellationToken);
    }

    public async Task<DocumentRequirementTemplateReadModel> SetDefaultAsync(
        int templateId,
        CancellationToken cancellationToken = default)
    {
        return await InSerializableTransactionAsync(async () =>
        {
            var identity = await db.DocumentRequirementTemplates.AsNoTracking()
                .Where(x => x.Id == templateId)
                .Select(x => new { x.ServiceId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new BusinessException("The document requirement template was not found.");
            await LockServiceAsync(identity.ServiceId, cancellationToken);
            var template = await GetTrackedTemplateWithItemsAsync(templateId, cancellationToken);
            Finance.Require(template.IsActive, "Only an active template can be the service default.");
            ValidateOperationalItems(template.Items);
            await SwitchDefaultWithinTransactionAsync(template, cancellationToken);
            return ToReadModel(template, await IsUsedAsync(template.Id, cancellationToken));
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentRequirementTemplateReadModel>> GetTemplatesAsync(
        int? serviceId = null,
        string? templateKey = null,
        CancellationToken cancellationToken = default)
    {
        if (serviceId is <= 0) throw new BusinessException("A valid service filter is required.");
        var normalizedKey = templateKey is null ? null : NormalizeRequired(templateKey, "Template key", 100);
        var query = db.DocumentRequirementTemplates.AsNoTracking();
        if (serviceId is int selectedServiceId) query = query.Where(x => x.ServiceId == selectedServiceId);
        if (normalizedKey is not null) query = query.Where(x => x.TemplateKey == normalizedKey);

        var templates = await query
            .OrderBy(x => x.ServiceId)
            .ThenBy(x => x.TemplateKey)
            .ThenBy(x => x.TemplateVersion)
            .ToListAsync(cancellationToken);
        return await ToReadModelsAsync(templates, cancellationToken);
    }

    public Task<IReadOnlyList<DocumentRequirementTemplateReadModel>> GetVersionsAsync(
        int serviceId,
        string templateKey,
        CancellationToken cancellationToken = default) =>
        GetTemplatesAsync(serviceId, templateKey, cancellationToken);

    public async Task<DocumentRequirementTemplateReadModel?> GetAsync(
        int templateId,
        CancellationToken cancellationToken = default)
    {
        if (templateId <= 0) return null;
        var template = await db.DocumentRequirementTemplates.AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        return template is null ? null : (await ToReadModelsAsync([template], cancellationToken)).Single();
    }

    public async Task<bool> IsUsedAsync(int templateId, CancellationToken cancellationToken = default) =>
        await db.DocumentRequests.AsNoTracking().AnyAsync(x => x.DocumentRequirementTemplateId == templateId, cancellationToken);

    private async Task<DocumentRequirementTemplateReadModel> GetRequiredAsync(int templateId, CancellationToken cancellationToken)
    {
        return await GetAsync(templateId, cancellationToken)
            ?? throw new BusinessException("The document requirement template was not found.");
    }

    private async Task<DocumentRequirementTemplate> GetTrackedTemplateAsync(int templateId, CancellationToken cancellationToken) =>
        await db.DocumentRequirementTemplates.SingleOrDefaultAsync(x => x.Id == templateId, cancellationToken)
        ?? throw new BusinessException("The document requirement template was not found.");

    private async Task<DocumentRequirementTemplate> GetTrackedTemplateWithItemsAsync(int templateId, CancellationToken cancellationToken) =>
        await db.DocumentRequirementTemplates.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == templateId, cancellationToken)
        ?? throw new BusinessException("The document requirement template was not found.");

    private async Task EnsureUnusedAsync(int templateId, CancellationToken cancellationToken)
    {
        Finance.Require(!await db.DocumentRequests.AsNoTracking().AnyAsync(x => x.DocumentRequirementTemplateId == templateId, cancellationToken),
            "This template version is already used by a document request and is immutable. Create a new template version instead.");
    }

    private async Task SwitchDefaultWithinTransactionAsync(
        DocumentRequirementTemplate target,
        CancellationToken cancellationToken)
    {
        Finance.Require(target.IsActive, "Only an active template can be the service default.");
        ValidateOperationalItems(target.Items);

        var existingDefaults = await db.DocumentRequirementTemplates
            .Where(x => x.ServiceId == target.ServiceId && x.IsDefault && x.Id != target.Id)
            .ToListAsync(cancellationToken);
        foreach (var existingDefault in existingDefaults) existingDefault.IsDefault = false;
        if (existingDefaults.Count > 0) await db.SaveChangesAsync(cancellationToken);

        if (!target.IsDefault)
        {
            target.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task LockServiceAsync(int serviceId, CancellationToken cancellationToken)
    {
        Finance.Require(serviceId > 0, "A valid service is required.");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\" FROM \"Services\" WHERE \"Id\" = @service_id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "service_id";
        parameter.Value = serviceId;
        command.Parameters.Add(parameter);
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        Finance.Require(await command.ExecuteScalarAsync(cancellationToken) is not null, "The selected service was not found.");
    }

    private async Task<T> InSerializableTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            throw new BusinessException("The template changed while this operation was in progress. Refresh and retry.", ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(TemplateConflictMessage, ex);
        }
        catch (Exception ex) when (FinancialTransaction.IsSerializationConflict(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(TemplateConflictMessage, ex);
        }
    }

    private async Task SaveSingleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            throw new BusinessException("The template changed while this operation was in progress. Refresh and retry.", ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static DocumentRequirementTemplateCreateInput NormalizeCreateInput(DocumentRequirementTemplateCreateInput input)
    {
        var items = input.Items ?? throw new BusinessException("Template items are required.");
        return input with
        {
            TemplateKey = NormalizeRequired(input.TemplateKey, "Template key", 100),
            Name = NormalizeRequired(input.Name, "Template name", 160),
            Description = NormalizeOptional(input.Description, "Template description", 2000),
            Items = items.Select(NormalizeItemInput).ToArray()
        };
    }

    private static DocumentRequirementTemplateItemInput NormalizeItemInput(DocumentRequirementTemplateItemInput input) => input with
    {
        RequirementKey = NormalizeRequired(input.RequirementKey, "Requirement key", 100),
        Name = NormalizeRequired(input.Name, "Requirement name", 160),
        Description = NormalizeOptional(input.Description, "Requirement description", 2000)
    };

    private static DocumentRequirementTemplateItemUpdateInput NormalizeItemUpdateInput(DocumentRequirementTemplateItemUpdateInput input) => input with
    {
        Name = NormalizeRequired(input.Name, "Requirement name", 160),
        Description = NormalizeOptional(input.Description, "Requirement description", 2000)
    };

    private static string NormalizeRequired(string? value, string label, int maxLength)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(value), $"{label} is required.");
        var normalized = value!.Trim();
        Finance.Require(normalized.Length <= maxLength, $"{label} must be {maxLength} characters or fewer.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        Finance.Require(normalized.Length <= maxLength, $"{label} must be {maxLength} characters or fewer.");
        return normalized;
    }

    private static void ValidateItems(IReadOnlyList<DocumentRequirementTemplateItemInput> items)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            ValidateItem(item);
            Finance.Require(keys.Add(item.RequirementKey), "Requirement keys must be unique within a template version.");
        }
    }

    private static void ValidateItem(DocumentRequirementTemplateItemInput item)
    {
        Finance.Require(Enum.IsDefined(item.Wave), "Select a valid document requirement wave.");
        Finance.Require(item.DisplayOrder >= 0, "Document requirement display order cannot be negative.");
    }

    private static void ValidateItem(DocumentRequirementTemplateItemUpdateInput item)
    {
        Finance.Require(Enum.IsDefined(item.Wave), "Select a valid document requirement wave.");
        Finance.Require(item.DisplayOrder >= 0, "Document requirement display order cannot be negative.");
    }

    private static void ValidateOperationalState(
        bool isActive,
        bool isDefault,
        IReadOnlyList<DocumentRequirementTemplateItemInput> items)
    {
        Finance.Require(!isDefault || isActive, "A default template must be active.");
        if (isActive || isDefault)
            Finance.Require(items.Any(x => x.IsActive), "An active or default template must contain at least one active requirement item.");
    }

    private static void ValidateOperationalItems(IReadOnlyCollection<DocumentRequirementTemplateItem> items) =>
        Finance.Require(items.Any(x => x.IsActive), "An active or default template must contain at least one active requirement item.");

    private static void EnsureItemActiveChangeAllowed(
        DocumentRequirementTemplate template,
        DocumentRequirementTemplateItem item,
        bool requestedActive)
    {
        if (template.IsActive && item.IsActive && !requestedActive)
            Finance.Require(template.Items.Any(x => x.Id != item.Id && x.IsActive),
                "An active template must retain at least one active requirement item.");
    }

    private static DocumentRequirementTemplateItem ToEntity(DocumentRequirementTemplateItemInput input) => new()
    {
        RequirementKey = input.RequirementKey,
        Name = input.Name,
        Description = input.Description,
        IsRequired = input.IsRequired,
        Wave = input.Wave,
        DisplayOrder = input.DisplayOrder,
        IsActive = input.IsActive
    };

    private static DocumentRequirementTemplateItemReadModel ToReadModel(DocumentRequirementTemplateItem item) => new(
        item.Id,
        item.RequirementKey,
        item.Name,
        item.Description,
        item.IsRequired,
        item.Wave,
        item.DisplayOrder,
        item.IsActive);

    private static DocumentRequirementTemplateReadModel ToReadModel(DocumentRequirementTemplate template, bool isUsed) => new(
        template.Id,
        template.ServiceId,
        template.TemplateKey,
        template.TemplateVersion,
        template.Name,
        template.Description,
        template.IsActive,
        template.IsDefault,
        isUsed,
        template.Items
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(ToReadModel)
            .ToArray());

    private async Task<IReadOnlyList<DocumentRequirementTemplateReadModel>> ToReadModelsAsync(
        IReadOnlyList<DocumentRequirementTemplate> templates,
        CancellationToken cancellationToken)
    {
        if (templates.Count == 0) return [];
        var templateIds = templates.Select(x => x.Id).ToArray();
        var usedIds = (await db.DocumentRequests.AsNoTracking()
            .Where(x => templateIds.Contains(x.DocumentRequirementTemplateId))
            .Select(x => x.DocumentRequirementTemplateId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var items = await db.DocumentRequirementTemplateItems.AsNoTracking()
            .Where(x => templateIds.Contains(x.DocumentRequirementTemplateId))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var itemsByTemplate = items.GroupBy(x => x.DocumentRequirementTemplateId).ToDictionary(x => x.Key, x => x.ToArray());
        return templates.Select(template =>
        {
            template.Items = itemsByTemplate.TryGetValue(template.Id, out var templateItems) ? templateItems.ToList() : [];
            return ToReadModel(template, usedIds.Contains(template.Id));
        }).ToArray();
    }
}
