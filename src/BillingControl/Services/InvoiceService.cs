using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static BillingControl.Services.Finance;

namespace BillingControl.Services;

public sealed class InvoiceService(AppDbContext db)
{
    public async Task<Invoice> CreateInvoice(InvoiceFlow flow, string number, DateOnly date, IDictionary<int, decimal> allocations)
    {
        try
        {
            return await FinancialTransaction.Serializable(db, async () =>
            {
                var invoice = await CreateInvoiceCore(flow, number, date, allocations);
                await db.SaveChangesAsync();
                return invoice;
            });
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == "23505")
        {
            db.ChangeTracker.Clear();
            throw new BusinessException("That invoice number is already used by this issuer.", ex);
        }
    }

    public async Task<Invoice> EditInvoice(int id, string number, DateOnly date, IDictionary<int, decimal> allocations, long version)
    {
        try
        {
            return await FinancialTransaction.Serializable(db, async () =>
            {
                var invoice = await db.Invoices
                    .Include(x => x.Lines).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Engagement).ThenInclude(x => x.BusinessParty)
                    .Include(x => x.Lines).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Engagement).ThenInclude(x => x.Customer)
                    .Include(x => x.Lines).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Engagement).ThenInclude(x => x.Manager)
                    .Include(x => x.Lines).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Shares)
                    .Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt)
                    .SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessException("Invoice was not found.");
                Require(invoice.Status != InvoiceStatus.Cancelled, "Cancelled invoices cannot be edited.");
                Require(invoice.Version == version, "This invoice changed. Refresh before saving.");
                Require(!string.IsNullOrWhiteSpace(number) && number.Trim().Length <= 100, "Invoice number is required (maximum 100 characters).");
                Require(date != default && date <= DateOnly.FromDateTime(DateTime.Today), "Invoice date must be today or earlier.");
                allocations = allocations.Where(x => x.Value != 0m).ToDictionary(x => x.Key, x => x.Value);
                Require(allocations.Count > 0 && allocations.Count <= 200, "Allocate the invoice to between 1 and 200 billing records.");
                Require(allocations.Values.All(x => x > 0 && Money(x) == x), "Invoice allocations must be positive amounts with no more than two decimal places.");
                var oldLines = invoice.Lines.ToList();
                var oldIds = oldLines.Select(x => x.BillingRecordId).ToHashSet();
                var selectedIds = allocations.Keys.ToHashSet();
                var allocationChanged = oldIds.Count != selectedIds.Count || !oldIds.SetEquals(selectedIds) || oldLines.Any(x => allocations[x.BillingRecordId] != x.AllocatedAmount);
                var hasActiveReceipts = invoice.ReceiptAllocations.Any(x => !x.CustomerReceipt.IsCancelled);
                Require(!allocationChanged || !hasActiveReceipts, "This invoice has active receipt allocations. Cancel the receipt before changing invoice allocations.");

                var bills = await db.BillingRecords
                    .Include(x => x.Engagement).ThenInclude(x => x.BusinessParty)
                    .Include(x => x.Engagement).ThenInclude(x => x.Customer)
                    .Include(x => x.Engagement).ThenInclude(x => x.Manager)
                    .Include(x => x.Shares)
                    .Where(x => selectedIds.Contains(x.Id)).ToListAsync();
                Require(bills.Count == selectedIds.Count, "Every invoice allocation must reference an existing billing record.");
                Require(bills.All(x => x.Status != BillingStatus.Cancelled), "Cancelled billing records cannot be added to an invoice.");
                var ids = oldIds.Union(selectedIds).ToArray();
                if (allocationChanged)
                {
                    ValidateGrouping(invoice.Flow, bills);
                    ValidateHeaderConsistency(invoice, bills);
                    var existing = await db.InvoiceLines
                        .Where(x => x.InvoiceId != id && selectedIds.Contains(x.BillingRecordId) && x.Invoice.Flow == invoice.Flow && x.Invoice.Status != InvoiceStatus.Cancelled)
                        .GroupBy(x => x.BillingRecordId)
                        .Select(g => new { BillingRecordId = g.Key, Amount = g.Sum(x => x.AllocatedAmount) })
                        .ToDictionaryAsync(x => x.BillingRecordId, x => x.Amount);
                    foreach (var bill in bills)
                    {
                        var cap = invoice.Flow switch
                        {
                            InvoiceFlow.AccountingFirmToCustomer => bill.Amount,
                            InvoiceFlow.ManagerToAccountingFirm => bill.Shares.Single(x => x.Kind == ShareKind.Manager).Amount,
                            InvoiceFlow.LcmToManager => bill.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount,
                            _ => 0m
                        };
                        Require(existing.GetValueOrDefault(bill.Id) + allocations[bill.Id] <= cap,
                            $"Invoice allocation exceeds the {FlowLabel(invoice.Flow)} amount for billing record B-{bill.Id:D5}.");
                    }
                }
                var trimmedNumber = number.Trim();
                var duplicate = invoice.Flow switch
                {
                    InvoiceFlow.AccountingFirmToCustomer => await db.Invoices.AnyAsync(x => x.Id != id && x.Flow == invoice.Flow && x.BusinessPartyId == invoice.BusinessPartyId && x.InvoiceNumber == trimmedNumber),
                    InvoiceFlow.ManagerToAccountingFirm => await db.Invoices.AnyAsync(x => x.Id != id && x.Flow == invoice.Flow && x.ManagerId == invoice.ManagerId && x.InvoiceNumber == trimmedNumber),
                    InvoiceFlow.LcmToManager => await db.Invoices.AnyAsync(x => x.Id != id && x.Flow == invoice.Flow && x.InvoiceNumber == trimmedNumber),
                    _ => true
                };
                Require(!duplicate, "That invoice number is already used by this issuer.");
                invoice.InvoiceNumber = trimmedNumber;
                invoice.InvoiceDate = date;
                if (allocationChanged)
                {
                    invoice.Total = allocations.Values.Sum();
                    PositiveMoney(invoice.Total);
                    foreach (var line in oldLines.Where(x => !selectedIds.Contains(x.BillingRecordId)))
                    {
                        invoice.Lines.Remove(line);
                        db.Remove(line);
                    }
                    foreach (var bill in bills)
                    {
                        var line = oldLines.SingleOrDefault(x => x.BillingRecordId == bill.Id);
                        if (line == null) invoice.Lines.Add(new InvoiceLine { BillingRecordId = bill.Id, AllocatedAmount = allocations[bill.Id] });
                        else line.AllocatedAmount = allocations[bill.Id];
                    }
                    db.Entry(invoice).Property(x => x.InvoiceDate).IsModified = true;
                    using (db.PermitInvoiceLineDeletion()) await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                    await RecalculateBillingStatuses(ids);
                    await db.SaveChangesAsync();
                    return invoice;
                }
                await db.SaveChangesAsync();
                return invoice;
            });
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == "23505")
        {
            db.ChangeTracker.Clear();
            throw new BusinessException("That invoice number is already used by this issuer.", ex);
        }
    }

    private async Task<Invoice> CreateInvoiceCore(InvoiceFlow flow, string number, DateOnly date, IDictionary<int, decimal> allocations)
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
            .Where(x => ids.Contains(x.Id)).ToListAsync();
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
            Require(existing.GetValueOrDefault(bill.Id) + allocations[bill.Id] <= cap,
                $"Invoice allocation exceeds the {FlowLabel(flow)} amount for billing record B-{bill.Id:D5}.");
        }

        var firms = bills.Select(x => x.Engagement.BusinessPartyId).Distinct().ToArray();
        var managers = bills.Select(x => x.Engagement.ManagerId).Distinct().ToArray();
        var customers = bills.Select(x => x.Engagement.CustomerId).Distinct().ToArray();
        Require(flow != InvoiceFlow.AccountingFirmToCustomer || (firms.Length == 1 && customers.Length == 1),
            "An accounting-firm customer invoice may contain records for only one firm and one end customer.");
        Require(flow != InvoiceFlow.ManagerToAccountingFirm || (managers.Length == 1 && firms.Length == 1),
            "A manager invoice may contain records for only one manager and one accounting firm.");
        Require(flow != InvoiceFlow.LcmToManager || managers.Length == 1, "An LCM invoice may contain records for only one manager.");

        int? businessPartyId = flow is InvoiceFlow.AccountingFirmToCustomer or InvoiceFlow.ManagerToAccountingFirm ? firms.Single() : null;
        int? customerId = flow == InvoiceFlow.AccountingFirmToCustomer ? customers.Single() : null;
        int? managerId = flow is InvoiceFlow.ManagerToAccountingFirm or InvoiceFlow.LcmToManager ? managers.Single() : null;
        var trimmedNumber = number.Trim();
        var duplicate = flow switch
        {
            InvoiceFlow.AccountingFirmToCustomer => await db.Invoices.AnyAsync(x => x.Flow == flow && x.BusinessPartyId == businessPartyId && x.InvoiceNumber == trimmedNumber),
            InvoiceFlow.ManagerToAccountingFirm => await db.Invoices.AnyAsync(x => x.Flow == flow && x.ManagerId == managerId && x.InvoiceNumber == trimmedNumber),
            InvoiceFlow.LcmToManager => await db.Invoices.AnyAsync(x => x.Flow == flow && x.InvoiceNumber == trimmedNumber),
            _ => true
        };
        Require(!duplicate, "That invoice number is already used by this issuer.");

        var invoice = new Invoice
        {
            InvoiceNumber = trimmedNumber, InvoiceDate = date, Flow = flow, Total = allocations.Values.Sum(),
            IssuerName = flow switch { InvoiceFlow.AccountingFirmToCustomer => bills[0].Engagement.BusinessParty.Name, InvoiceFlow.ManagerToAccountingFirm => bills[0].Engagement.Manager.Name, _ => "LCM MGT Sdn Bhd" },
            RecipientName = flow switch { InvoiceFlow.AccountingFirmToCustomer => bills[0].CustomerName, InvoiceFlow.ManagerToAccountingFirm => bills[0].Engagement.BusinessParty.Name, _ => bills[0].Engagement.Manager.Name },
            BusinessPartyId = businessPartyId, CustomerId = customerId, ManagerId = managerId,
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

    private static void ValidateGrouping(InvoiceFlow flow, IReadOnlyCollection<BillingRecord> bills)
    {
        var firms = bills.Select(x => x.Engagement.BusinessPartyId).Distinct().ToArray();
        var managers = bills.Select(x => x.Engagement.ManagerId).Distinct().ToArray();
        var customers = bills.Select(x => x.Engagement.CustomerId).Distinct().ToArray();
        Require(flow != InvoiceFlow.AccountingFirmToCustomer || (firms.Length == 1 && customers.Length == 1), "An accounting-firm customer invoice may contain records for only one firm and one end customer.");
        Require(flow != InvoiceFlow.ManagerToAccountingFirm || (managers.Length == 1 && firms.Length == 1), "A manager invoice may contain records for only one manager and one accounting firm.");
        Require(flow != InvoiceFlow.LcmToManager || managers.Length == 1, "An LCM invoice may contain records for only one manager.");
    }

    private static void ValidateHeaderConsistency(Invoice invoice, IReadOnlyCollection<BillingRecord> bills)
    {
        var firmId = bills.Select(x => x.Engagement.BusinessPartyId).Distinct().Single();
        var customerId = bills.Select(x => x.Engagement.CustomerId).Distinct().Single();
        var managerId = bills.Select(x => x.Engagement.ManagerId).Distinct().Single();
        switch (invoice.Flow)
        {
            case InvoiceFlow.AccountingFirmToCustomer:
                Require(invoice.BusinessPartyId == firmId && invoice.CustomerId == customerId && invoice.ManagerId == null,
                    "The selected billing records do not match this invoice's accounting firm and customer.");
                break;
            case InvoiceFlow.ManagerToAccountingFirm:
                Require(invoice.BusinessPartyId == firmId && invoice.ManagerId == managerId && invoice.CustomerId == null,
                    "The selected billing records do not match this invoice's manager and accounting firm.");
                break;
            case InvoiceFlow.LcmToManager:
                Require(invoice.BusinessPartyId == null && invoice.CustomerId == null && invoice.ManagerId == managerId,
                    "The selected billing records do not match this invoice's manager.");
                break;
            default:
                throw new BusinessException("Invoice flow is invalid.");
        }
    }

    public Task<CustomerReceipt> CreateReceipt(DateOnly date, string reference, Guid requestId, IDictionary<int, decimal> allocations) =>
        FinancialTransaction.Serializable(db, async () =>
        {
            PositiveMoney(allocations.Values.Sum());
            Require(requestId != Guid.Empty && !await db.CustomerReceipts.AnyAsync(x => x.RequestId == requestId), "This receipt was already submitted or its request identifier is missing.");
            Require(date != default && date <= DateOnly.FromDateTime(DateTime.Today) && !string.IsNullOrWhiteSpace(reference) && reference.Trim().Length <= 160, "Enter a receipt date (today or earlier) and reference up to 160 characters.");
            Require(allocations.Count > 0 && allocations.Count <= 200 && allocations.Values.All(x => x > 0 && Money(x) == x), "Allocate the receipt to one or more positive invoice amounts.");
            var ids = allocations.Keys.ToArray();
            var invoices = await db.Invoices.Include(x => x.Lines).Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).Where(x => ids.Contains(x.Id)).ToListAsync();
            Require(invoices.Count == allocations.Count && invoices.All(x => x.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Status != InvoiceStatus.Cancelled), "Receipts may only be allocated to active accounting-firm customer invoices.");
            Require(invoices.Select(x => x.BusinessPartyId).Distinct().Count() == 1 && invoices[0].BusinessPartyId != null, "One receipt may contain invoices for only one accounting firm.");
            Require(invoices.Select(x => x.CustomerId).Distinct().Count() == 1 && invoices[0].CustomerId != null, "One receipt may contain invoices for only one end customer.");
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
            await RecalculateBillingStatuses(invoices.SelectMany(x => x.Lines).Select(x => x.BillingRecordId));
            await db.SaveChangesAsync();
            return receipt;
        });

    public Task CancelInvoice(int id, string reason)
    {
        RequireReason(reason);
        return FinancialTransaction.Serializable(db, async () =>
        {
            var invoice = await db.Invoices.Include(x => x.Lines).Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessException("Invoice was not found.");
            Require(invoice.Status != InvoiceStatus.Cancelled, "Invoice is already cancelled.");
            Require(!invoice.ReceiptAllocations.Any(x => !x.CustomerReceipt.IsCancelled), "Cancel active customer receipts before cancelling this invoice.");
            invoice.Status = InvoiceStatus.Cancelled; invoice.CancellationReason = reason.Trim();
            await RecalculateBillingStatuses(invoice.Lines.Select(x => x.BillingRecordId));
            await db.SaveChangesAsync();
        });
    }

    public Task CancelReceipt(int id, string reason)
    {
        RequireReason(reason);
        return FinancialTransaction.Serializable(db, async () =>
        {
            var receipt = await db.CustomerReceipts.Include(x => x.Allocations).ThenInclude(x => x.Invoice).ThenInclude(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id) ?? throw new BusinessException("Receipt was not found.");
            Require(!receipt.IsCancelled, "Receipt is already cancelled.");
            receipt.IsCancelled = true; receipt.CancellationReason = reason.Trim();
            await db.SaveChangesAsync();
            var invoiceIds = receipt.Allocations.Select(x => x.InvoiceId).ToArray();
            var invoices = await db.Invoices.Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).Where(x => invoiceIds.Contains(x.Id)).ToListAsync();
            foreach (var invoice in invoices)
            {
                var paid = invoice.ReceiptAllocations.Where(x => !x.CustomerReceipt.IsCancelled).Sum(x => x.Amount);
                invoice.Status = paid == 0 ? InvoiceStatus.Issued : paid >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
            }
            await RecalculateBillingStatuses(receipt.Allocations.SelectMany(x => x.Invoice.Lines).Select(x => x.BillingRecordId));
            await db.SaveChangesAsync();
        });
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
                if (bill.Status is BillingStatus.Billed or BillingStatus.PartiallyPaid or BillingStatus.Paid) bill.Status = BillingStatus.ReadyToBill;
                continue;
            }
            var received = lines.Sum(x => paid.GetValueOrDefault(x.InvoiceId) * x.AllocatedAmount / x.Invoice.Total);
            bill.Status = received <= 0 ? BillingStatus.Billed : received >= bill.Amount ? BillingStatus.Paid : BillingStatus.PartiallyPaid;
        }
    }

    public static string FlowLabel(InvoiceFlow flow) => flow switch { InvoiceFlow.AccountingFirmToCustomer => "accounting firm customer invoice", InvoiceFlow.ManagerToAccountingFirm => "manager share", InvoiceFlow.LcmToManager => "LCM share", _ => "invoice" };
    private static void RequireReason(string reason) => Require(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 2000, "A cancellation reason is required (up to 2,000 characters).");
    private static PostgresException? FindPostgres(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
