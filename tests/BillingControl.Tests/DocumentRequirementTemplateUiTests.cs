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
    [PostgresFact]
    public async Task DocumentTemplateUiIsStaffOnlyAndKeepsTemplateOperationsScoped()
    {
        await using var db = await Fresh();
        const string password = "Template-Ui-Password!123";
        var keyPath = Path.Combine(AppContext.BaseDirectory, "document-template-ui-keys");
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Default", Connection)
                .UseSetting("DataProtection:Path", keyPath));

        int serviceId, firmId, managerId, workerId;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BootstrapAdmin:Email"] = "template-ui-admin@example.com",
                    ["BootstrapAdmin:Password"] = password
                })
                .Build());

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new Service { Name = "Template UI Service" };
            var firm = new BusinessParty { Name = "Template UI Firm" };
            var manager = new Manager { Name = "Template UI Manager" };
            var worker = new Worker { Name = "Template UI Worker" };
            context.AddRange(service, firm, manager, worker);
            await context.SaveChangesAsync();
            serviceId = service.Id;
            firmId = firm.Id;
            managerId = manager.Id;
            workerId = worker.Id;

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            async Task AddUser(string email, string role, int? businessPartyId = null, int? linkedManagerId = null, int? linkedWorkerId = null)
            {
                var user = new AppUser
                {
                    Email = email,
                    UserName = email,
                    EmailConfirmed = true,
                    BusinessPartyId = businessPartyId,
                    ManagerId = linkedManagerId,
                    WorkerId = linkedWorkerId
                };
                Seed.Check(await users.CreateAsync(user, password));
                Seed.Check(await users.AddToRoleAsync(user, role));
            }

            await AddUser("template-ui-internal@example.com", AppRoles.InternalUser);
            await AddUser("template-ui-firm@example.com", AppRoles.AccountingFirm, businessPartyId: firmId);
            await AddUser("template-ui-manager@example.com", AppRoles.Manager, linkedManagerId: managerId);
            await AddUser("template-ui-worker@example.com", AppRoles.Worker, linkedWorkerId: workerId);
        }

        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousResponse = await anonymous.GetAsync("/DocumentRequirementTemplates");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/Account/Login", anonymousResponse.Headers.Location!.ToString());

        using var admin = await SignedIn(app, "template-ui-admin@example.com", password);
        using var internalUser = await SignedIn(app, "template-ui-internal@example.com", password);
        using var firmClient = await SignedIn(app, "template-ui-firm@example.com", password);
        using var managerClient = await SignedIn(app, "template-ui-manager@example.com", password);
        using var workerClient = await SignedIn(app, "template-ui-worker@example.com", password);

        async Task AssertStaffOnly(HttpClient client)
        {
            var response = await client.GetAsync("/DocumentRequirementTemplates");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Denied", response.Headers.Location!.ToString());
        }

        await AssertStaffOnly(firmClient);
        await AssertStaffOnly(managerClient);
        await AssertStaffOnly(workerClient);
        var adminIndex = await admin.GetStringAsync("/DocumentRequirementTemplates");
        Assert.Contains("Document templates", adminIndex);
        Assert.Contains("href=\"/DocumentRequirementTemplates\"", await internalUser.GetStringAsync("/DocumentRequirementTemplates"));

        static Dictionary<string, string> CreateFields(int service, string key, string name, bool isDefault = false) => new()
        {
            ["ServiceId"] = service.ToString(),
            ["TemplateKey"] = key,
            ["Name"] = name,
            ["Description"] = $"{name} description",
            ["IsActive"] = "true",
            ["IsDefault"] = isDefault ? "true" : "false",
            ["Items[0].RequirementKey"] = "bank-statement",
            ["Items[0].Name"] = "Bank statement",
            ["Items[0].Description"] = "Latest bank statement",
            ["Items[0].IsRequired"] = "true",
            ["Items[0].Wave"] = DocumentRequirementWave.StartWork.ToString(),
            ["Items[0].DisplayOrder"] = "0",
            ["Items[0].IsActive"] = "true"
        };

        var invalidCreate = CreateFields(serviceId, "invalid-template", "");
        var invalidResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", invalidCreate, "/DocumentRequirementTemplates/Create");
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
        Assert.Contains("The Name field is required.", await invalidResponse.Content.ReadAsStringAsync());

        var firstResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(serviceId, "monthly-checklist", "Monthly checklist", isDefault: true));
        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        var firstLocation = firstResponse.Headers.Location!.ToString();
        Assert.Contains("/DocumentRequirementTemplates/Edit/", firstLocation);

        var secondResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(serviceId, "annual-checklist", "Annual checklist"));
        Assert.Equal(HttpStatusCode.Redirect, secondResponse.StatusCode);

        db.ChangeTracker.Clear();
        var firstId = await db.DocumentRequirementTemplates.Where(x => x.TemplateKey == "monthly-checklist").Select(x => x.Id).SingleAsync();
        var secondId = await db.DocumentRequirementTemplates.Where(x => x.TemplateKey == "annual-checklist").Select(x => x.Id).SingleAsync();
        var firstItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == firstId).Select(x => x.Id).SingleAsync();
        var secondItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == secondId).Select(x => x.Id).SingleAsync();
        var firstBefore = await db.DocumentRequirementTemplates.AsNoTracking().SingleAsync(x => x.Id == firstId);
        Assert.Equal(1, firstBefore.TemplateVersion);

        var addPayroll = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/AddItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["RequirementKey"] = "payroll", ["Name"] = "Payroll report",
            ["Description"] = "Monthly payroll", ["IsRequired"] = "false", ["Wave"] = DocumentRequirementWave.Normal.ToString(),
            ["DisplayOrder"] = "1", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, addPayroll.StatusCode);
        db.ChangeTracker.Clear();
        var payrollItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == secondId && x.RequirementKey == "payroll").Select(x => x.Id).SingleAsync();

        var addTax = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/AddItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["RequirementKey"] = "tax", ["Name"] = "Tax report",
            ["Description"] = "Annual tax report", ["IsRequired"] = "false", ["Wave"] = DocumentRequirementWave.Later.ToString(),
            ["DisplayOrder"] = "2", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, addTax.StatusCode);
        db.ChangeTracker.Clear();
        var taxItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == secondId && x.RequirementKey == "tax").Select(x => x.Id).SingleAsync();

        var editPayroll = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = payrollItemId.ToString(), ["RequirementKey"] = "payroll", ["Name"] = "Payroll register",
            ["Description"] = "Edited payroll register", ["IsRequired"] = "true", ["Wave"] = DocumentRequirementWave.StartWork.ToString(),
            ["DisplayOrder"] = "1", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, editPayroll.StatusCode);

        var deactivatePayroll = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetItemActive", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = payrollItemId.ToString(), ["isActive"] = "false"
        });
        Assert.Equal(HttpStatusCode.Redirect, deactivatePayroll.StatusCode);
        var activatePayroll = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetItemActive", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = payrollItemId.ToString(), ["isActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, activatePayroll.StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/MoveItem", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = taxItemId.ToString(), ["direction"] = "up"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/MoveItem", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = taxItemId.ToString(), ["direction"] = "up"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/MoveItem", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = payrollItemId.ToString(), ["direction"] = "up"
        })).StatusCode);

        db.ChangeTracker.Clear();
        var orderedItemIds = await db.DocumentRequirementTemplateItems
            .Where(x => x.DocumentRequirementTemplateId == secondId)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Equal([taxItemId, payrollItemId, secondItemId], orderedItemIds);
        Assert.Equal("Payroll register", await db.DocumentRequirementTemplateItems.Where(x => x.Id == payrollItemId).Select(x => x.Name).SingleAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = firstItemId.ToString(), ["Name"] = "Forged", ["Wave"] = DocumentRequirementWave.Normal.ToString(), ["DisplayOrder"] = "0", ["IsActive"] = "true"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetItemActive", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = firstItemId.ToString(), ["isActive"] = "false"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/MoveItem", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = firstItemId.ToString(), ["direction"] = "up"
        })).StatusCode);
        Assert.Equal("Bank statement", await db.DocumentRequirementTemplateItems.Where(x => x.Id == firstItemId).Select(x => x.Name).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/DocumentRequirementTemplates/Edit/999999")).StatusCode);

        var setDefault = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetDefault", new() { ["id"] = secondId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, setDefault.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.DocumentRequirementTemplates.CountAsync(x => x.ServiceId == serviceId && x.IsActive && x.IsDefault));
        Assert.True(await db.DocumentRequirementTemplates.Where(x => x.Id == secondId).Select(x => x.IsDefault).SingleAsync());

        var deactivateDefault = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetActive", new() { ["id"] = secondId.ToString(), ["isActive"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, deactivateDefault.StatusCode);
        Assert.Contains("current default template cannot be deactivated", await admin.GetStringAsync($"/DocumentRequirementTemplates/Edit/{secondId}"));

        var newVersion = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/CreateVersion", new() { ["id"] = secondId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, newVersion.StatusCode);
        db.ChangeTracker.Clear();
        var versions = await db.DocumentRequirementTemplates
            .Where(x => x.ServiceId == serviceId && x.TemplateKey == "annual-checklist")
            .OrderBy(x => x.TemplateVersion)
            .ToListAsync();
        Assert.Equal([1, 2], versions.Select(x => x.TemplateVersion).ToArray());
        Assert.Equal("Annual checklist", versions[0].Name);
        Assert.Equal("Annual checklist", versions[1].Name);
        var secondVersionId = versions.Single(x => x.TemplateVersion == 2).Id;
        Assert.Equal(3, await db.DocumentRequirementTemplateItems.CountAsync(x => x.DocumentRequirementTemplateId == secondVersionId));

        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetDefault", new() { ["id"] = secondVersionId.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetActive", new() { ["id"] = secondId.ToString(), ["isActive"] = "false" })).StatusCode);
        db.ChangeTracker.Clear();
        Assert.False(await db.DocumentRequirementTemplates.Where(x => x.Id == secondId).Select(x => x.IsActive).SingleAsync());
        Assert.True(await db.DocumentRequirementTemplates.Where(x => x.Id == secondVersionId).Select(x => x.IsDefault).SingleAsync());

        var serviceFiltered = await admin.GetStringAsync($"/DocumentRequirementTemplates?ServiceId={serviceId}");
        Assert.Contains("monthly-checklist", serviceFiltered);
        Assert.Contains("annual-checklist", serviceFiltered);
        var keyFiltered = await admin.GetStringAsync("/DocumentRequirementTemplates?TemplateKey=annual-checklist");
        Assert.Contains("annual-checklist", keyFiltered);
        Assert.DoesNotContain("monthly-checklist", keyFiltered);
        Assert.Contains("Inactive", keyFiltered);
        Assert.Contains("v1", keyFiltered);
        Assert.Contains("v2", keyFiltered);

        var fixture = await AddDocumentFixtureAsync(db, "template-ui-used", serviceId);
        await AddDocumentRequestAsync(db, fixture.WorkItemId, firstId);
        var protectedCounts = new
        {
            BillingRecords = await db.BillingRecords.CountAsync(),
            WorkItems = await db.WorkItems.CountAsync(),
            WorkerAssignments = await db.WorkerAssignments.CountAsync()
        };
        var deactivateUsed = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{firstId}", "/DocumentRequirementTemplates/SetActive", new() { ["id"] = firstId.ToString(), ["isActive"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, deactivateUsed.StatusCode);
        db.ChangeTracker.Clear();
        Assert.False(await db.DocumentRequirementTemplates.Where(x => x.Id == firstId).Select(x => x.IsActive).SingleAsync());
        var activateUsed = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{firstId}", "/DocumentRequirementTemplates/SetActive", new() { ["id"] = firstId.ToString(), ["isActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, activateUsed.StatusCode);
        db.ChangeTracker.Clear();
        Assert.True(await db.DocumentRequirementTemplates.Where(x => x.Id == firstId).Select(x => x.IsActive).SingleAsync());
        var usedPage = await admin.GetStringAsync($"/DocumentRequirementTemplates/Edit/{firstId}");
        Assert.Contains("This version has been used and its checklist definition is immutable. Create a new version to make changes.", usedPage);
        Assert.DoesNotContain("name=\"Name\"", usedPage);
        Assert.Contains("Create new version", usedPage);

        using var noAntiforgery = new HttpRequestMessage(HttpMethod.Post, $"/DocumentRequirementTemplates/CreateVersion?id={secondId}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>())
        };
        var blockedPost = await admin.SendAsync(noAntiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, blockedPost.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.GetAsync($"/DocumentRequirementTemplates/CreateVersion?id={secondId}")).StatusCode);

        db.ChangeTracker.Clear();
        Assert.Equal(protectedCounts.BillingRecords, await db.BillingRecords.CountAsync());
        Assert.Equal(protectedCounts.WorkItems, await db.WorkItems.CountAsync());
        Assert.Equal(protectedCounts.WorkerAssignments, await db.WorkerAssignments.CountAsync());
    }
}
