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
        using (var scope = app.Services.CreateScope()) { var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>(); var user = new AppUser { Email = "user@example.com", UserName = "user@example.com" }; Seed.Check(await manager.CreateAsync(user, "User-Password!123")); Seed.Check(await manager.AddToRoleAsync(user, "User")); }
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

}