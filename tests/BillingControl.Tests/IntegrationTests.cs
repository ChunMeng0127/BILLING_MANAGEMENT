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
public partial class IntegrationTests
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
    private static async Task<HttpResponseMessage> PostWithToken(HttpClient client, string tokenPage, string target, Dictionary<string, string> fields, string? referer = null)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(tokenPage));
        using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new FormUrlEncodedContent(fields) };
        if (!string.IsNullOrWhiteSpace(referer)) request.Headers.Referrer = new Uri(client.BaseAddress!, referer);
        return await client.SendAsync(request);
    }
    [PostgresFact]
    public async Task AllocationFormsAcceptBlankRowsAndReportUsefulValidation()
    {
        await using var db = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "allocation-test-keys")));
        const string password = "Allocation-Password!123";
        int workerId, bill1Id, bill2Id, bill3Id, assignment1Id, assignment2Id, assignment3Id;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "allocation-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var worker = new Worker { Name = "Allocation Worker" };
            var engagement = new Engagement
            {
                Customer = new Customer { Name = "Allocation Customer" },
                Service = new Service { Name = "Allocation Service" },
                BusinessParty = new BusinessParty { Name = "Allocation Firm" },
                Manager = new Manager { Name = "Allocation Manager" },
                StartDate = new(2026, 1, 1), BillingAmount = 1000m,
                Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
            };
            context.AddRange(worker, engagement); await context.SaveChangesAsync(); workerId = worker.Id;
            var finance = new BillingService(context);
            var first = await finance.Generate(engagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
            var second = await finance.Generate(engagement.Id, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
            var third = await finance.Generate(engagement.Id, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled);
            await finance.Assign(first.WorkItem.Id, worker.Id, 50m); await finance.Assign(second.WorkItem.Id, worker.Id, 50m); await finance.Assign(third.WorkItem.Id, worker.Id, 50m);
            bill1Id = first.Id; bill2Id = second.Id; bill3Id = third.Id;
            assignment1Id = await context.WorkerAssignments.Where(x => x.WorkItemId == first.WorkItem.Id).Select(x => x.Id).SingleAsync();
            assignment2Id = await context.WorkerAssignments.Where(x => x.WorkItemId == second.WorkItem.Id).Select(x => x.Id).SingleAsync();
            assignment3Id = await context.WorkerAssignments.Where(x => x.WorkItemId == third.WorkItem.Id).Select(x => x.Id).SingleAsync();
        }

        using var admin = await SignedIn(app, "allocation-admin@example.com", password);
        static string Value(decimal amount) => amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Dictionary<string, string> InvoiceFields(string number, string first, string second, string third) => new()
        {
            ["InvoiceNumber"] = number, ["InvoiceDate"] = "2026-03-31", ["Flow"] = InvoiceFlow.AccountingFirmToCustomer.ToString(),
            [$"Allocations[{bill1Id}]"] = first, [$"Allocations[{bill2Id}]"] = second, [$"Allocations[{bill3Id}]"] = third
        };
        var one = await PostWithToken(admin, "/Invoices/Create", "/Invoices/Create", InvoiceFields("WEB-INV-ONE", Value(400), "", ""));
        Assert.Equal(HttpStatusCode.Redirect, one.StatusCode);
        var multi = await PostWithToken(admin, "/Invoices/Create", "/Invoices/Create", InvoiceFields("WEB-INV-MULTI", Value(100), Value(200), ""));
        Assert.Equal(HttpStatusCode.Redirect, multi.StatusCode);

        var blank = await PostWithToken(admin, "/Invoices/Create", "/Invoices/Create", InvoiceFields("WEB-INV-BLANK", "", "", ""), "/Invoices/Create");
        Assert.Equal(HttpStatusCode.Redirect, blank.StatusCode); Assert.EndsWith("/Invoices/Create", blank.Headers.Location!.ToString());
        var blankPage = await admin.GetStringAsync("/Invoices/Create"); Assert.Contains("Enter an amount for at least one billing record.", blankPage);
        var negative = await PostWithToken(admin, "/Invoices/Create", "/Invoices/Create", InvoiceFields("WEB-INV-NEGATIVE", "-1", "", ""), "/Invoices/Create");
        Assert.Equal(HttpStatusCode.Redirect, negative.StatusCode); Assert.Contains("Invoice allocations must be positive amounts", await admin.GetStringAsync("/Invoices/Create"));
        var invalid = await PostWithToken(admin, "/Invoices/Create", "/Invoices/Create", InvoiceFields("WEB-INV-INVALID", "abc", "", ""), "/Invoices/Create");
        Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode); Assert.Contains("Enter a valid allocation amount", await admin.GetStringAsync("/Invoices/Create"));

        int invoiceOneId, invoiceMultiId;
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceOneId = await context.Invoices.Where(x => x.InvoiceNumber == "WEB-INV-ONE").Select(x => x.Id).SingleAsync();
            invoiceMultiId = await context.Invoices.Where(x => x.InvoiceNumber == "WEB-INV-MULTI").Select(x => x.Id).SingleAsync();
        }
        var receipt = await PostWithToken(admin, "/Invoices/Receipt", "/Invoices/Receive", new()
        {
            ["ReceiptDate"] = "2026-03-31", ["Reference"] = "WEB-RECEIPT", ["RequestId"] = Guid.NewGuid().ToString(),
            [$"Allocations[{invoiceOneId}]"] = "100.00", [$"Allocations[{invoiceMultiId}]"] = ""
        });
        Assert.Equal(HttpStatusCode.Redirect, receipt.StatusCode);

        var payment = await PostWithToken(admin, $"/Payments/Create?workerId={workerId}", "/Payments/Create", new()
        {
            ["workerId"] = workerId.ToString(), ["date"] = "2026-03-31", ["reference"] = "WEB-PAYMENT", ["requestId"] = Guid.NewGuid().ToString(),
            [$"amounts[{assignment1Id}]"] = "50.00", [$"amounts[{assignment2Id}]"] = "", [$"amounts[{assignment3Id}]"] = ""
        });
        Assert.Equal(HttpStatusCode.Redirect, payment.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await context.CustomerReceiptAllocations.CountAsync(x => x.Amount == 100m));
            Assert.Equal(1, await context.WorkerPaymentAllocations.CountAsync(x => x.Amount == 50m));
        }
    }
    [PostgresFact]
    public async Task FinancialWorkflowSnapshotsDuplicatesAndPayments()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var service = new BillingService(db);
        var bill = await service.Generate(id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        Assert.Equal(new DateOnly(2026, 2, 1), (await db.BillingSchedules.SingleAsync()).NextPeriodStart);
        await Assert.ThrowsAsync<BusinessException>(() => service.Generate(id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled));
        await Assert.ThrowsAsync<BusinessException>(() => service.Generate(id, new(2026, 1, 15), new(2026, 2, 15), BillingGenerationMode.Scheduled));
        var e = await db.Engagements.SingleAsync(); e.FirmPercent = 10; e.ManagerPercent = 20; e.LcmPercent = 70; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var old = await db.BillingRecords.Include(x => x.Shares).SingleAsync(); Assert.Equal(400m, old.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount); Assert.Equal(40m, old.Shares.Single(x => x.Kind == ShareKind.Lcm).Percent);
        var w = new Worker { Name = "Worker 1", Type = WorkerType.Freelancer }; db.Add(w); await db.SaveChangesAsync();
        await service.Assign(bill.WorkItem.Id, w.Id, 70);
        var a = await db.WorkerAssignments.SingleAsync(); Assert.Equal(280m, a.Entitlement);
        await service.Pay(w.Id, new(2026, 1, 31), "PARTIAL", Guid.NewGuid(), new() { [a.Id] = 100 });
        await Assert.ThrowsAsync<BusinessException>(() => service.Pay(w.Id, new(2026, 1, 31), "OVER", Guid.NewGuid(), new() { [a.Id] = 181 }));
        var next = await service.Generate(id, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled); await service.Assign(next.WorkItem.Id, w.Id, 40);
        var second = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == next.WorkItem.Id); Assert.Equal(280m, second.Entitlement);
        var request = Guid.NewGuid(); await service.Pay(w.Id, new(2026, 2, 28), "MULTI", request, new() { [a.Id] = 180, [second.Id] = 100 });
        Assert.Equal(380m, await db.WorkerPaymentAllocations.SumAsync(x => x.Amount));
        await Assert.ThrowsAsync<BusinessException>(() => service.Pay(w.Id, new(2026, 2, 28), "MULTI", request, new() { [second.Id] = 100 }));
        await Assert.ThrowsAsync<BusinessException>(() => service.Cancel("assignment", a.Id, "Paid"));
        var payment = await db.WorkerPayments.SingleAsync(x => x.RequestId == request); await service.Cancel("payment", payment.Id, "Reversed");
        Assert.Equal(100m, await db.WorkerPaymentAllocations.Where(x => !x.WorkerPayment.IsCancelled).SumAsync(x => x.Amount));
        old = await db.BillingRecords.SingleAsync(x => x.Id == bill.Id);
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateBillingStatus(old.Id, BillingStatus.Billed, old.Version));
        var invoiceService = new InvoiceService(db);
        var customerInvoice = await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "INV-001", new(2026, 1, 31), new Dictionary<int, decimal> { [old.Id] = old.Amount });
        await invoiceService.CreateReceipt(new(2026, 1, 31), "BANK", Guid.NewGuid(), new Dictionary<int, decimal> { [customerInvoice.Id] = 250m }); Assert.Equal(BillingStatus.PartiallyPaid, old.Status);
        await Assert.ThrowsAsync<BusinessException>(() => invoiceService.CreateReceipt(new(2026, 1, 31), "OVER", Guid.NewGuid(), new Dictionary<int, decimal> { [customerInvoice.Id] = 751m }));
        var receipt = await db.CustomerReceipts.SingleAsync(); await service.Cancel("receipt", receipt.Id, "Correction"); Assert.Equal(BillingStatus.Billed, old.Status);
        old.Amount = 2000; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [PostgresFact]
    public async Task DatabaseRejectsDuplicatePeriodAndOverAllocationAndForeignWorker()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var svc = new BillingService(db); var bill = await svc.Generate(id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var workers = new[] { new Worker { Name = "One" }, new Worker { Name = "Two" } }; db.AddRange(workers); await db.SaveChangesAsync();
        await svc.Assign(bill.WorkItem.Id, workers[0].Id, 70);
        await Assert.ThrowsAsync<BusinessException>(() => svc.Assign(bill.WorkItem.Id, workers[1].Id, 40));
        var a = await db.WorkerAssignments.SingleAsync(); await Assert.ThrowsAsync<BusinessException>(() => svc.Pay(workers[1].Id, new(2026, 1, 31), "WRONG", Guid.NewGuid(), new() { [a.Id] = 1 }));
        await using var other = Db(); other.BillingRecords.Add(new() { EngagementId = id, PeriodStart = new(2026, 1, 1), PeriodEnd = new(2026, 1, 31), Amount = 1000, CustomerName = "Duplicate", ServiceName = "Test" });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        await svc.Cancel("assignment", a.Id, "Reassign"); await svc.Cancel("billing", bill.Id, "Replace"); var replacement = await svc.Generate(id, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled); Assert.NotEqual(bill.Id, replacement.Id);
    }
    [PostgresFact]
    public async Task InvoiceDocumentsSupportSplitsConsolidationLimitsReceiptsCancellationAndConcurrency()
    {
        await using var db = await Fresh();
        var id = await Engagement(db);
        var firstEngagement = await db.Engagements.Include(x => x.BusinessParty).Include(x => x.Manager).SingleAsync(x => x.Id == id);
        var secondEngagement = new Engagement
        {
            Customer = new Customer { Name = "Second customer" },
            Service = new Service { Name = "Bookkeeping" },
            BusinessPartyId = firstEngagement.BusinessPartyId,
            ManagerId = firstEngagement.ManagerId,
            StartDate = new(2026, 1, 1), BillingAmount = 1000,
            Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        var sameCustomerEngagement = new Engagement
        {
            CustomerId = firstEngagement.CustomerId, Service = new Service { Name = "Payroll" },
            BusinessPartyId = firstEngagement.BusinessPartyId, ManagerId = firstEngagement.ManagerId,
            StartDate = new(2026, 1, 1), BillingAmount = 1000,
            Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        var otherFirmEngagement = new Engagement
        {
            CustomerId = firstEngagement.CustomerId, Service = new Service { Name = "Tax" },
            BusinessParty = new BusinessParty { Name = "Other Firm" }, ManagerId = firstEngagement.ManagerId,
            StartDate = new(2026, 1, 1), BillingAmount = 1000,
            Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        db.AddRange(secondEngagement, sameCustomerEngagement, otherFirmEngagement); await db.SaveChangesAsync();
        var billing = new BillingService(db);
        var bill1 = await billing.Generate(id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var bill2 = await billing.Generate(secondEngagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var sameCustomerBill = await billing.Generate(sameCustomerEngagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var otherFirmBill = await billing.Generate(otherFirmEngagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var invoices = new InvoiceService(db);

        var sameCustomerConsolidated = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "CUST-CONSOLIDATED", new(2026, 1, 31), new Dictionary<int, decimal> { [bill1.Id] = 100, [sameCustomerBill.Id] = 100 });
        Assert.Equal(2, sameCustomerConsolidated.Lines.Count);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "MIXED-CUSTOMERS", new(2026, 1, 31), new Dictionary<int, decimal> { [bill1.Id] = 1, [bill2.Id] = 1 }));
        var customerPart1 = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "CUST-001-A", new(2026, 1, 31), new Dictionary<int, decimal> { [bill1.Id] = 500 });
        var customerPart2 = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "CUST-001-B", new(2026, 2, 1), new Dictionary<int, decimal> { [bill1.Id] = 400 });
        db.ChangeTracker.Clear();
        var bill1State = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).SingleAsync(x => x.Id == bill1.Id);
        Assert.Equal(BillingInvoiceState.FullyInvoiced, bill1State.CustomerInvoiceState); Assert.Equal(BillingStatus.Billed, bill1State.Status);
        Assert.Equal(1000m, bill1State.CustomerInvoicedAmount);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "CUST-001-OVER", new(2026, 2, 2), new Dictionary<int, decimal> { [bill1.Id] = 1 }));

        var consolidated = await invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "MGR-001", new(2026, 1, 31), new Dictionary<int, decimal> { [bill1.Id] = 250, [bill2.Id] = 250 });
        Assert.Equal(2, consolidated.Lines.Count);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "MGR-OVER", new(2026, 2, 1), new Dictionary<int, decimal> { [bill1.Id] = 1 }));
        var lcm = await invoices.CreateInvoice(InvoiceFlow.LcmToManager, "LCM-001", new(2026, 1, 31), new Dictionary<int, decimal> { [bill1.Id] = 200, [bill2.Id] = 200 });
        Assert.Equal(400m, lcm.Total);
        Assert.Equal(2, lcm.Lines.Count);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.LcmToManager, "LCM-OVER", new(2026, 2, 1), new Dictionary<int, decimal> { [bill1.Id] = 201 }));

        await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "SHARED-NUMBER", new(2026, 2, 1), new Dictionary<int, decimal> { [sameCustomerBill.Id] = 1 });
        await invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "SHARED-NUMBER", new(2026, 2, 1), new Dictionary<int, decimal> { [sameCustomerBill.Id] = 1 });
        await invoices.CreateInvoice(InvoiceFlow.LcmToManager, "SHARED-NUMBER", new(2026, 2, 1), new Dictionary<int, decimal> { [sameCustomerBill.Id] = 1 });
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "SHARED-NUMBER", new(2026, 2, 1), new Dictionary<int, decimal> { [sameCustomerBill.Id] = 1 }));

        var otherCustomerInvoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "OTHER-CUSTOMER", new(2026, 2, 1), new Dictionary<int, decimal> { [bill2.Id] = 100 });
        var otherFirmInvoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "SHARED-NUMBER", new(2026, 2, 1), new Dictionary<int, decimal> { [otherFirmBill.Id] = 100 });
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateReceipt(new(2026, 2, 2), "MIXED-CUSTOMERS", Guid.NewGuid(), new Dictionary<int, decimal> { [customerPart1.Id] = 1, [otherCustomerInvoice.Id] = 1 }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateReceipt(new(2026, 2, 2), "MIXED-FIRMS", Guid.NewGuid(), new Dictionary<int, decimal> { [customerPart1.Id] = 1, [otherFirmInvoice.Id] = 1 }));
        var firstReceipt = await invoices.CreateReceipt(new(2026, 2, 2), "RCPT-1", Guid.NewGuid(), new Dictionary<int, decimal> { [customerPart1.Id] = 200 });
        Assert.Equal(InvoiceStatus.PartiallyPaid, (await db.Invoices.SingleAsync(x => x.Id == customerPart1.Id)).Status);
        var secondReceipt = await invoices.CreateReceipt(new(2026, 2, 3), "RCPT-2", Guid.NewGuid(), new Dictionary<int, decimal> { [customerPart1.Id] = 300, [customerPart2.Id] = 400 });
        Assert.Equal(InvoiceStatus.Paid, (await db.Invoices.SingleAsync(x => x.Id == customerPart1.Id)).Status);
        Assert.Equal(InvoiceStatus.Paid, (await db.Invoices.SingleAsync(x => x.Id == customerPart2.Id)).Status);
        Assert.Equal(900m, (await db.CustomerReceipts.Include(x => x.Allocations).SumAsync(x => x.Amount)));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateReceipt(new(2026, 2, 4), "RCPT-OVER", Guid.NewGuid(), new Dictionary<int, decimal> { [customerPart1.Id] = 1 }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CancelInvoice(customerPart1.Id, "Has receipt"));
        await invoices.CancelInvoice(lcm.Id, "LCM correction");
        Assert.Equal(InvoiceStatus.Cancelled, (await db.Invoices.SingleAsync(x => x.Id == lcm.Id)).Status);
        await invoices.CancelReceipt(firstReceipt.Id, "Correction");
        Assert.True((await db.CustomerReceipts.SingleAsync(x => x.Id == firstReceipt.Id)).IsCancelled);
        var snapshots = await db.BillingRecords.Include(x => x.Shares).Where(x => x.Id == bill1.Id).SingleAsync();
        var originalLcm = snapshots.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount;
        firstEngagement.BillingAmount = 2000; firstEngagement.FirmPercent = 10; firstEngagement.ManagerPercent = 20; firstEngagement.LcmPercent = 70; await db.SaveChangesAsync();
        Assert.Equal(originalLcm, (await db.BillingRecords.Include(x => x.Shares).SingleAsync(x => x.Id == bill1.Id)).Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);

        var bill3 = await billing.Generate(id, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        var c1 = Db(); var c2 = Db();
        async Task Attempt(AppDbContext context, string number)
        {
            try { await new InvoiceService(context).CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, number, new(2026, 2, 28), new Dictionary<int, decimal> { [bill3.Id] = 600 }); }
            catch (BusinessException) { }
        }
        await Task.WhenAll(Attempt(c1, "CONCURRENT-A"), Attempt(c2, "CONCURRENT-B")); await c1.DisposeAsync(); await c2.DisposeAsync();
        Assert.Equal(600m, await db.InvoiceLines.Where(x => x.BillingRecordId == bill3.Id && x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Invoice.Status != InvoiceStatus.Cancelled).SumAsync(x => x.AllocatedAmount));

        var bill4 = await billing.Generate(id, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled);
        var receiptInvoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "RECEIPT-CONCURRENCY", new(2026, 3, 31), new Dictionary<int, decimal> { [bill4.Id] = 1000 });
        async Task ReceiptAttempt()
        {
            await using var context = Db();
            try { await new InvoiceService(context).CreateReceipt(new(2026, 3, 31), "Concurrent receipt", Guid.NewGuid(), new Dictionary<int, decimal> { [receiptInvoice.Id] = 600 }); }
            catch (BusinessException) { }
        }
        await Task.WhenAll(ReceiptAttempt(), ReceiptAttempt());
        Assert.Equal(600m, await db.CustomerReceiptAllocations.Where(x => x.InvoiceId == receiptInvoice.Id && !x.CustomerReceipt.IsCancelled).SumAsync(x => x.Amount));
    }
    [PostgresFact]
    public async Task InvoiceMigrationPreservesLegacyBillingAndReceipts()
    {
        await using var db = Db(); await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync("20260907031528_EntityScopedAccess");
        var id = await Engagement(db);
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"BillingRecords\" (\"EngagementId\", \"PeriodStart\", \"PeriodEnd\", \"BillingDate\", \"InvoiceNumber\", \"Status\", \"Amount\", \"CustomerName\", \"ServiceName\", \"CancellationReason\", \"CreatedAt\", \"CreatedBy\", \"UpdatedAt\", \"UpdatedBy\", \"Version\") VALUES ({id}, {new DateOnly(2026, 1, 1)}, {new DateOnly(2026, 1, 31)}, {new DateOnly(2026, 1, 31)}, {"OLD-001"}, {(int)BillingStatus.Billed}, {1000m}, {"Test customer"}, {"Bookkeeping"}, {null}, {now}, {"legacy"}, {now}, {"legacy"}, {1})");
        var legacyBillingId = await db.Database.SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"BillingRecords\" WHERE \"InvoiceNumber\" = {"OLD-001"}").SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"RevenueShareAllocations\" (\"BillingRecordId\", \"Kind\", \"PartyName\", \"Percent\", \"Amount\", \"CreatedAt\", \"CreatedBy\", \"UpdatedAt\", \"UpdatedBy\", \"Version\") VALUES ({legacyBillingId}, {(int)ShareKind.Firm}, {"X Group"}, {35m}, {350m}, {now}, {"legacy"}, {now}, {"legacy"}, {1}), ({legacyBillingId}, {(int)ShareKind.Manager}, {"Signitive"}, {25m}, {250m}, {now}, {"legacy"}, {now}, {"legacy"}, {1}), ({legacyBillingId}, {(int)ShareKind.Lcm}, {"LCM MGT Sdn Bhd"}, {40m}, {400m}, {now}, {"legacy"}, {now}, {"legacy"}, {1})");
        var request = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CustomerReceipts\" (\"BillingRecordId\", \"ReceiptDate\", \"Amount\", \"Reference\", \"RequestId\", \"IsCancelled\", \"CancellationReason\", \"CreatedAt\", \"CreatedBy\", \"UpdatedAt\", \"UpdatedBy\", \"Version\") VALUES ({legacyBillingId}, {new DateOnly(2026, 2, 1)}, {125m}, {"OLD-RECEIPT"}, {request}, {false}, {null}, {now}, {"legacy"}, {now}, {"legacy"}, {1})");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var invoice = await db.Invoices.Include(x => x.Lines).SingleAsync(x => x.InvoiceNumber == "OLD-001");
        Assert.Equal(1000m, invoice.Total); Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status); Assert.Equal(legacyBillingId, invoice.Lines.Single().BillingRecordId);
        var migratedBilling = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == legacyBillingId);
        Assert.Equal(migratedBilling.Amount, migratedBilling.RevenueShareBaseAmount);
        var migratedShares = await db.RevenueShareAllocations.AsNoTracking().Where(x => x.BillingRecordId == legacyBillingId).ToDictionaryAsync(x => x.Kind, x => x.Amount);
        Assert.Equal(350m, migratedShares[ShareKind.Firm]); Assert.Equal(250m, migratedShares[ShareKind.Manager]); Assert.Equal(400m, migratedShares[ShareKind.Lcm]);
        Assert.Equal(await db.Engagements.Where(x => x.Id == id).Select(x => x.CustomerId).SingleAsync(), invoice.CustomerId);
        var allocation = await db.CustomerReceiptAllocations.Include(x => x.CustomerReceipt).SingleAsync(x => x.InvoiceId == invoice.Id);
        Assert.Equal(125m, allocation.Amount); Assert.Equal("OLD-RECEIPT", allocation.CustomerReceipt.Reference);
        Assert.False(await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='BillingRecords' AND column_name='InvoiceNumber') AS \"Value\"").SingleAsync());
    }
    [PostgresFact]
    public async Task ConcurrentPaymentsCannotOverpay()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var svc = new BillingService(db); var bill = await svc.Generate(id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled); var w = new Worker { Name = "Concurrency" }; db.Add(w); await db.SaveChangesAsync(); await svc.Assign(bill.WorkItem.Id, w.Id, 70); var a = await db.WorkerAssignments.SingleAsync();
        async Task Attempt() { await using var ctx = Db(); try { await new BillingService(ctx).Pay(w.Id, new(2026, 1, 31), "Concurrent", Guid.NewGuid(), new() { [a.Id] = 200 }); } catch (BusinessException) { } }
        await Task.WhenAll(Attempt(), Attempt()); Assert.Equal(200m, await db.WorkerPaymentAllocations.SumAsync(x => x.Amount));
    }
    [PostgresFact]
    public async Task EngagementPartiesFreezeAfterBillingWhileOtherEditsAndOriginalScopeRemain()
    {
        await using var reset = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "engagement-freeze-keys")));
        const string password = "Freeze-Password!123";
        int engagementId, customerId, serviceId, firmId, managerId, otherCustomerId, otherServiceId, otherFirmId, otherManagerId;
        long version, scheduleVersion;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "freeze-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "Frozen Customer" }; var service = new Service { Name = "Frozen Service" };
            var firm = new BusinessParty { Name = "Original Firm" }; var manager = new Manager { Name = "Original Manager" };
            var otherCustomer = new Customer { Name = "Replacement Customer" }; var otherService = new Service { Name = "Replacement Service" };
            var otherFirm = new BusinessParty { Name = "Replacement Firm" }; var otherManager = new Manager { Name = "Replacement Manager" };
            var engagement = new Engagement { Customer = customer, Service = service, BusinessParty = firm, Manager = manager, StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 } };
            db.AddRange(engagement, otherCustomer, otherService, otherFirm, otherManager); await db.SaveChangesAsync();
            await new BillingService(db).Generate(engagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
            engagementId = engagement.Id; customerId = customer.Id; serviceId = service.Id; firmId = firm.Id; managerId = manager.Id;
            otherCustomerId = otherCustomer.Id; otherServiceId = otherService.Id; otherFirmId = otherFirm.Id; otherManagerId = otherManager.Id;
            var loadedEngagement = await db.Engagements.Include(x => x.Schedule).AsNoTracking().SingleAsync(x => x.Id == engagementId);
            version = loadedEngagement.Version; scheduleVersion = loadedEngagement.Schedule.Version;
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            async Task AddUser(string email, string role, int? businessPartyId = null, int? linkedManagerId = null)
            {
                var user = new AppUser { Email = email, UserName = email, EmailConfirmed = true, BusinessPartyId = businessPartyId, ManagerId = linkedManagerId };
                Seed.Check(await users.CreateAsync(user, password)); Seed.Check(await users.AddToRoleAsync(user, role));
            }
            await AddUser("original-firm@example.com", AppRoles.AccountingFirm, businessPartyId: firmId);
            await AddUser("replacement-firm@example.com", AppRoles.AccountingFirm, businessPartyId: otherFirmId);
            await AddUser("original-manager@example.com", AppRoles.Manager, linkedManagerId: managerId);
            await AddUser("replacement-manager@example.com", AppRoles.Manager, linkedManagerId: otherManagerId);
        }

        using var admin = await SignedIn(app, "freeze-admin@example.com", password);
        var editUrl = $"/Engagements/Edit/{engagementId}";
        Dictionary<string, string> Form() => new()
        {
            ["Id"] = engagementId.ToString(), ["Version"] = version.ToString(),
            ["ScheduleVersion"] = scheduleVersion.ToString(),
            ["CustomerId"] = customerId.ToString(), ["ServiceId"] = serviceId.ToString(),
            ["BusinessPartyId"] = firmId.ToString(), ["ManagerId"] = managerId.ToString(),
            ["StartDate"] = "2026-01-01", ["BillingAmount"] = "1000.00",
            ["FirmPercent"] = "35", ["ManagerPercent"] = "25", ["LcmPercent"] = "40",
            ["Frequency"] = Frequency.Monthly.ToString(), ["NextPeriodStart"] = "2026-02-01",
            ["AnchorDay"] = "1", ["Status"] = EngagementStatus.Active.ToString(), ["Notes"] = "Original"
        };
        const string frozenMessage = "Customer, service, accounting firm and manager cannot be changed after billing has been generated. End this engagement and create a new engagement for the new arrangement.";
        async Task Reject(string field, int replacementId)
        {
            var form = Form(); form[field] = replacementId.ToString();
            var response = await PostWithToken(admin, editUrl, "/Engagements/Edit", form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(frozenMessage, await response.Content.ReadAsStringAsync());
        }
        await Reject("CustomerId", otherCustomerId);
        await Reject("ServiceId", otherServiceId);
        await Reject("BusinessPartyId", otherFirmId);
        await Reject("ManagerId", otherManagerId);

        var allowed = Form();
        allowed["BillingAmount"] = "1200.00"; allowed["FirmPercent"] = "30"; allowed["ManagerPercent"] = "30"; allowed["LcmPercent"] = "40";
        allowed["Frequency"] = Frequency.Quarterly.ToString(); allowed["NextPeriodStart"] = "2026-04-01"; allowed["AnchorDay"] = "15";
        allowed["EndDate"] = "2026-12-31"; allowed["Status"] = EngagementStatus.Paused.ToString(); allowed["Notes"] = "Updated non-party terms";
        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, editUrl, "/Engagements/Edit", allowed)).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Engagements.Include(x => x.Schedule).AsNoTracking().SingleAsync(x => x.Id == engagementId);
            Assert.Equal(customerId, saved.CustomerId); Assert.Equal(serviceId, saved.ServiceId); Assert.Equal(firmId, saved.BusinessPartyId); Assert.Equal(managerId, saved.ManagerId);
            Assert.Equal(1200m, saved.BillingAmount); Assert.Equal(30m, saved.FirmPercent); Assert.Equal(30m, saved.ManagerPercent); Assert.Equal(40m, saved.LcmPercent);
            Assert.Equal(Frequency.Quarterly, saved.Schedule.Frequency); Assert.Equal(new DateOnly(2026, 4, 1), saved.Schedule.NextPeriodStart); Assert.Equal(15, saved.Schedule.AnchorDay);
            Assert.Equal(new DateOnly(2026, 12, 31), saved.EndDate); Assert.Equal(EngagementStatus.Paused, saved.Status); Assert.Equal("Updated non-party terms", saved.Notes);
        }
        using var originalFirm = await SignedIn(app, "original-firm@example.com", password);
        using var replacementFirm = await SignedIn(app, "replacement-firm@example.com", password);
        using var originalManager = await SignedIn(app, "original-manager@example.com", password);
        using var replacementManager = await SignedIn(app, "replacement-manager@example.com", password);
        Assert.Contains("Frozen Customer", await originalFirm.GetStringAsync("/Billing"));
        Assert.DoesNotContain("Frozen Customer", await replacementFirm.GetStringAsync("/Billing"));
        Assert.Contains("Frozen Customer", await originalManager.GetStringAsync("/Billing"));
        Assert.DoesNotContain("Frozen Customer", await replacementManager.GetStringAsync("/Billing"));
    }
    [PostgresFact]
    public async Task StaffCanEditMastersAndFutureBillingSchedulesWithoutChangingSnapshots()
    {
        await using var reset = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "master-schedule-test-keys")));
        const string password = "Master-Schedule-Password!123";
        int customerId, serviceId, firmId, managerId, workerId, engagementId, billingId, assignmentId;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "master-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "Original customer", Email = "old@example.com", RegistrationNumber = "OLD-1" };
            var service = new Service { Name = "Original service" };
            var firm = new BusinessParty { Name = "Original firm" };
            var manager = new Manager { Name = "Original manager" };
            var worker = new Worker { Name = "Original worker", Type = WorkerType.Self };
            var engagement = new Engagement
            {
                Customer = customer, Service = service, BusinessParty = firm, Manager = manager,
                StartDate = new(2026, 1, 1), BillingAmount = 1000m,
                Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
            };
            db.AddRange(worker, engagement); await db.SaveChangesAsync();
            var bill = await new BillingService(db).Generate(engagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
            await new BillingService(db).Assign(bill.WorkItem.Id, worker.Id, 50m);
            customerId = customer.Id; serviceId = service.Id; firmId = firm.Id; managerId = manager.Id; workerId = worker.Id; engagementId = engagement.Id; billingId = bill.Id;
            assignmentId = await db.WorkerAssignments.Where(x => x.WorkItemId == bill.WorkItem.Id).Select(x => x.Id).SingleAsync();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var externalUser = new AppUser { Email = "master-firm@example.com", UserName = "master-firm@example.com", EmailConfirmed = true, BusinessPartyId = firm.Id };
            Seed.Check(await users.CreateAsync(externalUser, password)); Seed.Check(await users.AddToRoleAsync(externalUser, AppRoles.AccountingFirm));
        }

        using var admin = await SignedIn(app, "master-admin@example.com", password);
        async Task SaveMaster(string kind, int id, string name, string? email = null, string? registration = null, WorkerType? workerType = null, bool active = true)
        {
            long version;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                version = kind switch
                {
                    "Customers" => await db.Customers.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
                    "Services" => await db.Services.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
                    "Firms" => await db.BusinessParties.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
                    "Managers" => await db.Managers.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
                    "Workers" => await db.Workers.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
                    _ => throw new InvalidOperationException()
                };
            }
            var fields = new Dictionary<string, string> { ["Id"] = id.ToString(), ["Version"] = version.ToString(), ["Name"] = name, ["Notes"] = $"Updated {kind}", ["IsActive"] = active ? "true" : "false", ["Type"] = (workerType ?? WorkerType.Self).ToString() };
            if (email != null) fields["Email"] = email;
            if (registration != null) fields["RegistrationNumber"] = registration;
            var response = await PostWithToken(admin, $"/Masters/Edit?kind={kind}&id={id}", $"/Masters/Edit?kind={kind}", fields);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        await SaveMaster("Customers", customerId, "Edited customer", "new@example.com", "NEW-1");
        await SaveMaster("Services", serviceId, "Edited service", active: false);
        using (var scope = app.Services.CreateScope()) Assert.False((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Services.FindAsync(serviceId))!.IsActive);
        await SaveMaster("Services", serviceId, "Edited service");
        await SaveMaster("Firms", firmId, "Edited firm");
        await SaveMaster("Managers", managerId, "Edited manager");
        await SaveMaster("Workers", workerId, "Edited worker", "worker@example.com", workerType: WorkerType.Contractor);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bill = await db.BillingRecords.Include(x => x.Shares).SingleAsync(x => x.Id == billingId);
            var assignment = await db.WorkerAssignments.SingleAsync(x => x.Id == assignmentId);
            Assert.Equal("Original customer", bill.CustomerName); Assert.Equal("Original service", bill.ServiceName);
            Assert.Equal("Original firm", bill.Shares.Single(x => x.Kind == ShareKind.Firm).PartyName);
            Assert.Equal("Original manager", bill.Shares.Single(x => x.Kind == ShareKind.Manager).PartyName);
            Assert.Equal("Original worker", assignment.WorkerName);
            Assert.Equal("Edited customer", (await db.Customers.FindAsync(customerId))!.Name);
            Assert.Equal("Edited worker", (await db.Workers.FindAsync(workerId))!.Name);
        }
        long engagementVersion;
        using (var scope = app.Services.CreateScope()) engagementVersion = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Engagements.Where(x => x.Id == engagementId).Select(x => x.Version).SingleAsync();
        var invalidPercentages = await PostWithToken(admin, $"/Engagements/Edit/{engagementId}", "/Engagements/Edit", new()
        {
            ["Id"] = engagementId.ToString(), ["Version"] = engagementVersion.ToString(), ["CustomerId"] = customerId.ToString(), ["ServiceId"] = serviceId.ToString(),
            ["BusinessPartyId"] = firmId.ToString(), ["ManagerId"] = managerId.ToString(), ["StartDate"] = "2026-01-01", ["BillingAmount"] = "1000.00",
            ["FirmPercent"] = "50", ["ManagerPercent"] = "30", ["LcmPercent"] = "30", ["Frequency"] = Frequency.Monthly.ToString(), ["NextPeriodStart"] = "2026-02-01", ["AnchorDay"] = "1", ["Status"] = EngagementStatus.Active.ToString(), ["Notes"] = "Invalid"
        });
        Assert.Equal(HttpStatusCode.OK, invalidPercentages.StatusCode); Assert.Contains("total exactly 100%", await invalidPercentages.Content.ReadAsStringAsync());

        async Task<BillingScheduleForm> ScheduleForm()
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.Engagements.Include(x => x.Schedule).AsNoTracking().SingleAsync(x => x.Id == engagementId);
            return new BillingScheduleForm { EngagementId = e.Id, EngagementVersion = e.Version, Version = e.Schedule.Version, Frequency = e.Schedule.Frequency, NextPeriodStart = e.Schedule.NextPeriodStart, AnchorDay = e.Schedule.AnchorDay };
        }
        var staleSchedule = await ScheduleForm();
        var engagementPage = await admin.GetStringAsync($"/Engagements/Edit/{engagementId}");
        Assert.Contains($"name=\"ScheduleVersion\"", engagementPage);
        Assert.Contains($"value=\"{staleSchedule.Version}\"", engagementPage);
        var separateSchedule = await PostWithToken(admin, $"/Billing/EditSchedule/{engagementId}", "/Billing/EditSchedule", new()
        {
            ["EngagementId"] = staleSchedule.EngagementId.ToString(), ["EngagementVersion"] = staleSchedule.EngagementVersion.ToString(), ["Version"] = staleSchedule.Version.ToString(),
            ["Frequency"] = Frequency.Monthly.ToString(), ["NextPeriodStart"] = "2026-03-01", ["AnchorDay"] = "1"
        });
        Assert.Equal(HttpStatusCode.Redirect, separateSchedule.StatusCode);
        var staleEngagement = await PostWithToken(admin, $"/Engagements/Edit/{engagementId}", "/Engagements/Edit", new()
        {
            ["Id"] = engagementId.ToString(), ["Version"] = staleSchedule.EngagementVersion.ToString(), ["ScheduleVersion"] = staleSchedule.Version.ToString(),
            ["CustomerId"] = customerId.ToString(), ["ServiceId"] = serviceId.ToString(), ["BusinessPartyId"] = firmId.ToString(), ["ManagerId"] = managerId.ToString(),
            ["StartDate"] = "2026-01-01", ["BillingAmount"] = "1000.00", ["FirmPercent"] = "35", ["ManagerPercent"] = "25", ["LcmPercent"] = "40",
            ["Frequency"] = staleSchedule.Frequency.ToString(), ["NextPeriodStart"] = staleSchedule.NextPeriodStart!.Value.ToString("yyyy-MM-dd"), ["AnchorDay"] = staleSchedule.AnchorDay.ToString(),
            ["Status"] = EngagementStatus.Active.ToString(), ["Notes"] = "Stale engagement"
        });
        Assert.Equal(HttpStatusCode.OK, staleEngagement.StatusCode); Assert.Contains("The billing schedule changed. Refresh the engagement before saving.", await staleEngagement.Content.ReadAsStringAsync());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedSchedule = await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == engagementId);
            Assert.Equal(new DateOnly(2026, 3, 1), savedSchedule.NextPeriodStart);
        }
        var schedule = await ScheduleForm();
        schedule.Frequency = Frequency.Quarterly; schedule.NextPeriodStart = new(2026, 4, 1); schedule.AnchorDay = 15;
        var scheduleResponse = await PostWithToken(admin, $"/Billing/EditSchedule/{engagementId}", "/Billing/EditSchedule", new()
        {
            ["EngagementId"] = schedule.EngagementId.ToString(), ["EngagementVersion"] = schedule.EngagementVersion.ToString(), ["Version"] = schedule.Version.ToString(),
            ["Frequency"] = schedule.Frequency.ToString(), ["NextPeriodStart"] = "2026-04-01", ["AnchorDay"] = "15"
        });
        Assert.Equal(HttpStatusCode.Redirect, scheduleResponse.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.Engagements.Include(x => x.Schedule).SingleAsync(x => x.Id == engagementId);
            Assert.Equal(Frequency.Quarterly, e.Schedule.Frequency); Assert.Equal(new DateOnly(2026, 4, 1), e.Schedule.NextPeriodStart); Assert.Equal(15, e.Schedule.AnchorDay);
            var old = await db.BillingRecords.Include(x => x.Shares).SingleAsync(x => x.Id == billingId);
            Assert.Equal(new DateOnly(2026, 1, 1), old.PeriodStart); Assert.Equal("Original customer", old.CustomerName); Assert.Equal("Original firm", old.Shares.Single(x => x.Kind == ShareKind.Firm).PartyName);
        }
        var future = await new BillingService(Db()).Generate(engagementId, new(2026, 4, 1), new(2026, 7, 14), BillingGenerationMode.Scheduled);
        Assert.Equal(new DateOnly(2026, 7, 15), (await Db().BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == engagementId)).NextPeriodStart);
        Assert.Equal("Edited customer", future.CustomerName); Assert.Equal("Edited service", future.ServiceName);

        var overlapping = await ScheduleForm(); overlapping.Frequency = Frequency.Monthly; overlapping.NextPeriodStart = new(2026, 1, 1); overlapping.AnchorDay = 1;
        var overlapResponse = await PostWithToken(admin, $"/Billing/EditSchedule/{engagementId}", "/Billing/EditSchedule", new()
        {
            ["EngagementId"] = overlapping.EngagementId.ToString(), ["EngagementVersion"] = overlapping.EngagementVersion.ToString(), ["Version"] = overlapping.Version.ToString(),
            ["Frequency"] = overlapping.Frequency.ToString(), ["NextPeriodStart"] = "2026-01-01", ["AnchorDay"] = "1"
        });
        Assert.Equal(HttpStatusCode.OK, overlapResponse.StatusCode); Assert.Contains("overlaps an existing billing record", await overlapResponse.Content.ReadAsStringAsync());
        var outside = await ScheduleForm(); outside.NextPeriodStart = new(2025, 12, 1);
        var outsideResponse = await PostWithToken(admin, $"/Billing/EditSchedule/{engagementId}", "/Billing/EditSchedule", new()
        {
            ["EngagementId"] = outside.EngagementId.ToString(), ["EngagementVersion"] = outside.EngagementVersion.ToString(), ["Version"] = outside.Version.ToString(),
            ["Frequency"] = outside.Frequency.ToString(), ["NextPeriodStart"] = "2025-12-01", ["AnchorDay"] = outside.AnchorDay.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, outsideResponse.StatusCode); Assert.Contains("inside the engagement dates", await outsideResponse.Content.ReadAsStringAsync());

        var stale = await ScheduleForm(); var current = await ScheduleForm();
        current.AnchorDay = 20;
        Assert.Equal(HttpStatusCode.Redirect, (await PostWithToken(admin, $"/Billing/EditSchedule/{engagementId}", "/Billing/EditSchedule", new()
        {
            ["EngagementId"] = current.EngagementId.ToString(), ["EngagementVersion"] = current.EngagementVersion.ToString(), ["Version"] = current.Version.ToString(),
            ["Frequency"] = current.Frequency.ToString(), ["NextPeriodStart"] = current.NextPeriodStart!.Value.ToString("yyyy-MM-dd"), ["AnchorDay"] = "20"
        })).StatusCode);
        var staleResponse = await PostWithToken(admin, $"/Billing/EditSchedule/{engagementId}", "/Billing/EditSchedule", new()
        {
            ["EngagementId"] = stale.EngagementId.ToString(), ["EngagementVersion"] = stale.EngagementVersion.ToString(), ["Version"] = stale.Version.ToString(),
            ["Frequency"] = stale.Frequency.ToString(), ["NextPeriodStart"] = stale.NextPeriodStart!.Value.ToString("yyyy-MM-dd"), ["AnchorDay"] = stale.AnchorDay.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode); Assert.Contains("changed. Refresh before saving", await staleResponse.Content.ReadAsStringAsync());

        using var external = await SignedIn(app, "master-firm@example.com", password);
        Assert.Equal(HttpStatusCode.Redirect, (await external.GetAsync($"/Masters/Edit?kind=Customers&id={customerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await external.GetAsync($"/Billing/EditSchedule/{engagementId}")).StatusCode);
    }
    [PostgresFact]
    public async Task ManualReplacementUsesCurrentScheduleAndAdvancesOnlyTheSelectedSchedule()
    {
        await using var reset = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "manual-replacement-test-keys")));
        const string password = "Manual-Replacement-Password!123";
        using (var scope = app.Services.CreateScope())
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "manual-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());

        async Task<int> AddEngagement(Frequency frequency, DateOnly start, DateOnly? end = null, DateOnly? next = null)
        {
            await using var db = Db();
            var engagement = new Engagement
            {
                Customer = new() { Name = $"Customer {frequency} {start}" },
                Service = new() { Name = $"Service {frequency} {start}" },
                BusinessParty = new() { Name = $"Firm {frequency} {start}" },
                Manager = new() { Name = $"Manager {frequency} {start}" },
                StartDate = start, EndDate = end, BillingAmount = 1000m,
                Schedule = new() { Frequency = frequency, NextPeriodStart = next, AnchorDay = 1 }
            };
            db.Add(engagement); await db.SaveChangesAsync(); return engagement.Id;
        }

        var yearlyId = await AddEngagement(Frequency.Yearly, new(2024, 8, 1), next: new(2024, 8, 1));
        var shortYearlyId = await AddEngagement(Frequency.Yearly, new(2024, 8, 1), next: new(2024, 8, 1));
        var monthlyId = await AddEngagement(Frequency.Monthly, new(2026, 1, 1), next: new(2026, 1, 1));
        var quarterlyId = await AddEngagement(Frequency.Quarterly, new(2026, 1, 1), next: new(2026, 1, 1));
        var overlapId = await AddEngagement(Frequency.Monthly, new(2026, 1, 1), next: new(2026, 2, 1));
        var endDateId = await AddEngagement(Frequency.Yearly, new(2024, 8, 1), new(2025, 6, 30), new(2024, 8, 1));
        var oneOffId = await AddEngagement(Frequency.OneOff, new(2026, 1, 1), next: new(2026, 1, 1));
        var adHocId = await AddEngagement(Frequency.AdHoc, new(2026, 1, 1));

        using var admin = await SignedIn(app, "manual-admin@example.com", password);
        var schedulePage = await admin.GetStringAsync("/Billing/Schedule");
        Assert.Contains("replaces the current advised period", schedulePage);
        Assert.Contains("name=\"mode\" value=\"Scheduled\"", schedulePage);
        Assert.Contains("name=\"mode\" value=\"Replacement\"", schedulePage);
        Assert.Contains("name=\"mode\" value=\"AdHocManual\"", schedulePage);

        var forgedUnrestricted = await PostWithToken(admin, "/Billing/Schedule", "/Billing/Generate", new()
        {
            ["engagementId"] = yearlyId.ToString(), ["start"] = "2024-08-01", ["end"] = "2025-12-31",
            ["advanceSchedule"] = "false", ["manualReplacement"] = "false"
        }, "/Billing/Schedule");
        Assert.Equal(HttpStatusCode.Redirect, forgedUnrestricted.StatusCode);
        Assert.EndsWith("/Billing/Schedule", forgedUnrestricted.Headers.Location!.ToString());
        using (var db = Db()) Assert.False(await db.BillingRecords.AnyAsync(x => x.EngagementId == yearlyId));

        await using (var db = Db())
        {
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(monthlyId, new(2026, 1, 1), new(2026, 1, 15), null));
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(monthlyId, new(2026, 1, 1), new(2026, 1, 15), BillingGenerationMode.AdHocManual));
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(adHocId, new(2026, 3, 1), new(2026, 3, 10), BillingGenerationMode.Scheduled));
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(oneOffId, new(2026, 1, 1), new(2026, 2, 28), BillingGenerationMode.AdHocManual));
        }

        int yearlyBillingId;
        await using (var db = Db())
        {
            var bill = await new BillingService(db).Generate(yearlyId, new(2024, 8, 1), new(2025, 12, 31), BillingGenerationMode.Replacement);
            yearlyBillingId = bill.Id;
        }
        await using (var db = Db())
        {
            var schedule = await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == yearlyId);
            Assert.Equal(new DateOnly(2026, 1, 1), schedule.NextPeriodStart);
            Assert.Equal(1, schedule.AnchorDay);
        }
        await using (var db = Db())
        {
            var service = new BillingService(db);
            await service.Cancel("billing", yearlyBillingId, "Replacement correction");
            Assert.Equal(new DateOnly(2026, 1, 1), (await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == yearlyId)).NextPeriodStart);
        }
        await using (var db = Db())
        {
            await new BillingService(db).Generate(yearlyId, new(2026, 1, 1), new(2026, 12, 31), BillingGenerationMode.Scheduled);
            Assert.Equal(new DateOnly(2027, 1, 1), (await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == yearlyId)).NextPeriodStart);
        }

        await using (var db = Db())
        {
            await new BillingService(db).Generate(shortYearlyId, new(2024, 8, 1), new(2025, 2, 28), BillingGenerationMode.Replacement);
            var schedule = await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == shortYearlyId);
            Assert.Equal(new DateOnly(2025, 3, 1), schedule.NextPeriodStart); Assert.Equal(1, schedule.AnchorDay);
        }
        await using (var db = Db())
        {
            await new BillingService(db).Generate(shortYearlyId, new(2025, 3, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
            Assert.Equal(new DateOnly(2026, 3, 1), (await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == shortYearlyId)).NextPeriodStart);
        }

        await using (var db = Db())
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(monthlyId, new(2026, 1, 2), new(2026, 1, 31), BillingGenerationMode.Replacement));
        await using (var db = Db())
        {
            await new BillingService(db).Generate(monthlyId, new(2026, 1, 1), new(2026, 1, 15), BillingGenerationMode.Replacement);
            var schedule = await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == monthlyId);
            Assert.Equal(new DateOnly(2026, 1, 16), schedule.NextPeriodStart); Assert.Equal(16, schedule.AnchorDay);
        }
        await using (var db = Db())
        {
            await new BillingService(db).Generate(monthlyId, new(2026, 1, 16), new(2026, 2, 15), BillingGenerationMode.Scheduled);
            Assert.Equal(new DateOnly(2026, 2, 16), (await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == monthlyId)).NextPeriodStart);
        }

        await using (var db = Db())
        {
            await new BillingService(db).Generate(quarterlyId, new(2026, 1, 1), new(2026, 5, 31), BillingGenerationMode.Replacement);
            var schedule = await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == quarterlyId);
            Assert.Equal(new DateOnly(2026, 6, 1), schedule.NextPeriodStart); Assert.Equal(1, schedule.AnchorDay);
        }
        await using (var db = Db())
        {
            await new BillingService(db).Generate(quarterlyId, new(2026, 6, 1), new(2026, 8, 31), BillingGenerationMode.Scheduled);
            Assert.Equal(new DateOnly(2026, 9, 1), (await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == quarterlyId)).NextPeriodStart);
        }

        await using (var db = Db())
        {
            await new BillingService(db).Generate(overlapId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(overlapId, new(2026, 2, 1), new(2026, 2, 15), BillingGenerationMode.Replacement));
        }
        await using (var db = Db())
            await Assert.ThrowsAsync<BusinessException>(() => new BillingService(db).Generate(endDateId, new(2024, 8, 1), new(2025, 7, 1), BillingGenerationMode.Replacement));
        await using (var db = Db())
        {
            await new BillingService(db).Generate(oneOffId, new(2026, 1, 1), new(2026, 2, 28), BillingGenerationMode.Replacement);
            Assert.Null((await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == oneOffId)).NextPeriodStart);
        }
        await using (var db = Db())
        {
            await new BillingService(db).Generate(adHocId, new(2026, 3, 5), new(2026, 3, 10), BillingGenerationMode.AdHocManual);
            Assert.Null((await db.BillingSchedules.AsNoTracking().SingleAsync(x => x.EngagementId == adHocId)).NextPeriodStart);
        }
    }
    [PostgresFact]
    public async Task BillingAndInvoiceCorrectionsRespectLocksCapsConcurrencyAndScope()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var billing = new BillingService(db);
        var first = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var second = await billing.Generate(engagementId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);

        async Task<(long Version, long WorkItemVersion)> BillingVersions(int id)
        {
            await using var context = Db();
            var bill = await context.BillingRecords.Include(x => x.WorkItem).AsNoTracking().SingleAsync(x => x.Id == id);
            return (bill.Version, bill.WorkItem.Version);
        }
        var secondVersions = await BillingVersions(second.Id);
        await billing.Correct(second.Id, new(2026, 2, 2), new(2026, 2, 27), second.Status, "Corrected service period", secondVersions.Version, secondVersions.WorkItemVersion);
        var corrected = await db.BillingRecords.Include(x => x.Shares).Include(x => x.WorkItem).AsNoTracking().SingleAsync(x => x.Id == second.Id);
        Assert.Equal(new DateOnly(2026, 2, 2), corrected.PeriodStart); Assert.Equal(new DateOnly(2026, 2, 27), corrected.PeriodEnd); Assert.Equal("Corrected service period", corrected.WorkItem.Notes);
        Assert.Equal(1000m, corrected.Amount); Assert.Equal(400m, corrected.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);
        var correctedVersions = await BillingVersions(second.Id);
        await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(second.Id, new(2026, 1, 15), new(2026, 1, 20), second.Status, "Overlap", correctedVersions.Version, correctedVersions.WorkItemVersion));
        await billing.Correct(second.Id, new(2026, 2, 3), new(2026, 2, 26), second.Status, "Second correction", correctedVersions.Version, correctedVersions.WorkItemVersion);
        await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(second.Id, new(2026, 2, 4), new(2026, 2, 25), second.Status, "Stale", correctedVersions.Version, correctedVersions.WorkItemVersion));

        var invoiceService = new InvoiceService(db);
        var invoice = await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "CORRECT-001", new(2026, 1, 31), new Dictionary<int, decimal> { [first.Id] = 500m });
        var invoiceVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id)).Version;
        await invoiceService.EditInvoice(invoice.Id, "CORRECT-001-EDITED", new(2026, 2, 1), new Dictionary<int, decimal> { [first.Id] = 600m }, invoiceVersion);
        var editedInvoice = await db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal("CORRECT-001-EDITED", editedInvoice.InvoiceNumber); Assert.Equal(new DateOnly(2026, 2, 1), editedInvoice.InvoiceDate); Assert.Equal(600m, editedInvoice.Total); Assert.Equal(600m, editedInvoice.Lines.Single().AllocatedAmount);
        var firstState = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).SingleAsync(x => x.Id == first.Id);
        Assert.Equal(BillingInvoiceState.PartiallyInvoiced, firstState.CustomerInvoiceState);

        var duplicate = await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "DUPLICATE-ISSUER", new(2026, 2, 2), new Dictionary<int, decimal> { [second.Id] = 100m });
        var editedVersion = editedInvoice.Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoiceService.EditInvoice(invoice.Id, duplicate.InvoiceNumber, editedInvoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 600m }, editedVersion));
        await Assert.ThrowsAsync<BusinessException>(() => invoiceService.EditInvoice(invoice.Id, "CAP-OVER", editedInvoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 1000.01m }, editedVersion));

        await invoiceService.CreateReceipt(new(2026, 2, 3), "CORRECT-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoice.Id] = 100m });
        var receiptLockedVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id)).Version;
        await invoiceService.EditInvoice(invoice.Id, "RECEIPT-METADATA-EDIT", new(2026, 2, 4), new Dictionary<int, decimal> { [first.Id] = 600m }, receiptLockedVersion);
        var metadataEdited = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal("RECEIPT-METADATA-EDIT", metadataEdited.InvoiceNumber); Assert.Equal(new DateOnly(2026, 2, 4), metadataEdited.InvoiceDate); Assert.Equal(InvoiceStatus.PartiallyPaid, metadataEdited.Status);
        receiptLockedVersion = metadataEdited.Version;
        var receiptError = await Assert.ThrowsAsync<BusinessException>(() => invoiceService.EditInvoice(invoice.Id, "RECEIPT-LOCKED", editedInvoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 550m }, receiptLockedVersion));
        Assert.Contains("active receipt allocations", receiptError.Message);
        await Assert.ThrowsAsync<BusinessException>(() => invoiceService.EditInvoice(invoice.Id, "STALE", editedInvoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 550m }, editedVersion));

        var worker = new Worker { Name = "Correction worker" }; db.Workers.Add(worker); await db.SaveChangesAsync();
        var third = await billing.Generate(engagementId, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled);
        await billing.Assign(third.WorkItem.Id, worker.Id, 50m);
        var thirdVersions = await BillingVersions(third.Id);
        await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(third.Id, new(2026, 3, 2), new(2026, 3, 30), third.Status, "Assignment lock", thirdVersions.Version, thirdVersions.WorkItemVersion));

        var fourth = await billing.Generate(engagementId, new(2026, 4, 1), new(2026, 4, 30), BillingGenerationMode.Scheduled);
        var invoiceServiceForStatus = new InvoiceService(db);
        await invoiceServiceForStatus.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "STATUS-LOCK", new(2026, 4, 30), new Dictionary<int, decimal> { [fourth.Id] = fourth.Amount });
        var fourthVersions = await BillingVersions(fourth.Id);
        var statusError = await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(fourth.Id, fourth.PeriodStart, fourth.PeriodEnd, BillingStatus.ReadyToBill, "Forged status", fourthVersions.Version, fourthVersions.WorkItemVersion));
        Assert.Contains("calculated from active financial records", statusError.Message);
        var statusLocked = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == fourth.Id);
        Assert.Equal(BillingStatus.Billed, statusLocked.Status);

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "correction-scope-test-keys")));
        const string password = "Correction-Scope-Password!123";
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "correction-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var firmId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Engagements.Where(x => x.Id == engagementId).Select(x => x.BusinessPartyId).SingleAsync();
            var external = new AppUser { Email = "correction-firm@example.com", UserName = "correction-firm@example.com", EmailConfirmed = true, BusinessPartyId = firmId };
            Seed.Check(await users.CreateAsync(external, password)); Seed.Check(await users.AddToRoleAsync(external, AppRoles.AccountingFirm));
        }
        using var externalClient = await SignedIn(app, "correction-firm@example.com", password);
        Assert.Equal(HttpStatusCode.Redirect, (await externalClient.GetAsync($"/Billing/Edit/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await externalClient.GetAsync($"/Invoices/Edit/{invoice.Id}")).StatusCode);
    }
    [PostgresFact]
    public async Task RevenueShareBaseSeparatesCustomerBillingCapsAndSafeCorrections()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var billing = new BillingService(db);

        var defaultBill = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        db.ChangeTracker.Clear();
        var defaultSnapshot = await db.BillingRecords.Include(x => x.Shares).AsNoTracking().SingleAsync(x => x.Id == defaultBill.Id);
        Assert.Equal(1000m, defaultSnapshot.Amount);
        Assert.Equal(1000m, defaultSnapshot.RevenueShareBaseAmount);
        Assert.Equal(350m, defaultSnapshot.Shares.Single(x => x.Kind == ShareKind.Firm).Amount);
        Assert.Equal(250m, defaultSnapshot.Shares.Single(x => x.Kind == ShareKind.Manager).Amount);
        Assert.Equal(400m, defaultSnapshot.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);

        var customBill = await billing.Generate(engagementId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled, 4500m, 3000m);
        db.ChangeTracker.Clear();
        var customSnapshot = await db.BillingRecords.Include(x => x.Shares).AsNoTracking().SingleAsync(x => x.Id == customBill.Id);
        Assert.Equal(4500m, customSnapshot.Amount);
        Assert.Equal(3000m, customSnapshot.RevenueShareBaseAmount);
        Assert.Equal(1050m, customSnapshot.Shares.Single(x => x.Kind == ShareKind.Firm).Amount);
        Assert.Equal(750m, customSnapshot.Shares.Single(x => x.Kind == ShareKind.Manager).Amount);
        Assert.Equal(1200m, customSnapshot.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);

        var invoices = new InvoiceService(db);
        await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "BASE-CUSTOMER", new(2026, 2, 28), new Dictionary<int, decimal> { [customBill.Id] = 4500m });
        await invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "BASE-MANAGER", new(2026, 2, 28), new Dictionary<int, decimal> { [customBill.Id] = 750m });
        await invoices.CreateInvoice(InvoiceFlow.LcmToManager, "BASE-LCM", new(2026, 2, 28), new Dictionary<int, decimal> { [customBill.Id] = 1200m });
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "BASE-CUSTOMER-OVER", new(2026, 3, 1), new Dictionary<int, decimal> { [customBill.Id] = .01m }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "BASE-MANAGER-OVER", new(2026, 3, 1), new Dictionary<int, decimal> { [customBill.Id] = .01m }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.CreateInvoice(InvoiceFlow.LcmToManager, "BASE-LCM-OVER", new(2026, 3, 1), new Dictionary<int, decimal> { [customBill.Id] = .01m }));

        var correctionBill = await billing.Generate(engagementId, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled, 4500m, 3000m);
        var createdAt = correctionBill.CreatedAt;
        var versions = await db.BillingRecords.Include(x => x.WorkItem).AsNoTracking().Where(x => x.Id == correctionBill.Id).Select(x => new { x.Version, WorkItemVersion = x.WorkItem.Version }).SingleAsync();
        await billing.Correct(correctionBill.Id, correctionBill.PeriodStart, correctionBill.PeriodEnd, correctionBill.Status, "Amount correction", versions.Version, versions.WorkItemVersion, 5000m, 2500m);
        db.ChangeTracker.Clear();
        var corrected = await db.BillingRecords.Include(x => x.Shares).AsNoTracking().SingleAsync(x => x.Id == correctionBill.Id);
        Assert.Equal(5000m, corrected.Amount); Assert.Equal(2500m, corrected.RevenueShareBaseAmount); Assert.Equal(createdAt.Ticks / 10, corrected.CreatedAt.Ticks / 10);
        Assert.Equal(875m, corrected.Shares.Single(x => x.Kind == ShareKind.Firm).Amount);
        Assert.Equal(625m, corrected.Shares.Single(x => x.Kind == ShareKind.Manager).Amount);
        Assert.Equal(1000m, corrected.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);
        await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(correctionBill.Id, correctionBill.PeriodStart, correctionBill.PeriodEnd, correctionBill.Status, "Stale", versions.Version, versions.WorkItemVersion, 5001m, 2500m));

        await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "BASE-CORRECTION-LOCK", new(2026, 3, 31), new Dictionary<int, decimal> { [correctionBill.Id] = 1m });
        var lockedState = await db.BillingRecords.Include(x => x.WorkItem).AsNoTracking().Where(x => x.Id == correctionBill.Id).Select(x => new { x.Version, WorkItemVersion = x.WorkItem.Version, x.Status }).SingleAsync();
        await billing.Correct(correctionBill.Id, correctionBill.PeriodStart, correctionBill.PeriodEnd, lockedState.Status, "Customer amount correction with invoice", lockedState.Version, lockedState.WorkItemVersion, 5001m, 2500m);
        var customerAmountCorrected = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == correctionBill.Id);
        Assert.Equal(5001m, customerAmountCorrected.Amount);

        var worker = new Worker { Name = "Base worker" }; db.Workers.Add(worker); await db.SaveChangesAsync();
        var assignmentBill = await billing.Generate(engagementId, new(2026, 4, 1), new(2026, 4, 30), BillingGenerationMode.Scheduled, 4500m, 3000m);
        await billing.Assign(assignmentBill.WorkItem.Id, worker.Id, 50m);
        var assignment = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == assignmentBill.WorkItem.Id);
        Assert.Equal(600m, assignment.Entitlement);
        var assignmentVersions = await db.BillingRecords.Include(x => x.WorkItem).AsNoTracking().Where(x => x.Id == assignmentBill.Id).Select(x => new { x.Version, WorkItemVersion = x.WorkItem.Version }).SingleAsync();
        var assignmentLock = await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(assignmentBill.Id, assignmentBill.PeriodStart, assignmentBill.PeriodEnd, assignmentBill.Status, "Assignment lock", assignmentVersions.Version, assignmentVersions.WorkItemVersion, 4500m, 3001m));
        Assert.Contains("worker assignments", assignmentLock.Message);

        var replacement = await billing.Generate(engagementId, new(2026, 5, 1), new(2026, 5, 15), BillingGenerationMode.Replacement, 6000m, 3000m);
        Assert.Equal(6000m, replacement.Amount); Assert.Equal(3000m, replacement.RevenueShareBaseAmount); Assert.Equal(1200m, replacement.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);

        var adHoc = new Engagement
        {
            Customer = new Customer { Name = "Ad-Hoc customer" }, Service = new Service { Name = "Ad-Hoc service" },
            BusinessParty = new BusinessParty { Name = "Ad-Hoc firm" }, Manager = new Manager { Name = "Ad-Hoc manager" },
            StartDate = new(2026, 1, 1), BillingAmount = 1000m,
            Schedule = new BillingSchedule { Frequency = Frequency.AdHoc, AnchorDay = 1 }
        };
        db.Engagements.Add(adHoc); await db.SaveChangesAsync();
        var adHocBill = await billing.Generate(adHoc.Id, new(2026, 6, 1), new(2026, 6, 10), BillingGenerationMode.AdHocManual, 900m, 700m);
        Assert.Equal(900m, adHocBill.Amount); Assert.Equal(700m, adHocBill.RevenueShareBaseAmount); Assert.Equal(280m, adHocBill.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount);

        var invalidBillDate = new DateOnly(2026, 5, 16);
        await Assert.ThrowsAsync<BusinessException>(() => billing.Generate(engagementId, invalidBillDate, new(2026, 6, 15), BillingGenerationMode.Scheduled, 0m, 3000m));
        await Assert.ThrowsAsync<BusinessException>(() => billing.Generate(engagementId, invalidBillDate, new(2026, 6, 15), BillingGenerationMode.Scheduled, 4500.001m, 3000m));
    }
    [PostgresFact]
    public async Task InvoiceCorrectionsSupportMembershipChangesAndSameTotalConcurrency()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var billing = new BillingService(db);
        var first = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var second = await billing.Generate(engagementId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        var third = await billing.Generate(engagementId, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled);
        var invoices = new InvoiceService(db);
        var invoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "EDIT-MEMBERSHIP", new(2026, 3, 31), new Dictionary<int, decimal> { [first.Id] = 600m, [second.Id] = 400m });
        var staleVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id)).Version;
        await invoices.EditInvoice(invoice.Id, "EDIT-MEMBERSHIP", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 500m, [second.Id] = 500m }, staleVersion);
        var changedVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id)).Version;
        Assert.True(changedVersion > staleVersion);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "EDIT-MEMBERSHIP", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 400m, [second.Id] = 600m }, staleVersion));
        var sameTotal = await db.InvoiceLines.AsNoTracking().Where(x => x.InvoiceId == invoice.Id).ToDictionaryAsync(x => x.BillingRecordId, x => x.AllocatedAmount);
        Assert.Equal(500m, sameTotal[first.Id]); Assert.Equal(500m, sameTotal[second.Id]);

        await invoices.EditInvoice(invoice.Id, "EDIT-DATE", new(2026, 4, 1), new Dictionary<int, decimal> { [first.Id] = 500m, [second.Id] = 500m }, changedVersion);
        var metadataVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id)).Version;
        Assert.True(metadataVersion > changedVersion);
        await invoices.EditInvoice(invoice.Id, "EDIT-MEMBERSHIP", new(2026, 4, 1), new Dictionary<int, decimal> { [first.Id] = 300m, [third.Id] = 700m }, metadataVersion);
        var replaced = await db.Invoices.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(1000m, replaced.Total); Assert.Equal(new[] { first.Id, third.Id }.OrderBy(x => x), replaced.Lines.Select(x => x.BillingRecordId).OrderBy(x => x));
        var secondAfterRemoval = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).AsNoTracking().SingleAsync(x => x.Id == second.Id);
        var thirdAfterAdd = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).AsNoTracking().SingleAsync(x => x.Id == third.Id);
        Assert.Equal(BillingInvoiceState.Unbilled, secondAfterRemoval.CustomerInvoiceState); Assert.Equal(BillingInvoiceState.PartiallyInvoiced, thirdAfterAdd.CustomerInvoiceState);

        var versionAfterReplacement = replaced.Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "NEGATIVE", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = -1m, [third.Id] = 1001m }, versionAfterReplacement));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "EMPTY", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 0m, [third.Id] = 0m }, versionAfterReplacement));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "OVER-CAP", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 1000.01m }, versionAfterReplacement));

        var other = new Engagement
        {
            Customer = new Customer { Name = "Other correction customer" }, Service = new Service { Name = "Other correction service" },
            BusinessParty = new BusinessParty { Name = "Other correction firm" }, Manager = new Manager { Name = "Other correction manager" },
            StartDate = new(2026, 1, 1), BillingAmount = 1000m,
            Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        db.Engagements.Add(other); await db.SaveChangesAsync();
        var otherBill = await billing.Generate(other.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "WRONG-PARTY", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 300m, [otherBill.Id] = 700m }, versionAfterReplacement));
        await billing.Cancel("billing", otherBill.Id, "Cancelled correction candidate");
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "CANCELLED-LINE", invoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 300m, [otherBill.Id] = 700m }, versionAfterReplacement));

        var firstEngagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId);
        var otherManagerEngagement = new Engagement
        {
            Customer = new Customer { Name = "Manager mismatch customer" }, Service = new Service { Name = "Manager mismatch service" },
            BusinessPartyId = firstEngagement.BusinessPartyId, Manager = new Manager { Name = "Manager mismatch" },
            StartDate = new(2026, 1, 1), BillingAmount = 1000m,
            Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        db.Engagements.Add(otherManagerEngagement); await db.SaveChangesAsync();
        var otherManagerBill = await billing.Generate(otherManagerEngagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var managerInvoice = await invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "MANAGER-EDIT", new(2026, 4, 1), new Dictionary<int, decimal> { [first.Id] = 100m });
        var managerVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == managerInvoice.Id)).Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(managerInvoice.Id, managerInvoice.InvoiceNumber, managerInvoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 100m, [otherManagerBill.Id] = 100m }, managerVersion));
        var lcmInvoice = await invoices.CreateInvoice(InvoiceFlow.LcmToManager, "LCM-EDIT", new(2026, 4, 1), new Dictionary<int, decimal> { [first.Id] = 100m });
        var lcmVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == lcmInvoice.Id)).Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(lcmInvoice.Id, lcmInvoice.InvoiceNumber, lcmInvoice.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 100m, [otherManagerBill.Id] = 100m }, lcmVersion));

        var receipt = await invoices.CreateReceipt(new(2026, 4, 1), "EDIT-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoice.Id] = 100m });
        var receiptVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id)).Version;
        await invoices.EditInvoice(invoice.Id, "EDIT-AFTER-RECEIPT", new(2026, 4, 2), new Dictionary<int, decimal> { [first.Id] = 300m, [third.Id] = 700m }, receiptVersion);
        var afterReceiptMetadata = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(InvoiceFlow.AccountingFirmToCustomer, afterReceiptMetadata.Flow); Assert.Equal(InvoiceStatus.PartiallyPaid, afterReceiptMetadata.Status);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(invoice.Id, "RECEIPT-MEMBERSHIP", afterReceiptMetadata.InvoiceDate, new Dictionary<int, decimal> { [first.Id] = 300m, [second.Id] = 700m }, afterReceiptMetadata.Version));
        Assert.NotEqual(0, receipt.Id);
    }
    [PostgresFact]
    public async Task InvoiceCorrectionHeaderValidationUsesOnlyFlowRelevantParties()
    {
        await using var db = await Fresh();
        var baseEngagementId = await Engagement(db);
        var baseEngagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == baseEngagementId);
        var sameCustomerFirm = new Engagement
        {
            CustomerId = baseEngagement.CustomerId, Service = new Service { Name = "Second service" }, BusinessPartyId = baseEngagement.BusinessPartyId, ManagerId = baseEngagement.ManagerId,
            StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        var secondCustomer = new Engagement
        {
            Customer = new Customer { Name = "Second customer" }, Service = new Service { Name = "Second customer service" }, BusinessPartyId = baseEngagement.BusinessPartyId, ManagerId = baseEngagement.ManagerId,
            StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        var secondFirm = new Engagement
        {
            CustomerId = baseEngagement.CustomerId, Service = new Service { Name = "Second firm service" }, BusinessParty = new BusinessParty { Name = "Second firm" }, ManagerId = baseEngagement.ManagerId,
            StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        var secondManager = new Engagement
        {
            Customer = new Customer { Name = "Second manager customer" }, Service = new Service { Name = "Second manager service" }, BusinessPartyId = baseEngagement.BusinessPartyId, Manager = new Manager { Name = "Second manager" },
            StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new BillingSchedule { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 }
        };
        db.Engagements.AddRange(sameCustomerFirm, secondCustomer, secondFirm, secondManager); await db.SaveChangesAsync();
        var billing = new BillingService(db);
        var baseBill = await billing.Generate(baseEngagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var sameCustomerFirmBill = await billing.Generate(sameCustomerFirm.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var secondCustomerBill = await billing.Generate(secondCustomer.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var secondFirmBill = await billing.Generate(secondFirm.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var secondManagerBill = await billing.Generate(secondManager.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var invoices = new InvoiceService(db);

        var customerInvoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "FLOW-CUSTOMER", new(2026, 2, 1), new Dictionary<int, decimal> { [baseBill.Id] = 100m });
        var customerVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == customerInvoice.Id)).Version;
        await invoices.EditInvoice(customerInvoice.Id, customerInvoice.InvoiceNumber, customerInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [sameCustomerFirmBill.Id] = 100m }, customerVersion);
        var customerSameFirmVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == customerInvoice.Id)).Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(customerInvoice.Id, customerInvoice.InvoiceNumber, customerInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondCustomerBill.Id] = 100m }, customerSameFirmVersion));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(customerInvoice.Id, customerInvoice.InvoiceNumber, customerInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondFirmBill.Id] = 100m }, customerSameFirmVersion));

        var managerInvoice = await invoices.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "FLOW-MANAGER", new(2026, 2, 1), new Dictionary<int, decimal> { [baseBill.Id] = 100m });
        var managerVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == managerInvoice.Id)).Version;
        await invoices.EditInvoice(managerInvoice.Id, managerInvoice.InvoiceNumber, managerInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondCustomerBill.Id] = 100m }, managerVersion);
        var managerConsolidatedVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == managerInvoice.Id)).Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(managerInvoice.Id, managerInvoice.InvoiceNumber, managerInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondFirmBill.Id] = 100m }, managerConsolidatedVersion));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(managerInvoice.Id, managerInvoice.InvoiceNumber, managerInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondManagerBill.Id] = 100m }, managerConsolidatedVersion));

        var lcmInvoice = await invoices.CreateInvoice(InvoiceFlow.LcmToManager, "FLOW-LCM", new(2026, 2, 1), new Dictionary<int, decimal> { [baseBill.Id] = 100m });
        var lcmVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == lcmInvoice.Id)).Version;
        await invoices.EditInvoice(lcmInvoice.Id, lcmInvoice.InvoiceNumber, lcmInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondCustomerBill.Id] = 100m, [secondFirmBill.Id] = 100m }, lcmVersion);
        var lcmConsolidatedVersion = (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == lcmInvoice.Id)).Version;
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditInvoice(lcmInvoice.Id, lcmInvoice.InvoiceNumber, lcmInvoice.InvoiceDate, new Dictionary<int, decimal> { [baseBill.Id] = 100m, [secondCustomerBill.Id] = 100m, [secondManagerBill.Id] = 100m }, lcmConsolidatedVersion));
    }
    [PostgresFact]
    public async Task AuthenticationAndFullPageRender()
    {
        await using var db = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "test-keys")));
        using (var scope = app.Services.CreateScope()) await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "test@example.com" }, { "BootstrapAdmin:Password", "Testing-Password!123" }, { "Seed:Demo", "true" } }).Build());
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var path in new[] { "/", "/Masters", "/Engagements", "/Billing", "/Billing/Schedule", "/Invoices", "/Invoices/Details/1", "/Invoices/Create", "/Invoices/Receipt", "/Work", "/Work/Assignments", "/Payments", "/Home/Reports", "/Home/Export", "/Users", "/Account/Password" }) { var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Contains("/Account/Login", response.Headers.Location!.ToString()); }
        var login = await client.GetAsync("/Account/Login"); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var html = await login.Content.ReadAsStringAsync(); var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value); Assert.NotEmpty(token);
        var noToken = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "test@example.com" }, { "Password", "Testing-Password!123" } })); Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var signed = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "test@example.com" }, { "Password", "Testing-Password!123" }, { "__RequestVerificationToken", token }, { "ReturnUrl", "https://evil.example/" } })); Assert.Equal("/", signed.Headers.Location!.ToString());
        foreach (var path in new[] { "/", "/Masters?kind=Customers", "/Masters?kind=Workers", "/Masters/Edit?kind=Customers", "/Engagements", "/Engagements/Edit", "/Billing", "/Billing/Schedule", "/Invoices", "/Invoices/Create", "/Invoices/Receipt", "/Work", "/Work/Assignments", "/Payments", "/Payments/Create", "/Home/Reports", "/Home/Export", "/Users", "/Users/Create", "/Account/Password" }) { var response = await client.GetAsync(path); Assert.True(response.StatusCode == HttpStatusCode.OK, path + ": " + response.StatusCode); }
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
        int firmAId, firmBId, managerAId, managerBId, workerAId, workerBId, engagementAId, billAId, billBId, workAId, workBId, assignmentBId, invoiceAId, invoiceBId, managerInvoiceAId, receiptAId, paymentAId;
        long workBVersion, assignmentBVersion;
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
            firmAId = firmA.Id; firmBId = firmB.Id; managerAId = managerA.Id; managerBId = managerB.Id; workerAId = workerA.Id; workerBId = workerB.Id; engagementAId = engagementA.Id;
            var finance = new BillingService(db);
            var billA = await finance.Generate(engagementA.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
            var billB = await finance.Generate(engagementB.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
            await finance.Assign(billA.WorkItem.Id, workerA.Id, 70m); await finance.Assign(billB.WorkItem.Id, workerB.Id, 60m);
            var invoiceService = new InvoiceService(db);
            invoiceAId = (await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "ALPHA-INV", new(2026, 1, 31), new Dictionary<int, decimal> { [billA.Id] = billA.Amount })).Id;
            invoiceBId = (await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "BETA-INV", new(2026, 1, 31), new Dictionary<int, decimal> { [billB.Id] = billB.Amount })).Id;
            managerInvoiceAId = (await invoiceService.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "ALPHA-MGR-INV", new(2026, 1, 31), new Dictionary<int, decimal> { [billA.Id] = 250 })).Id;
            var alphaReceipt = await invoiceService.CreateReceipt(new(2026, 1, 31), "ALPHA-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoiceAId] = 100m });
            await invoiceService.CreateReceipt(new(2026, 1, 31), "BETA-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoiceBId] = 200m });
            var assignmentA = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == billA.WorkItem.Id);
            var assignmentB = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == billB.WorkItem.Id);
            await finance.Pay(workerA.Id, new(2026, 1, 31), "ALPHA-PAYMENT", Guid.NewGuid(), new() { [assignmentA.Id] = 100m });
            await finance.Pay(workerB.Id, new(2026, 1, 31), "BETA-PAYMENT", Guid.NewGuid(), new() { [assignmentB.Id] = 100m });
            billAId = billA.Id; billBId = billB.Id; workAId = billA.WorkItem.Id; workBId = billB.WorkItem.Id;
            workBVersion = (await db.WorkItems.SingleAsync(x => x.Id == workBId)).Version;
            assignmentBId = assignmentB.Id; assignmentBVersion = assignmentB.Version;
            receiptAId = alphaReceipt.Id;
            paymentAId = await db.WorkerPayments.Where(x => x.Reference == "ALPHA-PAYMENT").Select(x => x.Id).SingleAsync();
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
        foreach (var path in new[] { $"/Masters/Edit?kind=Customers&id={engagementAId}", $"/Engagements/Edit/{engagementAId}", $"/Billing/EditSchedule/{engagementAId}", $"/Billing/Edit/{billAId}", $"/Invoices/Edit/{invoiceAId}", $"/Invoices/EditReceipt/{receiptAId}", $"/Work/EditAssignment/{assignmentBId}", $"/Payments/Edit/{paymentAId}" })
        {
            var forbiddenEdit = await firmClient.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, forbiddenEdit.StatusCode);
            Assert.Contains("Denied", forbiddenEdit.Headers.Location!.ToString());
        }
        var firmDashboard = await firmClient.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", firmDashboard); Assert.DoesNotContain("Beta Scope Customer", firmDashboard); Assert.Contains("1,000.00", firmDashboard); Assert.DoesNotContain("2,000.00", firmDashboard);
        var firmBilling = await firmClient.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", firmBilling); Assert.DoesNotContain("Beta Scope Customer", firmBilling);
        Assert.Equal(HttpStatusCode.NotFound, (await firmClient.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
        var firmDetails = await firmClient.GetStringAsync($"/Billing/Details/{billAId}"); Assert.Contains("Your firm share", firmDetails); Assert.Contains("ALPHA-RECEIPT", firmDetails); Assert.DoesNotContain("Your manager share", firmDetails); Assert.DoesNotContain("LCM retained", firmDetails); Assert.DoesNotContain("Worker entitlement", firmDetails);
        var firmInvoices = await firmClient.GetStringAsync("/Invoices"); Assert.Contains("ALPHA-INV", firmInvoices); Assert.Contains("ALPHA-MGR-INV", firmInvoices); Assert.DoesNotContain("BETA-INV", firmInvoices); Assert.Equal(HttpStatusCode.NotFound, (await firmClient.GetAsync($"/Invoices/Details/{invoiceBId}")).StatusCode);
        var firmInvoiceCsv = await firmClient.GetStringAsync("/Invoices/Export"); Assert.Contains("ALPHA-INV", firmInvoiceCsv); Assert.DoesNotContain("BETA-INV", firmInvoiceCsv);
        var firmReport = await firmClient.GetStringAsync("/Home/Reports"); Assert.Contains("Your firm share", firmReport); Assert.DoesNotContain("Manager share", firmReport); Assert.DoesNotContain("LCM MGT gross", firmReport); Assert.DoesNotContain("Worker cost", firmReport); Assert.DoesNotContain("Beta Scope Customer", firmReport);
        var firmCsv = await firmClient.GetStringAsync("/Home/Export"); Assert.Contains("Alpha Scope Customer", firmCsv); Assert.DoesNotContain("Beta Scope Customer", firmCsv); Assert.DoesNotContain("LCM gross", firmCsv); Assert.DoesNotContain("Worker entitlement", firmCsv);

        using var managerClient = await SignedIn(app, "manager-a@example.com", password);
        var managerDashboard = await managerClient.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", managerDashboard); Assert.DoesNotContain("Beta Scope Customer", managerDashboard); Assert.Contains("1,000.00", managerDashboard); Assert.DoesNotContain("2,000.00", managerDashboard);
        var managerEngagements = await managerClient.GetStringAsync("/Engagements"); Assert.Contains("Alpha Scope Customer", managerEngagements); Assert.DoesNotContain("Beta Scope Customer", managerEngagements);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
        var managerDetails = await managerClient.GetStringAsync($"/Billing/Details/{billAId}"); Assert.Contains("Your manager share", managerDetails); Assert.Contains("Work status", managerDetails); Assert.DoesNotContain("Customer receipt history", managerDetails); Assert.DoesNotContain("LCM retained", managerDetails); Assert.DoesNotContain("Worker entitlement", managerDetails);
        var managerInvoices = await managerClient.GetStringAsync("/Invoices"); Assert.Contains("ALPHA-MGR-INV", managerInvoices); Assert.DoesNotContain("ALPHA-INV", managerInvoices); Assert.DoesNotContain("BETA-INV", managerInvoices); Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync($"/Invoices/Details/{invoiceAId}")).StatusCode);
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
        var invoiceDenied = await workerClient.GetAsync("/Invoices"); Assert.Equal(HttpStatusCode.Redirect, invoiceDenied.StatusCode); Assert.Contains("Denied", invoiceDenied.Headers.Location!.ToString());
        var invoiceExportDenied = await workerClient.GetAsync("/Invoices/Export"); Assert.Equal(HttpStatusCode.Redirect, invoiceExportDenied.StatusCode); Assert.Contains("Denied", invoiceExportDenied.Headers.Location!.ToString());
        foreach (var externalClient in new[] { firmClient, managerClient, workerClient })
        {
            var receiptEditDenied = await PostWithToken(externalClient, "/", "/Invoices/EditReceipt", new() { ["Id"] = receiptAId.ToString(), ["Version"] = "1", ["ReceiptDate"] = "2026-02-01", ["Reference"] = "forged" });
            Assert.Equal(HttpStatusCode.Redirect, receiptEditDenied.StatusCode); Assert.Contains("Denied", receiptEditDenied.Headers.Location!.ToString());
        }
        var forgedWork = await PostWithToken(workerClient, $"/Work/Details/{workAId}", "/Work/Update", new() { ["id"] = workBId.ToString(), ["version"] = workBVersion.ToString(), ["status"] = WorkStatus.Completed.ToString(), ["notes"] = "forged" });
        Assert.Equal(HttpStatusCode.Redirect, forgedWork.StatusCode);
        Assert.Contains("Denied", forgedWork.Headers.Location!.ToString());
        var forgedAssignment = await PostWithToken(workerClient, "/Work/Assignments", "/Work/EditAssignment", new() { ["Id"] = assignmentBId.ToString(), ["Version"] = assignmentBVersion.ToString(), ["WorkerId"] = workerBId.ToString(), ["Percent"] = "50" });
        Assert.Equal(HttpStatusCode.Redirect, forgedAssignment.StatusCode);
        Assert.Contains("Denied", forgedAssignment.Headers.Location!.ToString());
        var workerDirectory = await workerClient.GetStringAsync("/Masters?kind=Workers"); Assert.Contains("Alpha Worker", workerDirectory); Assert.DoesNotContain("Beta Worker", workerDirectory);

        using var admin = await SignedIn(app, "scope-admin@example.com", password);
        var adminDashboard = await admin.GetStringAsync("/"); Assert.Contains("Alpha Scope Customer", adminDashboard); Assert.Contains("Beta Scope Customer", adminDashboard);
        var adminInvoices = await admin.GetStringAsync("/Invoices"); Assert.Contains("ALPHA-INV", adminInvoices); Assert.Contains("ALPHA-MGR-INV", adminInvoices); Assert.Contains("BETA-INV", adminInvoices);
        var newInvoice = await admin.GetStringAsync("/Invoices/Create");
        Assert.Contains("data-customer-cap=\"1000.00\"", newInvoice); Assert.Contains("data-customer-allocated=\"1000.00\"", newInvoice);
        Assert.Contains("data-manager-cap=\"250.00\"", newInvoice); Assert.Contains("data-manager-allocated=\"250.00\"", newInvoice);
        Assert.Contains("data-lcm-cap=\"400.00\"", newInvoice); Assert.Contains("data-lcm-allocated=\"0.00\"", newInvoice); Assert.Contains("id=\"invoice-flow\"", newInvoice);
        var fullyCustomerAllocatedRow = Regex.Match(newInvoice, $"<tr class=\"invoice-allocation-row\"[^>]*data-billing-id=\"{billBId}\"[^>]*>[\\s\\S]*?</tr>").Value;
        Assert.NotEmpty(fullyCustomerAllocatedRow); Assert.Contains("data-grid-eligible=\"false\"", fullyCustomerAllocatedRow); Assert.Contains("max=\"0.00\"", fullyCustomerAllocatedRow); Assert.Contains("disabled=\"disabled\"", fullyCustomerAllocatedRow);
        Assert.Contains("data-manager-allocated=\"0.00\"", fullyCustomerAllocatedRow); Assert.Contains("data-lcm-allocated=\"0.00\"", fullyCustomerAllocatedRow);
        Assert.Contains("No billing records have a remaining amount for this invoice flow.", newInvoice); Assert.Contains("invoice-allocations", newInvoice);
        var newInvoiceText = WebUtility.HtmlDecode(newInvoice);
        Assert.Contains("Accounting Firm → Customer", newInvoiceText); Assert.Contains("Manager → Accounting Firm", newInvoiceText); Assert.Contains("LCM MGT → Manager", newInvoiceText);
        Assert.Contains("invoice-create-form", newInvoice); Assert.Contains("invoice-allocation-table", newInvoice); Assert.DoesNotContain("Allocated for selected flow (RM)", newInvoice);
        var adminBilling = await admin.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", adminBilling); Assert.Contains("Beta Scope Customer", adminBilling);
        var adminBillingDetails = await admin.GetStringAsync($"/Billing/Details/{billBId}");
        Assert.Contains("Receipt total (RM)", adminBillingDetails); Assert.Contains("Allocated to invoice (RM)", adminBillingDetails); Assert.Contains("Attributed to this BillingRecord (RM)", adminBillingDetails);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
        using var internalUser = await SignedIn(app, "internal@example.com", password);
        var internalBilling = await internalUser.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", internalBilling); Assert.Contains("Beta Scope Customer", internalBilling);
        var internalInvoices = await internalUser.GetStringAsync("/Invoices"); Assert.Contains("ALPHA-INV", internalInvoices); Assert.Contains("ALPHA-MGR-INV", internalInvoices); Assert.Contains("BETA-INV", internalInvoices);
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

    [PostgresFact]
    public async Task Part3CorrectionsPreserveAllocationAuditAndBlockUnsafeEdits()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var workerOne = new Worker { Name = "Correction Worker One" };
        var workerTwo = new Worker { Name = "Correction Worker Two" };
        db.AddRange(workerOne, workerTwo); await db.SaveChangesAsync();
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        await billing.Assign(bill.WorkItem.Id, workerOne.Id, 50m);
        var assignment = await db.WorkerAssignments.AsNoTracking().SingleAsync();
        var assignmentCreatedAt = assignment.CreatedAt; var assignmentCreatedBy = assignment.CreatedBy;
        await billing.EditAssignment(assignment.Id, workerOne.Id, 60m, assignment.Version);
        assignment = await db.WorkerAssignments.SingleAsync();
        Assert.Equal(60m, assignment.Percent); Assert.Equal(240m, assignment.Entitlement); Assert.Equal(400m, assignment.LcmGrossSnapshot);
        Assert.Equal(assignmentCreatedAt.Ticks / 10, assignment.CreatedAt.Ticks / 10); Assert.Equal(assignmentCreatedBy, assignment.CreatedBy);
        await billing.Assign(bill.WorkItem.Id, workerTwo.Id, 30m);
        var assignmentTwo = await db.WorkerAssignments.SingleAsync(x => x.WorkerId == workerTwo.Id);
        await Assert.ThrowsAsync<BusinessException>(() => billing.EditAssignment(assignment.Id, workerOne.Id, 80m, assignment.Version));
        await Assert.ThrowsAsync<BusinessException>(() => billing.EditAssignment(assignment.Id, workerTwo.Id, 20m, assignment.Version));

        var invoices = new InvoiceService(db);
        var invoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "PART3-RECEIPT", new(2026, 1, 31), new Dictionary<int, decimal> { [bill.Id] = bill.Amount });
        var receipt = await invoices.CreateReceipt(new(2026, 1, 31), "PART3-ORIGINAL", Guid.NewGuid(), new Dictionary<int, decimal> { [invoice.Id] = 250m });
        var receiptCreatedAt = (await db.CustomerReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id)).CreatedAt;
        var receiptCreatedBy = (await db.CustomerReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id)).CreatedBy;
        var receiptVersion = (await db.CustomerReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id)).Version;
        await invoices.EditReceipt(receipt.Id, new(2026, 2, 1), "PART3-CORRECTED", receiptVersion);
        var editedReceipt = await db.CustomerReceipts.Include(x => x.Allocations).AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        Assert.Equal(new DateOnly(2026, 2, 1), editedReceipt.ReceiptDate); Assert.Equal("PART3-CORRECTED", editedReceipt.Reference); Assert.Equal(250m, editedReceipt.Allocations.Single().Amount);
        Assert.Equal(receiptCreatedAt.Ticks / 10, editedReceipt.CreatedAt.Ticks / 10); Assert.Equal(receiptCreatedBy, editedReceipt.CreatedBy);
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(receipt.Id, new(2026, 2, 2), "STALE", receiptVersion));
        await invoices.CancelReceipt(receipt.Id, "Replace allocation");
        var replacement = await invoices.CreateReceipt(new(2026, 2, 3), "PART3-REPLACEMENT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoice.Id] = 500m });
        Assert.NotEqual(receipt.Id, replacement.Id);
        Assert.Equal(InvoiceStatus.PartiallyPaid, (await db.Invoices.SingleAsync(x => x.Id == invoice.Id)).Status);

        await billing.Pay(workerOne.Id, new(2026, 2, 1), "PART3-PAYMENT", Guid.NewGuid(), new Dictionary<int, decimal> { [assignment.Id] = 50m });
        var payment = await db.WorkerPayments.AsNoTracking().SingleAsync(x => x.Reference == "PART3-PAYMENT");
        var paymentCreatedAt = payment.CreatedAt; var paymentCreatedBy = payment.CreatedBy;
        assignment = await db.WorkerAssignments.SingleAsync(x => x.Id == assignment.Id);
        await Assert.ThrowsAsync<BusinessException>(() => billing.EditAssignment(assignment.Id, workerOne.Id, 55m, assignment.Version));
        var paymentVersion = (await db.WorkerPayments.AsNoTracking().SingleAsync(x => x.Id == payment.Id)).Version;
        await billing.EditPayment(payment.Id, new(2026, 2, 2), "PART3-PAYMENT-CORRECTED", paymentVersion);
        var editedPayment = await db.WorkerPayments.Include(x => x.Allocations).AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(new DateOnly(2026, 2, 2), editedPayment.PaymentDate); Assert.Equal("PART3-PAYMENT-CORRECTED", editedPayment.Reference); Assert.Equal(50m, editedPayment.Allocations.Single().Amount);
        Assert.Equal(paymentCreatedAt, editedPayment.CreatedAt); Assert.Equal(paymentCreatedBy, editedPayment.CreatedBy);
        await Assert.ThrowsAsync<BusinessException>(() => billing.EditPayment(payment.Id, new(2026, 2, 3), "STALE", paymentVersion));
        await Assert.ThrowsAsync<BusinessException>(() => billing.Pay(workerOne.Id, new(2026, 2, 4), "OVERPAY", Guid.NewGuid(), new Dictionary<int, decimal> { [assignment.Id] = 191m }));
        Assert.Equal(30m, assignmentTwo.Percent);
    }

    [PostgresFact]
    public async Task Part46ReceiptAllocationAndHistoricalBillingAmountCorrectionsAreSafe()
    {
        await using var db = await Fresh();
        var billing = new BillingService(db);
        var engagementId = await Engagement(db);
        var first = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled, 1600m, 1600m);
        var invoices = new InvoiceService(db);
        var firstInvoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "PART46-HISTORICAL", new(2026, 1, 31), new Dictionary<int, decimal> { [first.Id] = 1500m });
        var originalCreatedAt = first.CreatedAt;
        var engagement = await db.Engagements.SingleAsync(x => x.Id == engagementId);
        engagement.BillingAmount = 1500m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1600m, (await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == first.Id)).Amount);

        var second = await billing.Generate(engagementId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        Assert.Equal(1500m, second.Amount);
        var firstVersions = await db.BillingRecords.Include(x => x.WorkItem).AsNoTracking().Where(x => x.Id == first.Id).Select(x => new { x.Version, x.Status, WorkItemVersion = x.WorkItem.Version }).SingleAsync();
        await billing.Correct(first.Id, first.PeriodStart, first.PeriodEnd, firstVersions.Status, "Historical amount correction", firstVersions.Version, firstVersions.WorkItemVersion, 1500m, 1500m);
        var correctedFirst = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(1500m, correctedFirst.Amount);
        Assert.Equal(BillingInvoiceState.FullyInvoiced, correctedFirst.CustomerInvoiceState);
        Assert.Equal(BillingStatus.Billed, correctedFirst.Status);
        Assert.Equal(originalCreatedAt.Ticks / 10, correctedFirst.CreatedAt.Ticks / 10);
        Assert.Equal(1500m, await db.Invoices.Where(x => x.Id == firstInvoice.Id).Select(x => x.Total).SingleAsync());
        var correctedVersion = correctedFirst.Version;
        var workItemVersion = await db.WorkItems.Where(x => x.BillingRecordId == first.Id).Select(x => x.Version).SingleAsync();
        var lowerError = await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(first.Id, first.PeriodStart, first.PeriodEnd, correctedFirst.Status, "Too low", correctedVersion, workItemVersion, 1400m, 1500m));
        Assert.Contains("cannot be lower", lowerError.Message);

        await invoices.CreateReceipt(new(2026, 2, 1), "PART46-HISTORICAL-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [firstInvoice.Id] = 100m });
        var receiptBlockedVersion = await db.BillingRecords.Include(x => x.WorkItem).AsNoTracking().Where(x => x.Id == first.Id).Select(x => new { x.Version, x.Status, WorkItemVersion = x.WorkItem.Version }).SingleAsync();
        var receiptBlock = await Assert.ThrowsAsync<BusinessException>(() => billing.Correct(first.Id, first.PeriodStart, first.PeriodEnd, receiptBlockedVersion.Status, "Receipt dependency", receiptBlockedVersion.Version, receiptBlockedVersion.WorkItemVersion, 1600m, 1500m));
        Assert.Contains("active customer receipt", receiptBlock.Message);

        var third = await billing.Generate(engagementId, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled);
        var invoiceA = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "PART46-A", new(2026, 3, 31), new Dictionary<int, decimal> { [second.Id] = 1500m });
        var invoiceB = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "PART46-B", new(2026, 3, 31), new Dictionary<int, decimal> { [third.Id] = 1500m });
        var multiReceipt = await invoices.CreateReceipt(new(2026, 3, 31), "PART46-MULTI", Guid.NewGuid(), new Dictionary<int, decimal> { [invoiceA.Id] = 1500m, [invoiceB.Id] = 1500m });
        var multiCreatedAt = multiReceipt.CreatedAt;
        var multiRequestId = multiReceipt.RequestId;
        var allocationIds = await db.CustomerReceiptAllocations.Where(x => x.CustomerReceiptId == multiReceipt.Id).ToDictionaryAsync(x => x.InvoiceId, x => x.Id);
        var multiVersion = multiReceipt.Version;
        await invoices.EditReceipt(multiReceipt.Id, new(2026, 4, 1), "PART46-MULTI-CORRECTED", multiVersion, new Dictionary<int, decimal> { [invoiceA.Id] = 1200m, [invoiceB.Id] = 1300m });
        var correctedReceipt = await db.CustomerReceipts.Include(x => x.Allocations).AsNoTracking().SingleAsync(x => x.Id == multiReceipt.Id);
        Assert.Equal(2500m, correctedReceipt.Amount);
        Assert.Equal(new DateOnly(2026, 4, 1), correctedReceipt.ReceiptDate);
        Assert.Equal("PART46-MULTI-CORRECTED", correctedReceipt.Reference);
        Assert.Equal(multiCreatedAt.Ticks / 10, correctedReceipt.CreatedAt.Ticks / 10);
        Assert.Equal(multiRequestId, correctedReceipt.RequestId);
        Assert.True(correctedReceipt.Version > multiVersion);
        Assert.Equal(allocationIds[invoiceA.Id], correctedReceipt.Allocations.Single(x => x.InvoiceId == invoiceA.Id).Id);
        Assert.Equal(1200m, correctedReceipt.Allocations.Single(x => x.InvoiceId == invoiceA.Id).Amount);
        Assert.Equal(1300m, correctedReceipt.Allocations.Single(x => x.InvoiceId == invoiceB.Id).Amount);
        Assert.Equal(InvoiceStatus.PartiallyPaid, await db.Invoices.Where(x => x.Id == invoiceA.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(BillingStatus.PartiallyPaid, await db.BillingRecords.Where(x => x.Id == second.Id).Select(x => x.Status).SingleAsync());

        var stale = correctedReceipt.Version;
        await invoices.EditReceipt(multiReceipt.Id, correctedReceipt.ReceiptDate, correctedReceipt.Reference, stale, new Dictionary<int, decimal> { [invoiceA.Id] = 1500m, [invoiceB.Id] = 1500m });
        Assert.Equal(3000m, await db.CustomerReceipts.Where(x => x.Id == multiReceipt.Id).Select(x => x.Amount).SingleAsync());
        Assert.Equal(InvoiceStatus.Paid, await db.Invoices.Where(x => x.Id == invoiceA.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(BillingStatus.Paid, await db.BillingRecords.Where(x => x.Id == second.Id).Select(x => x.Status).SingleAsync());
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(multiReceipt.Id, correctedReceipt.ReceiptDate, correctedReceipt.Reference, stale, new Dictionary<int, decimal> { [invoiceA.Id] = 1400m, [invoiceB.Id] = 1600m }));
        var multiCurrentVersion = await db.CustomerReceipts.AsNoTracking().Where(x => x.Id == multiReceipt.Id).Select(x => x.Version).SingleAsync();
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(multiReceipt.Id, correctedReceipt.ReceiptDate, correctedReceipt.Reference, multiCurrentVersion, new Dictionary<int, decimal> { [invoiceA.Id] = 1400m }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(multiReceipt.Id, correctedReceipt.ReceiptDate, correctedReceipt.Reference, multiCurrentVersion, new Dictionary<int, decimal> { [invoiceA.Id] = 0m, [invoiceB.Id] = 1500m }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(multiReceipt.Id, correctedReceipt.ReceiptDate, correctedReceipt.Reference, multiCurrentVersion, new Dictionary<int, decimal> { [invoiceA.Id] = 1500.001m, [invoiceB.Id] = 1500m }));
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(multiReceipt.Id, correctedReceipt.ReceiptDate, correctedReceipt.Reference, multiCurrentVersion, new Dictionary<int, decimal> { [invoiceA.Id] = 1500m, [invoiceB.Id] = 1500m, [firstInvoice.Id] = 1m }));

        var overpayBill = await billing.Generate(engagementId, new(2026, 4, 1), new(2026, 4, 30), BillingGenerationMode.Scheduled);
        var overpayInvoice = await invoices.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "PART46-OVERPAY", new(2026, 4, 30), new Dictionary<int, decimal> { [overpayBill.Id] = 1500m });
        await invoices.CreateReceipt(new(2026, 4, 30), "PART46-OTHER", Guid.NewGuid(), new Dictionary<int, decimal> { [overpayInvoice.Id] = 500m });
        var currentReceipt = await invoices.CreateReceipt(new(2026, 4, 30), "PART46-CURRENT", Guid.NewGuid(), new Dictionary<int, decimal> { [overpayInvoice.Id] = 400m });
        var currentVersion = currentReceipt.Version;
        await invoices.EditReceipt(currentReceipt.Id, currentReceipt.ReceiptDate, currentReceipt.Reference, currentVersion, new Dictionary<int, decimal> { [overpayInvoice.Id] = 1000m });
        Assert.Equal(1000m, await db.CustomerReceiptAllocations.Where(x => x.CustomerReceiptId == currentReceipt.Id).Select(x => x.Amount).SingleAsync());
        var currentReceiptVersion = await db.CustomerReceipts.AsNoTracking().Where(x => x.Id == currentReceipt.Id).Select(x => x.Version).SingleAsync();
        var overpayError = await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(currentReceipt.Id, currentReceipt.ReceiptDate, currentReceipt.Reference, currentReceiptVersion, new Dictionary<int, decimal> { [overpayInvoice.Id] = 1000.01m }));
        Assert.Contains("outstanding balance", overpayError.Message);
        await invoices.CancelReceipt(currentReceipt.Id, "Part46 cancellation");
        var cancelledVersion = await db.CustomerReceipts.AsNoTracking().Where(x => x.Id == currentReceipt.Id).Select(x => x.Version).SingleAsync();
        await Assert.ThrowsAsync<BusinessException>(() => invoices.EditReceipt(currentReceipt.Id, currentReceipt.ReceiptDate, currentReceipt.Reference, cancelledVersion, new Dictionary<int, decimal> { [overpayInvoice.Id] = 800m }));
    }

    [PostgresFact]
    public async Task ConsolidatedInvoiceReceiptAttributionUsesInvoiceLineProportion()
    {
        await using var db = await Fresh();
        var billing = new BillingService(db);
        var engagementId = await Engagement(db);
        var first = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var second = await billing.Generate(engagementId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        var invoices = new InvoiceService(db);
        var invoice = await invoices.CreateInvoice(
            InvoiceFlow.AccountingFirmToCustomer,
            "PART46-PROPORTIONAL",
            new(2026, 2, 28),
            new Dictionary<int, decimal> { [first.Id] = 1000m, [second.Id] = 1000m });
        var receipt = await invoices.CreateReceipt(
            new(2026, 2, 28),
            "PART46-PARTIAL",
            Guid.NewGuid(),
            new Dictionary<int, decimal> { [invoice.Id] = 1000m });

        db.ChangeTracker.Clear();
        var records = await db.BillingRecords
            .Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).ThenInclude(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt)
            .AsNoTracking()
            .Where(x => x.Id == first.Id || x.Id == second.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();
        var savedInvoice = await db.Invoices.Include(x => x.Lines).ThenInclude(x => x.BillingRecord).AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        var savedReceipt = await db.CustomerReceipts.Include(x => x.Allocations).AsNoTracking().SingleAsync(x => x.Id == receipt.Id);

        Assert.Equal(2000m, savedInvoice.Total);
        Assert.Equal(1000m, savedReceipt.Amount);
        Assert.Equal(1000m, savedReceipt.Allocations.Single().Amount);
        Assert.Equal(500m, records[0].CustomerReceivedAmount);
        Assert.Equal(500m, records[1].CustomerReceivedAmount);
        Assert.Equal(savedReceipt.Amount, records.Sum(x => x.CustomerReceivedAmount));
        Assert.Equal(savedInvoice.Total, savedInvoice.Lines.Sum(x => x.AllocatedAmount));
    }

    [PostgresFact]
    public async Task ZeroPercentWorkerAssignmentsRemainActiveWithoutEntitlement()
    {
        await using var db = await Fresh();
        var billing = new BillingService(db);
        var engagementId = await Engagement(db);
        var workers = Enumerable.Range(1, 4).Select(i => new Worker { Name = $"Zero-share worker {i}" }).ToArray();
        db.AddRange(workers);
        await db.SaveChangesAsync();

        var bill = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var gross = bill.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount;
        await billing.Assign(bill.WorkItem.Id, workers[0].Id, 0m);
        var zero = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == bill.WorkItem.Id && x.WorkerId == workers[0].Id);
        Assert.Equal(0m, zero.Percent);
        Assert.Equal(0m, zero.Entitlement);
        Assert.False(zero.IsCancelled);
        var zeroEntitlement = await db.WorkerAssignments.Where(x => x.WorkItemId == bill.WorkItem.Id && !x.IsCancelled).SumAsync(x => x.Entitlement);
        Assert.Equal(0m, zeroEntitlement);
        Assert.Equal(gross, gross - zeroEntitlement);

        var noEntitlement = await Assert.ThrowsAsync<BusinessException>(() => billing.Pay(
            workers[0].Id,
            new(2026, 1, 31),
            "ZERO-SHARE-PAYMENT",
            Guid.NewGuid(),
            new Dictionary<int, decimal> { [zero.Id] = 1m }));
        Assert.Equal("This assignment has no unpaid worker entitlement.", noEntitlement.Message);

        await billing.Assign(bill.WorkItem.Id, workers[1].Id, 70m);
        Assert.Equal(120m, gross - await db.WorkerAssignments.Where(x => x.WorkItemId == bill.WorkItem.Id && !x.IsCancelled).SumAsync(x => x.Entitlement));
        await Assert.ThrowsAsync<BusinessException>(() => billing.Assign(bill.WorkItem.Id, workers[0].Id, 0m));
        await Assert.ThrowsAsync<BusinessException>(() => billing.Assign(bill.WorkItem.Id, workers[2].Id, 31m));

        var second = await billing.Generate(engagementId, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        await billing.Assign(second.WorkItem.Id, workers[2].Id, 0m);
        await billing.Assign(second.WorkItem.Id, workers[3].Id, 100m);
        var secondAssignments = await db.WorkerAssignments.Where(x => x.WorkItemId == second.WorkItem.Id && !x.IsCancelled).ToListAsync();
        Assert.Equal(100m, secondAssignments.Sum(x => x.Percent));
        Assert.Equal(0m, secondAssignments.Single(x => x.WorkerId == workers[2].Id).Entitlement);
        Assert.Equal(gross, secondAssignments.Single(x => x.WorkerId == workers[3].Id).Entitlement);

        var seventy = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == bill.WorkItem.Id && x.WorkerId == workers[1].Id);
        await billing.EditAssignment(seventy.Id, workers[1].Id, 0m, seventy.Version);
        Assert.Equal(0m, await db.WorkerAssignments.Where(x => x.Id == seventy.Id).Select(x => x.Entitlement).SingleAsync());
        zero = await db.WorkerAssignments.SingleAsync(x => x.Id == zero.Id);
        await billing.EditAssignment(zero.Id, workers[0].Id, 70m, zero.Version);
        var corrected = await db.WorkerAssignments.AsNoTracking().SingleAsync(x => x.Id == zero.Id);
        Assert.Equal(70m, corrected.Percent);
        Assert.Equal(280m, corrected.Entitlement);

        await billing.Pay(workers[0].Id, new(2026, 2, 1), "ZERO-SHARE-CORRECTED-PAYMENT", Guid.NewGuid(), new Dictionary<int, decimal> { [corrected.Id] = 100m });
        var paid = await db.WorkerAssignments.AsNoTracking().SingleAsync(x => x.Id == corrected.Id);
        var blocked = await Assert.ThrowsAsync<BusinessException>(() => billing.EditAssignment(paid.Id, workers[0].Id, 0m, paid.Version));
        Assert.Contains("active worker payments", blocked.Message);
    }

    [PostgresFact]
    public async Task WeeklyProgressReportsEnforceWeeksUniquenessConcurrencyAndEntityScope()
    {
        var time = new FixedProgressTime();
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        await using var reset = await Fresh();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection).UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "weekly-progress-keys")).ConfigureServices(services => services.AddSingleton<TimeProvider>(time)));
        const string password = "Weekly-Progress-Password!123";
        int workId, assignmentAId, assignmentBId, reportBId, workerAId, workerBId, managerId, firmId;
        long reportVersion;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "progress-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workerA = new Worker { Name = "Progress Worker A" }; var workerB = new Worker { Name = "Progress Worker B" };
            var firm = new BusinessParty { Name = "Progress Firm" }; var manager = new Manager { Name = "Progress Manager" };
            var engagement = new Engagement { Customer = new() { Name = "Progress Customer" }, Service = new() { Name = "Progress Service" }, BusinessParty = firm, Manager = manager, StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 1, 1), AnchorDay = 1 } };
            db.AddRange(workerA, workerB, engagement); await db.SaveChangesAsync();
            var billing = new BillingService(db); var bill = await billing.Generate(engagement.Id, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
            await billing.Assign(bill.WorkItem.Id, workerA.Id, 50m); await billing.Assign(bill.WorkItem.Id, workerB.Id, 50m);
            workId = bill.WorkItem.Id; workerAId = workerA.Id; workerBId = workerB.Id; managerId = manager.Id; firmId = firm.Id;
            assignmentAId = await db.WorkerAssignments.Where(x => x.WorkItemId == workId && x.WorkerId == workerA.Id).Select(x => x.Id).SingleAsync();
            assignmentBId = await db.WorkerAssignments.Where(x => x.WorkItemId == workId && x.WorkerId == workerB.Id).Select(x => x.Id).SingleAsync();
            var assignmentCreatedAt = new DateTime(2026, 1, 5, 4, 0, 0, DateTimeKind.Utc);
            await db.WorkerAssignments.Where(x => x.Id == assignmentAId || x.Id == assignmentBId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
                .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
            db.ChangeTracker.Clear();
            var reporting = new ProgressReportService(db, clock); var week = clock.CurrentWeekStart;
            var assignmentAVersion = await db.WorkerAssignments.Where(x => x.Id == assignmentAId).Select(x => x.Version).SingleAsync();
            var assignmentBVersion = await db.WorkerAssignments.Where(x => x.Id == assignmentBId).Select(x => x.Version).SingleAsync();
            var reportA = await reporting.SaveAsync(new AccessProfile("worker-a", AppRoles.Worker, null, null, workerA.Id), new WeeklyProgressForm { WorkerAssignmentId = assignmentAId, AssignmentVersion = assignmentAVersion, WeekStart = week, ProgressPercent = 40, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Reconciled the assigned source documents", NextAction = "Complete the review", IssuesOrBlockers = "Waiting for one bank statement" });
            var reportB = await reporting.SaveAsync(new AccessProfile("worker-b", AppRoles.Worker, null, null, workerB.Id), new WeeklyProgressForm { WorkerAssignmentId = assignmentBId, AssignmentVersion = assignmentBVersion, WeekStart = week, ProgressPercent = 100, WorkflowStatus = WorkflowStatus.Completed, WorkDone = "Completed the assigned work" });
            reportBId = reportB.Id; reportVersion = reportA.Version;
            Assert.Equal(week.AddDays(6), reportA.WeekEnd); Assert.NotEqual(default, reportA.CreatedAt); Assert.Equal("system", reportA.CreatedBy);
            assignmentAVersion = await db.WorkerAssignments.Where(x => x.Id == assignmentAId).Select(x => x.Version).SingleAsync();
            await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(new AccessProfile("worker-a", AppRoles.Worker, null, null, workerA.Id), new WeeklyProgressForm { WorkerAssignmentId = assignmentAId, AssignmentVersion = assignmentAVersion, WeekStart = week, ProgressPercent = 20, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Duplicate", NextAction = "Continue" }));
            await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(new AccessProfile("worker-a", AppRoles.Worker, null, null, workerA.Id), new WeeklyProgressForm { WorkerAssignmentId = assignmentAId, AssignmentVersion = assignmentAVersion, WeekStart = week.AddDays(7), ProgressPercent = 20, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Future", NextAction = "Continue" }));
            var past = await reporting.SaveAsync(new AccessProfile("worker-a", AppRoles.Worker, null, null, workerA.Id), new WeeklyProgressForm { WorkerAssignmentId = assignmentAId, AssignmentVersion = assignmentAVersion, WeekStart = week.AddDays(-7), ProgressPercent = 10, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Late submission", NextAction = "Continue" });
            Assert.Equal(week.AddDays(-1), past.WeekEnd);
            await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(new AccessProfile("worker-a", AppRoles.Worker, null, null, workerA.Id), new WeeklyProgressForm { Id = past.Id, Version = past.Version, AssignmentVersion = assignmentAVersion, WorkerAssignmentId = assignmentAId, WeekStart = past.WeekStart, ProgressPercent = 20, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Rewrite", NextAction = "Continue" }));
            await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(new AccessProfile("worker-a", AppRoles.Worker, null, null, workerA.Id), new WeeklyProgressForm { Id = reportA.Id, Version = reportVersion - 1, AssignmentVersion = assignmentAVersion, WorkerAssignmentId = assignmentAId, WeekStart = week, ProgressPercent = 50, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Stale", NextAction = "Continue" }));
            var cancelled = await db.WorkerAssignments.SingleAsync(x => x.Id == assignmentBId); cancelled.IsCancelled = true; cancelled.CancellationReason = "Testing"; await db.SaveChangesAsync();
            await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(new AccessProfile("worker-b", AppRoles.Worker, null, null, workerB.Id), new WeeklyProgressForm { WorkerAssignmentId = assignmentBId, AssignmentVersion = cancelled.Version, WeekStart = week.AddDays(-7), ProgressPercent = 10, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Cancelled", NextAction = "Continue" }));
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            async Task AddUser(string email, string role, int? linkedFirm = null, int? linkedManager = null, int? linkedWorker = null) { var user = new AppUser { Email = email, UserName = email, EmailConfirmed = true, BusinessPartyId = linkedFirm, ManagerId = linkedManager, WorkerId = linkedWorker }; Seed.Check(await users.CreateAsync(user, password)); Seed.Check(await users.AddToRoleAsync(user, role)); }
            await AddUser("progress-worker-a@example.com", AppRoles.Worker, linkedWorker: workerAId); await AddUser("progress-worker-b@example.com", AppRoles.Worker, linkedWorker: workerBId);
            await AddUser("progress-manager@example.com", AppRoles.Manager, linkedManager: managerId); await AddUser("progress-firm@example.com", AppRoles.AccountingFirm, linkedFirm: firmId);
        }
        using var workerClient = await SignedIn(app, "progress-worker-a@example.com", password);
        var workerPage = await workerClient.GetStringAsync("/Progress"); Assert.Contains("Progress Customer", workerPage); Assert.Contains("Submitted", workerPage); Assert.DoesNotContain("Progress Worker B", workerPage); Assert.DoesNotContain("LCM gross", workerPage);
        Assert.Equal(HttpStatusCode.NotFound, (await workerClient.GetAsync($"/Progress/Edit/{reportBId}")).StatusCode);
        var forged = await PostWithToken(workerClient, "/Progress", "/Progress/Save", new() { ["WorkerAssignmentId"] = assignmentBId.ToString(), ["WeekStart"] = clock.CurrentWeekStart.ToString("yyyy-MM-dd"), ["ProgressPercent"] = "20", ["WorkflowStatus"] = WorkflowStatus.AssignmentStarted.ToString(), ["WorkDone"] = "Forged", ["NextAction"] = "Continue" });
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        var blockedWorkUpdate = await PostWithToken(workerClient, $"/Work/Details/{workId}", "/Work/Update", new() { ["id"] = workId.ToString(), ["version"] = "1", ["status"] = WorkStatus.Completed.ToString(), ["notes"] = "worker must use report" });
        Assert.Equal(HttpStatusCode.Redirect, blockedWorkUpdate.StatusCode); Assert.Contains("Denied", blockedWorkUpdate.Headers.Location!.ToString());
        using var managerClient = await SignedIn(app, "progress-manager@example.com", password); var managerPage = await managerClient.GetStringAsync("/Progress"); Assert.Contains("Progress Customer", managerPage); Assert.Contains("Progress Worker A", managerPage); Assert.DoesNotContain("Entitlement", managerPage);
        using var firmClient = await SignedIn(app, "progress-firm@example.com", password); var firmDenied = await firmClient.GetAsync("/Progress"); Assert.Equal(HttpStatusCode.Redirect, firmDenied.StatusCode); Assert.Contains("Denied", firmDenied.Headers.Location!.ToString());
        using var adminClient = await SignedIn(app, "progress-admin@example.com", password); var adminPage = await adminClient.GetStringAsync("/Progress"); Assert.Contains("Progress Worker A", adminPage); Assert.DoesNotContain("Progress Worker B", adminPage);
    }

}
