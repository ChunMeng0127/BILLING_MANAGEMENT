using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    [Fact]
    public void ProgressBusinessClockUsesMalaysiaSundayDeadline()
    {
        var time = new FixedProgressTime { Now = new(2026, 1, 18, 15, 59, 59, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        Assert.Equal(new DateOnly(2026, 1, 18), clock.Today);
        Assert.Equal(new DateOnly(2026, 1, 12), clock.CurrentWeekStart);
        Assert.False(clock.IsLate(time.Now.UtcDateTime, new(2026, 1, 18)));
        time.Now = time.Now.AddSeconds(1);
        Assert.Equal(new DateOnly(2026, 1, 19), clock.Today);
        Assert.True(clock.IsLate(time.Now.UtcDateTime, new(2026, 1, 18)));
        var utc = new BusinessClock(time, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["BusinessTimeZone"] = "UTC" }).Build());
        Assert.Equal(new DateOnly(2026, 1, 18), utc.Today);
    }

    [PostgresFact]
    public async Task ActiveAssignmentsIgnoreBillingPeriodsAndPreserveWeeklyWorkflowSnapshots()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 2, 2, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Original reporting worker" };
        var replacement = new Worker { Name = "Replacement reporting worker" };
        db.AddRange(worker, replacement); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignmentId = await db.WorkerAssignments.Select(x => x.Id).SingleAsync();
        var assignmentCreatedAt = new DateTime(2025, 12, 29, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
            .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
        db.ChangeTracker.Clear();
        var assignment = await db.WorkerAssignments.SingleAsync(x => x.Id == assignmentId);
        var profile = new AccessProfile("worker", AppRoles.Worker, null, null, worker.Id);
        var scope = new AccessScope(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var reporting = new ProgressReportService(db, clock);
        WeeklyProgressForm Form(DateOnly week) => new() { WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = week, ProgressPercent = 25, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Reviewed documents", NextAction = "Finish review" };
        var expected = new[] { new DateOnly(2025,12,29), new DateOnly(2026,1,5), new DateOnly(2026,1,12), new DateOnly(2026,1,19), new DateOnly(2026,1,26) };
        var candidate = await scope.EligibleProgressAssignments(profile, clock.CurrentWeekStart)
            .Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord)
            .Include(x => x.WorkflowHistory)
            .SingleAsync();
        foreach (var week in expected) Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(candidate, week, clock));
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(candidate, new(2025,12,22), clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(candidate, new(2026,2,2), clock));
        time.Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero);
        var outside = await reporting.SaveAsync(profile, Form(new(2026,9,7)));
        Assert.Equal(WorkflowStatus.AssignmentStarted, outside.WorkflowStatusAtSubmission);
        Assert.Equal(WorkflowStatus.AssignmentStarted, assignment.CurrentWorkflowStatus);
        var missing = new ProgressReportRow { Assignment = assignment, WeekStart = expected[0], RequiresReport = true };
        Assert.Equal("Missing", missing.ReportingStatus);
        var first = await reporting.SaveAsync(profile, Form(expected[0]));
        var last = await reporting.SaveAsync(profile, Form(expected[^1]));
        Assert.Equal("Late", new ProgressReportRow { Assignment = assignment, Report = first, WeekStart = first.WeekStart, RequiresReport = true, IsLate = clock.IsLate(first.SubmittedAt, first.WeekEnd) }.ReportingStatus);
        var model = new ProgressReportModel { CurrentWeekStart = clock.CurrentWeekStart, Rows = [new() { Assignment = assignment, Report = last, WeekStart = last.WeekStart, RequiresReport = true, IsLate = true }] };
        Assert.Equal(1, model.Late); Assert.Equal(0, model.Missing); Assert.Equal(0, model.Submitted);
        var edit = Form(first.WeekStart); edit.Id = first.Id; edit.Version = first.Version;
        await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(profile, edit));
        var originalAt = first.CreatedAt; var originalBy = first.CreatedBy; var submitted = first.SubmittedAt;
        edit.WorkDone = "Staff correction";
        await reporting.SaveAsync(new("staff", AppRoles.InternalUser, null, null, null), edit);
        Assert.Equal(originalAt, first.CreatedAt); Assert.Equal(originalBy, first.CreatedBy); Assert.Equal(submitted, first.SubmittedAt);
        var changeWorker = await Assert.ThrowsAsync<BusinessException>(() => billing.EditAssignment(assignment.Id, replacement.Id, 0, assignment.Version));
        Assert.Equal("This assignment already has weekly progress history. Cancel it and create a new assignment to change the worker.", changeWorker.Message);
        worker.Name = "Updated directory name"; await db.SaveChangesAsync();
        await billing.EditAssignment(assignment.Id, worker.Id, 10, assignment.Version);
        Assert.Equal("Original reporting worker", assignment.WorkerName); Assert.Equal(worker.Id, assignment.WorkerId);
        Assert.Equal(40m, assignment.Entitlement); Assert.Equal(400m, assignment.LcmGrossSnapshot);
        Assert.Equal(1000m, bill.Amount); Assert.Empty(await db.WorkerPayments.ToListAsync());
        await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(new("staff", AppRoles.Admin, null, null, null), Form(expected[1])));
        // An on-time current-week submission stays on-time after a later staff correction.
        time.Now = new(2026, 1, 22, 4, 0, 0, TimeSpan.Zero);
        var current = await reporting.SaveAsync(profile, Form(new(2026,1,19)));
        Assert.False(clock.IsLate(current.SubmittedAt, current.WeekEnd));
        var currentEdit = Form(current.WeekStart); currentEdit.Id = current.Id; currentEdit.Version = current.Version; currentEdit.WorkDone = "More completed work";
        await reporting.SaveAsync(profile, currentEdit);
        await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(profile, currentEdit));
    }

    [PostgresFact]
    public async Task WorkerCurrentWeekReportEditUpdatesAssignmentStateAndPreservesSubmissionAudit()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Current week editor" };
        db.Add(worker); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleAsync();
        var access = new AccessProfile("current-week-worker", AppRoles.Worker, null, null, worker.Id);
        var reporting = new ProgressReportService(db, clock);
        var first = await reporting.SaveAsync(access, new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id,
            AssignmentVersion = assignment.Version,
            WeekStart = clock.CurrentWeekStart,
            WorkflowStatus = WorkflowStatus.StartPreparing,
            ProgressPercent = 30,
            WorkDone = "Started preparing the management accounts",
            NextAction = "Send the first query"
        });
        var submittedAt = first.SubmittedAt;
        var createdAt = first.CreatedAt;
        var createdBy = first.CreatedBy;
        var reportVersion = first.Version;
        var assignmentVersion = assignment.Version;
        var edited = await reporting.SaveAsync(access, new WeeklyProgressForm
        {
            Id = first.Id,
            Version = reportVersion,
            AssignmentVersion = assignmentVersion,
            WorkerAssignmentId = assignment.Id,
            WeekStart = clock.CurrentWeekStart,
            WorkflowStatus = WorkflowStatus.QueriesSent,
            WorkflowVersion = 1,
            ProgressPercent = 50,
            WorkDone = "Prepared and sent the first query",
            NextAction = "Follow up outstanding documents"
        });

        Assert.Equal(WorkflowStatus.QueriesSent, edited.WorkflowStatusAtSubmission);
        Assert.Equal(1, edited.WorkflowVersionAtSubmission);
        Assert.Equal(50m, edited.ProgressPercent);
        Assert.Equal(submittedAt, edited.SubmittedAt);
        Assert.Equal(createdAt, edited.CreatedAt);
        Assert.Equal(createdBy, edited.CreatedBy);
        Assert.Equal(WorkflowStatus.QueriesSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(1, assignment.CurrentWorkflowVersion);
        Assert.Equal(50m, assignment.CurrentProgressPercent);
        Assert.Equal(2, await db.WorkerAssignmentWorkflowHistories.CountAsync(x => x.WorkerAssignmentId == assignment.Id));

        await Assert.ThrowsAsync<BusinessException>(() => reporting.SaveAsync(access, new WeeklyProgressForm
        {
            Id = edited.Id,
            Version = edited.Version,
            AssignmentVersion = assignment.Version - 1,
            WorkerAssignmentId = assignment.Id,
            WeekStart = clock.CurrentWeekStart,
            WorkflowStatus = WorkflowStatus.QueriesSent,
            WorkflowVersion = 1,
            ProgressPercent = 55,
            WorkDone = "Stale assignment edit",
            NextAction = "Continue"
        }));
    }

    [PostgresFact]
    public async Task HistoricalLateReportDoesNotRewriteCurrentAssignmentWorkflow()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Historical report worker" };
        db.Add(worker); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignmentId = await db.WorkerAssignments.Select(x => x.Id).SingleAsync();
        var assignmentCreatedAt = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
            .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
        db.ChangeTracker.Clear();
        var assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        var reports = new ProgressReportService(db, clock);
        var current = await reports.SaveAsync(new AccessProfile("historical-worker", AppRoles.Worker, null, null, worker.Id), new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = clock.CurrentWeekStart,
            ProgressPercent = 80, WorkflowStatus = WorkflowStatus.DraftManagementReportSent, WorkflowVersion = 2,
            WorkDone = "Current workflow is at draft management report", NextAction = "Await review"
        });
        Assert.Equal(WorkflowStatus.DraftManagementReportSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(2, assignment.CurrentWorkflowVersion);
        Assert.Equal(80m, assignment.CurrentProgressPercent);
        var historyCount = await db.WorkerAssignmentWorkflowHistories.CountAsync(x => x.WorkerAssignmentId == assignment.Id);
        var late = await reports.SaveAsync(new AccessProfile("historical-worker", AppRoles.Worker, null, null, worker.Id), new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = new(2026, 8, 31),
            ProgressPercent = 30, WorkflowStatus = WorkflowStatus.StartPreparing,
            WorkDone = "Late historical weekly update", NextAction = "Continue the review"
        });

        Assert.Equal(WorkflowStatus.StartPreparing, late.WorkflowStatusAtSubmission);
        Assert.Equal(30m, late.ProgressPercent);
        Assert.True(clock.IsLate(late.SubmittedAt, late.WeekEnd));
        Assert.Equal(WorkflowStatus.DraftManagementReportSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(2, assignment.CurrentWorkflowVersion);
        Assert.Equal(80m, assignment.CurrentProgressPercent);
        Assert.Equal(historyCount, await db.WorkerAssignmentWorkflowHistories.CountAsync(x => x.WorkerAssignmentId == assignment.Id));
        Assert.Equal(WorkflowStatus.DraftManagementReportSent, current.WorkflowStatusAtSubmission);
    }

    [PostgresFact]
    public async Task CurrentWeekEditUsesLatestAssignmentStateAndRejectsStaleAssignmentVersion()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Current edit worker" };
        db.Add(worker); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignmentId = await db.WorkerAssignments.Select(x => x.Id).SingleAsync();
        var createdAt = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedAt, createdAt).SetProperty(x => x.UpdatedAt, createdAt));
        db.ChangeTracker.Clear();
        var assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).SingleAsync(x => x.Id == assignmentId);
        var reports = new ProgressReportService(db, clock);
        var report = await reports.SaveAsync(new AccessProfile("current-edit-worker", AppRoles.Worker, null, null, worker.Id), new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = clock.CurrentWeekStart,
            ProgressPercent = 30, WorkflowStatus = WorkflowStatus.StartPreparing, WorkDone = "Started preparing", NextAction = "Send queries"
        });
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        await new AssignmentWorkflowService(db, clock).BatchAsync(new AccessProfile("current-edit-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version },
            Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.QueriesSent, WorkflowVersion = 1
        });
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        report = await db.WeeklyProgressReports.SingleAsync(x => x.Id == report.Id);
        var pageAssignmentVersion = assignment.Version;
        var pageReportVersion = report.Version;

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection)
            .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "current-edit-keys"))
            .ConfigureServices(s => s.AddSingleton<TimeProvider>(time)));
        const string password = "Current-Edit!123";
        using (var services = app.Services.CreateScope())
        {
            await Seed.Initialize(services.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = "current-edit-admin@example.com", ["BootstrapAdmin:Password"] = password
            }).Build());
            var users = services.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var user = new AppUser { Email = "current-edit-worker@example.com", UserName = "current-edit-worker@example.com", WorkerId = worker.Id, EmailConfirmed = true };
            Seed.Check(await users.CreateAsync(user, password));
            Seed.Check(await users.AddToRoleAsync(user, AppRoles.Worker));
        }
        using var client = await SignedIn(app, "current-edit-worker@example.com", password);
        var edit = await client.GetAsync($"/Progress/Edit/{report.Id}");
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var html = await edit.Content.ReadAsStringAsync();
        Assert.Matches("(?s)<option(?=[^>]*value=\"QueriesSent\")(?=[^>]*selected)[^>]*>", html);
        Assert.Matches("name=\"ProgressPercent\"[^>]*value=\"30", html);
        Assert.Contains("Work done", html);

        await new AssignmentWorkflowService(db, clock).BatchAsync(new AccessProfile("current-edit-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [assignmentId], Versions = new() { [assignmentId] = pageAssignmentVersion },
            Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.PendingReview
        });
        var stale = new Dictionary<string, string>
        {
            ["Id"] = report.Id.ToString(), ["Version"] = pageReportVersion.ToString(), ["WorkerAssignmentId"] = assignmentId.ToString(),
            ["AssignmentVersion"] = pageAssignmentVersion.ToString(), ["WeekStart"] = clock.CurrentWeekStart.ToString("yyyy-MM-dd"),
            ["ProgressPercent"] = "35", ["WorkflowStatus"] = "QueriesSent", ["WorkflowVersion"] = "1",
            ["WorkDone"] = "Edited after a later batch change", ["NextAction"] = "Continue"
        };
        var staleResponse = await PostWithToken(client, "/Progress", "/Progress/Save", stale);
        Assert.Equal(HttpStatusCode.Redirect, staleResponse.StatusCode);
        db.ChangeTracker.Clear();
        var unchanged = await db.WeeklyProgressReports.SingleAsync(x => x.Id == report.Id);
        var current = await db.WorkerAssignments.SingleAsync(x => x.Id == assignmentId);
        Assert.Equal(WorkflowStatus.StartPreparing, unchanged.WorkflowStatusAtSubmission);
        Assert.Equal(WorkflowStatus.PendingReview, current.CurrentWorkflowStatus);
    }

    [PostgresFact]
    public async Task CompletedAssignmentCanSubmitEarlierMissingWeekWithoutReopening()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 16, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Historical completion worker" };
        db.Add(worker); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignmentId = await db.WorkerAssignments.Select(x => x.Id).SingleAsync();
        var assignmentCreatedAt = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
            .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
        db.ChangeTracker.Clear();
        var assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        var workflow = new AssignmentWorkflowService(db, clock);
        await workflow.BatchAsync(new AccessProfile("completion-history-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version },
            Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.Completed
        });
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        var beforeStatus = assignment.CurrentWorkflowStatus;
        var beforeVersion = assignment.CurrentWorkflowVersion;
        var beforeProgress = assignment.CurrentProgressPercent;
        var beforeHistory = assignment.WorkflowHistory.Count;
        var missingWeek = new DateOnly(2026, 9, 7);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, missingWeek, clock));
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, new(2026, 8, 17), clock));

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection)
            .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "historical-completion-keys"))
            .ConfigureServices(s => s.AddSingleton<TimeProvider>(time)));
        const string password = "Historical-Completion!123";
        using (var services = app.Services.CreateScope())
        {
            await Seed.Initialize(services.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = "historical-completion-admin@example.com", ["BootstrapAdmin:Password"] = password
            }).Build());
            var users = services.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var user = new AppUser { Email = "historical-completion-worker@example.com", UserName = "historical-completion-worker@example.com", WorkerId = worker.Id, EmailConfirmed = true };
            Seed.Check(await users.CreateAsync(user, password));
            Seed.Check(await users.AddToRoleAsync(user, AppRoles.Worker));
        }

        using var client = await SignedIn(app, "historical-completion-worker@example.com", password);
        var editPage = await client.GetAsync($"/Progress/Edit?assignmentId={assignmentId}&weekStart={missingWeek:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, editPage.StatusCode);
        Assert.Contains("Submit weekly update", await editPage.Content.ReadAsStringAsync());
        var fields = new Dictionary<string, string>
        {
            ["WorkerAssignmentId"] = assignmentId.ToString(), ["AssignmentVersion"] = assignment.Version.ToString(),
            ["WeekStart"] = missingWeek.ToString("yyyy-MM-dd"), ["ProgressPercent"] = "35", ["WorkflowStatus"] = "StartPreparing",
            ["WorkDone"] = "Submitted the missing historical update", ["NextAction"] = "Continue the work"
        };
        var response = await PostWithToken(client, $"/Progress/Edit?assignmentId={assignmentId}&weekStart={missingWeek:yyyy-MM-dd}", "/Progress/Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        db.ChangeTracker.Clear();
        var savedAssignment = await db.WorkerAssignments.Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        var report = await db.WeeklyProgressReports.SingleAsync(x => x.WorkerAssignmentId == assignmentId && x.WeekStart == missingWeek);
        Assert.Equal(WorkflowStatus.StartPreparing, report.WorkflowStatusAtSubmission);
        Assert.Equal(35m, report.ProgressPercent);
        Assert.True(clock.IsLate(report.SubmittedAt, report.WeekEnd));
        Assert.Equal(beforeStatus, savedAssignment.CurrentWorkflowStatus);
        Assert.Equal(beforeVersion, savedAssignment.CurrentWorkflowVersion);
        Assert.Equal(beforeProgress, savedAssignment.CurrentProgressPercent);
        Assert.Equal(beforeHistory, savedAssignment.WorkflowHistory.Count);
    }

    [PostgresFact]
    public async Task HistoricalMissingWeekSurvivesHideAndUnhideWithoutRetroactiveResumption()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Hide history worker" };
        db.Add(worker); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignmentId = await db.WorkerAssignments.Select(x => x.Id).SingleAsync();
        var assignmentCreatedAt = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
            .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
        db.ChangeTracker.Clear();
        var assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        var workflow = new AssignmentWorkflowService(db, clock);
        var week1 = new DateOnly(2026, 8, 31);
        var hiddenWeek = new DateOnly(2026, 9, 7);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, week1, clock));
        await workflow.BatchAsync(new AccessProfile("hide-history-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.Hide
        });
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, hiddenWeek, clock));
        var beforeUnhide = assignment.WorkflowHistory.Count;
        time.Now = new(2026, 9, 16, 4, 0, 0, TimeSpan.Zero);
        await workflow.BatchAsync(new AccessProfile("hide-history-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.Unhide
        });
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, week1, clock));
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, hiddenWeek, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart, clock));
        var reporting = new ProgressReportService(db, clock);
        var report = await reporting.SaveAsync(new AccessProfile("hide-history-worker", AppRoles.Worker, null, null, worker.Id), new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = week1,
            ProgressPercent = 20, WorkflowStatus = WorkflowStatus.StartPreparing,
            WorkDone = "Submitted the pre-hide missing update", NextAction = "Continue the work"
        });
        Assert.Equal(WorkflowStatus.StartPreparing, report.WorkflowStatusAtSubmission);
        Assert.True(clock.IsLate(report.SubmittedAt, report.WeekEnd));
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        Assert.Equal(beforeUnhide + 1, assignment.WorkflowHistory.Count);
        Assert.Equal(WorkflowStatus.AssignedNotStarted, assignment.CurrentWorkflowStatus);
    }

    [PostgresFact]
    public async Task BatchCompletionAllowsSingleFinalCurrentWeekReport()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var bill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Completion-week worker" };
        db.Add(worker); await db.SaveChangesAsync();
        await billing.Assign(bill.WorkItem.Id, worker.Id, 0);
        var assignmentId = await db.WorkerAssignments.Select(x => x.Id).SingleAsync();
        var assignmentCreatedAt = new DateTime(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignmentId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
            .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
        db.ChangeTracker.Clear();
        var assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);

        await new AssignmentWorkflowService(db, clock).BatchAsync(new AccessProfile("completion-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version },
            Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.Completed
        });
        db.ChangeTracker.Clear();
        assignment = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
        Assert.Equal(WorkflowStatus.Completed, assignment.CurrentWorkflowStatus);
        Assert.Empty(await db.WeeklyProgressReports.Where(x => x.WorkerAssignmentId == assignmentId).ToListAsync());
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart, clock));

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection)
            .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "completion-week-keys"))
            .ConfigureServices(s => s.AddSingleton<TimeProvider>(time)));
        const string password = "Completion-Week!123";
        using (var services = app.Services.CreateScope())
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = "completion-week-admin@example.com", ["BootstrapAdmin:Password"] = password
            }).Build();
            await Seed.Initialize(services.ServiceProvider, configuration);
            var users = services.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var user = new AppUser { Email = "completion-week-worker@example.com", UserName = "completion-week-worker@example.com", WorkerId = worker.Id, EmailConfirmed = true };
            Seed.Check(await users.CreateAsync(user, password));
            Seed.Check(await users.AddToRoleAsync(user, AppRoles.Worker));
        }

        using var workerClient = await SignedIn(app, "completion-week-worker@example.com", password);
        var completedList = await workerClient.GetStringAsync("/Progress?filter=Completed");
        Assert.Contains("Submit final weekly update", completedList);
        var editPage = await workerClient.GetAsync($"/Progress/Edit?assignmentId={assignmentId}&weekStart=2026-09-07");
        Assert.Equal(HttpStatusCode.OK, editPage.StatusCode);
        Assert.Contains("Submit final weekly update", await editPage.Content.ReadAsStringAsync());

        long assignmentVersion;
        using (var services = app.Services.CreateScope()) assignmentVersion = await services.ServiceProvider.GetRequiredService<AppDbContext>().WorkerAssignments.Where(x => x.Id == assignmentId).Select(x => x.Version).SingleAsync();
        Dictionary<string, string> Fields(string status) => new()
        {
            ["WorkerAssignmentId"] = assignmentId.ToString(), ["AssignmentVersion"] = assignmentVersion.ToString(),
            ["WeekStart"] = "2026-09-07", ["ProgressPercent"] = "80", ["WorkflowStatus"] = status,
            ["WorkDone"] = "Completed the final completion-week review", ["NextAction"] = ""
        };
        var invalid = await PostWithToken(workerClient, $"/Progress/Edit?assignmentId={assignmentId}&weekStart=2026-09-07", "/Progress/Save", Fields("StartPreparing"));
        Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        using (var services = app.Services.CreateScope()) Assert.Empty(await services.ServiceProvider.GetRequiredService<AppDbContext>().WeeklyProgressReports.Where(x => x.WorkerAssignmentId == assignmentId).ToListAsync());

        var submitted = await PostWithToken(workerClient, $"/Progress/Edit?assignmentId={assignmentId}&weekStart=2026-09-07", "/Progress/Save", Fields("Completed"));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        using (var services = app.Services.CreateScope())
        {
            var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedAssignment = await context.WorkerAssignments.SingleAsync(x => x.Id == assignmentId);
            var report = await context.WeeklyProgressReports.SingleAsync(x => x.WorkerAssignmentId == assignmentId && x.WeekStart == clock.CurrentWeekStart);
            Assert.Equal(WorkflowStatus.Completed, savedAssignment.CurrentWorkflowStatus);
            Assert.Equal(WorkflowStatus.Completed, report.WorkflowStatusAtSubmission);
            Assert.Null(report.WorkflowVersionAtSubmission);
            Assert.Equal(80m, report.ProgressPercent);
        }
        var dashboard = await workerClient.GetStringAsync("/");
        Assert.Contains("<span>Required</span><strong>1</strong>", dashboard);
        Assert.Contains("<span>Submitted</span><strong>1</strong>", dashboard);
        Assert.Contains("<span>Missing</span><strong>0</strong>", dashboard);
        Assert.Contains("<span>Late</span><strong>0</strong>", dashboard);
        using (var services = app.Services.CreateScope()) assignmentVersion = await services.ServiceProvider.GetRequiredService<AppDbContext>().WorkerAssignments.Where(x => x.Id == assignmentId).Select(x => x.Version).SingleAsync();
        var duplicate = await PostWithToken(workerClient, "/Progress?filter=Completed", "/Progress/Save", Fields("Completed"));
        Assert.Equal(HttpStatusCode.Redirect, duplicate.StatusCode);
        using (var services = app.Services.CreateScope()) Assert.Equal(1, await services.ServiceProvider.GetRequiredService<AppDbContext>().WeeklyProgressReports.CountAsync(x => x.WorkerAssignmentId == assignmentId));

        time.Now = new(2026, 9, 16, 4, 0, 0, TimeSpan.Zero);
        using (var services = app.Services.CreateScope())
        {
            var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await context.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignmentId);
            Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(current, clock.CurrentWeekStart, clock));
        }
    }

    [PostgresFact]
    public async Task HistoricalResponsibilityWindowsPreservePastResultsAndResumeSafely()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var firstBill = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var secondBill = await billing.Generate(engagement, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        var thirdBill = await billing.Generate(engagement, new(2026, 3, 1), new(2026, 3, 31), BillingGenerationMode.Scheduled);
        var workers = new[] { new Worker { Name = "Window A" }, new Worker { Name = "Window B" }, new Worker { Name = "Window C" } };
        db.AddRange(workers); await db.SaveChangesAsync();
        await billing.Assign(firstBill.WorkItem.Id, workers[0].Id, 0);
        await billing.Assign(secondBill.WorkItem.Id, workers[1].Id, 0);
        await billing.Assign(thirdBill.WorkItem.Id, workers[2].Id, 0);
        var assignments = await db.WorkerAssignments.OrderBy(x => x.Id).ToListAsync();
        var createdBeforeCurrentWeek = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc);
        var createdThisWeek = new DateTime(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc);
        await db.WorkerAssignments.Where(x => x.Id == assignments[0].Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedAt, createdBeforeCurrentWeek).SetProperty(x => x.UpdatedAt, createdBeforeCurrentWeek));
        await db.WorkerAssignments.Where(x => x.Id == assignments[1].Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedAt, createdThisWeek).SetProperty(x => x.UpdatedAt, createdThisWeek));
        await db.WorkerAssignments.Where(x => x.Id == assignments[2].Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedAt, createdBeforeCurrentWeek).SetProperty(x => x.UpdatedAt, createdBeforeCurrentWeek));
        db.ChangeTracker.Clear();
        var first = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignments[0].Id);
        var second = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignments[1].Id);
        var third = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == assignments[2].Id);
        var reporting = new ProgressReportService(db, clock);

        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(second, new(2026, 8, 31), clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(second, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(first, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(first, new(2026, 8, 31), clock));

        var final = await reporting.SaveAsync(new AccessProfile("window-a", AppRoles.Worker, null, null, first.WorkerId), new WeeklyProgressForm
        {
            WorkerAssignmentId = first.Id, AssignmentVersion = first.Version, WeekStart = clock.CurrentWeekStart,
            ProgressPercent = 100, WorkflowStatus = WorkflowStatus.Completed, WorkDone = "Completed this week's work"
        });
        Assert.Equal(WorkflowStatus.Completed, final.WorkflowStatusAtSubmission);
        first = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == first.Id);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(first, clock.CurrentWeekStart, clock));
        var completionWeek = new ProgressReportModel
        {
            Rows = [new ProgressReportRow
            {
                Assignment = first, Report = final, WeekStart = clock.CurrentWeekStart,
                RequiresReport = AssignmentWorkflowService.RequiresWeeklyReport(first, clock.CurrentWeekStart, clock),
                IsLate = clock.IsLate(final.SubmittedAt, final.WeekEnd)
            }]
        };
        Assert.Equal(1, completionWeek.Required);
        Assert.Equal(1, completionWeek.Submitted);

        time.Now = new(2026, 9, 16, 4, 0, 0, TimeSpan.Zero);
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(first, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(first, new(2026, 9, 7), clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(third, new(2026, 9, 7), clock));
        var workflow = new AssignmentWorkflowService(db, clock);
        await workflow.BatchAsync(new AccessProfile("window-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [third.Id], Versions = new() { [third.Id] = third.Version }, Action = BatchWorkflowAction.Hide
        });
        third = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == third.Id);
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(third, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(third, new(2026, 9, 7), clock));
        await workflow.BatchAsync(new AccessProfile("window-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [third.Id], Versions = new() { [third.Id] = third.Version }, Action = BatchWorkflowAction.Unhide
        });
        third = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == third.Id);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(third, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(third, new(2026, 9, 7), clock));

        await workflow.BatchAsync(new AccessProfile("window-staff", AppRoles.InternalUser, null, null, null), new BatchWorkflowForm
        {
            AssignmentIds = [first.Id], Versions = new() { [first.Id] = first.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.PendingReview
        });
        first = await db.WorkerAssignments.Include(x => x.WorkItem).ThenInclude(x => x.BillingRecord).Include(x => x.WorkflowHistory).SingleAsync(x => x.Id == first.Id);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(first, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(first, new(2026, 9, 7), clock));

        var currentRows = new[] { first, second, third }
            .Select(x => new ProgressReportRow { Assignment = x, WeekStart = clock.CurrentWeekStart, RequiresReport = AssignmentWorkflowService.RequiresWeeklyReport(x, clock.CurrentWeekStart, clock) })
            .ToList();
        var dashboardAndRegister = new ProgressReportModel { Rows = currentRows };
        Assert.Equal(dashboardAndRegister.Required, currentRows.Count(x => x.RequiresReport));
        Assert.Equal(3, dashboardAndRegister.Required);
        Assert.Equal(3, dashboardAndRegister.Missing);
    }

    [PostgresFact]
    public async Task ProgressHttpAuthorizationDashboardAndDuplicateRace()
    {
        await using var reset = await Fresh();
        var time = new FixedProgressTime();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Default", Connection)
            .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, "progress-correction-keys"))
            .ConfigureServices(s => s.AddSingleton<TimeProvider>(time)));
        const string password = "Progress-Correction!123";
        int assignmentId, otherAssignment, workId, otherWork, managerId;
        using (var services = app.Services.CreateScope())
        {
            await Seed.Initialize(services.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["BootstrapAdmin:Email"] = "progress-review-admin@example.com", ["BootstrapAdmin:Password"] = password }).Build());
            var db = services.ServiceProvider.GetRequiredService<AppDbContext>();
            var billing = new BillingService(db);
            var a = await Engagement(db); var b = await Engagement(db);
            var ea = await db.Engagements.Include(x=>x.Customer).SingleAsync(x=>x.Id==a);
            var eb = await db.Engagements.Include(x=>x.Customer).SingleAsync(x=>x.Id==b);
            ea.Customer.Name = "Visible weekly customer"; eb.Customer.Name = "Hidden weekly customer";
            var wa = new Worker { Name = "Weekly owner" }; var wb = new Worker { Name = "Other owner" };
            db.AddRange(wa,wb); await db.SaveChangesAsync(); managerId = ea.ManagerId;
            var ba = await billing.Generate(a,new(2026,1,1),new(2026,1,31),BillingGenerationMode.Scheduled);
            var bb = await billing.Generate(b,new(2026,1,1),new(2026,1,31),BillingGenerationMode.Scheduled);
             var future = await billing.Generate(a,new(2026,2,1),new(2026,2,28),BillingGenerationMode.Scheduled);
             await billing.Assign(ba.WorkItem.Id,wa.Id,0); await billing.Assign(bb.WorkItem.Id,wb.Id,0); await billing.Assign(future.WorkItem.Id,wa.Id,0);
               var assignmentCreatedAt = new DateTime(2026, 1, 12, 4, 0, 0, DateTimeKind.Utc);
              await db.WorkerAssignments
                  .Where(x => x.WorkItemId == ba.WorkItem.Id || x.WorkItemId == bb.WorkItem.Id || x.WorkItemId == future.WorkItem.Id)
                  .ExecuteUpdateAsync(setters => setters
                      .SetProperty(x => x.CreatedAt, assignmentCreatedAt)
                      .SetProperty(x => x.UpdatedAt, assignmentCreatedAt));
              db.ChangeTracker.Clear();
             workId=ba.WorkItem.Id; otherWork=bb.WorkItem.Id;
             assignmentId=await db.WorkerAssignments.Where(x=>x.WorkItemId==workId).Select(x=>x.Id).SingleAsync();
             otherAssignment=await db.WorkerAssignments.Where(x=>x.WorkItemId==otherWork).Select(x=>x.Id).SingleAsync();
             var otherAssignmentVersion = await db.WorkerAssignments.Where(x=>x.Id == otherAssignment).Select(x => x.Version).SingleAsync();
             await new ProgressReportService(db, new BusinessClock(time, new ConfigurationBuilder().Build())).SaveAsync(
                 new("other-worker",AppRoles.Worker,null,null,wb.Id),
                 new() { WorkerAssignmentId=otherAssignment, AssignmentVersion = otherAssignmentVersion, WeekStart=new(2026,1,19), WorkflowStatus=WorkflowStatus.AssignmentStarted, WorkDone="Other manager private report", NextAction="Continue" });
            var users=services.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            async Task User(string email,string role,int? worker=null,int? manager=null,int? firm=null)
            {
                var u=new AppUser { Email=email,UserName=email,WorkerId=worker,ManagerId=manager,BusinessPartyId=firm };
                Seed.Check(await users.CreateAsync(u,password)); Seed.Check(await users.AddToRoleAsync(u,role));
            }
            await User("weekly-worker@example.com",AppRoles.Worker,worker:wa.Id);
            await User("weekly-manager@example.com",AppRoles.Manager,manager:ea.ManagerId);
            await User("weekly-internal@example.com",AppRoles.InternalUser);
            await User("weekly-firm@example.com",AppRoles.AccountingFirm,firm:ea.BusinessPartyId);
        }
        using var worker=await SignedIn(app,"weekly-worker@example.com",password);
        using var manager=await SignedIn(app,"weekly-manager@example.com",password);
        using var admin=await SignedIn(app,"progress-review-admin@example.com",password);
        using var staff=await SignedIn(app,"weekly-internal@example.com",password);
        using var firm=await SignedIn(app,"weekly-firm@example.com",password);
        var missingCsrf = await worker.PostAsync("/Progress/Batch", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["AssignmentIds"] = assignmentId.ToString(), ["Versions[" + assignmentId + "]"] = "1", ["Action"] = "UpdateWorkflow", ["WorkflowStatus"] = "AssignmentStarted"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        long assignmentVersion;
        using (var services = app.Services.CreateScope()) assignmentVersion = await services.ServiceProvider.GetRequiredService<AppDbContext>().WorkerAssignments.Where(x => x.Id == assignmentId).Select(x => x.Version).SingleAsync();
        var batch = await PostWithToken(worker, "/Progress", "/Progress/Batch", new()
        {
            ["AssignmentIds"] = assignmentId.ToString(), ["Versions[" + assignmentId + "]"] = assignmentVersion.ToString(), ["Action"] = "UpdateWorkflow", ["WorkflowStatus"] = "QueriesSent", ["WorkflowVersion"] = "1"
        });
        Assert.Equal(HttpStatusCode.Redirect, batch.StatusCode);
        using (var services = app.Services.CreateScope())
        {
            var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(WorkflowStatus.QueriesSent, await context.WorkerAssignments.Where(x => x.Id == assignmentId).Select(x => x.CurrentWorkflowStatus).SingleAsync());
            Assert.Empty(await context.WeeklyProgressReports.Where(x => x.WorkerAssignmentId == assignmentId).ToListAsync());
        }
        var crossBatch = await PostWithToken(worker, "/Progress", "/Progress/Batch", new()
        {
            ["AssignmentIds"] = otherAssignment.ToString(), ["Versions[" + otherAssignment + "]"] = "1", ["Action"] = "UpdateWorkflow", ["WorkflowStatus"] = "AssignmentStarted"
        });
        Assert.Equal(HttpStatusCode.NotFound, crossBatch.StatusCode);
        foreach(var client in new[]{worker,manager})
        {
            var html=await client.GetStringAsync($"/Work/Details/{workId}");
            Assert.DoesNotContain("name=\"status\"",html); Assert.DoesNotContain("<textarea",html); Assert.DoesNotContain("Save progress",html);
            var denied=await PostWithToken(client,"/Progress","/Work/Update",new() { ["id"]=workId.ToString(),["version"]="1",["status"]="Completed",["notes"]="Forged" });
            Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode); Assert.Contains("Denied",denied.Headers.Location!.ToString());
            var dashboard=await client.GetStringAsync("/");
            if (ReferenceEquals(client, worker))
            {
                Assert.Contains("<span>Required</span><strong>2</strong>",dashboard);
                Assert.Contains("<span>Missing</span><strong>2</strong>",dashboard);
            }
            else
            {
                Assert.Contains("<span>Required</span><strong>2</strong>",dashboard);
                Assert.Contains("<span>Missing</span><strong>2</strong>",dashboard);
            }
            Assert.DoesNotContain("Hidden weekly customer",await client.GetStringAsync("/Progress"));
        }
        foreach(var client in new[]{admin,staff})
        {
            Assert.Contains("<span>Required</span><strong>3</strong>",await client.GetStringAsync("/"));
            long version; using(var services=app.Services.CreateScope()) version=await services.ServiceProvider.GetRequiredService<AppDbContext>().WorkItems.Where(x=>x.Id==workId).Select(x=>x.Version).SingleAsync();
            var saved=await PostWithToken(client,$"/Work/Details/{workId}","/Work/Update",new() { ["id"]=workId.ToString(),["version"]=version.ToString(),["status"]="InProgress",["notes"]="Staff operational update" });
            Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode); Assert.Contains("Staff operational update",await client.GetStringAsync($"/Work/Details/{workId}"));
        }
        Assert.Contains("Denied",(await firm.GetAsync("/Progress")).Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.NotFound,(await worker.GetAsync($"/Progress/Edit?assignmentId={otherAssignment}&weekStart=2026-01-19")).StatusCode);
         long reportAssignmentVersion; using (var services = app.Services.CreateScope()) reportAssignmentVersion = await services.ServiceProvider.GetRequiredService<AppDbContext>().WorkerAssignments.Where(x => x.Id == assignmentId).Select(x => x.Version).SingleAsync();
         Dictionary<string,string> Fields(int assignment,string week) => new() { ["WorkerAssignmentId"]=assignment.ToString(),["AssignmentVersion"]=reportAssignmentVersion.ToString(),["WeekStart"]=week,["ProgressPercent"]="20",["WorkflowStatus"]="AssignmentStarted",["WorkDone"]="Worker submitted report",["NextAction"]="Continue" };
        Assert.Equal(HttpStatusCode.NotFound,(await PostWithToken(worker,"/Progress","/Progress/Save",Fields(otherAssignment,"2026-01-19"))).StatusCode);
        Assert.Contains("Denied",(await PostWithToken(staff,"/Progress","/Progress/Save",Fields(assignmentId,"2026-01-19"))).Headers.Location!.ToString());
        var token=Token(await worker.GetStringAsync("/Progress"));
        async Task<HttpResponseMessage> Submit()
        {
            var fields=Fields(assignmentId,"2026-01-19"); fields["__RequestVerificationToken"]=token;
            return await worker.PostAsync("/Progress/Save",new FormUrlEncodedContent(fields));
        }
        var outcomes=await Task.WhenAll(Submit(),Submit());
        Assert.All(outcomes,x=>Assert.Equal(HttpStatusCode.Redirect,x.StatusCode));
        int reportId;
        using(var services=app.Services.CreateScope())
        {
            var db=services.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1,await db.WeeklyProgressReports.CountAsync(x=>x.WorkerAssignmentId==assignmentId));
            var report=await db.WeeklyProgressReports.SingleAsync(x=>x.WorkerAssignmentId==assignmentId);
            reportId=report.Id; Assert.NotEqual("system",report.CreatedBy);
            Assert.Equal(0m,await db.WorkerAssignments.Where(x=>x.Id==assignmentId).Select(x=>x.Entitlement).SingleAsync());
        }
        Assert.Contains("<span>Submitted</span><strong>1</strong>",await worker.GetStringAsync("/"));
        Assert.Contains("<span>Missing</span><strong>1</strong>",await worker.GetStringAsync("/"));
        var managerDetail=await manager.GetStringAsync($"/Progress/Details/{reportId}");
        Assert.Contains("Worker submitted report",managerDetail); Assert.DoesNotContain("Entitlement",managerDetail); Assert.DoesNotContain("<textarea",managerDetail);
        int foreignReport;
        using(var services=app.Services.CreateScope()) foreignReport=await services.ServiceProvider.GetRequiredService<AppDbContext>().WeeklyProgressReports.Where(x=>x.WorkerAssignmentId==otherAssignment).Select(x=>x.Id).SingleAsync();
        foreach(var client in new[]{worker,manager}) Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/Progress/Details/{foreignReport}")).StatusCode);
        var forgedReport=Fields(assignmentId,"2026-01-19"); forgedReport["Id"]=foreignReport.ToString(); forgedReport["Version"]="1";
         Assert.Equal(HttpStatusCode.NotFound,(await PostWithToken(worker,"/Progress","/Progress/Save",forgedReport)).StatusCode);
         Assert.Contains("Denied",(await PostWithToken(manager,"/Progress","/Progress/Save",Fields(assignmentId,"2026-01-19"))).Headers.Location!.ToString());
         Assert.Contains("Missing",await worker.GetStringAsync("/Progress?weekStart=2026-01-12"));
         using (var services = app.Services.CreateScope()) reportAssignmentVersion = await services.ServiceProvider.GetRequiredService<AppDbContext>().WorkerAssignments.Where(x => x.Id == assignmentId).Select(x => x.Version).SingleAsync();
         await PostWithToken(worker,"/Progress","/Progress/Save",Fields(assignmentId,"2026-01-12"));
        var past=await worker.GetStringAsync("/Progress?weekStart=2026-01-12");
        Assert.Contains("<span>Late</span><strong>1</strong>",past); Assert.Contains("<span>Missing</span><strong>1</strong>",past);
        time.Now=new(2026,9,9,4,0,0,TimeSpan.Zero);
        foreach(var email in new[]{"weekly-worker@example.com","weekly-manager@example.com","progress-review-admin@example.com","weekly-internal@example.com"})
        {
            using var refreshed = await SignedIn(app,email,password);
            var expectedRequired = email is "weekly-worker@example.com" or "weekly-manager@example.com" ? 2 : 3;
            Assert.Contains($"<span>Required</span><strong>{expectedRequired}</strong>",await refreshed.GetStringAsync("/"));
        }
    }

    [PostgresFact]
    public async Task WorkflowStateHistoryVisibilityAndBatchRulesAreSafe()
    {
        await using var db = await Fresh();
        var time = new FixedProgressTime { Now = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero) };
        var clock = new BusinessClock(time, new ConfigurationBuilder().Build());
        var engagement = await Engagement(db);
        var billing = new BillingService(db);
        var past = await billing.Generate(engagement, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var future = await billing.Generate(engagement, new(2026, 2, 1), new(2026, 2, 28), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Workflow owner" };
        var other = new Worker { Name = "Workflow other" };
        db.AddRange(worker, other); await db.SaveChangesAsync();
        await billing.Assign(past.WorkItem.Id, worker.Id, 0);
        await billing.Assign(future.WorkItem.Id, worker.Id, 50);
        await billing.Assign(future.WorkItem.Id, other.Id, 50);
        var assignments = await db.WorkerAssignments.OrderBy(x => x.Id).ToListAsync();
        var assignment = assignments.Single(x => x.WorkerId == worker.Id && x.WorkItemId == past.WorkItem.Id);
        var futureAssignment = assignments.Single(x => x.WorkerId == worker.Id && x.WorkItemId == future.WorkItem.Id);
        var otherAssignment = assignments.Single(x => x.WorkerId == other.Id);
        var workerAccess = new AccessProfile("workflow-worker", AppRoles.Worker, null, null, worker.Id);
        var staffAccess = new AccessProfile("workflow-staff", AppRoles.InternalUser, null, null, null);
        var managerAccess = new AccessProfile("workflow-manager", AppRoles.Manager, null, 1, null);
        var workflow = new AssignmentWorkflowService(db, clock);
        var reports = new ProgressReportService(db, clock, workflow);

        foreach (var status in Enum.GetValues<WorkflowStatus>())
            AssignmentWorkflowService.ValidateWorkflow(status, Finance.RequiresWorkflowVersion(status) ? 1 : null);

        Assert.Equal(WorkflowStatus.AssignedNotStarted, assignment.CurrentWorkflowStatus);
        Assert.Equal(0m, assignment.CurrentProgressPercent);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart, clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(futureAssignment, clock.CurrentWeekStart, clock));
        Assert.Equal(0m, assignment.Entitlement);

        var first = await reports.SaveAsync(workerAccess, new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = clock.CurrentWeekStart, WorkflowStatus = WorkflowStatus.QueriesSent,
            WorkflowVersion = 1, ProgressPercent = 40, WorkDone = "Sent initial queries", NextAction = "Follow up documents"
        });
        Assert.Equal(WorkflowStatus.QueriesSent, first.WorkflowStatusAtSubmission);
        Assert.Equal(1, first.WorkflowVersionAtSubmission);
        Assert.Equal(WorkflowStatus.QueriesSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(1, assignment.CurrentWorkflowVersion);
        Assert.Equal(40m, assignment.CurrentProgressPercent);
        Assert.Single(await db.WorkerAssignmentWorkflowHistories.Where(x => x.WorkerAssignmentId == assignment.Id).ToListAsync());

        time.Now = new(2026, 9, 16, 4, 0, 0, TimeSpan.Zero);
        var second = await reports.SaveAsync(workerAccess, new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, AssignmentVersion = assignment.Version, WeekStart = clock.CurrentWeekStart, WorkflowStatus = WorkflowStatus.DraftManagementReportSent,
            WorkflowVersion = 1, ProgressPercent = 65, WorkDone = "Sent management report draft", NextAction = "Await review"
        });
        Assert.Equal(WorkflowStatus.QueriesSent, first.WorkflowStatusAtSubmission);
        Assert.Equal(1, first.WorkflowVersionAtSubmission);
        Assert.Equal(40m, first.ProgressPercent);
        Assert.Equal(WorkflowStatus.DraftManagementReportSent, second.WorkflowStatusAtSubmission);
        Assert.Equal(WorkflowStatus.DraftManagementReportSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(65m, assignment.CurrentProgressPercent);

        await workflow.BatchAsync(workerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version },
            WorkflowStatus = WorkflowStatus.QueriesSent, WorkflowVersion = 2
        });
        Assert.Equal(WorkflowStatus.QueriesSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(2, assignment.CurrentWorkflowVersion);

        var staleBatch = new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id, futureAssignment.Id],
            Versions = new() { [assignment.Id] = assignment.Version - 1, [futureAssignment.Id] = futureAssignment.Version },
            WorkflowStatus = WorkflowStatus.AssignmentStarted
        };
        await Assert.ThrowsAsync<BusinessException>(() => workflow.BatchAsync(staffAccess, staleBatch));
        Assert.Equal(WorkflowStatus.QueriesSent, assignment.CurrentWorkflowStatus);
        Assert.Equal(WorkflowStatus.AssignedNotStarted, futureAssignment.CurrentWorkflowStatus);

        await workflow.BatchAsync(staffAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.Hide
        });
        Assert.True(assignment.IsHidden);
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart, clock));
        Assert.Equal(2, await db.WeeklyProgressReports.CountAsync(x => x.WorkerAssignmentId == assignment.Id));
        await Assert.ThrowsAsync<BusinessException>(() => workflow.BatchAsync(workerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.Unhide
        }));
        await Assert.ThrowsAsync<BusinessException>(() => workflow.BatchAsync(managerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.AssignmentStarted
        }));
        await Assert.ThrowsAsync<BusinessException>(() => workflow.BatchAsync(workerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [otherAssignment.Id], Versions = new() { [otherAssignment.Id] = otherAssignment.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.AssignmentStarted
        }));

        await workflow.BatchAsync(staffAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.Unhide
        });
        Assert.False(assignment.IsHidden);
        Assert.Equal(clock.CurrentWeekStart, assignment.ReportingResumedFromWeek);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, new(2026, 9, 7), clock));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart, clock));

        await workflow.BatchAsync(workerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.Completed
        });
        Assert.Equal(WorkflowStatus.Completed, assignment.CurrentWorkflowStatus);
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart, clock));
        var reportsBeforeReopen = await db.WeeklyProgressReports.CountAsync(x => x.WorkerAssignmentId == assignment.Id);
        await Assert.ThrowsAsync<BusinessException>(() => workflow.BatchAsync(workerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.PendingReview
        }));
        Assert.Equal(reportsBeforeReopen, await db.WeeklyProgressReports.CountAsync(x => x.WorkerAssignmentId == assignment.Id));
        await workflow.BatchAsync(staffAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.PendingReview
        });
        Assert.Equal(WorkflowStatus.PendingReview, assignment.CurrentWorkflowStatus);
        Assert.Equal(clock.CurrentWeekStart, assignment.ReportingResumedFromWeek);
        Assert.True((await db.WorkerAssignmentWorkflowHistories.CountAsync(x => x.WorkerAssignmentId == assignment.Id)) >= 5);
    }
}
