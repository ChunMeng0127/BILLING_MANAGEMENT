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
        var assignment = await db.WorkerAssignments.SingleAsync();
        var profile = new AccessProfile("worker", AppRoles.Worker, null, null, worker.Id);
        var scope = new AccessScope(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var reporting = new ProgressReportService(db, clock);
        WeeklyProgressForm Form(DateOnly week) => new() { WorkerAssignmentId = assignment.Id, WeekStart = week, ProgressPercent = 25, WorkflowStatus = WorkflowStatus.AssignmentStarted, WorkDone = "Reviewed documents", NextAction = "Finish review" };
        var expected = new[] { new DateOnly(2025,12,29), new DateOnly(2026,1,5), new DateOnly(2026,1,12), new DateOnly(2026,1,19), new DateOnly(2026,1,26) };
        foreach (var week in expected) Assert.Single(await scope.EligibleProgressAssignments(profile, week).ToListAsync());
        Assert.Single(await scope.EligibleProgressAssignments(profile, new(2025,12,22)).ToListAsync());
        Assert.Single(await scope.EligibleProgressAssignments(profile, new(2026,2,2)).ToListAsync());
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
            workId=ba.WorkItem.Id; otherWork=bb.WorkItem.Id;
            assignmentId=await db.WorkerAssignments.Where(x=>x.WorkItemId==workId).Select(x=>x.Id).SingleAsync();
            otherAssignment=await db.WorkerAssignments.Where(x=>x.WorkItemId==otherWork).Select(x=>x.Id).SingleAsync();
            await new ProgressReportService(db, new BusinessClock(time, new ConfigurationBuilder().Build())).SaveAsync(
                new("other-worker",AppRoles.Worker,null,null,wb.Id),
                new() { WorkerAssignmentId=otherAssignment, WeekStart=new(2026,1,19), WorkflowStatus=WorkflowStatus.AssignmentStarted, WorkDone="Other manager private report", NextAction="Continue" });
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
        Dictionary<string,string> Fields(int assignment,string week) => new() { ["WorkerAssignmentId"]=assignment.ToString(),["WeekStart"]=week,["ProgressPercent"]="20",["WorkflowStatus"]="AssignmentStarted",["WorkDone"]="Worker submitted report",["NextAction"]="Continue" };
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
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(futureAssignment, clock.CurrentWeekStart));
        Assert.Equal(0m, assignment.Entitlement);

        var first = await reports.SaveAsync(workerAccess, new WeeklyProgressForm
        {
            WorkerAssignmentId = assignment.Id, WeekStart = clock.CurrentWeekStart, WorkflowStatus = WorkflowStatus.QueriesSent,
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
            WorkerAssignmentId = assignment.Id, WeekStart = clock.CurrentWeekStart, WorkflowStatus = WorkflowStatus.DraftManagementReportSent,
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
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart));
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
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, new(2026, 9, 7)));
        Assert.True(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart));

        await workflow.BatchAsync(workerAccess, new BatchWorkflowForm
        {
            AssignmentIds = [assignment.Id], Versions = new() { [assignment.Id] = assignment.Version }, Action = BatchWorkflowAction.UpdateWorkflow, WorkflowStatus = WorkflowStatus.Completed
        });
        Assert.Equal(WorkflowStatus.Completed, assignment.CurrentWorkflowStatus);
        Assert.False(AssignmentWorkflowService.RequiresWeeklyReport(assignment, clock.CurrentWeekStart));
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
