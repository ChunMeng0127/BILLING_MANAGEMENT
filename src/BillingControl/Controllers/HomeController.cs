using BillingControl.Data;
using BillingControl.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Globalization;

namespace BillingControl.Controllers;

public class HomeController(AppDbContext db) : AppController
{
    private async Task<ReportModel> Report(ReportFilter f)
    {
        var q = db.BillingRecords.Include(x => x.Engagement).Include(x => x.Shares).Include(x => x.Receipts).Include(x => x.WorkItem).ThenInclude(x => x.Assignments).ThenInclude(x => x.Allocations).ThenInclude(x => x.WorkerPayment).AsQueryable();
        if (f.CustomerId != null) q = q.Where(x => x.Engagement.CustomerId == f.CustomerId);
        if (f.ServiceId != null) q = q.Where(x => x.Engagement.ServiceId == f.ServiceId);
        if (f.WorkerId != null) q = q.Where(x => x.WorkItem.Assignments.Any(a => a.WorkerId == f.WorkerId && !a.IsCancelled));
        if (f.Status != null) q = q.Where(x => x.Status == f.Status);
        if (f.From != null) q = q.Where(x => x.PeriodEnd >= f.From);
        if (f.To != null) q = q.Where(x => x.PeriodStart <= f.To);
        return new() { Filter = f, Bills = await q.AsSplitQuery().OrderByDescending(x => x.PeriodStart).ToListAsync() };
    }
    private async Task Choices()
    {
        ViewBag.Customers = new SelectList(await db.Customers.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Services = new SelectList(await db.Services.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Workers = new SelectList(await db.Workers.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
    }
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var model = await Report(filter); await Choices();
        model.Upcoming = await db.BillingSchedules.Include(x => x.Engagement).ThenInclude(x => x.Customer).Include(x => x.Engagement).ThenInclude(x => x.Service).Where(x => x.NextPeriodStart != null && x.Engagement.Status == EngagementStatus.Active).OrderBy(x => x.NextPeriodStart).Take(6).ToListAsync();
        return View(model);
    }
    public async Task<IActionResult> Reports(ReportFilter filter) { await Choices(); return View(await Report(filter)); }
    public async Task<IActionResult> Export(ReportFilter filter)
    {
        var r = await Report(filter);
        string Csv(string s) => "\"" + ((s.Length > 0 && "=+-@\t\r\n".Contains(s[0])) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        string N(decimal n) => n.ToString("0.00", CultureInfo.InvariantCulture);
        var lines = new List<string> { "Billing ID,Customer,Service,Period start,Period end,Status,Billing MYR,Firm MYR,Manager MYR,LCM gross MYR,Worker entitlement MYR,LCM retained MYR" };
        foreach (var b in r.Bills)
        {
            var gross = b.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount;
            var cost = b.WorkItem.Assignments.Where(x => !x.IsCancelled).Sum(x => x.Entitlement);
            lines.Add(string.Join(',', b.Id, Csv(b.CustomerName), Csv(b.ServiceName), b.PeriodStart.ToString("yyyy-MM-dd"), b.PeriodEnd.ToString("yyyy-MM-dd"), b.Status, N(b.Amount), N(b.Shares.Single(x => x.Kind == ShareKind.Firm).Amount), N(b.Shares.Single(x => x.Kind == ShareKind.Manager).Amount), N(gross), N(cost), N(gross - cost)));
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray(), "text/csv", "billing-report.csv");
    }
    public IActionResult Error() { Response.StatusCode = 500; return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier }); }
}