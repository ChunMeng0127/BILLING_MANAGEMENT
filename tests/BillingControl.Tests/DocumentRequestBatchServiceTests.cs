using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static async Task<Contact> AddBatchContactAsync(
        AppDbContext db,
        string name,
        params (int CustomerId, bool IsActive)[] customerLinks)
    {
        var contact = new Contact { Name = name, IsActive = true };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        db.ContactCustomerLinks.AddRange(customerLinks.Select(link => new ContactCustomerLink
        {
            ContactId = contact.Id,
            CustomerId = link.CustomerId,
            IsActive = link.IsActive,
            EffectiveFrom = new(2026, 1, 1),
            EffectiveTo = link.IsActive ? null : new DateOnly(2026, 9, 1)
        }));
        await db.SaveChangesAsync();
        return contact;
    }

    private static async Task<int> CustomerIdForAsync(AppDbContext db, int engagementId) =>
        await db.Engagements
            .Where(x => x.Id == engagementId)
            .Select(x => x.CustomerId)
            .SingleAsync();

    private static async Task<DocumentRequest> AddReadyBatchRequestAsync(
        AppDbContext db,
        DocumentFixture fixture,
        int templateId,
        int templateItemId,
        int revision = 1,
        DocumentRequestStatus status = DocumentRequestStatus.ReadyToSend,
        int? supersedesRequestId = null)
    {
        var request = await AddDocumentRequestAsync(
            db,
            fixture.WorkItemId,
            templateId,
            revision,
            status,
            supersedesRequestId);
        await AddDocumentRequestItemAsync(db, request.Id, templateItemId);
        return request;
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_ReturnsOnlyCurrentReadyRequestsForActiveCustomerLinks()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "batch-candidates");
        var template = await AddDocumentTemplateAsync(db, service.Id, "batch-candidates-template", 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "batch-candidates-item");
        var alphaFixture = await AddDocumentFixtureAsync(db, "batch-alpha", service.Id);
        var betaFixture = await AddDocumentFixtureAsync(db, "batch-beta", service.Id);
        var unlinkedFixture = await AddDocumentFixtureAsync(db, "batch-unlinked", service.Id);
        var inactiveLinkFixture = await AddDocumentFixtureAsync(db, "batch-inactive-link", service.Id);

        var alphaCustomerId = await CustomerIdForAsync(db, alphaFixture.EngagementId);
        var betaCustomerId = await CustomerIdForAsync(db, betaFixture.EngagementId);
        var unlinkedCustomerId = await CustomerIdForAsync(db, unlinkedFixture.EngagementId);
        var inactiveLinkCustomerId = await CustomerIdForAsync(db, inactiveLinkFixture.EngagementId);
        await db.Customers.Where(x => x.Id == alphaCustomerId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Alpha Customer"));
        await db.Customers.Where(x => x.Id == betaCustomerId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Beta Customer"));
        await db.Customers.Where(x => x.Id == unlinkedCustomerId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Unlinked Customer"));
        await db.Customers.Where(x => x.Id == inactiveLinkCustomerId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Inactive Link Customer"));

        var alpha = await AddReadyBatchRequestAsync(db, alphaFixture, template.Id, templateItem.Id);
        var beta = await AddReadyBatchRequestAsync(db, betaFixture, template.Id, templateItem.Id);
        var unlinked = await AddReadyBatchRequestAsync(db, unlinkedFixture, template.Id, templateItem.Id);
        var inactiveLink = await AddReadyBatchRequestAsync(db, inactiveLinkFixture, template.Id, templateItem.Id);
        var contact = await AddBatchContactAsync(
            db,
            "Alice PIC",
            (alphaCustomerId, true),
            (betaCustomerId, true),
            (inactiveLinkCustomerId, false));

        var candidates = await new DocumentRequestBatchService(db).GetCandidateRequestsAsync(contact.Id);

        Assert.Equal(new[] { alpha.Id, beta.Id }, candidates.Select(x => x.DocumentRequestId));
        Assert.Equal(new[] { "Alpha Customer", "Beta Customer" }, candidates.Select(x => x.CustomerName));
        Assert.DoesNotContain(candidates, x => x.DocumentRequestId == unlinked.Id);
        Assert.DoesNotContain(candidates, x => x.DocumentRequestId == inactiveLink.Id);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(1, candidate.Revision);
            Assert.Equal(DocumentRequestStatus.ReadyToSend, candidate.Status);
            Assert.NotEqual(0, candidate.EngagementId);
            Assert.Equal(service.Id, candidate.ServiceId);
            Assert.Equal(service.Name, candidate.ServiceName);
        });
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_ExcludesDraftFollowUpTerminalAndHistoricalRequests()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "batch-statuses");
        var template = await AddDocumentTemplateAsync(db, service.Id, "batch-statuses-template", 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "batch-statuses-item");
        var fixtures = new Dictionary<DocumentRequestStatus, DocumentFixture>();
        foreach (var status in new[]
        {
            DocumentRequestStatus.Draft,
            DocumentRequestStatus.Paused,
            DocumentRequestStatus.Requested,
            DocumentRequestStatus.PartiallyReceived,
            DocumentRequestStatus.Cancelled,
            DocumentRequestStatus.Superseded,
            DocumentRequestStatus.Complete
        })
        {
            fixtures[status] = await AddDocumentFixtureAsync(db, $"batch-{status}", service.Id);
        }

        var requests = new Dictionary<DocumentRequestStatus, DocumentRequest>();
        foreach (var pair in fixtures)
        {
            requests[pair.Key] = await AddReadyBatchRequestAsync(
                db,
                pair.Value,
                template.Id,
                templateItem.Id,
                status: pair.Key);
        }

        var historicalFixture = await AddDocumentFixtureAsync(db, "batch-historical", service.Id);
        var historicalCustomerId = await CustomerIdForAsync(db, historicalFixture.EngagementId);
        var historical = await AddDocumentRequestAsync(
            db,
            historicalFixture.WorkItemId,
            template.Id,
            revision: 1,
            status: DocumentRequestStatus.Superseded);
        var current = await AddReadyBatchRequestAsync(
            db,
            historicalFixture,
            template.Id,
            templateItem.Id,
            revision: 2,
            supersedesRequestId: historical.Id);

        var linkedCustomerIds = new List<int>();
        foreach (var fixture in fixtures.Values)
            linkedCustomerIds.Add(await CustomerIdForAsync(db, fixture.EngagementId));
        var contact = await AddBatchContactAsync(
            db,
            "Status PIC",
            linkedCustomerIds.Select(id => (id, true)).Append((historicalCustomerId, true)).ToArray());

        var candidates = await new DocumentRequestBatchService(db).GetCandidateRequestsAsync(contact.Id);

        Assert.Equal(new[] { current.Id }, candidates.Select(x => x.DocumentRequestId));
        Assert.DoesNotContain(candidates, x => requests.Values.Any(request => request.Id == x.DocumentRequestId));
        Assert.DoesNotContain(candidates, x => x.DocumentRequestId == historical.Id);
        Assert.Equal(2, candidates.Single().Revision);
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_RejectsDuplicateForeignAndStaleSelectionsSafely()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "batch-selection");
        var template = await AddDocumentTemplateAsync(db, service.Id, "batch-selection-template", 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "batch-selection-item");
        var linkedFixture = await AddDocumentFixtureAsync(db, "batch-selection-linked", service.Id);
        var unlinkedFixture = await AddDocumentFixtureAsync(db, "batch-selection-unlinked", service.Id);
        var draftFixture = await AddDocumentFixtureAsync(db, "batch-selection-draft", service.Id);
        var pausedFixture = await AddDocumentFixtureAsync(db, "batch-selection-paused", service.Id);
        var terminalFixture = await AddDocumentFixtureAsync(db, "batch-selection-terminal", service.Id);
        var linkedCustomerId = await CustomerIdForAsync(db, linkedFixture.EngagementId);
        var unlinkedCustomerId = await CustomerIdForAsync(db, unlinkedFixture.EngagementId);
        var contact = await AddBatchContactAsync(db, "Selection PIC", (linkedCustomerId, true));
        var ready = await AddReadyBatchRequestAsync(db, linkedFixture, template.Id, templateItem.Id);
        var unlinked = await AddReadyBatchRequestAsync(db, unlinkedFixture, template.Id, templateItem.Id);
        var draft = await AddReadyBatchRequestAsync(db, draftFixture, template.Id, templateItem.Id, status: DocumentRequestStatus.Draft);
        var paused = await AddReadyBatchRequestAsync(db, pausedFixture, template.Id, templateItem.Id, status: DocumentRequestStatus.Paused);
        var terminal = await AddReadyBatchRequestAsync(db, terminalFixture, template.Id, templateItem.Id, status: DocumentRequestStatus.Complete);
        _ = unlinkedCustomerId;

        var batchService = new DocumentRequestBatchService(db);
        var duplicate = await Assert.ThrowsAsync<BusinessException>(() =>
            batchService.PreviewBatchAsync(contact.Id, new[] { ready.Id, ready.Id }));
        Assert.Contains("Duplicate", duplicate.Message);

        foreach (var invalidId in new[] { 999999, unlinked.Id, draft.Id, paused.Id, terminal.Id })
        {
            var invalid = await Assert.ThrowsAsync<BusinessException>(() =>
                batchService.PreviewBatchAsync(contact.Id, new[] { ready.Id, invalidId }));
            Assert.Contains("not found or are not currently eligible", invalid.Message);
            Assert.DoesNotContain("SELECT ", invalid.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DocumentRequests", invalid.Message, StringComparison.OrdinalIgnoreCase);
        }

        var historicalFixture = await AddDocumentFixtureAsync(db, "batch-selection-historical", service.Id);
        var historicalCustomerId = await CustomerIdForAsync(db, historicalFixture.EngagementId);
        var historicalLink = new ContactCustomerLink
        {
            ContactId = contact.Id,
            CustomerId = historicalCustomerId,
            IsActive = true,
            EffectiveFrom = new(2026, 1, 1)
        };
        db.ContactCustomerLinks.Add(historicalLink);
        await db.SaveChangesAsync();
        var historical = await AddDocumentRequestAsync(db, historicalFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Superseded);
        var current = await AddReadyBatchRequestAsync(db, historicalFixture, template.Id, templateItem.Id, 2, supersedesRequestId: historical.Id);
        var stale = await Assert.ThrowsAsync<BusinessException>(() =>
            batchService.PreviewBatchAsync(contact.Id, new[] { new DocumentBatchRequestSelection(current.Id, 1) }));
        Assert.Contains("stale", stale.Message, StringComparison.OrdinalIgnoreCase);

        var historicalSelection = await Assert.ThrowsAsync<BusinessException>(() =>
            batchService.PreviewBatchAsync(contact.Id, new[] { historical.Id }));
        Assert.Contains("not found or are not currently eligible", historicalSelection.Message);
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_PreviewReportsFactsAndLeavesAllDomainStateUnchanged()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "batch-preview");
        var template = await AddDocumentTemplateAsync(db, service.Id, "batch-preview-template", 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "batch-preview-item");
        var fixture = await AddDocumentFixtureAsync(db, "batch-preview", service.Id);
        var customerId = await CustomerIdForAsync(db, fixture.EngagementId);
        await db.Customers.Where(x => x.Id == customerId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Preview Customer"));
        var request = await AddDocumentRequestAsync(db, fixture.WorkItemId, template.Id, status: DocumentRequestStatus.ReadyToSend);
        var itemStatuses = new[]
        {
            DocumentRequestItemStatus.Missing,
            DocumentRequestItemStatus.Received,
            DocumentRequestItemStatus.NotRequired,
            DocumentRequestItemStatus.Waived,
            DocumentRequestItemStatus.PartiallyReceived,
            DocumentRequestItemStatus.Requested
        };
        db.DocumentRequestItems.AddRange(itemStatuses.Select((status, index) => new DocumentRequestItem
        {
            DocumentRequestId = request.Id,
            DocumentRequirementTemplateItemId = templateItem.Id,
            RequirementKey = $"batch-preview-item-{index}",
            RequirementName = $"Preview item {index}",
            RequirementDescription = "Preview test item",
            IsRequired = true,
            Wave = DocumentRequirementWave.Normal,
            DisplayOrder = index,
            Status = status
        }));
        await db.SaveChangesAsync();

        var contact = await AddBatchContactAsync(db, "Preview PIC", (customerId, true));
        db.ContactWhatsAppAddresses.AddRange(
            new ContactWhatsAppAddress
            {
                ContactId = contact.Id,
                NormalizedE164 = "+60123456781",
                IsPrimary = true,
                IsActive = true,
                ConsentState = ContactWhatsAppConsentState.OptedIn,
                ConsentRecordedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ConsentSource = "test",
                ConsentEvidenceReference = "test-opt-in"
            },
            new ContactWhatsAppAddress
            {
                ContactId = contact.Id,
                NormalizedE164 = "+60123456782",
                IsPrimary = false,
                IsActive = true,
                ConsentState = ContactWhatsAppConsentState.DoNotWhatsApp,
                ConsentRecordedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ConsentSource = "test",
                LastOptOutAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                LastOptOutReason = "test opt-out"
            },
            new ContactWhatsAppAddress
            {
                ContactId = contact.Id,
                NormalizedE164 = "+60123456783",
                IsPrimary = false,
                IsActive = false,
                ConsentState = ContactWhatsAppConsentState.Unknown
            });
        await db.SaveChangesAsync();

        var worker = new Worker { Name = "Preview Worker" };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        db.WorkerAssignments.Add(new WorkerAssignment
        {
            WorkItemId = fixture.WorkItemId,
            WorkerId = worker.Id,
            WorkerName = worker.Name,
            Percent = 100m,
            LcmGrossSnapshot = 1000m,
            Entitlement = 1000m,
            CurrentWorkflowStatus = WorkflowStatus.AssignedNotStarted,
            CurrentProgressPercent = 0m
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var before = new
        {
            BatchCount = await db.DocumentRequestBatches.CountAsync(),
            MemberCount = await db.DocumentRequestBatchMembers.CountAsync(),
            RequestStatus = await db.DocumentRequests.Where(x => x.Id == request.Id).Select(x => x.Status).SingleAsync(),
            RequestVersion = await db.DocumentRequests.Where(x => x.Id == request.Id).Select(x => x.Version).SingleAsync(),
            ItemStatuses = await db.DocumentRequestItems.Where(x => x.DocumentRequestId == request.Id).OrderBy(x => x.Id).Select(x => x.Status).ToArrayAsync(),
            Contact = await db.Contacts.Where(x => x.Id == contact.Id).Select(x => new { x.Name, x.IsActive, x.Version }).SingleAsync(),
            Link = await db.ContactCustomerLinks.Where(x => x.ContactId == contact.Id && x.CustomerId == customerId).Select(x => new { x.IsActive, x.Version }).SingleAsync(),
            Addresses = await db.ContactWhatsAppAddresses.Where(x => x.ContactId == contact.Id).OrderBy(x => x.Id).Select(x => new { x.IsActive, x.IsPrimary, x.ConsentState, x.Version }).ToArrayAsync(),
            Billing = await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId).Select(x => new { x.Status, x.Amount, x.Version }).SingleAsync(),
            WorkItem = await db.WorkItems.Where(x => x.Id == fixture.WorkItemId).Select(x => new { x.Status, x.Version }).SingleAsync(),
            Assignments = await db.WorkerAssignments.Where(x => x.WorkItemId == fixture.WorkItemId).OrderBy(x => x.Id).Select(x => new { x.Version, x.CurrentWorkflowStatus, x.CurrentProgressPercent }).ToArrayAsync()
        };

        var preview = await new DocumentRequestBatchService(db).PreviewBatchAsync(
            contact.Id,
            new[] { new DocumentBatchRequestSelection(request.Id, request.Revision) });

        Assert.Equal(contact.Id, preview.ContactId);
        Assert.Equal("Preview PIC", preview.ContactName);
        Assert.True(preview.ContactIsActive);
        Assert.Equal(2, preview.ActiveWhatsAppAddressCount);
        Assert.Equal(1, preview.ActiveOptedInWhatsAppAddressCount);
        Assert.True(preview.HasActiveDoNotWhatsAppAddress);
        Assert.True(preview.RequiresConversationAuthorization);
        var requestPreview = Assert.Single(preview.Requests);
        Assert.Equal(request.Id, requestPreview.DocumentRequestId);
        Assert.Equal(request.Revision, requestPreview.Revision);
        Assert.Equal(request.WorkItemId, requestPreview.WorkItemId);
        Assert.Equal(customerId, requestPreview.CustomerId);
        Assert.Equal("Preview Customer", requestPreview.CustomerName);
        Assert.Equal(fixture.EngagementId, requestPreview.EngagementId);
        Assert.Equal(service.Id, requestPreview.ServiceId);
        Assert.Equal(service.Name, requestPreview.ServiceName);
        Assert.Equal(DocumentRequestStatus.ReadyToSend, requestPreview.Status);
        Assert.Equal(3, requestPreview.OutstandingUnresolvedItemCount);
        Assert.Equal(6, requestPreview.TotalItemCount);

        db.ChangeTracker.Clear();
        Assert.Equal(before.BatchCount, await db.DocumentRequestBatches.CountAsync());
        Assert.Equal(before.MemberCount, await db.DocumentRequestBatchMembers.CountAsync());
        Assert.Equal(before.RequestStatus, await db.DocumentRequests.Where(x => x.Id == request.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(before.RequestVersion, await db.DocumentRequests.Where(x => x.Id == request.Id).Select(x => x.Version).SingleAsync());
        Assert.Equal(before.ItemStatuses, await db.DocumentRequestItems.Where(x => x.DocumentRequestId == request.Id).OrderBy(x => x.Id).Select(x => x.Status).ToArrayAsync());
        Assert.Equal(before.Contact, await db.Contacts.Where(x => x.Id == contact.Id).Select(x => new { x.Name, x.IsActive, x.Version }).SingleAsync());
        Assert.Equal(before.Link, await db.ContactCustomerLinks.Where(x => x.ContactId == contact.Id && x.CustomerId == customerId).Select(x => new { x.IsActive, x.Version }).SingleAsync());
        Assert.Equal(before.Addresses, await db.ContactWhatsAppAddresses.Where(x => x.ContactId == contact.Id).OrderBy(x => x.Id).Select(x => new { x.IsActive, x.IsPrimary, x.ConsentState, x.Version }).ToArrayAsync());
        Assert.Equal(before.Billing, await db.BillingRecords.Where(x => x.Id == fixture.BillingRecordId).Select(x => new { x.Status, x.Amount, x.Version }).SingleAsync());
        Assert.Equal(before.WorkItem, await db.WorkItems.Where(x => x.Id == fixture.WorkItemId).Select(x => new { x.Status, x.Version }).SingleAsync());
        Assert.Equal(before.Assignments, await db.WorkerAssignments.Where(x => x.WorkItemId == fixture.WorkItemId).OrderBy(x => x.Id).Select(x => new { x.Version, x.CurrentWorkflowStatus, x.CurrentProgressPercent }).ToArrayAsync());
    }

    [PostgresFact]
    public async Task DocumentRequestBatchService_RejectsMissingAndInactiveContacts()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "batch-contact");
        var template = await AddDocumentTemplateAsync(db, service.Id, "batch-contact-template", 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "batch-contact-item");
        var fixture = await AddDocumentFixtureAsync(db, "batch-contact", service.Id);
        var customerId = await CustomerIdForAsync(db, fixture.EngagementId);
        var request = await AddReadyBatchRequestAsync(db, fixture, template.Id, templateItem.Id);
        var contact = await AddBatchContactAsync(db, "Inactive PIC", (customerId, true));
        await db.Contacts.Where(x => x.Id == contact.Id).ExecuteUpdateAsync(x => x.SetProperty(y => y.IsActive, false));

        var batchService = new DocumentRequestBatchService(db);
        var inactive = await Assert.ThrowsAsync<BusinessException>(() => batchService.GetCandidateRequestsAsync(contact.Id));
        Assert.Contains("inactive", inactive.Message, StringComparison.OrdinalIgnoreCase);
        var inactivePreview = await Assert.ThrowsAsync<BusinessException>(() => batchService.PreviewBatchAsync(contact.Id, new[] { request.Id }));
        Assert.Contains("inactive", inactivePreview.Message, StringComparison.OrdinalIgnoreCase);
        var missing = await Assert.ThrowsAsync<BusinessException>(() => batchService.GetCandidateRequestsAsync(999999));
        Assert.Contains("not found", missing.Message, StringComparison.OrdinalIgnoreCase);
    }
}
