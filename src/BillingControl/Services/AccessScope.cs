using System.Security.Claims;
using BillingControl.Data;
using BillingControl.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Services;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string InternalUser = "InternalUser";
    public const string AccountingFirm = "AccountingFirm";
    public const string Manager = "Manager";
    public const string Worker = "Worker";
    public const string Staff = Admin + "," + InternalUser;
    public const string BillingReaders = Admin + "," + InternalUser + "," + AccountingFirm + "," + Manager;
    public const string WorkReaders = Admin + "," + InternalUser + "," + Manager + "," + Worker;
    public const string WorkEditors = Staff;
    public const string ProgressReaders = Staff + "," + Manager + "," + Worker;
    public const string ProgressEditors = Staff + "," + Worker;
    public const string PaymentReaders = Admin + "," + InternalUser + "," + Worker;
    public static readonly string[] All = [Admin, InternalUser, AccountingFirm, Manager, Worker];
}

public sealed record AccessProfile(string UserId, string Role, int? BusinessPartyId, int? ManagerId, int? WorkerId)
{
    public static AccessProfile None { get; } = new("", "", null, null, null);
    public bool IsAdmin => Role == AppRoles.Admin;
    public bool IsInternal => Role == AppRoles.InternalUser;
    public bool IsStaff => IsAdmin || IsInternal;
    public bool IsAccountingFirm => Role == AppRoles.AccountingFirm;
    public bool IsManager => Role == AppRoles.Manager;
    public bool IsWorker => Role == AppRoles.Worker;
}

public static class UserAccessRules
{
    public static bool IsConsistent(AppUser user, string role) => role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => user.BusinessPartyId == null && user.ManagerId == null && user.WorkerId == null,
        AppRoles.AccountingFirm => user.BusinessPartyId != null && user.ManagerId == null && user.WorkerId == null,
        AppRoles.Manager => user.BusinessPartyId == null && user.ManagerId != null && user.WorkerId == null,
        AppRoles.Worker => user.BusinessPartyId == null && user.ManagerId == null && user.WorkerId != null,
        _ => false
    };
}

public sealed class ValidAccessProfileRequirement : IAuthorizationRequirement;

public sealed class ValidAccessProfileHandler(AppDbContext db) : AuthorizationHandler<ValidAccessProfileRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ValidAccessProfileRequirement requirement)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var roles = AppRoles.All.Where(context.User.IsInRole).ToList();
        if (id == null || roles.Count != 1) return;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is { IsActive: true } && UserAccessRules.IsConsistent(user, roles[0])) context.Succeed(requirement);
    }
}

public sealed class AccessScope(AppDbContext db, IHttpContextAccessor http)
{
    private AccessProfile? cached;

    public async Task<AccessProfile> CurrentAsync()
    {
        if (cached != null) return cached;
        var principal = http.HttpContext?.User ?? throw new InvalidOperationException("No current user.");
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No current user identifier.");
        var role = AppRoles.All.Single(principal.IsInRole);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
        if (!user.IsActive || !UserAccessRules.IsConsistent(user, role)) throw new InvalidOperationException("The user role and linked entity are inconsistent.");
        return cached = new(id, role, user.BusinessPartyId, user.ManagerId, user.WorkerId);
    }

    public IQueryable<Engagement> Engagements(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.Engagements,
        AppRoles.AccountingFirm => db.Engagements.Where(x => x.BusinessPartyId == a.BusinessPartyId),
        AppRoles.Manager => db.Engagements.Where(x => x.ManagerId == a.ManagerId),
        AppRoles.Worker => db.Engagements.Where(e => db.WorkerAssignments.Any(x => !x.IsCancelled && x.WorkerId == a.WorkerId && x.WorkItem.BillingRecord.EngagementId == e.Id)),
        _ => db.Engagements.Where(_ => false)
    };

    public IQueryable<BillingRecord> BillingRecords(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.BillingRecords,
        AppRoles.AccountingFirm => db.BillingRecords.Where(x => x.Engagement.BusinessPartyId == a.BusinessPartyId),
        AppRoles.Manager => db.BillingRecords.Where(x => x.Engagement.ManagerId == a.ManagerId),
        AppRoles.Worker => db.BillingRecords.Where(x => x.WorkItem.Assignments.Any(y => !y.IsCancelled && y.WorkerId == a.WorkerId)),
        _ => db.BillingRecords.Where(_ => false)
    };

    public IQueryable<Invoice> Invoices(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.Invoices,
        AppRoles.AccountingFirm => db.Invoices.Where(x => x.BusinessPartyId == a.BusinessPartyId && x.Lines.Any(line => line.BillingRecord.Engagement.BusinessPartyId == a.BusinessPartyId)),
        AppRoles.Manager => db.Invoices.Where(x => x.ManagerId == a.ManagerId && x.Lines.Any(line => line.BillingRecord.Engagement.ManagerId == a.ManagerId)),
        _ => db.Invoices.Where(_ => false)
    };

    public IQueryable<BillingSchedule> BillingSchedules(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.BillingSchedules,
        AppRoles.AccountingFirm => db.BillingSchedules.Where(x => x.Engagement.BusinessPartyId == a.BusinessPartyId),
        AppRoles.Manager => db.BillingSchedules.Where(x => x.Engagement.ManagerId == a.ManagerId),
        _ => db.BillingSchedules.Where(_ => false)
    };

    public IQueryable<WorkItem> WorkItems(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.WorkItems,
        AppRoles.Manager => db.WorkItems.Where(x => x.BillingRecord.Engagement.ManagerId == a.ManagerId),
        AppRoles.Worker => db.WorkItems.Where(x => x.Assignments.Any(y => !y.IsCancelled && y.WorkerId == a.WorkerId)),
        _ => db.WorkItems.Where(_ => false)
    };

    public IQueryable<WorkerAssignment> WorkerAssignments(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.WorkerAssignments,
        AppRoles.Worker => db.WorkerAssignments.Where(x => !x.IsCancelled && x.WorkerId == a.WorkerId),
        _ => db.WorkerAssignments.Where(_ => false)
    };

    public IQueryable<WorkerAssignment> ProgressAssignments(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.WorkerAssignments,
        AppRoles.Manager => db.WorkerAssignments.Where(x => x.WorkItem.BillingRecord.Engagement.ManagerId == a.ManagerId),
        AppRoles.Worker => db.WorkerAssignments.Where(x => !x.IsCancelled && x.WorkerId == a.WorkerId),
        _ => db.WorkerAssignments.Where(_ => false)
    };

    public IQueryable<WorkerAssignment> EligibleProgressAssignments(AccessProfile a, DateOnly week)
    {
        var end = week.AddDays(6);
        return ProgressAssignments(a).Where(x => !x.IsCancelled && x.WorkItem.BillingRecord.Status != BillingStatus.Cancelled
            && x.WorkItem.BillingRecord.PeriodStart <= end && x.WorkItem.BillingRecord.PeriodEnd >= week);
    }

    public IQueryable<WeeklyProgressReport> ProgressReports(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.WeeklyProgressReports,
        AppRoles.Manager => db.WeeklyProgressReports.Where(x => x.WorkerAssignment.WorkItem.BillingRecord.Engagement.ManagerId == a.ManagerId),
        AppRoles.Worker => db.WeeklyProgressReports.Where(x => !x.WorkerAssignment.IsCancelled && x.WorkerAssignment.WorkerId == a.WorkerId),
        _ => db.WeeklyProgressReports.Where(_ => false)
    };

    public IQueryable<WorkerPayment> WorkerPayments(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.WorkerPayments,
        AppRoles.Worker => db.WorkerPayments.Where(x => x.WorkerId == a.WorkerId),
        _ => db.WorkerPayments.Where(_ => false)
    };

    public IQueryable<Customer> Customers(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.Customers,
        AppRoles.AccountingFirm => db.Customers.Where(x => db.Engagements.Any(e => e.CustomerId == x.Id && e.BusinessPartyId == a.BusinessPartyId)),
        AppRoles.Manager => db.Customers.Where(x => db.Engagements.Any(e => e.CustomerId == x.Id && e.ManagerId == a.ManagerId)),
        AppRoles.Worker => db.Customers.Where(x => db.WorkerAssignments.Any(y => !y.IsCancelled && y.WorkerId == a.WorkerId && y.WorkItem.BillingRecord.Engagement.CustomerId == x.Id)),
        _ => db.Customers.Where(_ => false)
    };

    public IQueryable<Service> Services(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.Services,
        AppRoles.AccountingFirm => db.Services.Where(x => db.Engagements.Any(e => e.ServiceId == x.Id && e.BusinessPartyId == a.BusinessPartyId)),
        AppRoles.Manager => db.Services.Where(x => db.Engagements.Any(e => e.ServiceId == x.Id && e.ManagerId == a.ManagerId)),
        AppRoles.Worker => db.Services.Where(x => db.WorkerAssignments.Any(y => !y.IsCancelled && y.WorkerId == a.WorkerId && y.WorkItem.BillingRecord.Engagement.ServiceId == x.Id)),
        _ => db.Services.Where(_ => false)
    };

    public IQueryable<BusinessParty> BusinessParties(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.BusinessParties,
        AppRoles.AccountingFirm => db.BusinessParties.Where(x => x.Id == a.BusinessPartyId),
        _ => db.BusinessParties.Where(_ => false)
    };

    public IQueryable<Manager> Managers(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.Managers,
        AppRoles.Manager => db.Managers.Where(x => x.Id == a.ManagerId),
        _ => db.Managers.Where(_ => false)
    };

    public IQueryable<Worker> Workers(AccessProfile a) => a.Role switch
    {
        AppRoles.Admin or AppRoles.InternalUser => db.Workers,
        AppRoles.Worker => db.Workers.Where(x => x.Id == a.WorkerId),
        _ => db.Workers.Where(_ => false)
    };

    public static bool CanReadMaster(AccessProfile a, string kind) => a.IsStaff || a.Role switch
    {
        AppRoles.AccountingFirm => kind is "Customers" or "Services" or "Firms",
        AppRoles.Manager => kind is "Customers" or "Services" or "Managers",
        AppRoles.Worker => kind is "Customers" or "Services" or "Workers",
        _ => false
    };

    public async Task ApplyUserLinkAsync(AppUser user, string role, int? businessPartyId, int? managerId, int? workerId)
    {
        Finance.Require(AppRoles.All.Contains(role), "Select a valid role.");
        var valid = role switch
        {
            AppRoles.Admin or AppRoles.InternalUser => businessPartyId == null && managerId == null && workerId == null,
            AppRoles.AccountingFirm => businessPartyId != null && managerId == null && workerId == null && await db.BusinessParties.AnyAsync(x => x.Id == businessPartyId),
            AppRoles.Manager => businessPartyId == null && managerId != null && workerId == null && await db.Managers.AnyAsync(x => x.Id == managerId),
            AppRoles.Worker => businessPartyId == null && managerId == null && workerId != null && await db.Workers.AnyAsync(x => x.Id == workerId),
            _ => false
        };
        Finance.Require(valid, "The selected role must have exactly one matching linked entity and no other entity links.");
        user.BusinessPartyId = businessPartyId;
        user.ManagerId = managerId;
        user.WorkerId = workerId;
    }
}
