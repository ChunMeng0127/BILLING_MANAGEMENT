using System.Net;
using System.Text.RegularExpressions;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BillingControl.Services.WhatsApp;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static WebApplicationFactory<Program> WhatsAppUiFactory(string prefix) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Default", Connection)
            .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, $"{prefix}-keys")));

    private static WebApplicationFactory<Program> WhatsAppUiFactoryWithThrowingProvider(string prefix) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Default", Connection)
            .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, $"{prefix}-keys"))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IWhatsAppProvider>();
                services.AddSingleton<IWhatsAppProvider, ThrowingWhatsAppProvider>();
            }));

    private sealed class ThrowingWhatsAppProvider : IWhatsAppProvider
    {
        public Task<WhatsAppProviderCapabilities> GetCapabilitiesAsync(
            WhatsAppProviderAccountBinding accountBinding,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Phase 9C management UI must not query a WhatsApp provider.");

        public Task<WhatsAppSendResult> SendAsync(
            WhatsAppOutboundRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Phase 9C management UI must not send through a WhatsApp provider.");
    }

    private static async Task SeedWhatsAppUiAdminAsync(
        WebApplicationFactory<Program> app,
        string prefix,
        string password,
        bool addExternalRoles = false)
    {
        using var scope = app.Services.CreateScope();
        await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = $"{prefix}-admin@example.com",
                ["BootstrapAdmin:Password"] = password
            })
            .Build());

        if (!addExternalRoles)
            return;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firm = new BusinessParty { Name = $"{prefix} firm" };
        var manager = new Manager { Name = $"{prefix} manager" };
        var worker = new Worker { Name = $"{prefix} worker" };
        db.AddRange(firm, manager, worker);
        await db.SaveChangesAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        async Task AddUser(string email, string role, int? businessPartyId = null, int? managerId = null, int? workerId = null)
        {
            var user = new AppUser
            {
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                BusinessPartyId = businessPartyId,
                ManagerId = managerId,
                WorkerId = workerId
            };
            Seed.Check(await users.CreateAsync(user, password));
            Seed.Check(await users.AddToRoleAsync(user, role));
        }

        await AddUser($"{prefix}-internal@example.com", AppRoles.InternalUser);
        await AddUser($"{prefix}-firm@example.com", AppRoles.AccountingFirm, businessPartyId: firm.Id);
        await AddUser($"{prefix}-manager@example.com", AppRoles.Manager, managerId: manager.Id);
        await AddUser($"{prefix}-worker@example.com", AppRoles.Worker, workerId: worker.Id);
    }

    private static async Task<int> CreateWhatsAppConversationThroughUiAsync(
        HttpClient staff,
        WhatsAppConversationKind kind,
        string providerConversationKey,
        int? directAddressId = null)
    {
        using var response = await PostWithToken(staff, "/WhatsAppConversations/Create", "/WhatsAppConversations/Create", new()
        {
            ["Kind"] = kind.ToString(),
            ["ProviderName"] = "Meta",
            ["BusinessEndpointKey"] = "ui-business-endpoint",
            ["ProviderAccountReference"] = "ui-routing-account",
            ["ProviderConversationKey"] = providerConversationKey,
            ["DirectContactWhatsAppAddressId"] = directAddressId?.ToString() ?? "",
            ["Reason"] = "Phase 9C UI creation"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        var match = Regex.Match(location, @"/WhatsAppConversations/Details/(\d+)");
        Assert.True(match.Success, $"Expected a conversation details redirect, got {location}");
        return int.Parse(match.Groups[1].Value);
    }

    private static async Task<(long Version, int AuthorizationVersion, WhatsAppConversationStatus Status)>
        ConversationStateAsync(int id)
    {
        await using var db = Db();
        return await db.WhatsAppConversations.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ValueTuple<long, int, WhatsAppConversationStatus>(x.Version, x.AuthorizationVersion, x.Status))
            .SingleAsync();
    }

    [PostgresFact]
    public async Task WhatsAppConversationUiIsStaffOnlyAndCreatesDirectAndGroupWithoutSideEffects()
    {
        await using var db = await Fresh();
        var prefix = $"whatsapp-ui-access-{Guid.NewGuid():N}";
        const string password = "WhatsApp-Ui-Password!123";
        var contact = await AddContactAsync(db, "phase9c-ui-contact");
        var activeAddress = await AddContactAddressAsync(db, contact.Id, "+60139002001");
        var inactiveAddress = await AddContactAddressAsync(db, contact.Id, "+60139002002", isActive: false);
        _ = await Engagement(db);

        using var app = WhatsAppUiFactory(prefix);
        await SeedWhatsAppUiAdminAsync(app, prefix, password, addExternalRoles: true);

        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousResponse = await anonymous.GetAsync("/WhatsAppConversations");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/Account/Login", anonymousResponse.Headers.Location!.ToString());

        using var admin = await SignedIn(app, $"{prefix}-admin@example.com", password);
        using var internalUser = await SignedIn(app, $"{prefix}-internal@example.com", password);
        using var firm = await SignedIn(app, $"{prefix}-firm@example.com", password);
        using var manager = await SignedIn(app, $"{prefix}-manager@example.com", password);
        using var worker = await SignedIn(app, $"{prefix}-worker@example.com", password);

        foreach (var client in new[] { firm, manager, worker })
        {
            using var denied = await client.GetAsync("/WhatsAppConversations");
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/Account/Denied", denied.Headers.Location!.ToString());
            Assert.DoesNotContain("WhatsApp Conversations", await client.GetStringAsync("/"));
        }

        var adminHome = await admin.GetStringAsync("/");
        Assert.Contains("WhatsApp Conversations", adminHome);
        Assert.Contains("WhatsApp Conversations", await internalUser.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/WhatsAppConversations")).StatusCode);

        var createPage = await admin.GetStringAsync("/WhatsAppConversations/Create");
        Assert.Contains("Creating a conversation records provider/routing identity only", createPage);
        Assert.Contains("ui", createPage, StringComparison.OrdinalIgnoreCase);
        var decodedCreatePage = WebUtility.HtmlDecode(createPage);
        Assert.Contains("+60139002001", decodedCreatePage);
        Assert.DoesNotContain("+60139002002", decodedCreatePage);
        Assert.Contains("Provider/runtime group capability is separate", createPage);

        var directId = await CreateWhatsAppConversationThroughUiAsync(admin, WhatsAppConversationKind.Direct, "ui-direct", activeAddress.Id);
        var directDetails = await admin.GetStringAsync($"/WhatsAppConversations/Details/{directId}");
        Assert.Contains("Direct", directDetails);
        Assert.Contains("ui-direct", directDetails);
        Assert.Contains("Authorization Version", directDetails);
        Assert.Contains("This page does not send WhatsApp messages", await admin.GetStringAsync("/WhatsAppConversations"));

        var groupId = await CreateWhatsAppConversationThroughUiAsync(admin, WhatsAppConversationKind.Group, "ui-group");
        var groupDetails = await admin.GetStringAsync($"/WhatsAppConversations/Details/{groupId}");
        Assert.Contains("Group", groupDetails);
        Assert.Contains("Not applicable to Group", groupDetails);

        using (var verify = Db())
        {
            Assert.Equal(0, await verify.WhatsAppConversationParticipants.CountAsync());
            Assert.Equal(0, await verify.WhatsAppConversationEngagementScopes.CountAsync());
            Assert.Equal(0, await verify.BillingRecords.CountAsync());
            Assert.Equal(0, await verify.WorkItems.CountAsync());
            Assert.Equal(0, await verify.DocumentRequests.CountAsync());
            Assert.Equal(0, await verify.Invoices.CountAsync());
            Assert.Equal(0, await verify.CustomerReceipts.CountAsync());
            Assert.Equal(0, await verify.WorkerPayments.CountAsync());
        }

        using (var invalidInactive = await PostWithToken(admin, "/WhatsAppConversations/Create", "/WhatsAppConversations/Create", new()
        {
            ["Kind"] = "Direct",
            ["ProviderName"] = "Meta",
            ["BusinessEndpointKey"] = "ui-business-endpoint",
            ["ProviderConversationKey"] = "ui-inactive-anchor",
            ["DirectContactWhatsAppAddressId"] = inactiveAddress.Id.ToString(),
            ["Reason"] = "Inactive anchor test"
        }))
        {
            Assert.Equal(HttpStatusCode.OK, invalidInactive.StatusCode);
            Assert.Contains("must be active", await invalidInactive.Content.ReadAsStringAsync());
        }

        using (var invalidMissing = await PostWithToken(admin, "/WhatsAppConversations/Create", "/WhatsAppConversations/Create", new()
        {
            ["Kind"] = "Direct",
            ["ProviderName"] = "Meta",
            ["BusinessEndpointKey"] = "ui-business-endpoint",
            ["ProviderConversationKey"] = "ui-missing-anchor",
            ["DirectContactWhatsAppAddressId"] = "",
            ["Reason"] = "Missing anchor test"
        }))
        {
            Assert.Equal(HttpStatusCode.OK, invalidMissing.StatusCode);
            Assert.Contains("requires a Contact WhatsApp address", await invalidMissing.Content.ReadAsStringAsync());
        }
    }

    [PostgresFact]
    public async Task WhatsAppConversationManagementUiDoesNotCallProvider()
    {
        await using var db = await Fresh();
        var prefix = $"whatsapp-ui-provider-boundary-{Guid.NewGuid():N}";
        const string password = "WhatsApp-Provider-Password!123";

        using var app = WhatsAppUiFactoryWithThrowingProvider(prefix);
        await SeedWhatsAppUiAdminAsync(app, prefix, password);
        using var staff = await SignedIn(app, $"{prefix}-admin@example.com", password);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/WhatsAppConversations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/WhatsAppConversations/Create")).StatusCode);
        var conversationId = await CreateWhatsAppConversationThroughUiAsync(staff, WhatsAppConversationKind.Group, "ui-provider-boundary");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/WhatsAppConversations/Details/{conversationId}")).StatusCode);
    }

    [PostgresFact]
    public async Task WhatsAppConversationParticipantUiUsesExpectedVersionsAndRecordsUnknownMembershipSafely()
    {
        await using var db = await Fresh();
        var prefix = $"whatsapp-ui-membership-{Guid.NewGuid():N}";
        const string password = "WhatsApp-Membership-Password!123";
        var contact = await AddContactAsync(db, "phase9c-membership-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139002003");

        using var app = WhatsAppUiFactory(prefix);
        await SeedWhatsAppUiAdminAsync(app, prefix, password);
        using var staff = await SignedIn(app, $"{prefix}-admin@example.com", password);

        var conversationId = await CreateWhatsAppConversationThroughUiAsync(staff, WhatsAppConversationKind.Group, "ui-membership");
        var initial = await ConversationStateAsync(conversationId);

        using (var add = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/AddParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = initial.Version.ToString(),
            ["ParticipantKind"] = "Contact",
            ["ContactId"] = contact.Id.ToString(),
            ["ContactWhatsAppAddressId"] = address.Id.ToString(),
            ["ProviderParticipantKey"] = "ui-contact-member",
            ["NormalizedE164"] = "+60 13-900 2003",
            ["DisplayNameSnapshot"] = "UI Contact Member",
            ["Reason"] = "Record the mapped UI participant"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        }

        var joined = await ConversationStateAsync(conversationId);
        Assert.Equal(initial.AuthorizationVersion + 1, joined.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, joined.Status);
        int participantId;
        long participantVersion;
        string staffId;
        using (var verify = Db())
        {
            var participant = await verify.WhatsAppConversationParticipants.AsNoTracking().SingleAsync();
            participantId = participant.Id;
            participantVersion = participant.Version;
            staffId = await verify.Users.Where(x => x.Email == $"{prefix}-admin@example.com").Select(x => x.Id).SingleAsync();
            var history = await verify.WhatsAppConversationParticipantHistories.AsNoTracking().SingleAsync();
            Assert.Equal(staffId, history.Actor);
            Assert.Equal("BillingControl.WhatsAppConversations", history.Source);
        }
        var joinedDetails = await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}");
        var decodedJoinedDetails = WebUtility.HtmlDecode(joinedDetails);
        Assert.Contains("UI Contact Member", decodedJoinedDetails);
        Assert.Contains("+60139002003", decodedJoinedDetails);
        Assert.Contains("Participant history", decodedJoinedDetails);

        using (var staleConversation = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/AddParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = initial.Version.ToString(),
            ["ParticipantKind"] = "BusinessSender",
            ["ProviderParticipantKey"] = "ui-stale-member",
            ["Reason"] = "Stale conversation test"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, staleConversation.StatusCode);
        }
        Assert.Contains("Another WhatsApp conversation change completed first", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        var noOpBefore = await ConversationStateAsync(conversationId);
        using (var deactivate = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/DeactivateParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ParticipantId"] = participantId.ToString(),
            ["ExpectedConversationVersion"] = noOpBefore.Version.ToString(),
            ["ExpectedParticipantVersion"] = participantVersion.ToString(),
            ["Reason"] = "Participant left the group"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, deactivate.StatusCode);
        }
        var left = await ConversationStateAsync(conversationId);
        Assert.Equal(noOpBefore.AuthorizationVersion + 1, left.AuthorizationVersion);
        using (var verify = Db()) Assert.False(await verify.WhatsAppConversationParticipants.Where(x => x.Id == participantId).Select(x => x.IsActive).SingleAsync());

        long leftParticipantVersion;
        using (var verify = Db()) leftParticipantVersion = await verify.WhatsAppConversationParticipants.Where(x => x.Id == participantId).Select(x => x.Version).SingleAsync();
        using (var staleParticipant = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ReactivateParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ParticipantId"] = participantId.ToString(),
            ["ExpectedConversationVersion"] = left.Version.ToString(),
            ["ExpectedParticipantVersion"] = participantVersion.ToString(),
            ["Reason"] = "Stale participant version test"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, staleParticipant.StatusCode);
        }
        Assert.Contains("Another WhatsApp conversation participant change completed first", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        using (var reactivate = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ReactivateParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ParticipantId"] = participantId.ToString(),
            ["ExpectedConversationVersion"] = left.Version.ToString(),
            ["ExpectedParticipantVersion"] = leftParticipantVersion.ToString(),
            ["Reason"] = "Participant rejoined the group"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, reactivate.StatusCode);
        }
        var rejoined = await ConversationStateAsync(conversationId);
        Assert.Equal(left.AuthorizationVersion + 1, rejoined.AuthorizationVersion);

        long activeParticipantVersion;
        using (var verify = Db()) activeParticipantVersion = await verify.WhatsAppConversationParticipants.Where(x => x.Id == participantId).Select(x => x.Version).SingleAsync();
        using (var noOpReactivate = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ReactivateParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ParticipantId"] = participantId.ToString(),
            ["ExpectedConversationVersion"] = rejoined.Version.ToString(),
            ["ExpectedParticipantVersion"] = activeParticipantVersion.ToString(),
            ["Reason"] = "Repeated rejoin is a service no-op"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, noOpReactivate.StatusCode);
        }
        var afterNoOp = await ConversationStateAsync(conversationId);
        Assert.Equal(rejoined.Version, afterNoOp.Version);
        Assert.Equal(rejoined.AuthorizationVersion, afterNoOp.AuthorizationVersion);

        using (var unknown = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/RecordUnknownMembershipChange", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterNoOp.Version.ToString(),
            ["Reason"] = "Provider reported a membership change before the member was mapped"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, unknown.StatusCode);
        }
        var afterUnknown = await ConversationStateAsync(conversationId);
        Assert.Equal(afterNoOp.AuthorizationVersion + 1, afterUnknown.AuthorizationVersion);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, afterUnknown.Status);
        var afterUnknownDetails = await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}");
        Assert.Contains("Authorization review required", afterUnknownDetails);
        Assert.Contains("Record unknown membership change", afterUnknownDetails);

        using (var addUnknown = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/AddParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterUnknown.Version.ToString(),
            ["ParticipantKind"] = "UnknownExternal",
            ["ProviderParticipantKey"] = "ui-unmapped-member",
            ["DisplayNameSnapshot"] = "Unmapped UI member",
            ["Reason"] = "Record an explicitly evidenced unknown member"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, addUnknown.StatusCode);
        }
        using (var verify = Db())
        {
            Assert.Equal(2, await verify.WhatsAppConversationParticipants.CountAsync());
            Assert.True(await verify.WhatsAppConversationParticipants.AnyAsync(x => x.ParticipantKind == WhatsAppParticipantKind.UnknownExternal && x.IsActive));
        }

        var inactiveBefore = await ConversationStateAsync(conversationId);
        using (var deactivateConversation = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/Deactivate", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = inactiveBefore.Version.ToString(),
            ["Reason"] = "Conversation retired for lifecycle test"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, deactivateConversation.StatusCode);
        }
        var inactive = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.Inactive, inactive.Status);
        using (var reactivateConversation = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/Reactivate", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = inactive.Version.ToString(),
            ["Reason"] = "Reopen for authorization review"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, reactivateConversation.StatusCode);
        }
        var reactivated = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, reactivated.Status);
        Assert.Contains("NeedsAuthorizationReview", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        await using var countsBeforeDb = Db();
        var countsBeforeAntiforgery = await FinancialWorkflowCountsAsync(countsBeforeDb);
        using (var noToken = new HttpRequestMessage(HttpMethod.Post, "/WhatsAppConversations/RecordUnknownMembershipChange")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ConversationId"] = conversationId.ToString(),
                ["ExpectedConversationVersion"] = reactivated.Version.ToString(),
                ["Reason"] = "Missing token must fail"
            })
        })
        using (var response = await staff.SendAsync(noToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using var countsAfterDb = Db();
        var countsAfter = await FinancialWorkflowCountsAsync(countsAfterDb);
        Assert.Equal(countsBeforeAntiforgery, countsAfter);
    }

    [PostgresFact]
    public async Task WhatsAppConversationScopeUiKeepsPartialReviewStaleAndFailsClosed()
    {
        await using var db = await Fresh();
        var prefix = $"whatsapp-ui-scope-{Guid.NewGuid():N}";
        const string password = "WhatsApp-Scope-Password!123";
        var contact = await AddContactAsync(db, "phase9c-scope-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139002004");
        var engagementOne = await Engagement(db);
        var engagementTwo = await Engagement(db);
        var engagementThree = await Engagement(db);

        using var app = WhatsAppUiFactory(prefix);
        await SeedWhatsAppUiAdminAsync(app, prefix, password);
        using var staff = await SignedIn(app, $"{prefix}-admin@example.com", password);
        var conversationId = await CreateWhatsAppConversationThroughUiAsync(staff, WhatsAppConversationKind.Group, "ui-scope-group");
        var created = await ConversationStateAsync(conversationId);

        using (var add = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/AddParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = created.Version.ToString(),
            ["ParticipantKind"] = "Contact",
            ["ContactId"] = contact.Id.ToString(),
            ["ContactWhatsAppAddressId"] = address.Id.ToString(),
            ["ProviderParticipantKey"] = "ui-scope-contact",
            ["Reason"] = "Add a mapped participant before scope review"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        }

        var afterParticipant = await ConversationStateAsync(conversationId);
        using (var approveOne = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterParticipant.Version.ToString(),
            ["EngagementId"] = engagementOne.ToString(),
            ["ExpectedScopeVersion"] = "",
            ["Reason"] = "Explicitly approve the first Engagement scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, approveOne.StatusCode);
        }
        var afterFirstScope = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.Active, afterFirstScope.Status);

        using (var approveTwo = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterFirstScope.Version.ToString(),
            ["EngagementId"] = engagementTwo.ToString(),
            ["ExpectedScopeVersion"] = "",
            ["Reason"] = "Explicitly approve the second Engagement scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, approveTwo.StatusCode);
        }
        var allCurrentBeforeChange = await ConversationStateAsync(conversationId);
        using (var verify = Db())
        {
            Assert.Equal(2, await verify.WhatsAppConversationEngagementScopes.CountAsync());
            Assert.All(await verify.WhatsAppConversationEngagementScopes.AsNoTracking().ToListAsync(), scope => Assert.Equal(allCurrentBeforeChange.AuthorizationVersion, scope.ApprovedAuthorizationVersion));
        }
        var currentDetails = await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}");
        Assert.Contains("Authorization version: CURRENT", currentDetails);
        Assert.Contains("Approve a new Engagement scope", currentDetails);

        using (var approveThree = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = allCurrentBeforeChange.Version.ToString(),
            ["EngagementId"] = engagementThree.ToString(),
            ["ExpectedScopeVersion"] = "",
            ["Reason"] = "Explicitly approve the newly selected Engagement scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, approveThree.StatusCode);
        }
        var allCurrentAfterNewScope = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.Active, allCurrentAfterNewScope.Status);
        using (var verify = Db())
        {
            Assert.Equal(3, await verify.WhatsAppConversationEngagementScopes.CountAsync());
            Assert.All(await verify.WhatsAppConversationEngagementScopes.AsNoTracking().ToListAsync(), scope => Assert.Equal(allCurrentAfterNewScope.AuthorizationVersion, scope.ApprovedAuthorizationVersion));
        }

        using (var addBusinessSender = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/AddParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = allCurrentAfterNewScope.Version.ToString(),
            ["ParticipantKind"] = "BusinessSender",
            ["ProviderParticipantKey"] = "ui-scope-business-sender",
            ["Reason"] = "Membership changed and requires a fresh review"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, addBusinessSender.StatusCode);
        }
        var afterMembershipChange = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, afterMembershipChange.Status);
        var staleDetails = await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}");
        Assert.Contains("Authorization version: STALE", staleDetails);
        Assert.Contains("Reapprove stale scope", staleDetails);

        long scopeOneVersion;
        long scopeTwoVersion;
        long scopeThreeVersion;
        using (var verify = Db())
        {
            var scopes = await verify.WhatsAppConversationEngagementScopes.AsNoTracking().OrderBy(x => x.EngagementId).ToListAsync();
            scopeOneVersion = scopes[0].Version;
            scopeTwoVersion = scopes[1].Version;
            scopeThreeVersion = scopes[2].Version;
        }
        using (var reapproveOne = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterMembershipChange.Version.ToString(),
            ["EngagementId"] = engagementOne.ToString(),
            ["ExpectedScopeVersion"] = scopeOneVersion.ToString(),
            ["Reason"] = "Review and reapprove only the first stale scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, reapproveOne.StatusCode);
        }
        var afterPartial = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, afterPartial.Status);
        using (var partialVerify = Db())
        {
            var scopes = await partialVerify.WhatsAppConversationEngagementScopes.AsNoTracking().OrderBy(x => x.EngagementId).ToListAsync();
            Assert.Equal(afterPartial.AuthorizationVersion, scopes[0].ApprovedAuthorizationVersion);
            Assert.NotEqual(afterPartial.AuthorizationVersion, scopes[1].ApprovedAuthorizationVersion);
            Assert.NotEqual(afterPartial.AuthorizationVersion, scopes[2].ApprovedAuthorizationVersion);
        }
        Assert.Contains("Authorization review required", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        using (var reapproveTwo = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterPartial.Version.ToString(),
            ["EngagementId"] = engagementTwo.ToString(),
            ["ExpectedScopeVersion"] = scopeTwoVersion.ToString(),
            ["Reason"] = "Review and reapprove the final stale scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, reapproveTwo.StatusCode);
        }
        var afterSecondPartial = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.NeedsAuthorizationReview, afterSecondPartial.Status);

        using (var reapproveThree = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterSecondPartial.Version.ToString(),
            ["EngagementId"] = engagementThree.ToString(),
            ["ExpectedScopeVersion"] = scopeThreeVersion.ToString(),
            ["Reason"] = "Review and reapprove the final stale scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, reapproveThree.StatusCode);
        }
        var allCurrent = await ConversationStateAsync(conversationId);
        Assert.Equal(WhatsAppConversationStatus.Active, allCurrent.Status);
        using (var verify = Db())
        {
            var histories = await verify.WhatsAppConversationHistories.AsNoTracking().Where(x => x.WhatsAppConversationId == conversationId).ToListAsync();
            Assert.Contains(histories, x => x.Action == "AuthorizationReviewCleared" && x.Actor.Length > 0 && x.Source == "BillingControl.WhatsAppConversations");
        }

        long currentScopeTwoVersion;
        using (var verify = Db()) currentScopeTwoVersion = await verify.WhatsAppConversationEngagementScopes.Where(x => x.EngagementId == engagementTwo).Select(x => x.Version).SingleAsync();
        using (var revoke = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/RevokeScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ScopeId"] = (await ScopeIdAsync(conversationId, engagementTwo)).ToString(),
            ["ExpectedConversationVersion"] = allCurrent.Version.ToString(),
            ["ExpectedScopeVersion"] = currentScopeTwoVersion.ToString(),
            ["Reason"] = "Revoke the second Engagement scope"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);
        }
        using (var verify = Db())
        {
            var revoked = await verify.WhatsAppConversationEngagementScopes.AsNoTracking().SingleAsync(x => x.EngagementId == engagementTwo);
            Assert.False(revoked.IsActive);
            Assert.NotNull(revoked.RevokedAt);
            Assert.Equal(3, await verify.WhatsAppConversationEngagementScopes.CountAsync(x => x.WhatsAppConversationId == conversationId));
        }

        var afterRevoke = await ConversationStateAsync(conversationId);
        using (var staleScope = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterRevoke.Version.ToString(),
            ["EngagementId"] = engagementTwo.ToString(),
            ["ExpectedScopeVersion"] = "1",
            ["Reason"] = "Stale scope version test"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, staleScope.StatusCode);
        }
        Assert.Contains("Another WhatsApp Engagement scope change completed first", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        using (var addUnknown = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/AddParticipant", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterRevoke.Version.ToString(),
            ["ParticipantKind"] = "UnknownExternal",
            ["ProviderParticipantKey"] = "ui-scope-unknown",
            ["Reason"] = "An active unknown member must block group approval"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, addUnknown.StatusCode);
        }
        var afterUnknown = await ConversationStateAsync(conversationId);

        using (var staleConversation = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterRevoke.Version.ToString(),
            ["EngagementId"] = engagementOne.ToString(),
            ["ExpectedScopeVersion"] = "2",
            ["Reason"] = "Stale conversation version test"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, staleConversation.StatusCode);
        }
        Assert.Contains("Another WhatsApp conversation change completed first", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        using (var blockedApproval = await PostWithToken(staff, $"/WhatsAppConversations/Details/{conversationId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = conversationId.ToString(),
            ["ExpectedConversationVersion"] = afterUnknown.Version.ToString(),
            ["EngagementId"] = engagementTwo.ToString(),
            ["ExpectedScopeVersion"] = (await ScopeVersionAsync(conversationId, engagementTwo)).ToString(),
            ["Reason"] = "Attempt approval while an unknown member is active"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, blockedApproval.StatusCode);
        }
        Assert.Contains("active UnknownExternal participant", await staff.GetStringAsync($"/WhatsAppConversations/Details/{conversationId}"));

        var emptyGroupId = await CreateWhatsAppConversationThroughUiAsync(staff, WhatsAppConversationKind.Group, "ui-empty-group");
        var emptyGroup = await ConversationStateAsync(emptyGroupId);
        using (var noParticipantApproval = await PostWithToken(staff, $"/WhatsAppConversations/Details/{emptyGroupId}", "/WhatsAppConversations/ApproveScope", new()
        {
            ["ConversationId"] = emptyGroupId.ToString(),
            ["ExpectedConversationVersion"] = emptyGroup.Version.ToString(),
            ["EngagementId"] = engagementOne.ToString(),
            ["Reason"] = "Attempt approval without a participant"
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, noParticipantApproval.StatusCode);
        }
        Assert.Contains("must have at least one active participant", await staff.GetStringAsync($"/WhatsAppConversations/Details/{emptyGroupId}"));
    }

    private static async Task<int> ScopeIdAsync(int conversationId, int engagementId)
    {
        await using var db = Db();
        return await db.WhatsAppConversationEngagementScopes
            .Where(x => x.WhatsAppConversationId == conversationId && x.EngagementId == engagementId)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private static async Task<long> ScopeVersionAsync(int conversationId, int engagementId)
    {
        await using var db = Db();
        return await db.WhatsAppConversationEngagementScopes
            .Where(x => x.WhatsAppConversationId == conversationId && x.EngagementId == engagementId)
            .Select(x => x.Version)
            .SingleAsync();
    }
}
