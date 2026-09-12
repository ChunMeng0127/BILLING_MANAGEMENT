using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static BusinessClock RequestTestClock() => new(
        TimeProvider.System,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BusinessTimeZone"] = "UTC" })
            .Build());

    private static async Task<DocumentRequirementTemplate> AddRequestTemplateAsync(
        AppDbContext db,
        int serviceId,
        string key,
        bool isActive = true,
        bool isDefault = false)
    {
        var template = await AddDocumentTemplateAsync(db, serviceId, key, 1, isActive, isDefault);
        var start = await AddDocumentTemplateItemAsync(db, template.Id, $"{key}-start");
        start.Name = "Trial balance";
        start.Description = "Latest trial balance";
        start.IsRequired = true;
        start.Wave = DocumentRequirementWave.StartWork;
        start.DisplayOrder = 0;

        var optional = await AddDocumentTemplateItemAsync(db, template.Id, $"{key}-optional");
        optional.Name = "Bank statement";
        optional.Description = "Latest bank statement";
        optional.IsRequired = false;
        optional.Wave = DocumentRequirementWave.Optional;
        optional.DisplayOrder = 1;

        var inactive = await AddDocumentTemplateItemAsync(db, template.Id, $"{key}-inactive");
        inactive.Name = "Inactive requirement";
        inactive.IsActive = false;
        inactive.DisplayOrder = 2;
        await db.SaveChangesAsync();
        return template;
    }

    [PostgresFact]
    public async Task DocumentRequestService_CreatesDraftSnapshotsActiveItemsAndReadModels()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "request-create-service");
        var fixture = await AddDocumentFixtureAsync(db, "request-create", service.Id);
        var template = await AddRequestTemplateAsync(db, service.Id, "request-create-template", isDefault: true);
        var billingVersion = await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId).Select(x => x.Version).SingleAsync();
        var workItemVersion = await db.WorkItems.Where(x => x.Id == fixture.WorkItemId).Select(x => x.Version).SingleAsync();
        var assignmentCount = await db.WorkerAssignments.CountAsync();

        var created = await new DocumentRequestService(db, RequestTestClock()).CreateDraftAsync(
            new(fixture.WorkItemId, template.Id, "staff-1", "Phase3A.Test"));

        Assert.Equal(1, created.Revision);
        Assert.Equal(DocumentRequestStatus.Draft, created.Status);
        Assert.Equal(template.Id, created.DocumentRequirementTemplateId);
        Assert.Equal("request-create-template", created.TemplateKey);
        Assert.Equal(1, created.TemplateVersion);
        Assert.Equal(2, created.Items.Count);
        Assert.All(created.Items, item => Assert.Equal(DocumentRequestItemStatus.Missing, item.Status));
        Assert.Equal(new[] { "request-create-template-start", "request-create-template-optional" },
            created.Items.Select(x => x.RequirementKey));
        Assert.Equal(new[] { DocumentRequirementWave.StartWork, DocumentRequirementWave.Optional },
            created.Items.Select(x => x.Wave));
        Assert.All(created.Items, item => Assert.Single(item.StatusHistory));
        Assert.All(created.Items, item =>
        {
            var history = item.StatusHistory.Single();
            Assert.Null(history.PreviousStatus);
            Assert.Equal(DocumentRequestItemStatus.Missing, history.NewStatus);
            Assert.Equal("CreatedFromTemplate", history.Action);
            Assert.Equal("staff-1", history.Actor);
            Assert.Equal("Phase3A.Test", history.Source);
        });
        var requestHistory = Assert.Single(created.StatusHistory);
        Assert.Null(requestHistory.PreviousStatus);
        Assert.Equal(DocumentRequestStatus.Draft, requestHistory.NewStatus);
        Assert.Equal("CreatedFromTemplate", requestHistory.Action);
        Assert.Equal("staff-1", requestHistory.Actor);
        Assert.Equal("Phase3A.Test", requestHistory.Source);

        var current = await new DocumentRequestService(db, RequestTestClock()).GetCurrentForWorkItemAsync(fixture.WorkItemId);
        Assert.NotNull(current);
        Assert.Equal(created.Id, current!.Id);
        var revisions = await new DocumentRequestService(db, RequestTestClock()).GetRevisionsForWorkItemAsync(fixture.WorkItemId);
        Assert.Equal(new[] { created.Id }, revisions.Select(x => x.Id));

        var templates = await new DocumentRequestService(db, RequestTestClock()).GetAvailableTemplatesForWorkItemAsync(fixture.WorkItemId);
        var option = Assert.Single(templates);
        Assert.Equal(template.Id, option.Id);
        Assert.True(option.IsDefault);
        Assert.True(option.IsActive);

        Assert.Equal(billingVersion, await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId).Select(x => x.Version).SingleAsync());
        Assert.Equal(workItemVersion, await db.WorkItems.Where(x => x.Id == fixture.WorkItemId).Select(x => x.Version).SingleAsync());
        Assert.Equal(assignmentCount, await db.WorkerAssignments.CountAsync());
        Assert.Equal(0, await db.DocumentRequestItems.CountAsync(x => x.RequirementKey == "request-create-template-inactive"));
    }

    [PostgresFact]
    public async Task DocumentRequestService_ValidatesTemplateServiceActivityCurrentRequestsAndRevisions()
    {
        await using var db = await Fresh();
        var serviceA = await AddDocumentServiceAsync(db, "request-validation-a");
        var serviceB = await AddDocumentServiceAsync(db, "request-validation-b");
        var fixtureA = await AddDocumentFixtureAsync(db, "request-validation-a", serviceA.Id);
        var fixtureB = await AddDocumentFixtureAsync(db, "request-validation-b", serviceB.Id);
        var active = await AddRequestTemplateAsync(db, serviceA.Id, "request-valid-template");
        var inactive = await AddRequestTemplateAsync(db, serviceA.Id, "request-inactive-template", isActive: false);
        var empty = await AddDocumentTemplateAsync(db, serviceA.Id, "request-empty-template", 1);
        var emptyItem = await AddDocumentTemplateItemAsync(db, empty.Id, "request-empty-item");
        emptyItem.IsActive = false;
        await db.SaveChangesAsync();
        var foreign = await AddRequestTemplateAsync(db, serviceB.Id, "request-foreign-template");
        var requestService = new DocumentRequestService(db, RequestTestClock());

        await Assert.ThrowsAsync<BusinessException>(() => requestService.CreateDraftAsync(
            new(fixtureA.WorkItemId, foreign.Id, "staff", "test")));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.CreateDraftAsync(
            new(fixtureA.WorkItemId, inactive.Id, "staff", "test")));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.CreateDraftAsync(
            new(fixtureA.WorkItemId, empty.Id, "staff", "test")));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.CreateDraftAsync(
            new(fixtureB.WorkItemId, active.Id, "staff", "test")));

        var first = await requestService.CreateDraftAsync(new(fixtureA.WorkItemId, active.Id, "staff", "test"));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.CreateDraftAsync(
            new(fixtureA.WorkItemId, active.Id, "staff", "test")));

        var cancelled = await requestService.TransitionAsync(first.Id,
            new(DocumentRequestStatus.Cancelled, first.Version, "staff", "test", "No longer needed"));
        var second = await requestService.CreateDraftAsync(new(fixtureA.WorkItemId, active.Id, "staff", "test"));
        Assert.Equal(DocumentRequestStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, second.Revision);

        var history = await requestService.GetRevisionsForWorkItemAsync(fixtureA.WorkItemId);
        Assert.Equal(new[] { 1, 2 }, history.Select(x => x.Revision));
        Assert.Equal(new[] { DocumentRequestStatus.Cancelled, DocumentRequestStatus.Draft }, history.Select(x => x.Status));
        Assert.Null(await requestService.GetCurrentForWorkItemAsync(fixtureB.WorkItemId));
    }

    [PostgresFact]
    public async Task DocumentRequestService_EnforcesPreProviderRequestAndChecklistTransitions()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "request-transition-service");
        var fixture = await AddDocumentFixtureAsync(db, "request-transition", service.Id);
        var template = await AddRequestTemplateAsync(db, service.Id, "request-transition-template");
        var requestService = new DocumentRequestService(db, RequestTestClock());
        var request = await requestService.CreateDraftAsync(new(fixture.WorkItemId, template.Id, "staff", "test"));
        var item = request.Items.Single(x => x.RequirementKey == "request-transition-template-start");

        var paused = await requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Paused, request.Version, "staff", "test", "Wait for client confirmation"));
        var draft = await requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Draft, paused.Version, "staff", "test", "Resume internal preparation"));
        var ready = await requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.ReadyToSend, draft.Version, "staff", "test", "Checklist reviewed"));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Requested, ready.Version, "staff", "test", "Pretend provider accepted")));
        var pausedAgain = await requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Paused, ready.Version, "staff", "test", "Pause before provider send"));
        var readyAgain = await requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.ReadyToSend, pausedAgain.Version, "staff", "test", "Resume before provider send"));

        await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.PartiallyReceived, readyAgain.Version, "staff", "test", "Not yet supported")));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Complete, readyAgain.Version, "staff", "test", "Not yet supported")));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Superseded, readyAgain.Version, "staff", "test", "Not yet supported")));
        await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Draft, readyAgain.Version - 1, "staff", "test", "Stale version")));

        var notRequired = await requestService.TransitionItemAsync(item.Id,
            new(DocumentRequestItemStatus.NotRequired, item.Version, "staff", "test", "Not applicable for this period"));
        var missing = await requestService.TransitionItemAsync(item.Id,
            new(DocumentRequestItemStatus.Missing, notRequired.Version, "staff", "test", "Reactivate requirement"));
        var waived = await requestService.TransitionItemAsync(item.Id,
            new(DocumentRequestItemStatus.Waived, missing.Version, "staff", "test", "Approved waiver"));
        var missingAgain = await requestService.TransitionItemAsync(item.Id,
            new(DocumentRequestItemStatus.Missing, waived.Version, "staff", "test", "Reopen requirement"));
        Assert.Equal(DocumentRequestItemStatus.Missing, missingAgain.Status);
        Assert.Equal(DocumentRequestStatus.ReadyToSend,
            (await requestService.GetByIdAsync(request.Id))!.Status);

        foreach (var unsupported in new[]
        {
            DocumentRequestItemStatus.Requested,
            DocumentRequestItemStatus.PartiallyReceived,
            DocumentRequestItemStatus.Received
        })
        {
            await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionItemAsync(item.Id,
                new(unsupported, missingAgain.Version, "staff", "test", "Evidence operation is deferred")));
        }

        var cancelled = await requestService.TransitionAsync(request.Id,
            new(DocumentRequestStatus.Cancelled, readyAgain.Version, "staff", "test", "Cancel request"));
        Assert.Equal(DocumentRequestStatus.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<BusinessException>(() => requestService.TransitionItemAsync(item.Id,
            new(DocumentRequestItemStatus.NotRequired, missingAgain.Version, "staff", "test", "Cancelled request")));

        var histories = await requestService.GetByIdAsync(request.Id);
        Assert.NotNull(histories);
        Assert.Equal(7, histories!.StatusHistory.Count);
        Assert.Equal(5, histories.Items.Single().StatusHistory.Count);
        Assert.All(histories.StatusHistory, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Actor));
            Assert.False(string.IsNullOrWhiteSpace(x.Source));
            if (x.PreviousStatus is not null) Assert.False(string.IsNullOrWhiteSpace(x.Reason));
        });
    }

    [PostgresFact]
    public async Task DocumentRequestService_SerializesConcurrentCreationAndKeepsFinancialScopeUntouched()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "request-concurrency-service");
        var fixture = await AddDocumentFixtureAsync(db, "request-concurrency", service.Id);
        var template = await AddRequestTemplateAsync(db, service.Id, "request-concurrency-template");
        var beforeBillingVersion = await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId).Select(x => x.Version).SingleAsync();
        var beforeWorkItemVersion = await db.WorkItems.Where(x => x.Id == fixture.WorkItemId).Select(x => x.Version).SingleAsync();
        var beforeAssignments = await db.WorkerAssignments.CountAsync(x => x.WorkItemId == fixture.WorkItemId);

        async Task<bool> Attempt(AppDbContext context)
        {
            try
            {
                await new DocumentRequestService(context, RequestTestClock()).CreateDraftAsync(
                    new(fixture.WorkItemId, template.Id, "staff", "concurrency-test"));
                return true;
            }
            catch (BusinessException)
            {
                return false;
            }
        }

        await using var first = Db();
        await using var second = Db();
        var results = await Task.WhenAll(Attempt(first), Attempt(second));

        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, await db.DocumentRequests.CountAsync(x => x.WorkItemId == fixture.WorkItemId));
        Assert.Equal(1, await db.DocumentRequests.CountAsync(x => x.WorkItemId == fixture.WorkItemId && x.Revision == 1));
        Assert.Equal(beforeBillingVersion, await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId).Select(x => x.Version).SingleAsync());
        Assert.Equal(beforeWorkItemVersion, await db.WorkItems.Where(x => x.Id == fixture.WorkItemId).Select(x => x.Version).SingleAsync());
        Assert.Equal(beforeAssignments, await db.WorkerAssignments.CountAsync(x => x.WorkItemId == fixture.WorkItemId));
    }

    [PostgresFact]
    public async Task DocumentRequestService_AvailableTemplatesAreServiceScopedAndDefaultFirst()
    {
        await using var db = await Fresh();
        var serviceA = await AddDocumentServiceAsync(db, "request-options-a");
        var serviceB = await AddDocumentServiceAsync(db, "request-options-b");
        var fixture = await AddDocumentFixtureAsync(db, "request-options", serviceA.Id);
        var defaultTemplate = await AddRequestTemplateAsync(db, serviceA.Id, "default-family", isDefault: true);
        var otherFamily = await AddRequestTemplateAsync(db, serviceA.Id, "other-family");
        var inactive = await AddRequestTemplateAsync(db, serviceA.Id, "inactive-family", isActive: false);
        var foreign = await AddRequestTemplateAsync(db, serviceB.Id, "foreign-family");
        var requestService = new DocumentRequestService(db, RequestTestClock());

        var options = await requestService.GetAvailableTemplatesForWorkItemAsync(fixture.WorkItemId);

        Assert.Equal(new[] { defaultTemplate.Id, otherFamily.Id }, options.Select(x => x.Id));
        Assert.True(options[0].IsDefault);
        Assert.False(options[1].IsDefault);
        Assert.DoesNotContain(options, x => x.Id == inactive.Id);
        Assert.DoesNotContain(options, x => x.Id == foreign.Id);
    }
}
