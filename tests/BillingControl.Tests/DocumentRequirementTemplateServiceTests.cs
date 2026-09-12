using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    [PostgresFact]
    public async Task TemplateService_CreatesFirstVersionAndClonesDefinition()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-create");
        var service = new DocumentRequirementTemplateService(db);
        var key = DocumentToken("template-key");

        var first = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            key,
            "Monthly checklist",
            "Initial checklist",
            [
                new("bank-statement", "Bank statement", "Latest statement", true, DocumentRequirementWave.StartWork, 0),
                new("payroll", "Payroll report", null, false, DocumentRequirementWave.Later, 1)
            ],
            IsActive: true,
            IsDefault: true));

        Assert.Equal(1, first.TemplateVersion);
        Assert.True(first.IsActive);
        Assert.True(first.IsDefault);
        Assert.False(first.IsUsed);
        Assert.Equal(["bank-statement", "payroll"], first.Items.Select(x => x.RequirementKey).ToArray());

        var second = await service.CreateNewVersionAsync(first.Id);

        Assert.Equal(2, second.TemplateVersion);
        Assert.False(second.IsDefault);
        Assert.True(second.IsActive);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Description, second.Description);
        Assert.Equal(first.Items.Select(x => (x.RequirementKey, x.Name, x.Description, x.IsRequired, x.Wave, x.DisplayOrder, x.IsActive)),
            second.Items.Select(x => (x.RequirementKey, x.Name, x.Description, x.IsRequired, x.Wave, x.DisplayOrder, x.IsActive)));

        var updated = await service.UpdateUnusedVersionAsync(second.Id, new("Monthly checklist v2", "Edited before use"));
        Assert.Equal("Monthly checklist v2", updated.Name);
        Assert.Equal("Edited before use", updated.Description);
        Assert.Equal("Monthly checklist", (await service.GetAsync(first.Id))!.Name);

        var updatedItem = await service.UpdateItemAsync(second.Items[0].Id,
            new("Bank statement / PDF", "Updated description", true, DocumentRequirementWave.Normal, 2));
        Assert.Equal("bank-statement", updatedItem.RequirementKey);
        Assert.Equal(second.Items[0].DisplayOrder, updatedItem.DisplayOrder);

        var reordered = await service.ReorderItemsAsync(second.Id, [second.Items[1].Id, second.Items[0].Id]);
        Assert.Equal([second.Items[1].Id, second.Items[0].Id], reordered.Items.Select(x => x.Id).ToArray());
    }

    [PostgresFact]
    public async Task TemplateService_ValidatesKeysAndRejectsUsedVersionEdits()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-validation");
        var service = new DocumentRequirementTemplateService(db);

        await Assert.ThrowsAsync<BusinessException>(() => service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            DocumentToken("duplicate-requirements"),
            "Duplicate requirements",
            null,
            [
                new("same", "First", null, true, DocumentRequirementWave.Normal, 0),
                new(" SAME ", "Second", null, false, DocumentRequirementWave.Normal, 1)
            ])));

        var created = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            DocumentToken("used-template"),
            "Used template",
            null,
            [new("bank-statement", "Bank statement", null, true, DocumentRequirementWave.Normal, 0)]));
        await Assert.ThrowsAsync<BusinessException>(() => service.AddItemAsync(created.Id,
            new("bank-statement", "Duplicate key", null, false, DocumentRequirementWave.Later, 1)));

        var fixture = await AddDocumentFixtureAsync(db, "template-service-used", serviceMaster.Id);
        await AddDocumentRequestAsync(db, fixture.WorkItemId, created.Id);

        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateUnusedVersionAsync(created.Id, new("Should fail", null)));
        await Assert.ThrowsAsync<BusinessException>(() => service.SetItemActiveAsync(created.Items[0].Id, false));
        await Assert.ThrowsAsync<BusinessException>(() => service.AddItemAsync(created.Id,
            new("new-item", "New item", null, false, DocumentRequirementWave.Later, 1)));
    }

    [PostgresFact]
    public async Task TemplateService_DefaultSwitchAndActivationRulesAreExplicit()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-defaults");
        var service = new DocumentRequirementTemplateService(db);
        var first = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            DocumentToken("default-template"),
            "Default v1",
            null,
            [new("bank-statement", "Bank statement", null, true, DocumentRequirementWave.Normal, 0)],
            IsActive: true,
            IsDefault: true));
        var second = await service.CreateNewVersionAsync(first.Id);

        var switched = await service.SetDefaultAsync(second.Id);
        Assert.True(switched.IsDefault);
        Assert.False((await service.GetAsync(first.Id))!.IsDefault);
        Assert.Equal(1, (await service.GetTemplatesAsync(serviceMaster.Id)).Count(x => x.IsActive && x.IsDefault));

        await Assert.ThrowsAsync<BusinessException>(() => service.SetTemplateActiveAsync(second.Id, false));
        await service.SetTemplateActiveAsync(first.Id, false);
        await Assert.ThrowsAsync<BusinessException>(() => service.SetDefaultAsync(first.Id));
        Assert.False((await service.GetAsync(first.Id))!.IsActive);
        Assert.True((await service.GetAsync(second.Id))!.IsDefault);
    }

    [PostgresFact]
    public async Task TemplateService_ReadsFamiliesFiltersAndRetainsInactiveHistory()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-read-models");
        var otherServiceMaster = await AddDocumentServiceAsync(db, "template-service-read-models-other");
        var service = new DocumentRequirementTemplateService(db);

        var alpha = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            "alpha-checklist",
            "Alpha checklist",
            null,
            [
                new("one", "One", null, true, DocumentRequirementWave.StartWork, 0),
                new("two", "Two", null, false, DocumentRequirementWave.Normal, 1),
                new("three", "Three", null, false, DocumentRequirementWave.Later, 2)
            ]));
        var alphaV2 = await service.CreateNewVersionAsync(alpha.Id);
        await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            "beta-checklist",
            "Beta checklist",
            null,
            [new("beta", "Beta", null, true, DocumentRequirementWave.Normal, 0)]));
        await service.CreateFirstVersionAsync(new(
            otherServiceMaster.Id,
            "alpha-checklist",
            "Other service checklist",
            null,
            [new("other", "Other", null, true, DocumentRequirementWave.Normal, 0)]));

        await service.SetTemplateActiveAsync(alpha.Id, false);
        var alphaItemIds = alphaV2.Items.Select(x => x.Id).ToArray();
        await service.ReorderItemsAsync(alphaV2.Id, [alphaItemIds[2], alphaItemIds[0], alphaItemIds[1]]);
        await service.ReorderItemsAsync(alphaV2.Id, [alphaItemIds[1], alphaItemIds[2], alphaItemIds[0]]);

        var serviceModels = await service.GetTemplatesAsync(serviceMaster.Id);
        Assert.Equal(["alpha-checklist", "alpha-checklist", "beta-checklist"], serviceModels.Select(x => x.TemplateKey).ToArray());
        Assert.Equal([1, 2], serviceModels.Where(x => x.TemplateKey == "alpha-checklist").Select(x => x.TemplateVersion).ToArray());
        Assert.False(serviceModels.Single(x => x.Id == alpha.Id).IsActive);
        Assert.True(serviceModels.Single(x => x.Id == alphaV2.Id).IsActive);
        Assert.Equal([alphaItemIds[1], alphaItemIds[2], alphaItemIds[0]],
            serviceModels.Single(x => x.Id == alphaV2.Id).Items.Select(x => x.Id).ToArray());

        var filteredFamily = await service.GetTemplatesAsync(serviceMaster.Id, "alpha-checklist");
        Assert.Equal([alpha.Id, alphaV2.Id], filteredFamily.Select(x => x.Id).ToArray());
        var otherServiceFamily = await service.GetTemplatesAsync(otherServiceMaster.Id, "alpha-checklist");
        Assert.Single(otherServiceFamily);
        Assert.Equal("Other service checklist", otherServiceFamily[0].Name);
    }

    [PostgresFact]
    public async Task TemplateService_ConcurrentVersionAllocationDoesNotDuplicateVersions()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-concurrency");
        var seed = await new DocumentRequirementTemplateService(db).CreateFirstVersionAsync(new(
            serviceMaster.Id,
            DocumentToken("concurrent-template"),
            "Concurrent template",
            null,
            [new("bank-statement", "Bank statement", null, true, DocumentRequirementWave.Normal, 0)]));

        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var context = Db();
            try
            {
                return (Success: true, Version: (int?)((await new DocumentRequirementTemplateService(context).CreateNewVersionAsync(seed.Id)).TemplateVersion));
            }
            catch (BusinessException)
            {
                return (Success: false, Version: (int?)null);
            }
        }).ToArray();
        var results = await Task.WhenAll(attempts);

        var versions = await db.DocumentRequirementTemplates
            .Where(x => x.ServiceId == serviceMaster.Id && x.TemplateKey == seed.TemplateKey)
            .OrderBy(x => x.TemplateVersion)
            .Select(x => x.TemplateVersion)
            .ToListAsync();
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(results.Count(x => x.Success) + 1, versions.Count);
        Assert.InRange(results.Count(x => x.Success), 1, 2);
        Assert.Equal(
            versions.Skip(1),
            results.Where(x => x.Success).Select(x => x.Version!.Value).OrderBy(x => x));
    }

    [PostgresFact]
    public async Task TemplateService_GeneratesStableKeysAndAppendsRequirementsWithoutChangingOrder()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-generated-keys");
        var service = new DocumentRequirementTemplateService(db);

        var first = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            null,
            "Yearly Bookkeeping - Standard Documents",
            "Reusable yearly checklist",
            [
                new(null, "Bank Statements", "All accounts", true, DocumentRequirementWave.StartWork, 99),
                new(null, "Bank Statements", "Duplicate name", false, DocumentRequirementWave.Normal, 42)
            ]));

        Assert.Equal("yearly-bookkeeping-standard-documents", first.TemplateKey);
        Assert.Equal(["bank-statements", "bank-statements-2"], first.Items.Select(x => x.RequirementKey).ToArray());
        Assert.Equal([0, 1], first.Items.Select(x => x.DisplayOrder).ToArray());

        var added = await service.AddItemAsync(first.Id,
            new(null, "Bank Statements", "Appended duplicate", false, DocumentRequirementWave.Later, 999));
        Assert.Equal("bank-statements-3", added.RequirementKey);
        Assert.Equal(2, added.DisplayOrder);

        var changedItem = await service.UpdateItemAsync(first.Items[0].Id,
            new("Bank Statements - Revised", "Updated", true, DocumentRequirementWave.Normal, 999));
        Assert.Equal("bank-statements", changedItem.RequirementKey);
        Assert.Equal(0, changedItem.DisplayOrder);

        var second = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            null,
            "Yearly Bookkeeping - Standard Documents",
            null,
            [new(null, "Bank Statements", null, true, DocumentRequirementWave.Normal, 12)]));
        Assert.Equal("yearly-bookkeeping-standard-documents-2", second.TemplateKey);

        var otherServiceMaster = await AddDocumentServiceAsync(db, "template-service-generated-keys-other");
        var otherService = await service.CreateFirstVersionAsync(new(
            otherServiceMaster.Id,
            null,
            "Yearly Bookkeeping - Standard Documents",
            null,
            [new(null, "Bank Statements", null, true, DocumentRequirementWave.Normal, 12)]));
        Assert.Equal("yearly-bookkeeping-standard-documents", otherService.TemplateKey);

        var renamed = await service.UpdateUnusedVersionAsync(first.Id, new("Renamed checklist", null));
        Assert.Equal("yearly-bookkeeping-standard-documents", renamed.TemplateKey);
        var cloned = await service.CreateNewVersionAsync(first.Id);
        Assert.Equal(first.TemplateKey, cloned.TemplateKey);
        Assert.Equal(
            (await service.GetAsync(first.Id))!.Items.Select(x => x.RequirementKey),
            cloned.Items.Select(x => x.RequirementKey));
    }

    [PostgresFact]
    public async Task TemplateService_ConcurrentChecklistCreationAllocatesDistinctGeneratedKeys()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-generated-concurrency");

        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var context = Db();
            try
            {
                var created = await new DocumentRequirementTemplateService(context).CreateFirstVersionAsync(new(
                    serviceMaster.Id,
                    null,
                    "Concurrent Checklist",
                    null,
                    [new(null, "Bank Statements", null, true, DocumentRequirementWave.Normal, 0)]));
                return (Success: true, Key: (string?)created.TemplateKey);
            }
            catch (BusinessException)
            {
                return (Success: false, Key: (string?)null);
            }
        }).ToArray();

        var results = await Task.WhenAll(attempts);
        var keys = await db.DocumentRequirementTemplates
            .Where(x => x.ServiceId == serviceMaster.Id)
            .Select(x => x.TemplateKey)
            .ToListAsync();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(results.Count(x => x.Success), keys.Count);
        Assert.Contains("concurrent-checklist", keys);
        if (results.Count(x => x.Success) == 2)
            Assert.Contains("concurrent-checklist-2", keys);
    }

    [PostgresFact]
    public async Task TemplateService_DoesNotChangeFinancialOrWorkflowState()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "template-service-boundary");
        var before = new
        {
            BillingRecords = await db.BillingRecords.CountAsync(),
            WorkItems = await db.WorkItems.CountAsync(),
            WorkerAssignments = await db.WorkerAssignments.CountAsync()
        };
        var service = new DocumentRequirementTemplateService(db);
        var template = await service.CreateFirstVersionAsync(new(
            serviceMaster.Id,
            DocumentToken("boundary-template"),
            "Boundary template",
            null,
            [new("bank-statement", "Bank statement", null, true, DocumentRequirementWave.Normal, 0)]));
        await service.CreateNewVersionAsync(template.Id);
        await service.SetTemplateActiveAsync(template.Id, false);

        Assert.Equal(before.BillingRecords, await db.BillingRecords.CountAsync());
        Assert.Equal(before.WorkItems, await db.WorkItems.CountAsync());
        Assert.Equal(before.WorkerAssignments, await db.WorkerAssignments.CountAsync());
    }
}
