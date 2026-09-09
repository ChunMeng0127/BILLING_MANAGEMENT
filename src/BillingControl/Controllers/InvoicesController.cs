using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace BillingControl.Controllers;

public class InvoicesController(AppDbContext db, AccessScope access, InvoiceService invoices) : AppController
{
    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Index()
    {
        var a = await access.CurrentAsync();
        ViewBag.Access = a;
        var models = await access.Invoices(a)
            .Include(x => x.Lines)
            .ThenInclude(x => x.BillingRecord)
            .OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
            .ToListAsync();
        return View(models);
    }

    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Details(int id)
    {
        var a = await access.CurrentAsync();
        var invoice = await access.Invoices(a)
            .Include(x => x.Lines).ThenInclude(x => x.BillingRecord).ThenInclude(x => x.Engagement)
            .Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);
        if (invoice == null) return NotFound();
        ViewBag.Access = a;
        return View(invoice);
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Edit(int id)
    {
        var a = await access.CurrentAsync();
        var invoice = await access.Invoices(a)
            .Include(x => x.Lines).ThenInclude(x => x.BillingRecord)
            .Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);
        if (invoice == null) return NotFound();
        var bills = await db.BillingRecords
            .Include(x => x.Engagement).ThenInclude(x => x.BusinessParty)
            .Include(x => x.Engagement).ThenInclude(x => x.Manager)
            .Include(x => x.Shares)
            .Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice)
            .Where(x => x.Status != BillingStatus.Cancelled)
            .OrderByDescending(x => x.PeriodStart)
            .AsSplitQuery().ToListAsync();
        ViewBag.Access = a; ViewBag.Invoice = invoice; ViewBag.Bills = bills;
        return View(new InvoiceEditForm
        {
            Id = invoice.Id, Version = invoice.Version, InvoiceNumber = invoice.InvoiceNumber, InvoiceDate = invoice.InvoiceDate, Flow = invoice.Flow,
            Allocations = invoice.Lines.ToDictionary(x => x.BillingRecordId, x => (decimal?)x.AllocatedAmount)
        });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Edit(InvoiceEditForm form)
    {
        ValidForm();
        var a = await access.CurrentAsync();
        if (!await access.Invoices(a).AnyAsync(x => x.Id == form.Id)) return NotFound();
        var allocations = form.Allocations.ToDictionary(x => x.Key, x => x.Value ?? 0m);
        await invoices.EditInvoice(form.Id, form.InvoiceNumber, form.InvoiceDate, allocations, form.Version);
        TempData["Success"] = "Invoice correction saved; active allocations and totals were recalculated.";
        return RedirectToAction(nameof(Details), new { id = form.Id });
    }

    [Authorize(Roles = AppRoles.BillingReaders)]
    public async Task<IActionResult> Export()
    {
        var a = await access.CurrentAsync();
        var invoices = await access.Invoices(a)
            .Include(x => x.Lines).ThenInclude(x => x.BillingRecord)
            .OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
            .AsNoTracking().ToListAsync();
        string Csv(string value) => "\"" + ((value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
        string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        var rows = new List<string> { "Invoice,Date,Flow,Issuer,Recipient,Status,Total MYR,Billing ID,Allocated MYR" };
        foreach (var invoice in invoices)
            foreach (var line in invoice.Lines)
                rows.Add(string.Join(',', Csv(invoice.InvoiceNumber), invoice.InvoiceDate.ToString("yyyy-MM-dd"), invoice.Flow, Csv(invoice.IssuerName), Csv(invoice.RecipientName), invoice.Status, Money(invoice.Total), line.BillingRecordId, Money(line.AllocatedAmount)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", rows))).ToArray(), "text/csv", "invoice-register.csv");
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Create()
    {
        ViewBag.Access = await access.CurrentAsync();
        ViewBag.Bills = await db.BillingRecords
            .Include(x => x.Engagement).ThenInclude(x => x.BusinessParty)
            .Include(x => x.Engagement).ThenInclude(x => x.Manager)
            .Include(x => x.Shares)
            .Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice)
            .Where(x => x.Status != BillingStatus.Cancelled).OrderByDescending(x => x.PeriodStart).ToListAsync();
        return View(new InvoiceForm());
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Create(InvoiceForm form)
    {
        ValidForm();
        var allocations = NormalizeAllocations(form.Allocations, "Enter an amount for at least one billing record.");
        await invoices.CreateInvoice(form.Flow, form.InvoiceNumber, form.InvoiceDate, allocations);
        TempData["Success"] = "Invoice created with immutable billing allocations.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Receipt()
    {
        ViewBag.Bills = await db.Invoices.Where(x => x.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Status != InvoiceStatus.Cancelled).Include(x => x.Lines).ThenInclude(x => x.BillingRecord).Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).OrderByDescending(x => x.InvoiceDate).ToListAsync();
        return View(new ReceiptForm());
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> EditReceipt(int id)
    {
        var receipt = await db.CustomerReceipts
            .Include(x => x.Allocations).ThenInclude(x => x.Invoice).ThenInclude(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (receipt == null) return NotFound();
        ViewBag.Receipt = receipt;
        return View(new ReceiptEditForm
        {
            Id = receipt.Id, Version = receipt.Version, ReceiptDate = receipt.ReceiptDate, Reference = receipt.Reference,
            Allocations = receipt.Allocations.ToDictionary(x => x.InvoiceId, x => (decimal?)x.Amount)
        });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> EditReceipt(ReceiptEditForm form)
    {
        ValidForm();
        var allocations = form.Allocations.Count == 0 ? null : form.Allocations.ToDictionary(x => x.Key, x => x.Value ?? 0m);
        await invoices.EditReceipt(form.Id, form.ReceiptDate, form.Reference, form.Version, allocations);
        TempData["Success"] = allocations == null
            ? "Receipt date and reference corrected."
            : "Receipt correction saved. Allocation amounts and receipt total were recalculated. To change which invoice is included, cancel the receipt and create a replacement.";
        return RedirectToAction(nameof(Details), new { id = await db.CustomerReceiptAllocations.Where(x => x.CustomerReceiptId == form.Id).Select(x => x.InvoiceId).FirstOrDefaultAsync() });
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Receive(ReceiptForm form)
    {
        ValidForm();
        var allocations = NormalizeAllocations(form.Allocations, "Enter an amount for at least one invoice.");
        await invoices.CreateReceipt(form.ReceiptDate, form.Reference, form.RequestId, allocations);
        TempData["Success"] = "Customer receipt allocated to invoice documents.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Cancel(int id, string reason)
    {
        ValidForm();
        await invoices.CancelInvoice(id, reason);
        TempData["Success"] = "Invoice cancelled; history retained.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
