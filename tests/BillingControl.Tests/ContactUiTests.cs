using System.Net;
using System.Text.RegularExpressions;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    [PostgresFact]
    public async Task ContactManagementUiIsStaffOnlyAndKeepsContactOperationsAudited()
    {
        await using var db = await Fresh();
        var prefix = $"contact-ui-{Guid.NewGuid():N}";
        const string password = "Contact-Ui-Password!123";
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Default", Connection)
                .UseSetting("DataProtection:Path", Path.Combine(AppContext.BaseDirectory, $"{prefix}-keys")));

        int customerAId;
        int customerBId;
        using (var scope = app.Services.CreateScope())
        {
            await Seed.Initialize(scope.ServiceProvider, new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BootstrapAdmin:Email"] = $"{prefix}-admin@example.com",
                    ["BootstrapAdmin:Password"] = password
                })
                .Build());

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customerA = new Customer { Name = "Contact UI Customer A" };
            var customerB = new Customer { Name = "Contact UI Customer B" };
            var firm = new BusinessParty { Name = "Contact UI Firm" };
            var manager = new Manager { Name = "Contact UI Manager" };
            var worker = new Worker { Name = "Contact UI Worker" };
            context.AddRange(customerA, customerB, firm, manager, worker);
            await context.SaveChangesAsync();
            customerAId = customerA.Id;
            customerBId = customerB.Id;

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

        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousResponse = await anonymous.GetAsync("/Contacts");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/Account/Login", anonymousResponse.Headers.Location!.ToString());

        using var admin = await SignedIn(app, $"{prefix}-admin@example.com", password);
        using var internalUser = await SignedIn(app, $"{prefix}-internal@example.com", password);
        using var firmClient = await SignedIn(app, $"{prefix}-firm@example.com", password);
        using var managerClient = await SignedIn(app, $"{prefix}-manager@example.com", password);
        using var workerClient = await SignedIn(app, $"{prefix}-worker@example.com", password);

        foreach (var client in new[] { firmClient, managerClient, workerClient })
        {
            var denied = await client.GetAsync("/Contacts");
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/Account/Denied", denied.Headers.Location!.ToString());
            Assert.DoesNotContain("Contacts / PICs", await client.GetStringAsync("/"));
        }

        Assert.Contains("Contacts / PICs", await admin.GetStringAsync("/"));
        Assert.Contains("Contacts / PICs", await internalUser.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Contacts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await internalUser.GetAsync("/Contacts")).StatusCode);

        async Task<(int Engagements, int DocumentRequests, int BillingRecords, int WorkItems, int WorkerAssignments, int Invoices, int Payments, int Receipts)> IsolationSnapshot()
        {
            using var verify = Db();
            return (
                await verify.Engagements.CountAsync(),
                await verify.DocumentRequests.CountAsync(),
                await verify.BillingRecords.CountAsync(),
                await verify.WorkItems.CountAsync(),
                await verify.WorkerAssignments.CountAsync(),
                await verify.Invoices.CountAsync(),
                await verify.WorkerPayments.CountAsync(),
                await verify.CustomerReceipts.CountAsync());
        }

        var isolationBefore = await IsolationSnapshot();

        static int ContactIdFrom(HttpResponseMessage response)
        {
            var location = response.Headers.Location?.ToString() ?? "";
            var match = Regex.Match(location, @"/Contacts/Details/(\d+)");
            Assert.True(match.Success, $"Expected a Contact details redirect, got {location}");
            return int.Parse(match.Groups[1].Value);
        }

        async Task<int> CreateContact(string name)
        {
            var response = await PostWithToken(admin, "/Contacts/Create", "/Contacts/Create", new()
            {
                ["Name"] = name,
                ["PreferredLanguage"] = " EN-mY "
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return ContactIdFrom(response);
        }

        var contactId = await CreateContact("  Alice PIC  ");
        var secondContactId = await CreateContact("Bob PIC");
        Assert.Contains("Alice PIC", await admin.GetStringAsync($"/Contacts/Details/{contactId}"));

        var list = await admin.GetStringAsync("/Contacts");
        Assert.Contains("Alice PIC", list);
        Assert.Contains("en-MY", list);
        Assert.Contains("Bob PIC", list);
        Assert.Contains("Contacts / PICs", list);

        var nameSearch = await admin.GetStringAsync("/Contacts?search=Alice");
        Assert.Contains("Alice PIC", nameSearch);
        Assert.DoesNotContain("Bob PIC", nameSearch);

        var addressResponse = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/AddAddress", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["PhoneNumber"] = "+60 (12) 345-6789"
        });
        Assert.Equal(HttpStatusCode.Redirect, addressResponse.StatusCode);

        int firstAddressId;
        long firstAddressInitialVersion;
        using (var verify = Db())
        {
            var first = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.ContactId == contactId);
            firstAddressId = first.Id;
            firstAddressInitialVersion = first.Version;
            Assert.Equal("+60123456789", first.NormalizedE164);
            Assert.True(first.IsPrimary);
            Assert.Equal(ContactWhatsAppConsentState.Unknown, first.ConsentState);
        }

        var detailsWithFirstAddress = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("+60123456789", detailsWithFirstAddress);
        Assert.Contains("Primary", detailsWithFirstAddress);
        Assert.Contains("Unknown", detailsWithFirstAddress);
        Assert.Contains("Provider-owned, read-only", detailsWithFirstAddress);
        Assert.DoesNotContain("name=\"ProviderWaId\"", detailsWithFirstAddress);
        var formattedPhoneSearch = Uri.EscapeDataString("+60 (12) 345-6789");
        var phoneSearchUrl = $"/Contacts?search={formattedPhoneSearch}";
        var phoneSearch = await admin.GetStringAsync(phoneSearchUrl);
        var echoedSearch = Regex.Match(phoneSearch, "id=\"Search\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;
        Assert.True(phoneSearch.Contains("+60123456789"), $"Formatted phone search failed. url={phoneSearchUrl}; echoed={echoedSearch}; noResults={phoneSearch.Contains("No contacts found")}");

        var secondAddressResponse = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/AddAddress", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["PhoneNumber"] = "+60129876543"
        });
        Assert.Equal(HttpStatusCode.Redirect, secondAddressResponse.StatusCode);

        int secondAddressId;
        long secondAddressVersion;
        using (var verify = Db())
        {
            var addresses = await verify.ContactWhatsAppAddresses.AsNoTracking()
                .Where(x => x.ContactId == contactId).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, addresses.Count);
            secondAddressId = addresses[1].Id;
            secondAddressVersion = addresses[1].Version;
            Assert.False(addresses[1].IsPrimary);
        }

        var primarySwitch = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressPrimary", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = secondAddressId.ToString(),
            ["ExpectedVersion"] = secondAddressVersion.ToString()
        });
        Assert.Equal(HttpStatusCode.Redirect, primarySwitch.StatusCode);
        using (var verify = Db())
        {
            var addresses = await verify.ContactWhatsAppAddresses.AsNoTracking()
                .Where(x => x.ContactId == contactId).ToListAsync();
            Assert.False(addresses.Single(x => x.Id == firstAddressId).IsPrimary);
            Assert.True(addresses.Single(x => x.Id == secondAddressId).IsPrimary);
        }

        // The address version is also optimistic-concurrency protected.
        var stalePrimary = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressPrimary", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = firstAddressId.ToString(),
            ["ExpectedVersion"] = firstAddressInitialVersion.ToString()
        });
        Assert.Equal(HttpStatusCode.Redirect, stalePrimary.StatusCode);
        Assert.Contains("Another WhatsApp address change completed first", await admin.GetStringAsync($"/Contacts/Details/{contactId}"));

        // A primary address can be deactivated without promoting another address.
        using (var verify = Db())
        {
            var current = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressActive", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["AddressId"] = secondAddressId.ToString(),
                ["ExpectedVersion"] = current.Version.ToString(),
                ["IsActive"] = "false",
                ["MakePrimary"] = "false",
                ["Reason"] = "Retired primary endpoint"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        using (var verify = Db())
        {
            var address = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            Assert.False(address.IsActive);
            Assert.False(address.IsPrimary);
            Assert.Equal(0, await verify.ContactWhatsAppAddresses.CountAsync(x => x.ContactId == contactId && x.IsPrimary));
        }

        // Reactivating without MakePrimary remains non-primary; explicit MakePrimary is separate.
        using (var verify = Db())
        {
            var current = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressActive", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["AddressId"] = secondAddressId.ToString(),
                ["ExpectedVersion"] = current.Version.ToString(),
                ["IsActive"] = "true",
                ["MakePrimary"] = "false",
                ["Reason"] = "Endpoint returned"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        using (var verify = Db())
        {
            var address = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            Assert.True(address.IsActive);
            Assert.False(address.IsPrimary);
        }

        using (var verify = Db())
        {
            var current = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressPrimary", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["AddressId"] = secondAddressId.ToString(),
                ["ExpectedVersion"] = current.Version.ToString()
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        // Consent evidence and retained opt-out facts remain visible through later transitions.
        async Task<long> AddressVersion(int addressId)
        {
            using var verify = Db();
            return await verify.ContactWhatsAppAddresses.Where(x => x.Id == addressId).Select(x => x.Version).SingleAsync();
        }

        var optIn = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/RecordConsent", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = secondAddressId.ToString(),
            ["ExpectedVersion"] = (await AddressVersion(secondAddressId)).ToString(),
            ["ConsentState"] = "OptedIn",
            ["ConsentSource"] = "Signed form",
            ["ConsentEvidenceReference"] = "evidence-A",
            ["Reason"] = "Initial opt-in"
        });
        Assert.Equal(HttpStatusCode.Redirect, optIn.StatusCode);

        var optOut = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/RecordConsent", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = secondAddressId.ToString(),
            ["ExpectedVersion"] = (await AddressVersion(secondAddressId)).ToString(),
            ["ConsentState"] = "DoNotWhatsApp",
            ["ConsentSource"] = "Client call",
            ["Reason"] = "Client opted out"
        });
        Assert.Equal(HttpStatusCode.Redirect, optOut.StatusCode);

        var reOptIn = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/RecordConsent", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = secondAddressId.ToString(),
            ["ExpectedVersion"] = (await AddressVersion(secondAddressId)).ToString(),
            ["ConsentState"] = "OptedIn",
            ["ConsentSource"] = "New signed form",
            ["ConsentEvidenceReference"] = "evidence-B",
            ["Reason"] = "Re-confirmed"
        });
        Assert.Equal(HttpStatusCode.Redirect, reOptIn.StatusCode);

        var reset = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/RecordConsent", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = secondAddressId.ToString(),
            ["ExpectedVersion"] = (await AddressVersion(secondAddressId)).ToString(),
            ["ConsentState"] = "Unknown",
            ["Reason"] = "Consent record needs review"
        });
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);

        var consentDetails = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("evidence-A", consentDetails);
        Assert.Contains("evidence-B", consentDetails);
        Assert.Contains("Client opted out", consentDetails);
        Assert.Contains("Opted In", consentDetails);
        Assert.Contains("Do Not WhatsApp", consentDetails);
        Assert.Contains("Reset consent to Unknown", consentDetails);
        using (var verify = Db())
        {
            var address = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            Assert.Equal(ContactWhatsAppConsentState.Unknown, address.ConsentState);
            Assert.NotNull(address.LastOptOutAt);
            Assert.Equal("Client opted out", address.LastOptOutReason);
        }

        var invalidOptIn = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/RecordConsent", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["AddressId"] = secondAddressId.ToString(),
            ["ExpectedVersion"] = (await AddressVersion(secondAddressId)).ToString(),
            ["ConsentState"] = "OptedIn",
            ["ConsentSource"] = "Missing evidence test",
            ["Reason"] = "Should be rejected"
        });
        Assert.Equal(HttpStatusCode.Redirect, invalidOptIn.StatusCode);
        Assert.Contains("Consent evidence reference is required for OptedIn", await admin.GetStringAsync($"/Contacts/Details/{contactId}"));

        // Switching primary after consent does not erase the address consent snapshot.
        using (var verify = Db())
        {
            var first = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == firstAddressId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressPrimary", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["AddressId"] = firstAddressId.ToString(),
                ["ExpectedVersion"] = first.Version.ToString()
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        var afterConsentPrimarySwitch = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("evidence-A", afterConsentPrimarySwitch);
        Assert.Contains("evidence-B", afterConsentPrimarySwitch);

        // Opt-out remains available for an inactive address and an inactive Contact.
        using (var verify = Db())
        {
            var address = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetAddressActive", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["AddressId"] = secondAddressId.ToString(),
                ["ExpectedVersion"] = address.Version.ToString(),
                ["IsActive"] = "false",
                ["MakePrimary"] = "false",
                ["Reason"] = "Temporarily inactive"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        var inactiveAddressDetails = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("Do Not WhatsApp", inactiveAddressDetails);

        // Add, edit, deactivate, duplicate-attempt, and reactivate the same durable customer link.
        var addLink = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/AddCustomerLink", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["CustomerId"] = customerAId.ToString(),
            ["Role"] = "Finance PIC",
            ["Note"] = "Primary bookkeeping contact"
        });
        Assert.Equal(HttpStatusCode.Redirect, addLink.StatusCode);

        int linkId;
        long linkVersion;
        using (var verify = Db())
        {
            var link = await verify.ContactCustomerLinks.AsNoTracking().SingleAsync(x => x.ContactId == contactId && x.CustomerId == customerAId);
            linkId = link.Id;
            linkVersion = link.Version;
            Assert.True(link.IsActive);
            Assert.Null(link.EffectiveTo);
        }

        var editLink = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/EditCustomerLink", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["LinkId"] = linkId.ToString(),
            ["ExpectedVersion"] = linkVersion.ToString(),
            ["Role"] = "Updated Finance PIC",
            ["Note"] = "Updated note"
        });
        Assert.Equal(HttpStatusCode.Redirect, editLink.StatusCode);

        var staleLink = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/EditCustomerLink", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["LinkId"] = linkId.ToString(),
            ["ExpectedVersion"] = linkVersion.ToString(),
            ["Role"] = "Stale edit",
            ["Note"] = "Must not overwrite"
        });
        Assert.Equal(HttpStatusCode.Redirect, staleLink.StatusCode);
        Assert.Contains("Another Contact/Customer link change completed first", await admin.GetStringAsync($"/Contacts/Details/{contactId}"));

        using (var verify = Db())
        {
            var link = await verify.ContactCustomerLinks.AsNoTracking().SingleAsync(x => x.Id == linkId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetCustomerLinkActive", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["LinkId"] = linkId.ToString(),
                ["ExpectedVersion"] = link.Version.ToString(),
                ["IsActive"] = "false",
                ["Reason"] = "Relationship paused"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var duplicate = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/AddCustomerLink", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["CustomerId"] = customerAId.ToString(),
            ["Role"] = "Duplicate",
            ["Note"] = "Must not create a second durable row"
        });
        Assert.Equal(HttpStatusCode.Redirect, duplicate.StatusCode);
        Assert.Contains("Reactivate the existing durable link", await admin.GetStringAsync($"/Contacts/Details/{contactId}"));

        using (var verify = Db())
        {
            Assert.Equal(1, await verify.ContactCustomerLinks.CountAsync(x => x.ContactId == contactId && x.CustomerId == customerAId));
            var link = await verify.ContactCustomerLinks.AsNoTracking().SingleAsync(x => x.Id == linkId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetCustomerLinkActive", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["LinkId"] = linkId.ToString(),
                ["ExpectedVersion"] = link.Version.ToString(),
                ["IsActive"] = "true",
                ["Reason"] = "Relationship restored"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var addSecondLink = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/AddCustomerLink", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["CustomerId"] = customerBId.ToString(),
            ["Role"] = "Operations PIC",
            ["Note"] = "Secondary customer relationship"
        });
        Assert.Equal(HttpStatusCode.Redirect, addSecondLink.StatusCode);
        var linksDetails = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("Contact UI Customer A", linksDetails);
        Assert.Contains("Contact UI Customer B", linksDetails);
        Assert.Contains("Updated Finance PIC", linksDetails);
        Assert.Contains("Relationship paused", linksDetails);
        Assert.Contains("Relationship restored", linksDetails);

        // The stale profile version is rejected without silently overwriting the newer name.
        long contactVersion;
        using (var verify = Db()) contactVersion = await verify.Contacts.Where(x => x.Id == contactId).Select(x => x.Version).SingleAsync();
        var currentEdit = await PostWithToken(admin, $"/Contacts/Edit/{contactId}", "/Contacts/Edit", new()
        {
            ["Id"] = contactId.ToString(),
            ["ExpectedVersion"] = contactVersion.ToString(),
            ["Name"] = "Alice PIC Updated",
            ["PreferredLanguage"] = "zh-Hant-MY"
        });
        Assert.Equal(HttpStatusCode.Redirect, currentEdit.StatusCode);
        var staleEdit = await PostWithToken(admin, $"/Contacts/Edit/{contactId}", "/Contacts/Edit", new()
        {
            ["Id"] = contactId.ToString(),
            ["ExpectedVersion"] = contactVersion.ToString(),
            ["Name"] = "SHOULD NOT OVERWRITE",
            ["PreferredLanguage"] = "en"
        });
        Assert.Equal(HttpStatusCode.Redirect, staleEdit.StatusCode);
        var staleDetails = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("Another Contact change completed first", staleDetails);
        Assert.Contains("Alice PIC Updated", staleDetails);
        Assert.DoesNotContain("SHOULD NOT OVERWRITE", staleDetails);

        // Contact deactivation is independent of child state and inactive contacts remain visible on request.
        using (var verify = Db())
        {
            var current = await verify.Contacts.AsNoTracking().SingleAsync(x => x.Id == contactId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetActive", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["ExpectedVersion"] = current.Version.ToString(),
                ["IsActive"] = "false",
                ["Reason"] = "Contact temporarily inactive"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        using (var verify = Db())
        {
            Assert.False(await verify.Contacts.Where(x => x.Id == contactId).Select(x => x.IsActive).SingleAsync());
            Assert.Equal(2, await verify.ContactWhatsAppAddresses.CountAsync(x => x.ContactId == contactId));
            Assert.Equal(2, await verify.ContactCustomerLinks.CountAsync(x => x.ContactId == contactId));
        }

        var contactReactivated = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetActive", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["ExpectedVersion"] = (await DbContactVersion(contactId)).ToString(),
            ["IsActive"] = "true",
            ["Reason"] = "Contact returned"
        });
        Assert.Equal(HttpStatusCode.Redirect, contactReactivated.StatusCode);
        using (var verify = Db()) Assert.True(await verify.Contacts.Where(x => x.Id == contactId).Select(x => x.IsActive).SingleAsync());

        var contactDeactivatedAgain = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/SetActive", new()
        {
            ["ContactId"] = contactId.ToString(),
            ["ExpectedVersion"] = (await DbContactVersion(contactId)).ToString(),
            ["IsActive"] = "false",
            ["Reason"] = "Keep inactive for audit test"
        });
        Assert.Equal(HttpStatusCode.Redirect, contactDeactivatedAgain.StatusCode);
        var inactiveContactDetails = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.Contains("Do Not WhatsApp", inactiveContactDetails);
        Assert.DoesNotContain("name=\"PhoneNumber\"", inactiveContactDetails);
        Assert.Contains("Contact temporarily inactive", inactiveContactDetails);
        Assert.DoesNotContain("Alice PIC Updated", await admin.GetStringAsync("/Contacts"));
        Assert.Contains("Alice PIC Updated", await admin.GetStringAsync("/Contacts?includeInactive=true"));

        // An opt-out is still permitted while the Contact and address are inactive.
        using (var verify = Db())
        {
            var address = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == secondAddressId);
            var response = await PostWithToken(admin, $"/Contacts/Details/{contactId}", "/Contacts/RecordConsent", new()
            {
                ["ContactId"] = contactId.ToString(),
                ["AddressId"] = secondAddressId.ToString(),
                ["ExpectedVersion"] = address.Version.ToString(),
                ["ConsentState"] = "DoNotWhatsApp",
                ["ConsentSource"] = "Safety review",
                ["Reason"] = "Opt-out retained while inactive"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        Assert.Contains("Opt-out retained while inactive", await admin.GetStringAsync($"/Contacts/Details/{contactId}"));

        // A missing antiforgery token is rejected, GET does not mutate, and foreign/malformed identifiers are safe.
        using var noAntiforgery = new HttpRequestMessage(HttpMethod.Post, "/Contacts/Create")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "No CSRF" })
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(noAntiforgery)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.GetAsync($"/Contacts/SetActive?ContactId={contactId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Contacts/Details/not-an-id")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Contacts/Details/999999")).StatusCode);

        using (var verify = Db())
        {
            var address = await verify.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == firstAddressId);
            var beforePrimary = address.IsPrimary;
            var foreign = await PostWithToken(admin, $"/Contacts/Details/{secondContactId}", "/Contacts/SetAddressPrimary", new()
            {
                ["ContactId"] = secondContactId.ToString(),
                ["AddressId"] = firstAddressId.ToString(),
                ["ExpectedVersion"] = address.Version.ToString()
            });
            Assert.Equal(HttpStatusCode.Redirect, foreign.StatusCode);
            Assert.Equal(beforePrimary, await verify.ContactWhatsAppAddresses.Where(x => x.Id == firstAddressId).Select(x => x.IsPrimary).SingleAsync());
        }

        var finalDetails = await admin.GetStringAsync($"/Contacts/Details/{contactId}");
        Assert.DoesNotContain("name=\"ProviderWaId\"", finalDetails);
        Assert.DoesNotContain("action=\"/Contacts/Send", finalDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(isolationBefore, await IsolationSnapshot());

        async Task<long> DbContactVersion(int id)
        {
            using var verify = Db();
            return await verify.Contacts.Where(x => x.Id == id).Select(x => x.Version).SingleAsync();
        }
    }
}
