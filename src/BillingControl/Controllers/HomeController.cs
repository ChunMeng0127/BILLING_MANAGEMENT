using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace BillingControl.Controllers;

public class HomeController(AppDbContext db, AccessScope access) : AppController
{
    private async Task<ReportModel> Report(ReportFilter f)
    {
        var a = await access.CurrentAsync();
        if (a.IsWorker) f.WorkerId = a.WorkerId;
        else if (!a.IsStaff) f.WorkerId = null;
        var staff = a.IsStaff;
        var firm = a.IsAccountingFirm;
        var manager = a.IsManager;
        var worker = a.IsWorker;
        var q = access.BillingRecords(a)
            .Include(x => x.Engagement)
            .Include(x => x.Shares.Where(s => staff || (firm && s.Kind == ShareKind.Firm) || (manager && s.Kind == ShareKind.Manager)))
            .Include(x => x.InvoiceLines.Where(_ => staff || firm || manager))
            .ThenInclude(x => x.Invoice)
            .ThenInclude(x => x.ReceiptAllocations)
            .ThenInclude(x => x.CustomerReceipt)
            .Include(x => x.WorkItem)
            .ThenInclude(x => x.Assignments.Where(y => staff || (worker && !y.IsCancelled && y.WorkerId == a.WorkerId)))
            .ThenInclude(x => x.Allocations)
            .ThenInclude(x => x.WorkerPayment)
            .AsQueryable();
        if (f.CustomerId != null) q = q.Where(x => x.Engagement.CustomerId == f.CustomerId);
        if (f.ServiceId != null) q = q.Where(x => x.Engagement.ServiceId == f.ServiceId);
        if (f.WorkerId != null) q = q.Where(x => x.WorkItem.Assignments.Any(y => y.WorkerId == f.WorkerId && !y.IsCancelled));
        if (f.Status != null) q = q.Where(x => x.Status == f.Status);
        if (f.From != null) q = q.Where(x => x.PeriodEnd >= f.From);
        if (f.To != null) q = q.Where(x => x.PeriodStart <= f.To);
        var model = new ReportModel { Access = a, Filter = f, Bills = await q.AsSplitQuery().OrderByDescending(x => x.PeriodStart).ToListAsync() };
        if (a.IsStaff || a.IsManager || a.IsWorker)
        {
            var currentWeek = ProgressReportService.CurrentWeekStart();
            var assignmentQuery = access.ProgressAssignments(a).Where(x => !x.IsCancelled);
            model.ProgressRequired = await assignmentQuery.CountAsync();
            model.ProgressSubmitted = await access.ProgressReports(a).Where(x => !x.WorkerAssignment.IsCancelled).CountAsync(x => x.WeekStart == currentWeek);
            model.ProgressMissing = Math.Max(0, model.ProgressRequired - model.ProgressSubmitted);
            var previousWeek = currentWeek.AddDays(-7);
            model.ProgressLate = await assignmentQuery.Where(x => x.WorkItem.BillingRecord.PeriodStart <= previousWeek.AddDays(6) && x.WorkItem.BillingRecord.PeriodEnd >= previousWeek)
                .CountAsync(x => !access.ProgressReports(a).Where(r => !r.WorkerAssignment.IsCancelled).Any(r => r.WorkerAssignmentId == x.Id && r.WeekStart == previousWeek));
        }
        return model;
    }

    private async Task Choices(AccessProfile a)
    {
        ViewBag.Customers = new SelectList(await access.Customers(a).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Services = new SelectList(await access.Services(a).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Workers = new SelectList(a.IsStaff ? await db.Workers.OrderBy(x => x.Name).ToListAsync() : [], "Id", "Name");
    }

    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var model = await Report(filter);
        await Choices(model.Access);
        model.Upcoming = await access.BillingSchedules(model.Access).Include(x => x.Engagement).ThenInclude(x => x.Customer).Include(x => x.Engagement).ThenInclude(x => x.Service).Where(x => x.NextPeriodStart != null && x.Engagement.Status == EngagementStatus.Active).OrderBy(x => x.NextPeriodStart).Take(6).ToListAsync();
        return View(model);
    }

    public async Task<IActionResult> Reports(ReportFilter filter)
    {
        var model = await Report(filter);
        await Choices(model.Access);
        return View(model);
    }

    public async Task<IActionResult> Export(ReportFilter filter)
    {
        var r = await Report(filter);
        string Csv(string s) => "\"" + ((s.Length > 0 && "=+-@\t\r\n".Contains(s[0])) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        string N(decimal n) => n.ToString("0.00", CultureInfo.InvariantCulture);
        var lines = new List<string>();
        if (r.Access.IsWorker)
        {
            lines.Add("Assignment ID,Customer,Service,Period start,Period end,Work status,Worker percent,Entitlement MYR,Paid MYR,Due MYR");
            foreach (var b in r.Bills)
                foreach (var a in b.WorkItem.Assignments.Where(x => !x.IsCancelled && x.WorkerId == r.Access.WorkerId))
                {
                    var paid = a.Allocations.Where(x => !x.WorkerPayment.IsCancelled).Sum(x => x.Amount);
                    lines.Add(string.Join(',', a.Id, Csv(b.CustomerName), Csv(b.ServiceName), b.PeriodStart.ToString("yyyy-MM-dd"), b.PeriodEnd.ToString("yyyy-MM-dd"), b.WorkItem.Status, N(a.Percent), N(a.Entitlement), N(paid), N(a.Entitlement - paid)));
                }
        }
        else if (r.Access.IsAccountingFirm)
        {
            lines.Add("Billing ID,Customer,Service,Period start,Period end,Status,Customer billing MYR,Firm percent,Firm share MYR,Customer invoice state,Customer invoiced MYR,Received MYR,Outstanding MYR");
            foreach (var b in r.Bills)
            {
                var share = b.Shares.Single(x => x.Kind == ShareKind.Firm);
                lines.Add(string.Join(',', b.Id, Csv(b.CustomerName), Csv(b.ServiceName), b.PeriodStart.ToString("yyyy-MM-dd"), b.PeriodEnd.ToString("yyyy-MM-dd"), b.Status, N(b.Amount), N(share.Percent), N(share.Amount), b.CustomerInvoiceState, N(b.CustomerInvoicedAmount), N(b.CustomerReceivedAmount), N(b.CustomerOutstandingAmount)));
            }
        }
        else if (r.Access.IsManager)
        {
            lines.Add("Billing ID,Customer,Service,Period start,Period end,Billing status,Work status,Customer billing MYR,Manager percent,Manager share MYR,Manager invoice MYR");
            foreach (var b in r.Bills)
            {
                var share = b.Shares.Single(x => x.Kind == ShareKind.Manager);
                var invoiced = b.InvoiceLines.Where(x => x.Invoice.Status != InvoiceStatus.Cancelled && x.Invoice.Flow == InvoiceFlow.ManagerToAccountingFirm).Sum(x => x.AllocatedAmount);
                lines.Add(string.Join(',', b.Id, Csv(b.CustomerName), Csv(b.ServiceName), b.PeriodStart.ToString("yyyy-MM-dd"), b.PeriodEnd.ToString("yyyy-MM-dd"), b.Status, b.WorkItem.Status, N(b.Amount), N(share.Percent), N(share.Amount), N(invoiced)));
            }
        }
        else
        {
            lines.Add("Billing ID,Customer,Service,Period start,Period end,Status,Customer billing MYR,Revenue-share base MYR,Firm MYR,Manager MYR,LCM gross MYR,Worker entitlement MYR,LCM retained MYR,Customer invoice state,Customer invoiced MYR,Customer received MYR,Customer outstanding MYR");
            foreach (var b in r.Bills)
            {
                var gross = b.Shares.Single(x => x.Kind == ShareKind.Lcm).Amount;
                var cost = b.WorkItem.Assignments.Where(x => !x.IsCancelled).Sum(x => x.Entitlement);
                lines.Add(string.Join(',', b.Id, Csv(b.CustomerName), Csv(b.ServiceName), b.PeriodStart.ToString("yyyy-MM-dd"), b.PeriodEnd.ToString("yyyy-MM-dd"), b.Status, N(b.Amount), N(b.RevenueShareBaseAmount), N(b.Shares.Single(x => x.Kind == ShareKind.Firm).Amount), N(b.Shares.Single(x => x.Kind == ShareKind.Manager).Amount), N(gross), N(cost), N(gross - cost), b.CustomerInvoiceState, N(b.CustomerInvoicedAmount), N(b.CustomerReceivedAmount), N(b.CustomerOutstandingAmount)));
            }
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray(), "text/csv", "billing-report.csv");
    }

    public IActionResult Error() { Response.StatusCode = 500; return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier }); }
}
