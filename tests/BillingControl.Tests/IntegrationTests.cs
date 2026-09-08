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
            var first = await finance.Generate(engagement.Id, new(2026, 1, 1), new(2026, 1, 31));
            var second = await finance.Generate(engagement.Id, new(2026, 2, 1), new(2026, 2, 28));
            var third = await finance.Generate(engagement.Id, new(2026, 3, 1), new(2026, 3, 31));
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
        var bill1 = await billing.Generate(id, new(2026, 1, 1), new(2026, 1, 31));
        var bill2 = await billing.Generate(secondEngagement.Id, new(2026, 1, 1), new(2026, 1, 31));
        var sameCustomerBill = await billing.Generate(sameCustomerEngagement.Id, new(2026, 1, 1), new(2026, 1, 31));
        var otherFirmBill = await billing.Generate(otherFirmEngagement.Id, new(2026, 1, 1), new(2026, 1, 31));
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

        var bill3 = await billing.Generate(id, new(2026, 2, 1), new(2026, 2, 28));
        var c1 = Db(); var c2 = Db();
        async Task Attempt(AppDbContext context, string number)
        {
            try { await new InvoiceService(context).CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, number, new(2026, 2, 28), new Dictionary<int, decimal> { [bill3.Id] = 600 }); }
            catch (BusinessException) { }
        }
        await Task.WhenAll(Attempt(c1, "CONCURRENT-A"), Attempt(c2, "CONCURRENT-B")); await c1.DisposeAsync(); await c2.DisposeAsync();
        Assert.Equal(600m, await db.InvoiceLines.Where(x => x.BillingRecordId == bill3.Id && x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Invoice.Status != InvoiceStatus.Cancelled).SumAsync(x => x.AllocatedAmount));

        var bill4 = await billing.Generate(id, new(2026, 3, 1), new(2026, 3, 31));
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
        var id = await Engagement(db); var billing = new BillingService(db); var bill = await billing.Generate(id, new(2026, 1, 1), new(2026, 1, 31));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BillingRecords\" SET \"BillingDate\"={new DateOnly(2026, 1, 31)}, \"InvoiceNumber\"={"OLD-001"}, \"Status\"={(int)BillingStatus.Billed} WHERE \"Id\"={bill.Id}");
        var request = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CustomerReceipts\" (\"BillingRecordId\", \"ReceiptDate\", \"Amount\", \"Reference\", \"RequestId\", \"IsCancelled\", \"CancellationReason\", \"CreatedAt\", \"CreatedBy\", \"UpdatedAt\", \"UpdatedBy\", \"Version\") VALUES ({bill.Id}, {new DateOnly(2026, 2, 1)}, {125m}, {"OLD-RECEIPT"}, {request}, {false}, {null}, {DateTime.UtcNow}, {"legacy"}, {DateTime.UtcNow}, {"legacy"}, {1})");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var invoice = await db.Invoices.Include(x => x.Lines).SingleAsync(x => x.InvoiceNumber == "OLD-001");
        Assert.Equal(1000m, invoice.Total); Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status); Assert.Equal(bill.Id, invoice.Lines.Single().BillingRecordId);
        Assert.Equal(await db.Engagements.Where(x => x.Id == id).Select(x => x.CustomerId).SingleAsync(), invoice.CustomerId);
        var allocation = await db.CustomerReceiptAllocations.Include(x => x.CustomerReceipt).SingleAsync(x => x.InvoiceId == invoice.Id);
        Assert.Equal(125m, allocation.Amount); Assert.Equal("OLD-RECEIPT", allocation.CustomerReceipt.Reference);
        Assert.False(await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='BillingRecords' AND column_name='InvoiceNumber') AS \"Value\"").SingleAsync());
    }
    [PostgresFact]
    public async Task ConcurrentPaymentsCannotOverpay()
    {
        await using var db = await Fresh(); var id = await Engagement(db); var svc = new BillingService(db); var bill = await svc.Generate(id, new(2026, 1, 1), new(2026, 1, 31)); var w = new Worker { Name = "Concurrency" }; db.Add(w); await db.SaveChangesAsync(); await svc.Assign(bill.WorkItem.Id, w.Id, 70); var a = await db.WorkerAssignments.SingleAsync();
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
        long version;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "BootstrapAdmin:Email", "freeze-admin@example.com" }, { "BootstrapAdmin:Password", password } }).Build());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { Name = "Frozen Customer" }; var service = new Service { Name = "Frozen Service" };
            var firm = new BusinessParty { Name = "Original Firm" }; var manager = new Manager { Name = "Original Manager" };
            var otherCustomer = new Customer { Name = "Replacement Customer" }; var otherService = new Service { Name = "Replacement Service" };
            var otherFirm = new BusinessParty { Name = "Replacement Firm" }; var otherManager = new Manager { Name = "Replacement Manager" };
            var engagement = new Engagement { Customer = customer, Service = service, BusinessParty = firm, Manager = manager, StartDate = new(2026, 1, 1), BillingAmount = 1000m, Schedule = new() { Frequency = Frequency.Monthly, NextPeriodStart = new(2026, 2, 1), AnchorDay = 1 } };
            db.AddRange(engagement, otherCustomer, otherService, otherFirm, otherManager); await db.SaveChangesAsync();
            await new BillingService(db).Generate(engagement.Id, new(2026, 1, 1), new(2026, 1, 31));
            engagementId = engagement.Id; customerId = customer.Id; serviceId = service.Id; firmId = firm.Id; managerId = manager.Id;
            otherCustomerId = otherCustomer.Id; otherServiceId = otherService.Id; otherFirmId = otherFirm.Id; otherManagerId = otherManager.Id;
            version = (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId)).Version;
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
        int firmAId, firmBId, managerAId, managerBId, workerAId, workerBId, billAId, billBId, workAId, workBId, invoiceAId, invoiceBId, managerInvoiceAId;
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
            var invoiceService = new InvoiceService(db);
            invoiceAId = (await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "ALPHA-INV", new(2026, 1, 31), new Dictionary<int, decimal> { [billA.Id] = billA.Amount })).Id;
            invoiceBId = (await invoiceService.CreateInvoice(InvoiceFlow.AccountingFirmToCustomer, "BETA-INV", new(2026, 1, 31), new Dictionary<int, decimal> { [billB.Id] = billB.Amount })).Id;
            managerInvoiceAId = (await invoiceService.CreateInvoice(InvoiceFlow.ManagerToAccountingFirm, "ALPHA-MGR-INV", new(2026, 1, 31), new Dictionary<int, decimal> { [billA.Id] = 250 })).Id;
            await invoiceService.CreateReceipt(new(2026, 1, 31), "ALPHA-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoiceAId] = 100m });
            await invoiceService.CreateReceipt(new(2026, 1, 31), "BETA-RECEIPT", Guid.NewGuid(), new Dictionary<int, decimal> { [invoiceBId] = 200m });
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
        var forgedWork = await PostWithToken(workerClient, $"/Work/Details/{workAId}", "/Work/Update", new() { ["id"] = workBId.ToString(), ["version"] = workBVersion.ToString(), ["status"] = WorkStatus.Completed.ToString(), ["notes"] = "forged" });
        Assert.Equal(HttpStatusCode.NotFound, forgedWork.StatusCode);
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
        var adminBilling = await admin.GetStringAsync("/Billing"); Assert.Contains("Alpha Scope Customer", adminBilling); Assert.Contains("Beta Scope Customer", adminBilling); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Billing/Details/{billBId}")).StatusCode);
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

}
