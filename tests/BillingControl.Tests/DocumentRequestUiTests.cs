using System.Net;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private sealed record DocumentRequestUiSetup(
        WebApplicationFactory<Program> App,
        string Password,
        int ServiceId,
        int TemplateId,
        int WorkItemId,
        int WorkerId,
        int WorkerAssignmentId,
        int BusinessPartyId,
        int ManagerId);

    private static async Task<DocumentRequestUiSetup> CreateDocumentRequestUiSetupAsync(
        AppDbContext db,
        string prefix)
    {
        const string password = "Document-Request-Ui-Password!123";
        var adminEmail = $"{prefix}-admin@example.com";
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Default", Connection)
                .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, $"document-request-ui-keys-{prefix}-{Guid.NewGuid():N}")));

        int serviceId;
        int templateId;
        int workItemId;
        int workerId;
        int workerAssignmentId;
        int businessPartyId;
        int managerId;

        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"BootstrapAdmin:Email"] = adminEmail,
                    ["BootstrapAdmin:Password"] = password
                })
                .Build());

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = await AddDocumentServiceAsync(context, $"{prefix}-service");
            var fixture = await AddDocumentFixtureAsync(context, prefix, service.Id);
            context.RevenueShareAllocations.Add(new RevenueShareAllocation
            {
                BillingRecordId = fixture.BillingRecordId,
                Kind = ShareKind.Lcm,
                PartyName = "LCM MGT Sdn Bhd",
                Percent = 100m,
                Amount = 1000m
            });
            await context.SaveChangesAsync();
            var template = await AddRequestTemplateAsync(context, service.Id, $"{prefix}-template", isDefault: true);
            var engagement = await context.Engagements.AsNoTracking().SingleAsync(x => x.Id == fixture.EngagementId);
            var worker = new Worker { Name = DocumentToken($"{prefix}-worker") };
            context.Workers.Add(worker);
            await context.SaveChangesAsync();
            await new BillingService(context).Assign(fixture.WorkItemId, worker.Id, 50m);
            workerAssignmentId = await context.WorkerAssignments
                .Where(x => x.WorkItemId == fixture.WorkItemId && x.WorkerId == worker.Id)
                .Select(x => x.Id)
                .SingleAsync();

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            async Task AddUser(
                string email,
                string role,
                int? linkedBusinessPartyId = null,
                int? linkedManagerId = null,
                int? linkedWorkerId = null)
            {
                var user = new AppUser
                {
                    Email = email,
                    UserName = email,
                    EmailConfirmed = true,
                    BusinessPartyId = linkedBusinessPartyId,
                    ManagerId = linkedManagerId,
                    WorkerId = linkedWorkerId
                };
                Seed.Check(await users.CreateAsync(user, password));
                Seed.Check(await users.AddToRoleAsync(user, role));
            }

            await AddUser($"{prefix}-internal@example.com", AppRoles.InternalUser);
            await AddUser($"{prefix}-firm@example.com", AppRoles.AccountingFirm, linkedBusinessPartyId: engagement.BusinessPartyId);
            await AddUser($"{prefix}-manager@example.com", AppRoles.Manager, linkedManagerId: engagement.ManagerId);
            await AddUser($"{prefix}-worker@example.com", AppRoles.Worker, linkedWorkerId: worker.Id);

            serviceId = service.Id;
            templateId = template.Id;
            workItemId = fixture.WorkItemId;
            workerId = worker.Id;
            businessPartyId = engagement.BusinessPartyId;
            managerId = engagement.ManagerId;
        }

        return new(app, password, serviceId, templateId, workItemId, workerId, workerAssignmentId, businessPartyId, managerId);
    }

    private static string AdminEmail(string prefix) => $"{prefix}-admin@example.com";
    private static string InternalEmail(string prefix) => $"{prefix}-internal@example.com";
    private static string FirmEmail(string prefix) => $"{prefix}-firm@example.com";
    private static string ManagerEmail(string prefix) => $"{prefix}-manager@example.com";
    private static string WorkerEmail(string prefix) => $"{prefix}-worker@example.com";

    private static int Occurrences(string value, string search) =>
        string.IsNullOrEmpty(search) ? 0 : value.Split(search).Length - 1;

    private static Dictionary<string, string> RequestCreateFields(int workItemId, int templateId) => new()
    {
        ["WorkItemId"] = workItemId.ToString(),
        ["DocumentRequirementTemplateId"] = templateId.ToString()
    };

    [PostgresFact]
    public async Task DocumentRequestUi_IsStaffOnly_AndWorkDetailsKeepsCollectionDataStaffOnly()
    {
        await using var db = await Fresh();
        const string prefix = "request-ui-access";
        var setup = await CreateDocumentRequestUiSetupAsync(db, prefix);
        using var app = setup.App;

        var noTemplateService = await AddDocumentServiceAsync(db, "request-ui-no-template-service");
        var noTemplateFixture = await AddDocumentFixtureAsync(db, "request-ui-no-template", noTemplateService.Id);
        db.RevenueShareAllocations.Add(new RevenueShareAllocation
        {
            BillingRecordId = noTemplateFixture.BillingRecordId,
            Kind = ShareKind.Lcm,
            PartyName = "LCM MGT Sdn Bhd",
            Percent = 100m,
            Amount = 1000m
        });
        await db.SaveChangesAsync();

        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousResponse = await anonymous.GetAsync($"/DocumentRequests/Details/{setup.WorkItemId}");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/Account/Login", anonymousResponse.Headers.Location!.ToString());

        using var admin = await SignedIn(app, AdminEmail(prefix), setup.Password);
        using var internalUser = await SignedIn(app, InternalEmail(prefix), setup.Password);
        using var firm = await SignedIn(app, FirmEmail(prefix), setup.Password);
        using var manager = await SignedIn(app, ManagerEmail(prefix), setup.Password);
        using var worker = await SignedIn(app, WorkerEmail(prefix), setup.Password);

        foreach (var client in new[] { firm, manager, worker })
        {
            var denied = await client.GetAsync($"/DocumentRequests/Create?workItemId={setup.WorkItemId}");
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/Account/Denied", denied.Headers.Location!.ToString());
        }

        var adminCreate = await admin.GetStringAsync($"/DocumentRequests/Create?workItemId={setup.WorkItemId}");
        Assert.Contains("document request", adminCreate);
        Assert.Contains("Default", adminCreate);
        Assert.Contains($"value=\"{setup.TemplateId}\"", adminCreate);
        Assert.Equal(HttpStatusCode.OK, (await internalUser.GetAsync($"/DocumentRequests/Create?workItemId={setup.WorkItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await internalUser.GetAsync("/DocumentRequests/Details/999999")).StatusCode);

        var staffDetails = await admin.GetStringAsync($"/Work/Details/{setup.WorkItemId}");
        Assert.Contains("id=\"document-collection\"", staffDetails);
        Assert.Contains("No active document request", staffDetails);
        Assert.Contains("Eligible active templates", staffDetails);
        Assert.Contains("Default", staffDetails);
        Assert.Contains($"/DocumentRequests/Create?workItemId={setup.WorkItemId}", staffDetails);
        Assert.Contains($"/DocumentRequests/Create?workItemId={setup.WorkItemId}", await internalUser.GetStringAsync($"/Work/Details/{setup.WorkItemId}"));

        var noTemplateDetails = await admin.GetStringAsync($"/Work/Details/{noTemplateFixture.WorkItemId}");
        Assert.Contains("No eligible active document templates", noTemplateDetails);
        Assert.Contains($"/DocumentRequirementTemplates?serviceId={noTemplateService.Id}", noTemplateDetails);

        var managerDetails = await manager.GetStringAsync($"/Work/Details/{setup.WorkItemId}");
        var workerDetails = await worker.GetStringAsync($"/Work/Details/{setup.WorkItemId}");
        Assert.DoesNotContain("document-collection", managerDetails, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Document collection", managerDetails, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/DocumentRequests/", managerDetails, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document-collection", workerDetails, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Document collection", workerDetails, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/DocumentRequests/", workerDetails, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task DocumentRequestUi_CreatesDraft_PreventsDuplicates_AndRendersExactRequestActions()
    {
        await using var db = await Fresh();
        const string prefix = "request-ui-actions";
        var setup = await CreateDocumentRequestUiSetupAsync(db, prefix);
        using var app = setup.App;
        using var admin = await SignedIn(app, AdminEmail(prefix), setup.Password);
        var createPath = $"/DocumentRequests/Create?workItemId={setup.WorkItemId}";
        var detailsPath = string.Empty;

        var beforeGetCount = await db.DocumentRequests.CountAsync();
        var createPage = await admin.GetStringAsync(createPath);
        Assert.Equal(beforeGetCount, await db.DocumentRequests.CountAsync());
        Assert.Contains("Default", createPage);

        using (var noAntiforgery = new HttpRequestMessage(HttpMethod.Post, "/DocumentRequests/Create")
        {
            Content = new FormUrlEncodedContent(RequestCreateFields(setup.WorkItemId, setup.TemplateId))
        })
        {
            var rejected = await admin.SendAsync(noAntiforgery);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        var createdResponse = await PostWithToken(admin, createPath, "/DocumentRequests/Create", new()
        {
            ["WorkItemId"] = setup.WorkItemId.ToString(),
            ["DocumentRequirementTemplateId"] = setup.TemplateId.ToString(),
            ["Revision"] = "999",
            ["ServiceId"] = "999999",
            ["Actor"] = "forged-actor",
            ["Source"] = "forged-source",
            ["Status"] = DocumentRequestStatus.Cancelled.ToString()
        }, createPath);
        Assert.Equal(HttpStatusCode.Redirect, createdResponse.StatusCode);
        detailsPath = createdResponse.Headers.Location!.ToString();
        Assert.Contains("/DocumentRequests/Details/", detailsPath);

        db.ChangeTracker.Clear();
        var request = await db.DocumentRequests.SingleAsync(x => x.WorkItemId == setup.WorkItemId);
        var actorId = await db.Users.Where(x => x.Email == AdminEmail(prefix)).Select(x => x.Id).SingleAsync();
        var requestHistory = await db.DocumentRequestStatusHistories.SingleAsync(x => x.DocumentRequestId == request.Id);
        Assert.Equal(1, request.Revision);
        Assert.Equal(DocumentRequestStatus.Draft, request.Status);
        Assert.Equal(actorId, requestHistory.Actor);
        Assert.Equal("BillingControl.DocumentRequests", requestHistory.Source);

        var duplicateGet = await admin.GetAsync(createPath);
        Assert.Equal(HttpStatusCode.Redirect, duplicateGet.StatusCode);
        Assert.Contains(detailsPath, duplicateGet.Headers.Location!.ToString());
        var duplicatePost = await PostWithToken(admin, detailsPath, "/DocumentRequests/Create", RequestCreateFields(setup.WorkItemId, setup.TemplateId), detailsPath);
        Assert.Equal(HttpStatusCode.Redirect, duplicatePost.StatusCode);
        Assert.Contains(detailsPath, duplicatePost.Headers.Location!.ToString());

        var draftResponse = await admin.GetAsync(detailsPath);
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        var draftPage = await draftResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"request-reason\"", draftPage);
        Assert.Equal(1, Occurrences(draftPage, "id=\"request-reason\""));
        Assert.Contains("name=\"targetStatus\" value=\"ReadyToSend\"", draftPage);
        Assert.Contains("name=\"targetStatus\" value=\"Paused\"", draftPage);
        Assert.Contains("name=\"targetStatus\" value=\"Cancelled\"", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Requested\"", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"PartiallyReceived\"", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Complete\"", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Superseded\"", draftPage);
        Assert.DoesNotContain("name=\"actor\"", draftPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"source\"", draftPage, StringComparison.OrdinalIgnoreCase);

        async Task Transition(DocumentRequestStatus target, long expectedVersion, string reason)
        {
            var response = await PostWithToken(admin, detailsPath, "/DocumentRequests/Transition", new()
            {
                ["requestId"] = request.Id.ToString(),
                ["expectedVersion"] = expectedVersion.ToString(),
                ["targetStatus"] = target.ToString(),
                ["reason"] = reason
            }, detailsPath);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(detailsPath, response.Headers.Location!.ToString());
            db.ChangeTracker.Clear();
            request = await db.DocumentRequests.SingleAsync(x => x.Id == request.Id);
        }

        await Transition(DocumentRequestStatus.ReadyToSend, request.Version, "Checklist reviewed");
        var readyPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("name=\"targetStatus\" value=\"Paused\"", readyPage);
        Assert.Contains("name=\"targetStatus\" value=\"Cancelled\"", readyPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"ReadyToSend\"", readyPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Draft\"", readyPage);

        await Transition(DocumentRequestStatus.Paused, request.Version, "Pause before sending");
        var pausedPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("Resume to Draft", pausedPage);
        Assert.Contains("Resume to Ready to Send", pausedPage);
        Assert.Contains("name=\"targetStatus\" value=\"Cancelled\"", pausedPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Paused\"", pausedPage);

        await Transition(DocumentRequestStatus.Draft, request.Version, "Resume checklist editing");
        await Transition(DocumentRequestStatus.Cancelled, request.Version, "No longer needed");
        var cancelledPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("This cancelled request is read-only", cancelledPage);
        Assert.DoesNotContain("action=\"/DocumentRequests/Transition\"", cancelledPage);
        Assert.DoesNotContain("name=\"targetStatus\"", cancelledPage);

        foreach (var status in new[] { DocumentRequestStatus.Requested, DocumentRequestStatus.Complete, DocumentRequestStatus.Superseded })
        {
            var fixture = await AddDocumentFixtureAsync(db, $"request-ui-{status}", setup.ServiceId);
            var terminal = await AddDocumentRequestAsync(db, fixture.WorkItemId, setup.TemplateId, status: status);
            var terminalPage = await admin.GetStringAsync($"/DocumentRequests/Details/{terminal.Id}");
            Assert.DoesNotContain("action=\"/DocumentRequests/Transition\"", terminalPage);
            Assert.DoesNotContain("name=\"targetStatus\"", terminalPage);
            if (status == DocumentRequestStatus.Requested)
                Assert.Contains("Requested will become available only after a future outbound provider send is accepted.", terminalPage);
        }
    }

    [PostgresFact]
    public async Task DocumentRequestUi_ChecklistActions_Histories_AndIsolationAreSafe()
    {
        await using var db = await Fresh();
        const string prefix = "request-ui-checklist";
        var setup = await CreateDocumentRequestUiSetupAsync(db, prefix);
        using var app = setup.App;
        using var admin = await SignedIn(app, AdminEmail(prefix), setup.Password);

        var billingBefore = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.WorkItem.Id == setup.WorkItemId);
        var workBefore = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == setup.WorkItemId);
        var assignmentBefore = await db.WorkerAssignments.AsNoTracking().SingleAsync(x => x.Id == setup.WorkerAssignmentId);

        var createPath = $"/DocumentRequests/Create?workItemId={setup.WorkItemId}";
        var foreignService = await AddDocumentServiceAsync(db, "request-ui-foreign-template-service");
        var foreignTemplate = await AddRequestTemplateAsync(db, foreignService.Id, "request-ui-foreign-template");
        var foreignTemplatePost = await PostWithToken(admin, createPath, "/DocumentRequests/Create", RequestCreateFields(setup.WorkItemId, foreignTemplate.Id), createPath);
        Assert.Equal(HttpStatusCode.OK, foreignTemplatePost.StatusCode);
        Assert.Contains("belongs to another service", await foreignTemplatePost.Content.ReadAsStringAsync());
        Assert.Equal(0, await db.DocumentRequests.CountAsync(x => x.WorkItemId == setup.WorkItemId));

        var created = await PostWithToken(admin, createPath, "/DocumentRequests/Create", RequestCreateFields(setup.WorkItemId, setup.TemplateId), createPath);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var detailsPath = created.Headers.Location!.ToString();
        db.ChangeTracker.Clear();
        var request = await db.DocumentRequests.SingleAsync(x => x.WorkItemId == setup.WorkItemId);
        var firstItem = await db.DocumentRequestItems.Where(x => x.DocumentRequestId == request.Id).OrderBy(x => x.DisplayOrder).FirstAsync();
        var firstItemVersion = firstItem.Version;

        var draftPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("Mark Not Required", draftPage);
        Assert.Contains("Waive", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Requested\"", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"PartiallyReceived\"", draftPage);
        Assert.DoesNotContain("name=\"targetStatus\" value=\"Received\"", draftPage);

        var notRequired = await PostWithToken(admin, detailsPath, "/DocumentRequests/TransitionItem", new()
        {
            ["requestId"] = request.Id.ToString(),
            ["itemId"] = firstItem.Id.ToString(),
            ["expectedVersion"] = firstItemVersion.ToString(),
            ["targetStatus"] = DocumentRequestItemStatus.NotRequired.ToString(),
            ["reason"] = "Not applicable this period"
        }, detailsPath);
        Assert.Equal(HttpStatusCode.Redirect, notRequired.StatusCode);

        db.ChangeTracker.Clear();
        firstItem = await db.DocumentRequestItems.SingleAsync(x => x.Id == firstItem.Id);
        var notRequiredPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("Restore to Missing", notRequiredPage);
        Assert.Equal(1, Occurrences(notRequiredPage, "name=\"targetStatus\" value=\"NotRequired\""));
        Assert.Equal(1, Occurrences(notRequiredPage, "name=\"targetStatus\" value=\"Waived\""));

        var restored = await PostWithToken(admin, detailsPath, "/DocumentRequests/TransitionItem", new()
        {
            ["requestId"] = request.Id.ToString(),
            ["itemId"] = firstItem.Id.ToString(),
            ["expectedVersion"] = firstItem.Version.ToString(),
            ["targetStatus"] = DocumentRequestItemStatus.Missing.ToString(),
            ["reason"] = "Recheck requirement"
        }, detailsPath);
        Assert.Equal(HttpStatusCode.Redirect, restored.StatusCode);

        db.ChangeTracker.Clear();
        firstItem = await db.DocumentRequestItems.SingleAsync(x => x.Id == firstItem.Id);
        var staleItemVersion = firstItem.Version;
        var staleItemHistoryCount = await db.DocumentRequestItemStatusHistories.CountAsync(x => x.DocumentRequestItemId == firstItem.Id);
        await new DocumentRequestService(db, RequestTestClock()).TransitionItemAsync(firstItem.Id, new(
            DocumentRequestItemStatus.Waived,
            staleItemVersion,
            "concurrent-user",
            "test",
            "Concurrent waiver"));
        var staleItemPost = await PostWithToken(admin, detailsPath, "/DocumentRequests/TransitionItem", new()
        {
            ["requestId"] = request.Id.ToString(),
            ["itemId"] = firstItem.Id.ToString(),
            ["expectedVersion"] = staleItemVersion.ToString(),
            ["targetStatus"] = DocumentRequestItemStatus.Missing.ToString(),
            ["reason"] = "Stale restore"
        }, detailsPath);
        Assert.Equal(HttpStatusCode.Redirect, staleItemPost.StatusCode);
        Assert.Contains(detailsPath, staleItemPost.Headers.Location!.ToString());
        var staleItemPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("The document request item changed. Refresh before applying this transition.", staleItemPage);
        db.ChangeTracker.Clear();
        firstItem = await db.DocumentRequestItems.SingleAsync(x => x.Id == firstItem.Id);
        Assert.Equal(DocumentRequestItemStatus.Waived, firstItem.Status);
        Assert.Equal(staleItemHistoryCount + 1, await db.DocumentRequestItemStatusHistories.CountAsync(x => x.DocumentRequestItemId == firstItem.Id));

        db.ChangeTracker.Clear();
        request = await db.DocumentRequests.SingleAsync(x => x.Id == request.Id);
        var staleRequestVersion = request.Version;
        var staleRequestHistoryCount = await db.DocumentRequestStatusHistories.CountAsync(x => x.DocumentRequestId == request.Id);
        await new DocumentRequestService(db, RequestTestClock()).TransitionAsync(request.Id, new(
            DocumentRequestStatus.Paused,
            staleRequestVersion,
            "concurrent-user",
            "test",
            "Concurrent pause"));
        var staleRequestPost = await PostWithToken(admin, detailsPath, "/DocumentRequests/Transition", new()
        {
            ["requestId"] = request.Id.ToString(),
            ["expectedVersion"] = staleRequestVersion.ToString(),
            ["targetStatus"] = DocumentRequestStatus.ReadyToSend.ToString(),
            ["reason"] = "Stale ready"
        }, detailsPath);
        Assert.Equal(HttpStatusCode.Redirect, staleRequestPost.StatusCode);
        var staleRequestPage = await admin.GetStringAsync(detailsPath);
        Assert.Contains("The document request changed. Refresh before applying this transition.", staleRequestPage);
        db.ChangeTracker.Clear();
        request = await db.DocumentRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(DocumentRequestStatus.Paused, request.Status);
        Assert.Equal(staleRequestHistoryCount + 1, await db.DocumentRequestStatusHistories.CountAsync(x => x.DocumentRequestId == request.Id));

        var otherFixture = await AddDocumentFixtureAsync(db, "request-ui-foreign-item", setup.ServiceId);
        var otherRequest = await new DocumentRequestService(db, RequestTestClock()).CreateDraftAsync(new(
            otherFixture.WorkItemId,
            setup.TemplateId,
            "other-user",
            "test"));
        var otherItem = otherRequest.Items.First();
        var foreignItemPost = await PostWithToken(admin, detailsPath, "/DocumentRequests/TransitionItem", new()
        {
            ["requestId"] = request.Id.ToString(),
            ["itemId"] = otherItem.Id.ToString(),
            ["expectedVersion"] = otherItem.Version.ToString(),
            ["targetStatus"] = DocumentRequestItemStatus.NotRequired.ToString(),
            ["reason"] = "Foreign item"
        }, detailsPath);
        Assert.Equal(HttpStatusCode.NotFound, foreignItemPost.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(DocumentRequestItemStatus.Missing, await db.DocumentRequestItems.Where(x => x.Id == otherItem.Id).Select(x => x.Status).SingleAsync());

        var statusTemplate = await AddDocumentTemplateAsync(db, setup.ServiceId, DocumentToken("request-ui-item-statuses"), 1, isActive: true);
        var statusTemplateItems = new List<DocumentRequirementTemplateItem>();
        foreach (var key in new[] { "requested", "partially-received", "received" })
            statusTemplateItems.Add(await AddDocumentTemplateItemAsync(db, statusTemplate.Id, $"status-{key}"));
        var statusFixture = await AddDocumentFixtureAsync(db, "request-ui-item-statuses", setup.ServiceId);
        var statusRequest = await AddDocumentRequestAsync(db, statusFixture.WorkItemId, statusTemplate.Id);
        foreach (var pair in statusTemplateItems.Zip(new[]
                 {
                     DocumentRequestItemStatus.Requested,
                     DocumentRequestItemStatus.PartiallyReceived,
                     DocumentRequestItemStatus.Received
                 }))
        {
            var item = await AddDocumentRequestItemAsync(db, statusRequest.Id, pair.First.Id, pair.First.RequirementKey);
            item.Status = pair.Second;
            await db.SaveChangesAsync();
        }
        var itemStatusesPage = await admin.GetStringAsync($"/DocumentRequests/Details/{statusRequest.Id}");
        Assert.Equal(0, Occurrences(itemStatusesPage, "class=\"item-action-form\""));
        Assert.Equal(3, Occurrences(itemStatusesPage, "Received states depend on future document evidence/classification."));

        Assert.Equal(0, await db.DocumentRequests.CountAsync(x => x.WorkItemId == 0));
        Assert.Equal(billingBefore.Version, await db.BillingRecords.Where(x => x.Id == billingBefore.Id).Select(x => x.Version).SingleAsync());
        Assert.Equal(billingBefore.Status, await db.BillingRecords.Where(x => x.Id == billingBefore.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(workBefore.Version, await db.WorkItems.Where(x => x.Id == workBefore.Id).Select(x => x.Version).SingleAsync());
        Assert.Equal(workBefore.Status, await db.WorkItems.Where(x => x.Id == workBefore.Id).Select(x => x.Status).SingleAsync());
        var assignmentAfter = await db.WorkerAssignments.AsNoTracking().SingleAsync(x => x.Id == assignmentBefore.Id);
        Assert.Equal(assignmentBefore.Version, assignmentAfter.Version);
        Assert.Equal(assignmentBefore.CurrentWorkflowVersion, assignmentAfter.CurrentWorkflowVersion);
        Assert.Equal(assignmentBefore.CurrentWorkflowStatus, assignmentAfter.CurrentWorkflowStatus);
        Assert.Equal(assignmentBefore.Entitlement, assignmentAfter.Entitlement);

        var readModel = await new DocumentRequestService(db, RequestTestClock()).GetByIdAsync(request.Id);
        Assert.NotNull(readModel);
        Assert.Equal(new[]
        {
            DocumentRequestStatus.Draft,
            DocumentRequestStatus.Paused
        }, readModel!.StatusHistory.Select(x => x.NewStatus));
        Assert.Equal(new[]
        {
            DocumentRequestItemStatus.Missing,
            DocumentRequestItemStatus.NotRequired,
            DocumentRequestItemStatus.Missing,
            DocumentRequestItemStatus.Waived
        }, readModel.Items.Single(x => x.Id == firstItem.Id).StatusHistory.Select(x => x.NewStatus));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/DocumentRequests/Details/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/DocumentRequests/Create?workItemId=999999")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.GetAsync($"/DocumentRequests/Transition?requestId={request.Id}")).StatusCode);
    }

    [PostgresFact]
    public async Task DocumentRequestUi_HistoricalRevisionsRemainViewable_AndReadsDoNotMutate()
    {
        await using var db = await Fresh();
        const string prefix = "request-ui-history";
        var setup = await CreateDocumentRequestUiSetupAsync(db, prefix);
        using var app = setup.App;
        using var admin = await SignedIn(app, AdminEmail(prefix), setup.Password);
        var createPath = $"/DocumentRequests/Create?workItemId={setup.WorkItemId}";

        var firstResponse = await PostWithToken(admin, createPath, "/DocumentRequests/Create", RequestCreateFields(setup.WorkItemId, setup.TemplateId), createPath);
        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        var firstDetails = firstResponse.Headers.Location!.ToString();
        db.ChangeTracker.Clear();
        var first = await db.DocumentRequests.SingleAsync(x => x.WorkItemId == setup.WorkItemId);
        var firstVersion = first.Version;
        var requestCountBeforeReads = await db.DocumentRequests.CountAsync();
        var itemCountBeforeReads = await db.DocumentRequestItems.CountAsync();
        var historyCountBeforeReads = await db.DocumentRequestStatusHistories.CountAsync();

        var cancelled = await PostWithToken(admin, firstDetails, "/DocumentRequests/Transition", new()
        {
            ["requestId"] = first.Id.ToString(),
            ["expectedVersion"] = firstVersion.ToString(),
            ["targetStatus"] = DocumentRequestStatus.Cancelled.ToString(),
            ["reason"] = "Replace checklist revision"
        }, firstDetails);
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);

        var secondResponse = await PostWithToken(admin, createPath, "/DocumentRequests/Create", RequestCreateFields(setup.WorkItemId, setup.TemplateId), createPath);
        Assert.Equal(HttpStatusCode.Redirect, secondResponse.StatusCode);
        var secondDetails = secondResponse.Headers.Location!.ToString();
        var workDetails = await admin.GetStringAsync($"/Work/Details/{setup.WorkItemId}");
        Assert.Contains("Document collection", workDetails);
        Assert.Contains("Revision 1", workDetails);
        Assert.Contains("Cancelled", workDetails);
        Assert.Contains(secondDetails.Replace("/DocumentRequests/Details/", "/DocumentRequests/Details/"), workDetails);

        var firstHistoryPage = await admin.GetStringAsync(firstDetails);
        Assert.Contains("This cancelled request is read-only", firstHistoryPage);
        Assert.Contains("Created", firstHistoryPage);
        Assert.Contains("CreatedFromTemplate", firstHistoryPage);
        Assert.Contains("Replace checklist revision", firstHistoryPage);
        Assert.DoesNotContain("name=\"targetStatus\"", firstHistoryPage);

        var secondPage = await admin.GetStringAsync(secondDetails);
        Assert.Contains("WorkItem request revisions", secondPage);
        Assert.Contains($"/DocumentRequests/Details/{first.Id}", secondPage);
        Assert.Contains("Revision 1", secondPage);
        Assert.Contains("Revision 2", secondPage);

        db.ChangeTracker.Clear();
        var revisions = await new DocumentRequestService(db, RequestTestClock()).GetRevisionsForWorkItemAsync(setup.WorkItemId);
        Assert.Equal(new[] { 1, 2 }, revisions.Select(x => x.Revision));
        Assert.Equal(new[] { DocumentRequestStatus.Cancelled, DocumentRequestStatus.Draft }, revisions.Select(x => x.StatusHistory.Last().NewStatus));
        Assert.Equal(requestCountBeforeReads + 1, await db.DocumentRequests.CountAsync());
        Assert.Equal(itemCountBeforeReads + 2, await db.DocumentRequestItems.CountAsync());
        Assert.Equal(historyCountBeforeReads + 2, await db.DocumentRequestStatusHistories.CountAsync());
    }
}
