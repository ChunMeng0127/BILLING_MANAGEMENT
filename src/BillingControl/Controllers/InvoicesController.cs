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
        ViewBag.Bills = await db.BillingRecords.Include(x => x.InvoiceLines).ThenInclude(x => x.Invoice).Where(x => x.Status != BillingStatus.Cancelled).OrderByDescending(x => x.PeriodStart).ToListAsync();
        return View(new InvoiceForm());
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Create(InvoiceForm form)
    {
        ValidForm();
        await invoices.CreateInvoice(form.Flow, form.InvoiceNumber, form.InvoiceDate, form.Allocations.Where(x => x.Value != 0).ToDictionary(x => x.Key, x => x.Value));
        TempData["Success"] = "Invoice created with immutable billing allocations.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Receipt()
    {
        ViewBag.Bills = await db.Invoices.Where(x => x.Flow == InvoiceFlow.AccountingFirmToCustomer && x.Status != InvoiceStatus.Cancelled).Include(x => x.Lines).ThenInclude(x => x.BillingRecord).Include(x => x.ReceiptAllocations).ThenInclude(x => x.CustomerReceipt).OrderByDescending(x => x.InvoiceDate).ToListAsync();
        return View(new ReceiptForm());
    }

    [HttpPost, Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Receive(ReceiptForm form)
    {
        ValidForm();
        await invoices.CreateReceipt(form.ReceiptDate, form.Reference, form.RequestId, form.Allocations.Where(x => x.Value != 0).ToDictionary(x => x.Key, x => x.Value));
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
