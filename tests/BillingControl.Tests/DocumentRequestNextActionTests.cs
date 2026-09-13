using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static async Task<DocumentRequestItem> AddNextActionItemAsync(
        AppDbContext db,
        int requestId,
        string name,
        int displayOrder,
        DocumentRequestItemStatus status = DocumentRequestItemStatus.Missing,
        bool required = true,
        DocumentRequirementWave wave = DocumentRequirementWave.Normal)
    {
        var item = new DocumentRequestItem
        {
            DocumentRequestId = requestId,
            RequirementKey = $"phase6-{Guid.NewGuid():N}",
            RequirementName = name,
            RequirementDescription = $"Description for {name}",
            IsRequired = required,
            Wave = wave,
            DisplayOrder = displayOrder,
            Status = status
        };
        db.DocumentRequestItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_NextActionItemsUseDisplayOrderAndExcludeResolvedItems()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "phase6-order");
        var template = await AddDocumentTemplateAsync(db, serviceMaster.Id, "phase6-order-template", 1);
        var fixture = await AddDocumentFixtureAsync(db, "phase6-order", serviceMaster.Id);
        var customerId = await CustomerIdForAsync(db, fixture.EngagementId);
        await db.Customers.Where(x => x.Id == customerId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Phase 6 Customer"));
        var request = await AddDocumentRequestAsync(
            db,
            fixture.WorkItemId,
            template.Id,
            status: DocumentRequestStatus.ReadyToSend);

        var resolvedReceived = await AddNextActionItemAsync(
            db, request.Id, "Already received", 0, DocumentRequestItemStatus.Received,
            wave: DocumentRequirementWave.StartWork);
        var resolvedNotRequired = await AddNextActionItemAsync(
            db, request.Id, "Not applicable", 1, DocumentRequestItemStatus.NotRequired,
            wave: DocumentRequirementWave.StartWork);
        var resolvedWaived = await AddNextActionItemAsync(
            db, request.Id, "Waived item", 2, DocumentRequestItemStatus.Waived,
            wave: DocumentRequirementWave.StartWork);
        var laterButFirst = await AddNextActionItemAsync(
            db, request.Id, "Later wave, first display order", 3,
            required: false, wave: DocumentRequirementWave.Later);
        var normalItem = await AddNextActionItemAsync(
            db, request.Id, "Normal item", 7, DocumentRequestItemStatus.PartiallyReceived,
            wave: DocumentRequirementWave.Normal);
        var startWorkButLater = await AddNextActionItemAsync(
            db, request.Id, "Start work wave, later display order", 99,
            status: DocumentRequestItemStatus.Requested,
            wave: DocumentRequirementWave.StartWork);

        var contact = await AddBatchContactAsync(db, "Phase 6 Order PIC", (customerId, true));
        var batchService = new DocumentRequestBatchService(db);
        var firstOnly = await batchService.GetNextActionItemsAsync(contact.Id, 1);
        var allAvailable = await batchService.GetNextActionItemsAsync(contact.Id);

        var first = Assert.Single(firstOnly);
        Assert.Equal(request.Id, first.DocumentRequestId);
        Assert.Equal(request.Revision, first.Revision);
        Assert.Equal(laterButFirst.Id, first.DocumentRequestItemId);
        Assert.Equal(customerId, first.CustomerId);
        Assert.Equal("Phase 6 Customer", first.CustomerName);
        Assert.Equal(serviceMaster.Id, first.ServiceId);
        Assert.Equal(serviceMaster.Name, first.ServiceName);
        Assert.Equal("Later wave, first display order", first.RequirementName);
        Assert.False(first.Required);
        Assert.False(first.IsRequired);
        Assert.Equal(3, first.DisplayOrder);
        Assert.Equal(DocumentRequestItemStatus.Missing, first.Status);

        Assert.Equal(
            new[] { laterButFirst.Id, normalItem.Id, startWorkButLater.Id },
            allAvailable.Select(x => x.DocumentRequestItemId));
        Assert.Equal(
            new[]
            {
                DocumentRequestItemStatus.Missing,
                DocumentRequestItemStatus.PartiallyReceived,
                DocumentRequestItemStatus.Requested
            },
            allAvailable.Select(x => x.Status));
        Assert.DoesNotContain(allAvailable, x => x.DocumentRequestItemId == resolvedReceived.Id);
        Assert.DoesNotContain(allAvailable, x => x.DocumentRequestItemId == resolvedNotRequired.Id);
        Assert.DoesNotContain(allAvailable, x => x.DocumentRequestItemId == resolvedWaived.Id);
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_NextActionItemsHonorDefaultBoundedLimitAndFewerAvailable()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "phase6-limit");
        var template = await AddDocumentTemplateAsync(db, serviceMaster.Id, "phase6-limit-template", 1);
        var fixture = await AddDocumentFixtureAsync(db, "phase6-limit", serviceMaster.Id);
        var customerId = await CustomerIdForAsync(db, fixture.EngagementId);
        var request = await AddDocumentRequestAsync(
            db,
            fixture.WorkItemId,
            template.Id,
            status: DocumentRequestStatus.ReadyToSend);
        var items = new List<DocumentRequestItem>();
        for (var index = 0; index < 7; index++)
        {
            items.Add(await AddNextActionItemAsync(
                db, request.Id, $"Limit item {index}", index));
        }

        var contact = await AddBatchContactAsync(db, "Phase 6 Limit PIC", (customerId, true));
        var batchService = new DocumentRequestBatchService(db);

        foreach (var limit in Enumerable.Range(1, 5))
        {
            var selected = await batchService.GetNextActionItemsAsync(contact.Id, limit);
            Assert.Equal(limit, selected.Count);
            Assert.Equal(items.Take(limit).Select(x => x.Id), selected.Select(x => x.DocumentRequestItemId));
        }

        var defaultSelected = await batchService.GetNextActionItemsAsync(contact.Id);
        Assert.Equal(5, defaultSelected.Count);
        Assert.Equal(items.Take(5).Select(x => x.Id), defaultSelected.Select(x => x.DocumentRequestItemId));

        await db.DocumentRequestItems
            .Where(x => x.DocumentRequestId == request.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Status, DocumentRequestItemStatus.Received));
        var fewerThanLimit = await batchService.GetNextActionItemsAsync(contact.Id);
        Assert.Empty(fewerThanLimit);

        var zero = await Assert.ThrowsAsync<BusinessException>(() =>
            batchService.GetNextActionItemsAsync(contact.Id, 0));
        Assert.Contains("between 1 and 5", zero.Message, StringComparison.OrdinalIgnoreCase);
        var six = await Assert.ThrowsAsync<BusinessException>(() =>
            batchService.GetNextActionItemsAsync(contact.Id, 6));
        Assert.Contains("between 1 and 5", six.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_NextActionItemsOrderMultipleCustomersAndRequestsDeterministically()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "phase6-deterministic");
        var template = await AddDocumentTemplateAsync(db, serviceMaster.Id, "phase6-deterministic-template", 1);
        var firstFixture = await AddDocumentFixtureAsync(db, "phase6-deterministic-alpha-first", serviceMaster.Id);
        var secondFixture = await AddDocumentFixtureAsync(db, "phase6-deterministic-alpha-second", serviceMaster.Id);
        var betaFixture = await AddDocumentFixtureAsync(db, "phase6-deterministic-beta", serviceMaster.Id);
        var alphaCustomerId = await CustomerIdForAsync(db, firstFixture.EngagementId);
        var secondCustomerId = await CustomerIdForAsync(db, secondFixture.EngagementId);
        var betaCustomerId = await CustomerIdForAsync(db, betaFixture.EngagementId);

        await db.Engagements.Where(x => x.Id == secondFixture.EngagementId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.CustomerId, alphaCustomerId));
        await db.Customers.Where(x => x.Id == alphaCustomerId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Alpha Customer"));
        await db.Customers.Where(x => x.Id == betaCustomerId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Beta Customer"));
        db.ChangeTracker.Clear();

        var firstRequest = await AddDocumentRequestAsync(
            db, firstFixture.WorkItemId, template.Id, status: DocumentRequestStatus.ReadyToSend);
        var secondRequest = await AddDocumentRequestAsync(
            db, secondFixture.WorkItemId, template.Id, status: DocumentRequestStatus.ReadyToSend);
        var betaRequest = await AddDocumentRequestAsync(
            db, betaFixture.WorkItemId, template.Id, status: DocumentRequestStatus.ReadyToSend);
        var firstItem = await AddNextActionItemAsync(db, firstRequest.Id, "Alpha first item", 2);
        var firstTieItem = await AddNextActionItemAsync(db, firstRequest.Id, "Alpha first tie item", 2);
        var secondItem = await AddNextActionItemAsync(db, secondRequest.Id, "Alpha second request item", 2);
        var betaItem = await AddNextActionItemAsync(db, betaRequest.Id, "Beta item", 2);

        var contact = await AddBatchContactAsync(
            db,
            "Phase 6 Deterministic PIC",
            (alphaCustomerId, true),
            (betaCustomerId, true));
        var batchService = new DocumentRequestBatchService(db);

        var firstCall = await batchService.GetNextActionItemsAsync(contact.Id, 5);
        var secondCall = await batchService.GetNextActionItemsAsync(contact.Id, 5);

        Assert.Equal(
            new[] { firstItem.Id, firstTieItem.Id, secondItem.Id, betaItem.Id },
            firstCall.Select(x => x.DocumentRequestItemId));
        Assert.Equal(firstCall, secondCall);
        Assert.Equal(new[] { alphaCustomerId, alphaCustomerId, alphaCustomerId, betaCustomerId }, firstCall.Select(x => x.CustomerId));
        Assert.Equal(new[] { firstRequest.Id, firstRequest.Id, secondRequest.Id, betaRequest.Id }, firstCall.Select(x => x.DocumentRequestId));
        Assert.Equal(new[] { 2, 2, 2, 2 }, firstCall.Select(x => x.DisplayOrder));
        Assert.DoesNotContain(firstCall, x => x.CustomerId == secondCustomerId);
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_NextActionItemsUseCurrentPhase5AEligibility()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "phase6-eligibility");
        var template = await AddDocumentTemplateAsync(db, serviceMaster.Id, "phase6-eligibility-template", 1);
        var currentFixture = await AddDocumentFixtureAsync(db, "phase6-current", serviceMaster.Id);
        var inactiveFixture = await AddDocumentFixtureAsync(db, "phase6-inactive", serviceMaster.Id);
        var currentCustomerId = await CustomerIdForAsync(db, currentFixture.EngagementId);
        var inactiveCustomerId = await CustomerIdForAsync(db, inactiveFixture.EngagementId);
        var historical = await AddDocumentRequestAsync(
            db, currentFixture.WorkItemId, template.Id, revision: 1, status: DocumentRequestStatus.Superseded);
        var current = await AddDocumentRequestAsync(
            db, currentFixture.WorkItemId, template.Id, revision: 2,
            status: DocumentRequestStatus.ReadyToSend, supersedesRequestId: historical.Id);
        var inactive = await AddDocumentRequestAsync(
            db, inactiveFixture.WorkItemId, template.Id, status: DocumentRequestStatus.ReadyToSend);
        var historicalItem = await AddNextActionItemAsync(db, historical.Id, "Historical item", 0);
        var currentItem = await AddNextActionItemAsync(db, current.Id, "Current item", 1);
        var inactiveItem = await AddNextActionItemAsync(db, inactive.Id, "Inactive link item", 0);
        var contact = await AddBatchContactAsync(
            db,
            "Phase 6 Eligibility PIC",
            (currentCustomerId, true),
            (inactiveCustomerId, false));

        var selected = await new DocumentRequestBatchService(db).GetNextActionItemsAsync(contact.Id);

        var item = Assert.Single(selected);
        Assert.Equal(current.Id, item.DocumentRequestId);
        Assert.Equal(2, item.Revision);
        Assert.Equal(currentItem.Id, item.DocumentRequestItemId);
        Assert.DoesNotContain(selected, x => x.DocumentRequestItemId == historicalItem.Id);
        Assert.DoesNotContain(selected, x => x.DocumentRequestItemId == inactiveItem.Id);
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_NextActionItemsReadWithoutDomainMutation()
    {
        await using var db = await Fresh();
        var serviceMaster = await AddDocumentServiceAsync(db, "phase6-read-only");
        var template = await AddDocumentTemplateAsync(db, serviceMaster.Id, "phase6-read-only-template", 1);
        var fixture = await AddDocumentFixtureAsync(db, "phase6-read-only", serviceMaster.Id);
        var customerId = await CustomerIdForAsync(db, fixture.EngagementId);
        var request = await AddDocumentRequestAsync(
            db, fixture.WorkItemId, template.Id, status: DocumentRequestStatus.ReadyToSend);
        var item = await AddNextActionItemAsync(db, request.Id, "Read-only item", 0);
        var worker = new Worker { Name = "Phase 6 Worker" };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        var assignment = new WorkerAssignment
        {
            WorkItemId = fixture.WorkItemId,
            WorkerId = worker.Id,
            WorkerName = worker.Name,
            Percent = 100m,
            LcmGrossSnapshot = 1000m,
            Entitlement = 1000m,
            CurrentWorkflowStatus = WorkflowStatus.AssignedNotStarted,
            CurrentProgressPercent = 0m
        };
        db.WorkerAssignments.Add(assignment);
        await db.SaveChangesAsync();
        var contact = await AddBatchContactAsync(db, "Phase 6 Read-only PIC", (customerId, true));
        db.ChangeTracker.Clear();

        var before = new
        {
            Batches = await db.DocumentRequestBatches.CountAsync(),
            Members = await db.DocumentRequestBatchMembers.CountAsync(),
            Request = await db.DocumentRequests.Where(x => x.Id == request.Id)
                .Select(x => new { x.Status, x.Revision, x.Version }).SingleAsync(),
            Item = await db.DocumentRequestItems.Where(x => x.Id == item.Id)
                .Select(x => new { x.Status, x.Version }).SingleAsync(),
            WorkItem = await db.WorkItems.Where(x => x.Id == fixture.WorkItemId)
                .Select(x => new { x.Status, x.Version }).SingleAsync(),
            Billing = await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId)
                .Select(x => new { x.Status, x.Amount, x.Version }).SingleAsync(),
            Assignment = await db.WorkerAssignments.Where(x => x.Id == assignment.Id)
                .Select(x => new { x.CurrentWorkflowStatus, x.CurrentProgressPercent, x.Version }).SingleAsync()
        };

        var selected = await new DocumentRequestBatchService(db).GetNextActionItemsAsync(contact.Id);

        Assert.Single(selected);
        db.ChangeTracker.Clear();
        Assert.Equal(before.Batches, await db.DocumentRequestBatches.CountAsync());
        Assert.Equal(before.Members, await db.DocumentRequestBatchMembers.CountAsync());
        Assert.Equal(before.Request, await db.DocumentRequests.Where(x => x.Id == request.Id)
            .Select(x => new { x.Status, x.Revision, x.Version }).SingleAsync());
        Assert.Equal(before.Item, await db.DocumentRequestItems.Where(x => x.Id == item.Id)
            .Select(x => new { x.Status, x.Version }).SingleAsync());
        Assert.Equal(before.WorkItem, await db.WorkItems.Where(x => x.Id == fixture.WorkItemId)
            .Select(x => new { x.Status, x.Version }).SingleAsync());
        Assert.Equal(before.Billing, await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId)
            .Select(x => new { x.Status, x.Amount, x.Version }).SingleAsync());
        Assert.Equal(before.Assignment, await db.WorkerAssignments.Where(x => x.Id == assignment.Id)
            .Select(x => new { x.CurrentWorkflowStatus, x.CurrentProgressPercent, x.Version }).SingleAsync());
    }
}
