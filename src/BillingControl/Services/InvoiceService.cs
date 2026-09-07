using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using static BillingControl.Services.Finance;

namespace BillingControl.Services;

public sealed class InvoiceService(AppDbContext db)
{
    public async Task<Invoice> CreateInvoice(InvoiceFlow flow, string number, DateOnly date, IDictionary<int, decimal> allocations)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var invoice = await CreateInvoiceCore(flow, number, date, allocations);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return invoice;
    }

    internal async Task<Invoice> CreateInvoiceCore(InvoiceFlow flow, string number, DateOnly date, IDictionary<int, decimal> allocations)
    {
        Require(Enum.IsDefined(flow), "Select a valid invoice flow.");
        Require(!string.IsNullOrWhiteSpace(number) && number.Trim().Length <= 100, "Invoice number is required (maximum 100 characters).");
        Require(date != default && date <= DateOnly.FromDateTime(DateTime.Today), "Invoice date must be today or earlier.");
        Require(allocations.Count > 0 && allocations.Count <= 200, "Allocate the invoice to between 1 and 200 billing records.");
        Require(allocations.Values.All(x => x > 0 && Money(x) == x), "Invoice allocations must be positive amounts with no more than two decimal places.");
        var ids = allocations.Keys.ToArray();
        var bills = await db.BillingRecords
            .Include(x => x.Engagement).ThenInclude(x => x.BusinessParty)
            .Include(x => x.Engagement).ThenInclude(x => x.Manager)
            .Include(x => x.Engagement).ThenInclude(x => x.Customer)
            .Include(x => x.Shares)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();
        Require(bills.Count == allocations.Count, "Every invoice allocation must reference an existing billing record.");
        Require(bills.All(x => x.Status != BillingStatus.Cancelled), "Cancelled billing records cannot be invoiced.");

        var existing = await db.InvoiceLines
            .Where(x => ids.Contains(x.BillingRecordId) && x.Invoice.Flow == flow && x.Invoice.Status != InvoiceStatus.Cancelled)
            .GroupBy(x => x.BillingRecordId)
            .Select(g => new { BillingRecordId = g.Key, Amount = g.Sum(x => x.AllocatedAmount) })
            .ToDictionaryAsync(x => x.BillingRecordId, x => x.Amount);

        foreach (var bill in bills)
        {
            var cap = flow switch
            {
                InvoiceFlow.AccountingFirmToCustomer => bill.Amount,
                InvoiceFlow.ManagerToAccountingFirm => bill.Shares.Single(x => x.Kind == ShareKind.Manager).Amount,
                InvoiceFlow.LcmToManager => bill.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount,
                _ => 0m
            };
            var already = existing.GetValueOrDefault(bill.Id);
            Require(already + allocations[bill.Id] <= cap, $"Invoice allocation exceeds the {FlowLabel(flow)} amount for billing record B-{bill.Id:D5}.");
        }

        var firms = bills.Select(x => x.Engagement.BusinessPartyId).Distinct().ToArray();
        var managers = bills.Select(x => x.Engagement.ManagerId).Distinct().ToArray();
        int? businessPartyId = flow switch
        {
            InvoiceFlow.AccountingFirmToCustomer or InvoiceFlow.ManagerToAccountingFirm when firms.Length == 1 => firms[0],
            _ => null
        };
        int? managerId = flow switch
        {
            InvoiceFlow.ManagerToAccountingFirm or InvoiceFlow.LcmToManager when managers.Length == 1 => managers[0],
            _ => null
        };
        Require(flow != InvoiceFlow.AccountingFirmToCustomer || firms.Length == 1, "An accounting-firm customer invoice may contain records for only one firm.");
        Require(flow != InvoiceFlow.ManagerToAccountingFirm || (managers.Length == 1 && firms.Length == 1), "A manager invoice may contain records for only one manager and one accounting firm.");
        Require(flow != InvoiceFlow.LcmToManager || managers.Length == 1, "An LCM invoice may contain records for only one manager.");

        var issuer = flow switch
        {
            InvoiceFlow.AccountingFirmToCustomer => bills[0].Engagement.BusinessParty.Name,
            InvoiceFlow.ManagerToAccountingFirm => bills[0].Engagement.Manager.Name,
            InvoiceFlow.LcmToManager => "LCM MGT Sdn Bhd",
            _ => ""
        };
        var recipient = flow switch
        {
            InvoiceFlow.AccountingFirmToCustomer => DistinctLabel(bills.Select(x => x.CustomerName), "Multiple customers"),
            InvoiceFlow.ManagerToAccountingFirm => bills[0].Engagement.BusinessParty.Name,
            InvoiceFlow.LcmToManager => bills[0].Engagement.Manager.Name,
            _ => ""
        };
        var invoice = new Invoice
        {
            InvoiceNumber = number.Trim(),
            InvoiceDate = date,
            Flow = flow,
            Status = InvoiceStatus.Issued,
            Total = allocations.Values.Sum(),
            IssuerName = issuer,
            RecipientName = recipient,
            BusinessPartyId = businessPartyId,
            ManagerId = managerId,
            Lines = allocations.Select(x => new InvoiceLine { BillingRecordId = x.Key, AllocatedAmount = x.Value }).ToList()
        };
        PositiveMoney(invoice.Total);
        db.Invoices.Add(invoice);
        if (flow == InvoiceFlow.AccountingFirmToCustomer)
            foreach (var bill in bills)
                if (existing.GetValueOrDefault(bill.Id) + allocations[bill.Id] >= bill.Amount && bill.Status < BillingStatus.Billed)
                    bill.Status = BillingStatus.Billed;
        return invoice;
    }

    public async Task<CustomerReceipt> CreateReceipt(DateOnly date, string reference, Guid requestId, IDictionary<int, decimal> allocations)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var receipt = await CreateReceiptCore(date, reference, requestId, allocations);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return receipt;
    }

    internal async Task<CustomerReceipt> CreateReceiptForBillingRecord(DateOnly date, decimal amount, string reference, Guid requestId, int billingRecordId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var billingStatus = await db.BillingRecords.Where(x => x.Id == billingRecordId).Select(x => x.Status).SingleOrDefaultAsync();
        Require(billingStatus is BillingStatus.Billed or BillingStatus.PartiallyPaid, "Only billed records with an outstanding balance can receive payment.");
        var candidateIds = await db.InvoiceLines
            .Where(x => x.BillingRecordId == billingRecordId && x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Invoice.Status != InvoiceStatus.Cancelled)
            .Select(x => x.InvoiceId).Distinct().ToArrayAsync();
        var candidates = await db.Invoices
            .Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt)
            .Where(x => candidateIds.Contains(x.Id))
            .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id)
            .ToListAsync();
        var selectedInvoice = candidates.FirstOrDefault(x => x.ReceiptAllocations.Where(y => !y.CustomerReceipt.IsCancelled).Sum(y => y.Amount) < x.Total)
            ?? throw new BusinessException("Create a customer invoice before recording a receipt.");
        var receipt = await CreateReceiptCore(date, reference, requestId, new Dictionary<int, decimal> { [selectedInvoice.Id] = amount });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return receipt;
    }

    private async Task<CustomerReceipt> CreateReceiptCore(DateOnly date, string reference, Guid requestId, IDictionary<int, decimal> allocations)
    {
        PositiveMoney(allocations.Values.Sum());
        Require(requestId != Guid.Empty && !await db.CustomerReceipts.AnyAsync(x => x.RequestId == requestId), "This receipt was already submitted or its request identifier is missing.");
        Require(date != default && date <= DateOnly.FromDateTime(DateTime.Today) && !string.IsNullOrWhiteSpace(reference) && reference.Trim().Length <= 160, "Enter a receipt date (today or earlier) and reference up to 160 characters.");
        Require(allocations.Count > 0 && allocations.Count <= 200 && allocations.Values.All(x => x > 0 && Money(x) == x), "Allocate the receipt to one or more positive invoice amounts.");
        var ids = allocations.Keys.ToArray();
        var invoices = await db.Invoices.Include(x => x.Lines).Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).Where(x => ids.Contains(x.Id)).ToListAsync();
        Require(invoices.Count == allocations.Count && invoices.All(x => x.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Status != InvoiceStatus.Cancelled), "Receipts may only be allocated to active accounting-firm customer invoices.");
        foreach (var invoice in invoices)
        {
            var paid = invoice.ReceiptAllocations.Where(x => !x.CustomerReceipt.IsCancelled).Sum(x => x.Amount);
            Require(paid + allocations[invoice.Id] <= invoice.Total, $"Receipt exceeds the outstanding balance of invoice {invoice.InvoiceNumber}.");
        }
        var paidBefore = invoices.ToDictionary(x => x.Id, x => x.ReceiptAllocations.Where(y => !y.CustomerReceipt.IsCancelled).Sum(y => y.Amount));
        var receipt = new CustomerReceipt { ReceiptDate = date, Amount = allocations.Values.Sum(), Reference = reference.Trim(), RequestId = requestId, Allocations = allocations.Select(x => new CustomerReceiptAllocation { InvoiceId = x.Key, Amount = x.Value }).ToList() };
        db.CustomerReceipts.Add(receipt);
        foreach (var invoice in invoices)
        {
            var paid = paidBefore[invoice.Id] + allocations[invoice.Id];
            invoice.Status = paid >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        }
        await db.SaveChangesAsync();
        await RecalculateBillingStatuses(invoices.SelectMany(x => x.Lines).Select(x => x.BillingRecordId).Distinct());
        return receipt;
    }

    public async Task CancelInvoice(int id, string reason)
    {
        RequireReason(reason);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var invoice = await db.Invoices.Include(x => x.Lines).Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).SingleOrDefaultAsync(x => x.Id == id);
        if (invoice == null) throw new BusinessException("Invoice was not found.");
        Require(invoice.Status != InvoiceStatus.Cancelled, "Invoice is already cancelled.");
        Require(!invoice.ReceiptAllocations.Any(x => !x.CustomerReceipt.IsCancelled), "Cancel active customer receipts before cancelling this invoice.");
        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancellationReason = reason.Trim();
        await RecalculateBillingStatuses(invoice.Lines.Select(x => x.BillingRecordId));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task CancelReceipt(int id, string reason)
    {
        RequireReason(reason);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var receipt = await db.CustomerReceipts.Include(x => x.Allocations).ThenInclude(x => x.Invoice).ThenInclude(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id);
        if (receipt == null) throw new BusinessException("Receipt was not found.");
        Require(!receipt.IsCancelled, "Receipt is already cancelled.");
        receipt.IsCancelled = true;
        receipt.CancellationReason = reason.Trim();
        await db.SaveChangesAsync();
        var invoiceIds = receipt.Allocations.Select(x => x.InvoiceId).ToArray();
        var invoices = await db.Invoices.Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).Where(x => invoiceIds.Contains(x.Id)).ToListAsync();
        foreach (var invoice in invoices)
        {
            var paid = invoice.ReceiptAllocations.Where(x => !x.CustomerReceipt.IsCancelled).Sum(x => x.Amount);
            invoice.Status = paid == 0 ? InvoiceStatus.Issued : paid >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        }
        await RecalculateBillingStatuses(receipt.Allocations.Select(x => x.Invoice.Lines.Select(y => y.BillingRecordId)).SelectMany(x => x));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task RecalculateBillingStatuses(IEnumerable<int> ids)
    {
        var billIds = ids.Distinct().ToArray();
        if (billIds.Length == 0) return;
        var bills = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).Where(x => billIds.Contains(x.Id)).ToListAsync();
        var invoiceIds = bills.SelectMany(x => x.InvoiceLines).Where(x => x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Invoice.Status != InvoiceStatus.Cancelled).Select(x => x.InvoiceId).Distinct().ToArray();
        var paid = await db.CustomerReceiptAllocations.Where(x => invoiceIds.Contains(x.InvoiceId) && !x.CustomerReceipt.IsCancelled).GroupBy(x => x.InvoiceId).Select(g => new { InvoiceId = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.InvoiceId, x => x.Amount);
        foreach (var bill in bills)
        {
            if (bill.Status == BillingStatus.Cancelled) continue;
            var lines = bill.InvoiceLines.Where(x => x.Invoice.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Invoice.Status != InvoiceStatus.Cancelled).ToList();
            var invoiced = lines.Sum(x => x.AllocatedAmount);
            if (invoiced < bill.Amount)
            {
                if (bill.Status is BillingStatus.Billed or BillingStatus.PartiallyPaid or BillingStatus.Paid)
                    bill.Status = BillingStatus.ReadyToBill;
                continue;
            }
            var received = lines.Sum(x => paid.GetValueOrDefault(x.InvoiceId) * x.AllocatedAmount / x.Invoice.Total);
            bill.Status = received <= 0 ? BillingStatus.Billed : received >= bill.Amount ? BillingStatus.Paid : BillingStatus.PartiallyPaid;
        }
    }

    private static string DistinctLabel(IEnumerable<string> values, string multiple) { var distinct = values.Distinct(StringComparer.Ordinal).ToArray(); return distinct.Length == 1 ? distinct[0] : multiple; }
    public static string FlowLabel(InvoiceFlow flow) => flow switch { InvoiceFlow.AccountingFirmToCustomer => "accounting firm customer invoice", InvoiceFlow.ManagerToAccountingFirm => "manager share", InvoiceFlow.LcmToManager => "LCM share", _ => "invoice" };
    private static void RequireReason(string reason) => Require(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 2000, "A cancellation reason is required (up to 2,000 characters).");
}
