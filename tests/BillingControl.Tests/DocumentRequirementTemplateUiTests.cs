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

        int serviceId, otherServiceId, firmId, managerId, workerId;
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
            var otherService = new Service { Name = "Template UI Other Service" };
            var firm = new BusinessParty { Name = "Template UI Firm" };
            var manager = new Manager { Name = "Template UI Manager" };
            var worker = new Worker { Name = "Template UI Worker" };
            context.AddRange(service, otherService, firm, manager, worker);
            await context.SaveChangesAsync();
            serviceId = service.Id;
            otherServiceId = otherService.Id;
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
        Assert.Contains("Document Checklists", adminIndex);
        Assert.Contains("data-nav-group=\"documents\" open", adminIndex);
        Assert.Contains("Document Collection", adminIndex);
        Assert.Contains("Work Management", adminIndex);
        Assert.Contains("Administration", adminIndex);
        Assert.Contains("href=\"/DocumentRequirementTemplates\"", await internalUser.GetStringAsync("/DocumentRequirementTemplates"));
        var createPage = await admin.GetStringAsync("/DocumentRequirementTemplates/Create");
        Assert.Contains("Checklist name", createPage);
        Assert.Contains("Checklist description", createPage);
        Assert.Contains("Use a reusable checklist name for this service, not a customer name.", createPage);
        Assert.Contains("Version 1 is created automatically. Add the documents normally required for this service.", createPage);
        Assert.Contains("Documents will appear in the order shown below. You can change the order later.", createPage);
        Assert.Contains("Use as default checklist for this service", createPage);
        Assert.Contains("Bank Statement", createPage);
        Assert.Contains("Other Documents", createPage);
        Assert.Contains("Payment Voucher", createPage);
        Assert.Contains("Official Receipt", createPage);
        Assert.DoesNotContain("Payment / Receipt", createPage);
        Assert.Contains("data-searchable-dropdown=\"combobox\"", createPage);
        Assert.Contains("data-custom-placeholder=\"Type document name\"", createPage);
        Assert.Contains("list=\"checklist-document-options\"", createPage);
        Assert.DoesNotContain("data-other-document-wrapper", createPage);
        Assert.DoesNotContain("data-other-document-name", createPage);
        Assert.DoesNotContain("OtherDocumentName", createPage);
        Assert.Contains("Include in new requests", createPage);
        Assert.DoesNotContain("Request priority", createPage);
        Assert.DoesNotContain("Required?", createPage);
        Assert.DoesNotContain("stable internal keys", createPage);
        Assert.DoesNotContain("Internal keys", createPage);
        Assert.DoesNotContain("TemplateKey", createPage);
        Assert.DoesNotContain("RequirementKey", createPage);
        Assert.DoesNotContain("DisplayOrder", createPage);

        static Dictionary<string, string> CreateFields(
            int service,
            string name,
            bool isDefault = false,
            bool checklistActive = true,
            string documentSelection = "Bank Statement",
            bool includeInNewRequests = true) => new()
        {
            ["ServiceId"] = service.ToString(),
            ["Name"] = name,
            ["Description"] = $"{name} description",
            ["IsActive"] = checklistActive ? "true" : "false",
            ["IsDefault"] = isDefault ? "true" : "false",
            ["Items[0].DocumentSelection"] = documentSelection,
            ["Items[0].Description"] = "Latest bank statement",
            ["Items[0].IncludeInNewRequests"] = includeInNewRequests ? "true" : "false"
        };

        var invalidCreate = CreateFields(serviceId, "");
        var invalidResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", invalidCreate, "/DocumentRequirementTemplates/Create");
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
        Assert.Contains("The Name field is required.", await invalidResponse.Content.ReadAsStringAsync());

        var invalidOther = CreateFields(
            serviceId,
            "Invalid custom checklist",
            documentSelection: DocumentChecklistDocumentOptions.OtherDocuments);
        var invalidOtherResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", invalidOther, "/DocumentRequirementTemplates/Create");
        Assert.Equal(HttpStatusCode.OK, invalidOtherResponse.StatusCode);
        Assert.Contains("Enter the document name when Other Documents is selected.", await invalidOtherResponse.Content.ReadAsStringAsync());

        var firstResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(serviceId, "Monthly checklist", isDefault: true));
        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        var firstLocation = firstResponse.Headers.Location!.ToString();
        Assert.Contains("/DocumentRequirementTemplates/Edit/", firstLocation);

        var duplicateFamily = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(serviceId, "  MONTHLY CHECKLIST  "), "/DocumentRequirementTemplates/Create");
        Assert.Equal(HttpStatusCode.OK, duplicateFamily.StatusCode);
        Assert.Contains("A checklist with this name already exists for this service. Open the existing checklist and create a new version instead.", await duplicateFamily.Content.ReadAsStringAsync());

        var sameNameOtherService = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(otherServiceId, "Monthly checklist"));
        Assert.Equal(HttpStatusCode.Redirect, sameNameOtherService.StatusCode);

        var secondResponse = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(serviceId, "Annual checklist"));
        Assert.Equal(HttpStatusCode.Redirect, secondResponse.StatusCode);

        var familyIndex = await admin.GetStringAsync("/DocumentRequirementTemplates");
        Assert.Equal(3, familyIndex.Split("data-checklist-family").Length - 1);
        Assert.Equal(1, familyIndex.Split("href=\"/DocumentRequirementTemplates/Create\"").Length - 1);
        Assert.Contains("Current version</span><strong>1</strong>", familyIndex);
        Assert.Contains("Versions</span><strong>1</strong>", familyIndex);
        Assert.DoesNotContain("Previous versions", familyIndex);

        var customCreate = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(
            serviceId,
            "Custom checklist",
            documentSelection: "  Loan Statement  "));
        Assert.Equal(HttpStatusCode.Redirect, customCreate.StatusCode);
        db.ChangeTracker.Clear();
        var customItem = await db.DocumentRequirementTemplateItems
            .Where(x => x.DocumentRequirementTemplate!.Name == "Custom checklist")
            .SingleAsync();
        Assert.Equal("Loan Statement", customItem.Name);
        Assert.True(customItem.IsRequired);
        Assert.Equal(DocumentRequirementWave.Normal, customItem.Wave);
        Assert.True(customItem.IsActive);

        var excludedCreate = await PostWithToken(admin, "/DocumentRequirementTemplates/Create", "/DocumentRequirementTemplates/Create", CreateFields(
            serviceId,
            "Excluded checklist",
            checklistActive: false,
            includeInNewRequests: false));
        Assert.Equal(HttpStatusCode.Redirect, excludedCreate.StatusCode);
        db.ChangeTracker.Clear();
        var excludedItem = await db.DocumentRequirementTemplateItems
            .Where(x => x.DocumentRequirementTemplate!.Name == "Excluded checklist")
            .SingleAsync();
        Assert.True(excludedItem.IsRequired);
        Assert.Equal(DocumentRequirementWave.Normal, excludedItem.Wave);
        Assert.False(excludedItem.IsActive);

        db.ChangeTracker.Clear();
        var firstId = await db.DocumentRequirementTemplates.Where(x => x.ServiceId == serviceId && x.TemplateKey == "monthly-checklist").Select(x => x.Id).SingleAsync();
        var secondId = await db.DocumentRequirementTemplates.Where(x => x.TemplateKey == "annual-checklist").Select(x => x.Id).SingleAsync();
        var firstItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == firstId).Select(x => x.Id).SingleAsync();
        var secondItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == secondId).Select(x => x.Id).SingleAsync();
        var firstBefore = await db.DocumentRequirementTemplates.AsNoTracking().SingleAsync(x => x.Id == firstId);
        Assert.Equal(1, firstBefore.TemplateVersion);
        var renameCollision = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditDefinition", new()
        {
            ["Id"] = secondId.ToString(), ["Name"] = "  monthly CHECKLIST  ", ["Description"] = "Should remain unchanged"
        }, $"/DocumentRequirementTemplates/Edit/{secondId}");
        Assert.Equal(HttpStatusCode.OK, renameCollision.StatusCode);
        Assert.Contains("A checklist with this name already exists for this service. Open the existing checklist and create a new version instead.", await renameCollision.Content.ReadAsStringAsync());
        var secondEditPage = await admin.GetStringAsync($"/DocumentRequirementTemplates/Edit/{secondId}");
        Assert.Contains("Annual checklist", secondEditPage);
        Assert.Contains("Checklist details", secondEditPage);
        Assert.Contains("Checklist status", secondEditPage);
        Assert.Contains("This checklist can be reused when requesting documents for this service.", secondEditPage);
        Assert.Contains("Use a reusable checklist name for this service, not a customer name.", secondEditPage);
        Assert.Contains("You can rename an unused checklist without affecting how its versions are tracked.", secondEditPage);
        Assert.Contains("Use as default checklist for this service", secondEditPage);
        Assert.Contains("Include in new requests", secondEditPage);
        Assert.Contains("list=\"checklist-document-options\"", secondEditPage);
        Assert.DoesNotContain("data-other-document-wrapper", secondEditPage);
        Assert.DoesNotContain("data-other-document-name", secondEditPage);
        Assert.DoesNotContain("OtherDocumentName", secondEditPage);
        Assert.DoesNotContain("Request priority", secondEditPage);
        Assert.DoesNotContain("Required?", secondEditPage);
        Assert.DoesNotContain("Start Work", secondEditPage);
        Assert.DoesNotContain("Wave", secondEditPage);
        Assert.DoesNotContain("Version numbers and internal identifiers are managed automatically.", secondEditPage);
        Assert.DoesNotContain("stable internal identity", secondEditPage);
        Assert.DoesNotContain("TemplateKey", secondEditPage);
        Assert.DoesNotContain("RequirementKey", secondEditPage);
        Assert.DoesNotContain("type=\"number\"", secondEditPage);
        Assert.Contains("Save changes", secondEditPage);
        Assert.DoesNotContain("Save document", secondEditPage);
        Assert.DoesNotContain("Use Save document to update", secondEditPage);

        var addPayroll = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/AddItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["DocumentSelection"] = "Payroll Report",
            ["Description"] = "Monthly payroll", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, addPayroll.StatusCode);
        db.ChangeTracker.Clear();
        var payrollItem = await db.DocumentRequirementTemplateItems.SingleAsync(x => x.DocumentRequirementTemplateId == secondId && x.RequirementKey == "payroll-report");
        var payrollItemId = payrollItem.Id;
        Assert.True(payrollItem.IsRequired);
        Assert.Equal(DocumentRequirementWave.Normal, payrollItem.Wave);
        Assert.True(payrollItem.IsActive);

        var addTax = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/AddItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["DocumentSelection"] = "  Tax report  ",
            ["Description"] = "Annual tax report", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, addTax.StatusCode);
        db.ChangeTracker.Clear();
        var taxItemId = await db.DocumentRequirementTemplateItems.Where(x => x.DocumentRequirementTemplateId == secondId && x.RequirementKey == "tax-report").Select(x => x.Id).SingleAsync();

        var customMappingPage = await admin.GetStringAsync($"/DocumentRequirementTemplates/Edit/{secondId}");
        Assert.Contains("value=\"Tax report\"", customMappingPage);
        Assert.Contains("Other Documents", customMappingPage);
        Assert.Contains("Payment Voucher", customMappingPage);
        Assert.Contains("Official Receipt", customMappingPage);
        Assert.Contains("data-searchable-dropdown=\"combobox\"", customMappingPage);

        var legacyItem = await db.DocumentRequirementTemplateItems.SingleAsync(x => x.Id == secondItemId);
        legacyItem.IsRequired = false;
        legacyItem.Wave = DocumentRequirementWave.Later;
        await db.SaveChangesAsync();

        var editPayroll = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = payrollItemId.ToString(), ["DocumentSelection"] = "Payroll Report",
            ["Description"] = "Edited payroll register", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, editPayroll.StatusCode);

        var editLegacyItem = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = secondItemId.ToString(), ["DocumentSelection"] = "Sales Invoice",
            ["Description"] = "Edited legacy document", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, editLegacyItem.StatusCode);
        db.ChangeTracker.Clear();
        var preservedLegacyItem = await db.DocumentRequirementTemplateItems.SingleAsync(x => x.Id == secondItemId);
        Assert.Equal("Sales Invoice", preservedLegacyItem.Name);
        Assert.Equal("Edited legacy document", preservedLegacyItem.Description);
        Assert.False(preservedLegacyItem.IsRequired);
        Assert.Equal(DocumentRequirementWave.Later, preservedLegacyItem.Wave);

        var editLegacyValue = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = taxItemId.ToString(), ["DocumentSelection"] = "Payment / Receipt",
            ["Description"] = "Annual tax receipt", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, editLegacyValue.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal("Payment / Receipt", await db.DocumentRequirementTemplateItems.Where(x => x.Id == taxItemId).Select(x => x.Name).SingleAsync());

        var legacyPage = await admin.GetStringAsync($"/DocumentRequirementTemplates/Edit/{secondId}");
        Assert.Contains("value=\"Payment / Receipt\"", legacyPage);

        var editLegacyToPaymentVoucher = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = taxItemId.ToString(), ["DocumentSelection"] = "Payment Voucher",
            ["Description"] = "Annual tax voucher", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, editLegacyToPaymentVoucher.StatusCode);
        var editPaymentVoucherToOfficialReceipt = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = taxItemId.ToString(), ["DocumentSelection"] = "Official Receipt",
            ["Description"] = "Annual tax receipt", ["IncludeInNewRequests"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, editPaymentVoucherToOfficialReceipt.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal("Official Receipt", await db.DocumentRequirementTemplateItems.Where(x => x.Id == taxItemId).Select(x => x.Name).SingleAsync());

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
        Assert.Equal("Payroll Report", await db.DocumentRequirementTemplateItems.Where(x => x.Id == payrollItemId).Select(x => x.Name).SingleAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/EditItem", new()
        {
            ["TemplateId"] = secondId.ToString(), ["Id"] = firstItemId.ToString(), ["DocumentSelection"] = "Other Documents", ["IncludeInNewRequests"] = "true"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetItemActive", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = firstItemId.ToString(), ["isActive"] = "false"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/MoveItem", new()
        {
            ["templateId"] = secondId.ToString(), ["itemId"] = firstItemId.ToString(), ["direction"] = "up"
        })).StatusCode);
        Assert.Equal("Bank Statement", await db.DocumentRequirementTemplateItems.Where(x => x.Id == firstItemId).Select(x => x.Name).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/DocumentRequirementTemplates/Edit/999999")).StatusCode);

        var setDefault = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetDefault", new() { ["id"] = secondId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, setDefault.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.DocumentRequirementTemplates.CountAsync(x => x.ServiceId == serviceId && x.IsActive && x.IsDefault));
        Assert.True(await db.DocumentRequirementTemplates.Where(x => x.Id == secondId).Select(x => x.IsDefault).SingleAsync());

        var deactivateDefault = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetActive", new() { ["id"] = secondId.ToString(), ["isActive"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, deactivateDefault.StatusCode);
        Assert.Contains("current default checklist cannot be deactivated", await admin.GetStringAsync($"/DocumentRequirementTemplates/Edit/{secondId}"));

        var newVersion = await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/CreateVersion", new() { ["id"] = secondId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, newVersion.StatusCode);
        Assert.Contains("The new version was copied from the previous checklist. The previous version was not changed.",
            await admin.GetStringAsync(newVersion.Headers.Location!.ToString()));
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

        var versionedIndex = await admin.GetStringAsync("/DocumentRequirementTemplates");
        Assert.Contains("Current version</span><strong>2</strong>", versionedIndex);
        Assert.Contains("Versions</span><strong>2</strong>", versionedIndex);
        Assert.Contains("Documents</span><strong>3</strong>", versionedIndex);
        Assert.Contains("Previous versions (1)", versionedIndex);
        Assert.Contains("Version 1", versionedIndex);
        Assert.Equal(1, versionedIndex.Split("href=\"/DocumentRequirementTemplates/Create\"").Length - 1);

        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetDefault", new() { ["id"] = secondVersionId.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/DocumentRequirementTemplates/Edit/{secondId}", "/DocumentRequirementTemplates/SetActive", new() { ["id"] = secondId.ToString(), ["isActive"] = "false" })).StatusCode);
        db.ChangeTracker.Clear();
        Assert.False(await db.DocumentRequirementTemplates.Where(x => x.Id == secondId).Select(x => x.IsActive).SingleAsync());
        Assert.True(await db.DocumentRequirementTemplates.Where(x => x.Id == secondVersionId).Select(x => x.IsDefault).SingleAsync());

        var serviceFiltered = await admin.GetStringAsync($"/DocumentRequirementTemplates?ServiceId={serviceId}");
        Assert.Contains("Monthly checklist", serviceFiltered);
        Assert.Contains("Annual checklist", serviceFiltered);
        Assert.DoesNotContain("monthly-checklist", serviceFiltered);
        Assert.DoesNotContain("annual-checklist", serviceFiltered);
        Assert.DoesNotContain("TemplateKey", serviceFiltered);
        Assert.Contains("Inactive", serviceFiltered);

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
        Assert.Contains("This checklist version has already been used in a document request. Its documents can no longer be changed. Create a new version to make changes.", usedPage);
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
