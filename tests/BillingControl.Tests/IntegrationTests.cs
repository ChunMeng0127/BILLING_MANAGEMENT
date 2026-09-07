using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Text.RegularExpressions;

namespace BillingControl.Tests;

public class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BILLING_TEST_CONNECTION"))) Skip = "Set BILLING_TEST_CONNECTION to an isolated PostgreSQL database (name must end in _test)."; }
}
public class IntegrationTests
{
    private static string Connection => Environment.GetEnvironmentVariable("BILLING_TEST_CONNECTION")!;
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
    private static async Task<AppDbContext> Fresh()
    {
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(Connection);
        Assert.EndsWith("_test", cs.Database);
        var db = Db(); await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync(); return db;
    }
    private static async Task<int> Engagement(AppDbContext db)
    {
        var e = new Engagement { Customer = new() { Name = "Test customer" }, Service = new() { Name = "Bookkeeping" }, BusinessParty = new() { Name = "X Group" }, Manager = new() { Name = "Signitive" }, StartDate = new(2026, 1, 1), BillingAmount = 1000, Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 } };
        db.Add(e); await db.SaveChangesAsync(); return e.Id;
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> app, string email, string password)
    {
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Account/Login"));
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", email }, { "Password", password }, { "__RequestVerificationToken", token } }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> PostWithToken(HttpClient client, string tokenPage, string target, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(tokenPage));
        return await client.PostAsync(target, new FormUrlEncodedContent(fields));
    }
    [PostgresFact]
    public async Task FinancialWorkflowSnapshotsDuplicatesAndPayments()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var service = new BillingService(db);
        var bill = await service.Generate(id, new(2026, 1, 1), new(2026, 1, 31), true);
        Assert.Equal(new DateOnly(2026, 2, 1), (await db.BillingSchedules.SingleAsync()).NextPeriodStart);
        await Assert.ThrowsAsync<BusinessException>(() => service.Generate(id, new(2026, 1, 1), new(2026, 1, 31)));
        await Assert.ThrowsAsync<BusinessException>(() => service.Generate(id, new(2026, 1, 15), new(2026, 2, 15)));
        var e = await db.Engagements.SingleAsync(); e.FirmPercent = 10; e.ManagerPercent = 20; e.LcmPercent = 70; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var old = await db.BillingRecords.Include(x => x.Shares).SingleAsync(); Assert.Equal(400m, old.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount); Assert.Equal(40m, old.Shares.Single(x => x.Kind == ShareKind.Lcm).Percent);
        var w = new Worker { Name = "Worker 1", Type = WorkerType.Freelancer }; db.Add(w); await db.SaveChangesAsync();
        await service.Assign(bill.WorkItem.Id, w.Id, 70);
        var a = await db.WorkerAssignments.SingleAsync(); Assert.Equal(280m, a.Entitlement);
        await service.Pay(w.Id, new(2026, 1, 31), "PARTIAL", Guid.NewGuid(), new() { [a.Id] = 100 });
        await Assert.ThrowsAsync<BusinessException>(() => service.Pay(w.Id, new(2026, 1, 31), "OVER", Guid.NewGuid(), new() { [a.Id] = 181 }));
        var next = await service.Generate(id, new(2026, 2, 1), new(2026, 2, 28)); await service.Assign(next.WorkItem.Id, w.Id, 40);
        var second = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == next.WorkItem.Id); Assert.Equal(280m, second.Entitlement);
        var request = Guid.NewGuid(); await service.Pay(w.Id, new(2026, 2, 28), "MULTI", request, new() { [a.Id] = 180, [second.Id] = 100 });
        Assert.Equal(380m, await db.WorkerPaymentAllocations.SumAsync(x => x.Amount));
        await Assert.ThrowsAsync<BusinessException>(() => service.Pay(w.Id, new(2026, 2, 28), "MULTI", request, new() { [second.Id] = 100 }));
        await Assert.ThrowsAsync<BusinessException>(() => service.Cancel("assignment", a.Id, "Paid"));
        var payment = await db.WorkerPayments.SingleAsync(x => x.RequestId == request); await service.Cancel("payment", payment.Id, "Reversed");
        Assert.Equal(100m, await db.WorkerPaymentAllocations.Where(x => !x.WorkerPayment.IsCancelled).SumAsync(x => x.Amount));
        old = await db.BillingRecords.SingleAsync(x => x.Id == bill.Id); await service.UpdateBillingStatus(old.Id, BillingStatus.Billed, new(2026, 1, 31), "INV-001", old.Version);
        await service.Receive(old.Id, new(2026, 1, 31), 250m, "BANK", Guid.NewGuid()); Assert.Equal(BillingStatus.PartiallyPaid, old.Status);
        await Assert.ThrowsAsync<BusinessException>(() => service.Receive(old.Id, new(2026, 1, 31), 751, "OVER", Guid.NewGuid()));
        var receipt = await db.CustomerReceipts.SingleAsync(); await service.Cancel("receipt", receipt.Id, "Correction"); Assert.Equal(BillingStatus.Billed, old.Status);
        old.Amount = 2000; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [PostgresFact]
    public async Task DatabaseRejectsDuplicatePeriodAndOverAllocationAndForeignWorker()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var svc = new BillingService(db); var bill = await svc.Generate(id, new(2026, 1, 1), new(2026, 1, 31));
        var workers = new[] { new Worker { Name = "One" }, new Worker { Name = "Two" } }; db.AddRange(workers); await db.SaveChangesAsync();
        await svc.Assign(bill.WorkItem.Id, workers[0].Id, 70);
        await Assert.ThrowsAsync<BusinessException>(() => svc.Assign(bill.WorkItem.Id, workers[1].Id, 40));
        var a = await db.WorkerAssignments.SingleAsync(); await Assert.ThrowsAsync<BusinessException>(() => svc.Pay(workers[1].Id, new(2026, 1, 31), "WRONG", Guid.NewGuid(), new() { [a.Id] = 1 }));
        await using var other = Db(); other.BillingRecords.Add(new() { EngagementId = id, PeriodStart = new(2026, 1, 1), PeriodEnd = new(2026, 1, 31), Amount = 1000, CustomerName = "Duplicate", ServiceName = "Test" });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        await svc.Cancel("assignment", a.Id, "Reassign"); await svc.Cancel("billing", bill.Id, "Replace"); var replacement = await svc.Generate(id, new(2026, 1, 1), new(2026, 1, 31)); Assert.NotEqual(bill.Id, replacement.Id);
    }
    [PostgresFact]
    public async Task ConcurrentPaymentsCannotOverpay()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var svc = new BillingService(db); var bill = await svc.Generate(id, new(2026, 1, 1), new(2026, 1, 31)); var w = new Worker { Name = "Concurrency" }; db.Add(w); await db.SaveChangesAsync(); await svc.Assign(bill.WorkItem.Id, w.Id, 70); var a = await db.WorkerAssignments.SingleAsync();
        async Task Attempt() { await using var ctx = Db(); try { await new BillingService(ctx).Pay(w.Id, new(2026, 1, 31), "Concurrent", Guid.NewGuid(), new() { [a.Id] = 200 }); } catch (BusinessException) { } catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001") { } catch (DbUpdateException) { } }
        await Task.WhenAll(Attempt(), Attempt()); Assert.Equal(200m, await db.WorkerPaymentAllocations.SumAsync(x => x.Amount));
    }
    [PostgresFact]
    public async Task AuthenticationAndFullPageRender()
    {
        await using var db = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "test-keys")));
        using (var scope = app.Services.CreateScope()) await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "test@example.com" }, { "BootstrapAdmin:Password", "Testing-Password!123" }, { "Seed:Demo", "true" } }).Build());
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var path in new[] { "/", "/Masters", "/Engagements", "/Billing", "/Billing/Schedule", "/Work", "/Work/Assignments", "/Payments", "/Home/Reports", "/Home/Export", "/Users", "/Account/Password" }) { var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Contains("/Account/Login", response.Headers.Location!.ToString()); }
        var login = await client.GetAsync("/Account/Login"); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var html = await login.Content.ReadAsStringAsync(); var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value); Assert.NotEmpty(token);
        var noToken = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "test@example.com" }, { "Password", "Testing-Password!123" } })); Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var signed = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "test@example.com" }, { "Password", "Testing-Password!123" }, { "__RequestVerificationToken", token }, { "ReturnUrl", "https://evil.example/" } })); Assert.Equal("/", signed.Headers.Location!.ToString());
        foreach (var path in new[] { "/", "/Masters?kind=Customers", "/Masters?kind=Workers", "/Masters/Edit?kind=Customers", "/Engagements", "/Engagements/Edit", "/Billing", "/Billing/Schedule", "/Work", "/Work/Assignments", "/Payments", "/Payments/Create", "/Home/Reports", "/Home/Export", "/Users", "/Users/Create", "/Account/Password" }) { var response = await client.GetAsync(path); Assert.True(response.StatusCode == HttpStatusCode.OK, path + ": " + response.StatusCode); }
        using (var scope = app.Services.CreateScope()) { var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>(); var user = new AppUser { Email = "user@example.com", UserName = "user@example.com" }; Seed.Check(await manager.CreateAsync(user, "User-Password!123")); Seed.Check(await manager.AddToRoleAsync(user, AppRoles.InternalUser)); }
        using var normal = app.CreateClient(new() { AllowAutoRedirect = false }); var page = await normal.GetStringAsync("/Account/Login"); token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        await normal.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "user@example.com" }, { "Password", "User-Password!123" }, { "__RequestVerificationToken", token } })); var denied = await normal.GetAsync("/Users"); Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode); Assert.Contains("Denied", denied.Headers.Location!.ToString());
    }
    [PostgresFact]
    public async Task LoginLockoutAndDisabledSessionRevocation()
    {
        await using var db = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "test-keys")));
        using (var scope = app.Services.CreateScope()) await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "security@example.com" }, { "BootstrapAdmin:Password", "Security-Password!123" } }).Build());
        async Task<HttpResponseMessage> Login(HttpClient client, string password)
        {
            var html = await client.GetStringAsync("/Account/Login");
            var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
            return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "security@example.com" }, { "Password", password }, { "__RequestVerificationToken", token } }));
        }
        using var session = app.CreateClient(new() { AllowAutoRedirect = false }); Assert.Equal(HttpStatusCode.Redirect, (await Login(session, "Security-Password!123")).StatusCode);
        using var failures = app.CreateClient(new() { AllowAutoRedirect = false });
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await Login(failures, "Wrong-Password!123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(failures, "Security-Password!123")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>(); var u = (await manager.FindByEmailAsync("security@example.com"))!;
            Assert.True(await manager.IsLockedOutAsync(u)); u.IsActive = false; Seed.Check(await manager.UpdateAsync(u)); Seed.Check(await manager.UpdateSecurityStampAsync(u));
        }
        var revoked = await session.GetAsync("/"); Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode); Assert.Contains("/Account/Login", revoked.Headers.Location!.ToString());
    }

    [Fact]
    public void RoleLinkRulesRejectInvalidCombinations()
    {
        Assert.True(UserAccessRules.IsConsistent(new AppUser(), AppRoles.Admin));
        Assert.True(UserAccessRules.IsConsistent(new AppUser(), AppRoles.InternalUser));
        Assert.True(UserAccessRules.IsConsistent(new AppUser { BusinessPartyId = 1 }, AppRoles.AccountingFirm));
        Assert.True(UserAccessRules.IsConsistent(new AppUser { ManagerId = 1 }, AppRoles.Manager));
        Assert.True(UserAccessRules.IsConsistent(new AppUser { WorkerId = 1 }, AppRoles.Worker));
        Assert.False(UserAccessRules.IsConsistent(new AppUser(), AppRoles.Worker));
        Assert.False(UserAccessRules.IsConsistent(new AppUser { BusinessPartyId = 1, ManagerId = 2 }, AppRoles.AccountingFirm));
        Assert.False(UserAccessRules.IsConsistent(new AppUser { WorkerId = 1 }, AppRoles.InternalUser));
    }

    [PostgresFact]
    public async Task EntityScopedAuthorizationProtectsPagesActionsReportsAndSessions()
    {
        await using var reset = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "scope-test-keys")));
        const string password = "Scope-Password!123";
        int firmAId, firmBId, managerAId, managerBId, workerAId, workerBId, billAId, billBId, workAId, workBId;
        long workBVersion;
        string internalId, firmBUserId;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "scope-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var firmA = new BusinessParty { Name = "Alpha Accounting Firm" }; var firmB = new BusinessParty { Name = "Beta Accounting Firm" };
            var managerA = new Manager { Name = "Alpha Manager" }; var managerB = new Manager { Name = "Beta Manager" };
            var workerA = new Worker { Name = "Alpha Worker" }; var workerB = new Worker { Name = "Beta Worker" };
            var service = new Service { Name = "Scoped bookkeeping" };
            var engagementA = new Engagement { Customer = new() { Name = "Alpha Scope Customer" }, Service = service, BusinessParty = firmA, Manager = managerA, StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 } };
            var engagementB = new Engagement { Customer = new() { Name = "Beta Scope Customer" }, Service = service, BusinessParty = firmB, Manager = managerB, StartDate = new(2026, 1, 1), BillingAmount = 2000m, Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 } };
            db.AddRange(workerA, workerB, engagementA, engagementB); await db.SaveChangesAsync();
            firmAId = firmA.Id; firmBId = firmB.Id; managerAId = managerA.Id; managerBId = managerB.Id; workerAId = workerA.Id; workerBId = workerB.Id;
            var finance = new BillingService(db);
            var billA = await finance.Generate(engagementA.Id, new(2026, 1, 1), new(2026, 1, 31));
            var billB = await finance.Generate(engagementB.Id, new(2026, 1, 1), new(2026, 1, 31));
            await finance.Assign(billA.WorkItem.Id, workerA.Id, 70m); await finance.Assign(billB.WorkItem.Id, workerB.Id, 60m);
            await finance.UpdateBillingStatus(billA.Id, BillingStatus.Billed, new(2026, 1, 31), "ALPHA-INV", billA.Version);
            await finance.UpdateBillingStatus(billB.Id, BillingStatus.Billed, new(2026, 1, 31), "BETA-INV", billB.Version);
            await finance.Receive(billA.Id, new(2026, 1, 31), 100m, "ALPHA-RECEIPT", Guid.NewGuid());
            await finance.Receive(billB.Id, new(2026, 1, 31), 200m, "BETA-RECEIPT", Guid.NewGuid());
            var assignmentA = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == billA.WorkItem.Id);
            var assignmentB = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == billB.WorkItem.Id);
            await finance.Pay(workerA.Id, new(2026, 1, 31), "ALPHA-PAYMENT", Guid.NewGuid(), new() { [assignmentA.Id] = 100m });
            await finance.Pay(workerB.Id, new(2026, 1, 31), "BETA-PAYMENT", Guid.NewGuid(), new() { [assignmentB.Id] = 100m });
            billAId = billA.Id; billBId = billB.Id; workAId = billA.WorkItem.Id; workBId = billB.WorkItem.Id;
            workBVersion = (await db.WorkItems.SingleAsync(x => x.Id == workBId)).Version;
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            async Task<AppUser> AddUser(string email, string role, int? businessPartyId = null, int? managerId = null, int? workerId = null)
            {
                var user = new AppUser { Email = email, UserName = email, EmailConfirmed = true, BusinessPartyId = businessPartyId, ManagerId = managerId, WorkerId = workerId };
                Seed.Check(await users.CreateAsync(user, password)); Seed.Check(await users.AddToRoleAsync(user, role)); return user;
            }
            await AddUser("firm-a@example.com", AppRoles.AccountingFirm, businessPartyId: firmAId);
            firmBUserId = (await AddUser("firm-b@example.com", AppRoles.AccountingFirm, businessPartyId: firmBId)).Id;
            await AddUser("manager-a@example.com", AppRoles.Manager, managerId: managerAId);
            await AddUser("manager-b@example.com", AppRoles.Manager, managerId: managerBId);
            await AddUser("worker-a@example.com", AppRoles.Worker, workerId: workerAId);
            await AddUser("worker-b@example.com", AppRoles.Worker, workerId: workerBId);
            internalId = (await AddUser("internal@example.com", AppRoles.InternalUser)).Id;
        }

        using var firmClient = await SignedIn(app, "firm-a@example.com", password);
        var firmDashboard = await firmClient.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", firmDashboard); Assert.DoesNotContain("Beta Scope Customer", firmDashboard); Assert.Contains("1,000.00", firmDashboard); Assert.DoesNotContain("2,000.00", firmDashboard);
        var firmBilling = await firmClient.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", firmBilling); Assert.DoesNotContain("Beta Scope Customer", firmBilling);
        Assert.Equal(HttpStatusCode.NotFound, (await firmClient.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
        var firmDetails = await firmClient.GetStringAsync($"/Billing/Details/{billAId}"); Assert.Contains("Your firm share", firmDetails); Assert.Contains("ALPHA-RECEIPT", firmDetails); Assert.DoesNotContain("Your manager share", firmDetails); Assert.DoesNotContain("LCM retained", firmDetails); Assert.DoesNotContain("Worker entitlement", firmDetails);
        var firmReport = await firmClient.GetStringAsync("/Home/Reports"); Assert.Contains("Your firm share", firmReport); Assert.DoesNotContain("Manager share", firmReport); Assert.DoesNotContain("LCM MGT gross", firmReport); Assert.DoesNotContain("Worker cost", firmReport); Assert.DoesNotContain("Beta Scope Customer", firmReport);
        var firmCsv = await firmClient.GetStringAsync("/Home/Export"); Assert.Contains("Alpha Scope Customer", firmCsv); Assert.DoesNotContain("Beta Scope Customer", firmCsv); Assert.DoesNotContain("LCM gross", firmCsv); Assert.DoesNotContain("Worker entitlement", firmCsv);

        using var managerClient = await SignedIn(app, "manager-a@example.com", password);
        var managerDashboard = await managerClient.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", managerDashboard); Assert.DoesNotContain("Beta Scope Customer", managerDashboard); Assert.Contains("1,000.00", managerDashboard); Assert.DoesNotContain("2,000.00", managerDashboard);
        var managerEngagements = await managerClient.GetStringAsync("/Engagements"); Assert.Contains("Alpha Scope Customer", managerEngagements); Assert.DoesNotContain("Beta Scope Customer", managerEngagements);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
        var managerDetails = await managerClient.GetStringAsync($"/Billing/Details/{billAId}"); Assert.Contains("Your manager share", managerDetails); Assert.Contains("Work status", managerDetails); Assert.DoesNotContain("Customer receipt history", managerDetails); Assert.DoesNotContain("LCM retained", managerDetails); Assert.DoesNotContain("Worker entitlement", managerDetails);
        var managerReport = await managerClient.GetStringAsync("/Home/Reports"); Assert.Contains("Your manager share", managerReport); Assert.DoesNotContain("LCM MGT gross", managerReport); Assert.DoesNotContain("Worker cost", managerReport); Assert.DoesNotContain("Beta Scope Customer", managerReport);
        var managerCsv = await managerClient.GetStringAsync("/Home/Export"); Assert.Contains("Alpha Scope Customer", managerCsv); Assert.DoesNotContain("Beta Scope Customer", managerCsv); Assert.Contains("Manager share MYR", managerCsv); Assert.DoesNotContain("LCM gross", managerCsv); Assert.DoesNotContain("Worker entitlement", managerCsv);

        using var workerClient = await SignedIn(app, "worker-a@example.com", password);
        var workerDashboard = await workerClient.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", workerDashboard); Assert.DoesNotContain("Beta Scope Customer", workerDashboard); Assert.Contains("280.00", workerDashboard); Assert.DoesNotContain("480.00", workerDashboard);
        var workerWork = await workerClient.GetStringAsync("/Work"); Assert.Contains("Alpha Scope Customer", workerWork); Assert.DoesNotContain("Beta Scope Customer", workerWork);
        Assert.Equal(HttpStatusCode.NotFound, (await workerClient.GetAsync($"/Work/Details/{workBId}")).StatusCode);
        var workerDetails = await workerClient.GetStringAsync($"/Work/Details/{workAId}"); Assert.Contains("Alpha Worker", workerDetails); Assert.Contains("Entitlement", workerDetails); Assert.DoesNotContain("Beta Worker", workerDetails); Assert.DoesNotContain("LCM gross", workerDetails); Assert.DoesNotContain("Pay worker", workerDetails);
        var workerPayments = await workerClient.GetStringAsync("/Payments"); Assert.Contains("ALPHA-PAYMENT", workerPayments); Assert.DoesNotContain("BETA-PAYMENT", workerPayments); Assert.DoesNotContain("Beta Worker", workerPayments);
        var workerReport = await workerClient.GetStringAsync("/Home/Reports"); Assert.Contains("Your entitlement", workerReport); Assert.DoesNotContain("Accounting firm share", workerReport); Assert.DoesNotContain("Manager share", workerReport); Assert.DoesNotContain("LCM MGT gross", workerReport); Assert.DoesNotContain("Beta Scope Customer", workerReport);
        var workerCsv = await workerClient.GetStringAsync("/Home/Export"); Assert.Contains("Alpha Scope Customer", workerCsv); Assert.DoesNotContain("Beta Scope Customer", workerCsv); Assert.DoesNotContain("Firm share", workerCsv); Assert.DoesNotContain("LCM", workerCsv);
        var billingDenied = await workerClient.GetAsync($"/Billing/Details/{billAId}"); Assert.Equal(HttpStatusCode.Redirect, billingDenied.StatusCode); Assert.Contains("Denied", billingDenied.Headers.Location!.ToString());
        var forgedWork = await PostWithToken(workerClient, $"/Work/Details/{workAId}", "/Work/Update", new() { ["id"] = workBId.ToString(), ["version"] = workBVersion.ToString(), ["status"] = WorkStatus.Completed.ToString(), ["notes"] = "forged" });
        Assert.Equal(HttpStatusCode.NotFound, forgedWork.StatusCode);
        var workerDirectory = await workerClient.GetStringAsync("/Masters?kind=Workers"); Assert.Contains("Alpha Worker", workerDirectory); Assert.DoesNotContain("Beta Worker", workerDirectory);

        using var admin = await SignedIn(app, "scope-admin@example.com", password);
        var adminDashboard = await admin.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", adminDashboard); Assert.Contains("Beta Scope Customer", adminDashboard);
        var adminBilling = await admin.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", adminBilling); Assert.Contains("Beta Scope Customer", adminBilling); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
        using var internalUser = await SignedIn(app, "internal@example.com", password);
        var internalBilling = await internalUser.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", internalBilling); Assert.Contains("Beta Scope Customer", internalBilling);
        var usersDenied = await internalUser.GetAsync("/Users"); Assert.Equal(HttpStatusCode.Redirect, usersDenied.StatusCode); Assert.Contains("Denied", usersDenied.Headers.Location!.ToString());

        var invalidUpdate = await PostWithToken(admin, "/Users", "/Users/Update", new() { ["id"] = firmBUserId, ["role"] = AppRoles.AccountingFirm, ["active"] = "true", ["managerId"] = managerBId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, invalidUpdate.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var unchanged = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(x => x.Id == firmBUserId);
            Assert.Equal(firmBId, unchanged.BusinessPartyId); Assert.Null(unchanged.ManagerId);
        }

        var changed = await PostWithToken(admin, "/Users", "/Users/Update", new() { ["id"] = internalId, ["role"] = AppRoles.AccountingFirm, ["active"] = "true", ["businessPartyId"] = firmAId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        var revoked = await internalUser.GetAsync("/"); Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode); Assert.Contains("/Account/Login", revoked.Headers.Location!.ToString());
    }

}
