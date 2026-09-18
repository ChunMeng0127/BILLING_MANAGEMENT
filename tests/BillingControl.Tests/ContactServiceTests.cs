using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static BusinessClock ContactTestClock() => new(
        new FixedProgressTime
        {
            Now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero)
        },
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BusinessTimeZone"] = "UTC" })
            .Build());

    private static ContactCreateInput NewContact(string name) =>
        new(name, null, "contact-user", "BillingControl.Contacts");

    private static ContactAddressCreateInput NewAddress(int contactId, string phone) =>
        new(contactId, phone, "contact-user", "BillingControl.Contacts");

    private static ContactConsentInput Consent(
        long version,
        ContactWhatsAppConsentState state,
        string? consentSource,
        string? evidence,
        string reason = "Contact instruction recorded") =>
        new(version, state, consentSource, evidence, reason, "contact-user", "BillingControl.Contacts");

    [PostgresFact]
    public async Task ContactServiceCreatesUpdatesSearchesAndReturnsReadModels()
    {
        await using var db = await Fresh();
        var service = new ContactService(db, ContactTestClock());

        var created = await service.CreateContactAsync(new(
            "  Alice Tan  ", " ZH-hANT-my ", " staff-1 ", " BillingControl.Contacts "));

        Assert.Equal("Alice Tan", created.Name);
        Assert.Equal("zh-Hant-MY", created.PreferredLanguage);
        Assert.True(created.IsActive);
        var createdHistory = Assert.Single(created.StatusHistory);
        Assert.Null(createdHistory.PreviousIsActive);
        Assert.True(createdHistory.NewIsActive);
        Assert.Equal("Created", createdHistory.Action);
        Assert.Equal("staff-1", createdHistory.Actor);
        Assert.Equal("BillingControl.Contacts", createdHistory.Source);
        Assert.Equal(new DateTime(2026, 9, 12, 12, 0, 0), createdHistory.OccurredAt);
        Assert.Empty(db.ChangeTracker.Entries<Contact>());

        var updated = await service.UpdateContactAsync(created.Id, new(
            created.Version, " Alice T. ", "en-my", "staff-1", "BillingControl.Contacts"));
        Assert.Equal("Alice T.", updated.Name);
        Assert.Equal("en-MY", updated.PreferredLanguage);
        Assert.Single(updated.StatusHistory);
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateContactAsync(created.Id, new(
            created.Version, "stale", "en", "staff-1", "BillingControl.Contacts")));

        var deactivated = await service.SetContactActiveAsync(updated.Id, new(
            updated.Version, false, "No longer the current PIC", "staff-1", "BillingControl.Contacts"));
        Assert.False(deactivated.IsActive);
        Assert.Equal(2, deactivated.StatusHistory.Count);
        Assert.False(deactivated.StatusHistory.Last().NewIsActive);

        var reactivated = await service.SetContactActiveAsync(deactivated.Id, new(
            deactivated.Version, true, "PIC relationship restored", "staff-1", "BillingControl.Contacts"));
        Assert.True(reactivated.IsActive);
        Assert.Equal(3, reactivated.StatusHistory.Count);

        var visible = await service.GetContactsAsync();
        Assert.Contains(visible, x => x.Id == created.Id);
        var inactiveContact = await service.CreateContactAsync(NewContact("Inactive PIC"));
        inactiveContact = await service.SetContactActiveAsync(inactiveContact.Id, new(
            inactiveContact.Version, false, "Test inactive filter", "staff-1", "BillingControl.Contacts"));
        Assert.DoesNotContain((await service.GetContactsAsync()).Select(x => x.Id), x => x == inactiveContact.Id);
        Assert.Contains((await service.GetContactsAsync(includeInactive: true)).Select(x => x.Id), x => x == inactiveContact.Id);
    }

    [PostgresFact]
    public async Task ContactServiceAppliesSafeAddressPrimaryActiveAndOwnershipRules()
    {
        await using var db = await Fresh();
        var service = new ContactService(db, ContactTestClock());
        var contact = await service.CreateContactAsync(NewContact("Address PIC"));

        var firstAddress = await service.AddAddressAsync(NewAddress(contact.Id, "+60 12-345 6789"));
        Assert.Equal("+60123456789", firstAddress.NormalizedE164);
        Assert.True(firstAddress.IsPrimary);
        Assert.True(firstAddress.IsActive);
        Assert.Equal(ContactWhatsAppConsentState.Unknown, firstAddress.ConsentState);
        Assert.Null(firstAddress.ProviderWaId);
        Assert.Equal("Created", Assert.Single(firstAddress.History).Action);
        var unchangedPrimary = await service.SetAddressPrimaryAsync(contact.Id, firstAddress.Id,
            new(firstAddress.Version, "contact-user", "BillingControl.Contacts"));
        Assert.Single(unchangedPrimary.Addresses.Single(x => x.Id == firstAddress.Id).History);
        Assert.Single(await service.GetContactsAsync("+60 12-345 6789"));

        var secondAddress = await service.AddAddressAsync(NewAddress(contact.Id, "+60 (13) 987-6543"));
        Assert.False(secondAddress.IsPrimary);
        Assert.Single(secondAddress.History);

        var switched = await service.SetAddressPrimaryAsync(contact.Id, secondAddress.Id,
            new(secondAddress.Version, "contact-user", "BillingControl.Contacts"));
        var switchedFirst = switched.Addresses.Single(x => x.Id == firstAddress.Id);
        var switchedSecond = switched.Addresses.Single(x => x.Id == secondAddress.Id);
        Assert.False(switchedFirst.IsPrimary);
        Assert.True(switchedSecond.IsPrimary);
        Assert.Equal(ContactWhatsAppConsentState.Unknown, switchedFirst.ConsentState);
        Assert.Equal(ContactWhatsAppConsentState.Unknown, switchedSecond.ConsentState);
        Assert.Equal(2, switchedFirst.History.Count);
        Assert.Equal(2, switchedSecond.History.Count);
        Assert.Equal("PrimaryCleared", switchedFirst.History.Last().Action);
        Assert.False(switchedFirst.History.Last().PreviousIsPrimary is null);
        Assert.True(switchedFirst.History.Last().NewConsentRecordedAt is null);

        await Assert.ThrowsAsync<BusinessException>(() => service.SetAddressPrimaryAsync(contact.Id, secondAddress.Id,
            new(secondAddress.Version, "contact-user", "BillingControl.Contacts")));

        var deactivated = await service.SetAddressActiveAsync(secondAddress.Id,
            new(switchedSecond.Version, false, false, "Number retired", "contact-user", "BillingControl.Contacts"));
        Assert.False(deactivated.IsActive);
        Assert.False(deactivated.IsPrimary);
        var afterDeactivation = await service.GetContactDetailsAsync(contact.Id);
        Assert.False(afterDeactivation!.Addresses.Single(x => x.Id == firstAddress.Id).IsPrimary);

        var reactivated = await service.SetAddressActiveAsync(secondAddress.Id,
            new(deactivated.Version, true, false, "Number restored", "contact-user", "BillingControl.Contacts"));
        var reactivatedSecond = reactivated;
        Assert.True(reactivatedSecond.IsActive);
        Assert.False(reactivatedSecond.IsPrimary);

        var madePrimary = await service.SetAddressActiveAsync(secondAddress.Id,
            new(reactivatedSecond.Version, true, true, "Use restored number as primary", "contact-user", "BillingControl.Contacts"));
        Assert.True(madePrimary.IsPrimary);
        var afterMakePrimary = await service.GetContactDetailsAsync(contact.Id);
        Assert.False(afterMakePrimary!.Addresses.Single(x => x.Id == firstAddress.Id).IsPrimary);
        Assert.Equal("+60139876543", madePrimary.NormalizedE164);

        var owner = await service.CreateContactAsync(NewContact("Other owner"));
        var ownershipFailure = await Assert.ThrowsAsync<BusinessException>(() => service.AddAddressAsync(
            NewAddress(owner.Id, "+60123456789")));
        Assert.DoesNotContain("IX_", ownershipFailure.Message);
        Assert.DoesNotContain("Postgres", ownershipFailure.Message);
        Assert.Equal(2, await db.ContactWhatsAppAddresses.CountAsync());
    }

    [PostgresFact]
    public async Task ContactServiceAuditsAllConsentSnapshotsAndAllowsSafeOptOuts()
    {
        await using var db = await Fresh();
        var service = new ContactService(db, ContactTestClock());
        var contact = await service.CreateContactAsync(NewContact("Consent PIC"));
        var address = await service.AddAddressAsync(NewAddress(contact.Id, "+60112223344"));

        await Assert.ThrowsAsync<BusinessException>(() => service.RecordConsentAsync(address.Id,
            Consent(address.Version, ContactWhatsAppConsentState.OptedIn, "staff-form", null)));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordConsentAsync(address.Id,
            Consent(address.Version, ContactWhatsAppConsentState.DoNotWhatsApp, null, null, "")));

        var optedInA = await service.RecordConsentAsync(address.Id,
            Consent(address.Version, ContactWhatsAppConsentState.OptedIn, "client-email", "evidence-A"));
        var optedInHistory = optedInA.History.Last();
        Assert.Equal(ContactWhatsAppConsentState.Unknown, optedInHistory.PreviousConsentState);
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, optedInHistory.NewConsentState);
        Assert.Equal("client-email", optedInHistory.NewConsentSource);
        Assert.Equal("evidence-A", optedInHistory.NewConsentEvidenceReference);
        Assert.Equal(optedInA.ConsentRecordedAt, optedInHistory.NewConsentRecordedAt);

        var optedOut = await service.RecordConsentAsync(optedInA.Id,
            Consent(optedInA.Version, ContactWhatsAppConsentState.DoNotWhatsApp, "client-request", null, "Client opted out"));
        var optOutHistory = optedOut.History.Last();
        Assert.Equal("evidence-A", optOutHistory.PreviousConsentEvidenceReference);
        Assert.Equal(ContactWhatsAppConsentState.DoNotWhatsApp, optOutHistory.NewConsentState);
        Assert.Equal("Client opted out", optOutHistory.NewLastOptOutReason);
        Assert.NotNull(optOutHistory.NewLastOptOutAt);

        var optedInB = await service.RecordConsentAsync(optedOut.Id,
            Consent(optedOut.Version, ContactWhatsAppConsentState.OptedIn, "client-email", "evidence-B", "Re-confirmed"));
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, optedInB.ConsentState);
        Assert.Equal("evidence-B", optedInB.ConsentEvidenceReference);
        Assert.Equal("Client opted out", optedInB.LastOptOutReason);
        Assert.NotNull(optedInB.LastOptOutAt);
        var reOptInHistory = optedInB.History.Last();
        Assert.Equal("evidence-A", optedInB.History[1].NewConsentEvidenceReference);
        Assert.Null(reOptInHistory.PreviousConsentEvidenceReference);
        Assert.Equal("evidence-B", reOptInHistory.NewConsentEvidenceReference);
        Assert.Equal("Client opted out", reOptInHistory.PreviousLastOptOutReason);
        Assert.Equal("Client opted out", reOptInHistory.NewLastOptOutReason);

        var reset = await service.RecordConsentAsync(optedInB.Id,
            Consent(optedInB.Version, ContactWhatsAppConsentState.Unknown, null, null, "Consent status reset"));
        Assert.Equal(ContactWhatsAppConsentState.Unknown, reset.ConsentState);
        Assert.Null(reset.ConsentRecordedAt);
        Assert.Null(reset.ConsentSource);
        Assert.Null(reset.ConsentEvidenceReference);
        Assert.Equal("Client opted out", reset.LastOptOutReason);
        var historyCount = reset.History.Count;
        var noOp = await service.RecordConsentAsync(reset.Id,
            Consent(reset.Version, ContactWhatsAppConsentState.Unknown, null, null, "Repeated reset"));
        Assert.Equal(historyCount, noOp.History.Count);
        Assert.Equal(reset.Version, noOp.Version);

        var explicitOptOut = await service.RecordConsentAsync(noOp.Id,
            Consent(noOp.Version, ContactWhatsAppConsentState.DoNotWhatsApp, "phone-call", null, "Second opt-out"));
        Assert.Equal(historyCount + 1, explicitOptOut.History.Count);
        Assert.Equal("Second opt-out", explicitOptOut.LastOptOutReason);

        var inactiveAddress = await service.SetAddressActiveAsync(explicitOptOut.Id,
            new(explicitOptOut.Version, false, false, "Temporarily inactive", "contact-user", "BillingControl.Contacts"));
        var inactiveContact = await service.SetContactActiveAsync(contact.Id, new(
            contact.Version, false, "Contact inactive for safety test", "contact-user", "BillingControl.Contacts"));
        var inactiveOptOut = await service.RecordConsentAsync(inactiveAddress.Id,
            Consent(inactiveAddress.Version, ContactWhatsAppConsentState.DoNotWhatsApp, "support-ticket", null, "Opt-out while inactive"));
        Assert.Equal(ContactWhatsAppConsentState.DoNotWhatsApp, inactiveOptOut.ConsentState);
        Assert.False(inactiveContact.IsActive);
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordConsentAsync(inactiveOptOut.Id,
            Consent(inactiveOptOut.Version, ContactWhatsAppConsentState.OptedIn, "new-consent", "evidence-C")));
    }

    [PostgresFact]
    public async Task ContactServiceMaintainsDurableCustomerLinksAndEffectiveDates()
    {
        await using var db = await Fresh();
        var clock = ContactTestClock();
        var service = new ContactService(db, clock);
        var contactA = await service.CreateContactAsync(NewContact("Link PIC A"));
        var contactB = await service.CreateContactAsync(NewContact("Link PIC B"));
        var customerA = await AddContactCustomerAsync(db, "link-customer-a");
        var customerB = await AddContactCustomerAsync(db, "link-customer-b");

        var linkA = await service.AddCustomerLinkAsync(new(
            contactA.Id, customerA.Id, " Director ", "  Main customer contact  ", "contact-user", "BillingControl.Contacts"));
        Assert.True(linkA.IsActive);
        Assert.Equal(clock.Today, linkA.EffectiveFrom);
        Assert.Null(linkA.EffectiveTo);
        Assert.Equal("Director", linkA.Role);
        Assert.Equal("Main customer contact", linkA.Note);
        Assert.Equal("Created", Assert.Single(linkA.History).Action);

        var linkB = await service.AddCustomerLinkAsync(new(
            contactA.Id, customerB.Id, "Finance", null, "contact-user", "BillingControl.Contacts"));
        var linkC = await service.AddCustomerLinkAsync(new(
            contactB.Id, customerA.Id, "Alternate", null, "contact-user", "BillingControl.Contacts"));
        Assert.Equal(2, (await service.GetContactDetailsAsync(contactA.Id))!.CustomerLinks.Count);
        Assert.Single((await service.GetContactDetailsAsync(contactB.Id))!.CustomerLinks);
        Assert.Contains((await service.GetActiveCustomerOptionsAsync()).Select(x => x.Id), x => x == customerA.Id);
        Assert.Contains((await service.GetActiveCustomerOptionsAsync()).Select(x => x.Id), x => x == customerB.Id);

        var updated = await service.UpdateCustomerLinkAsync(linkA.Id, new(
            linkA.Version, " Finance ", "Updated note ", "contact-user", "BillingControl.Contacts"));
        Assert.Equal("Finance", updated.Role);
        Assert.Equal("Updated note", updated.Note);
        Assert.Equal(2, updated.History.Count);
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateCustomerLinkAsync(linkA.Id, new(
            linkA.Version, "Stale", null, "contact-user", "BillingControl.Contacts")));

        var inactive = await service.SetCustomerLinkActiveAsync(updated.Id, new(
            updated.Version, false, "Relationship paused", "contact-user", "BillingControl.Contacts"));
        Assert.False(inactive.IsActive);
        Assert.Equal(clock.Today, inactive.EffectiveTo);
        var reactivated = await service.SetCustomerLinkActiveAsync(inactive.Id, new(
            inactive.Version, true, "Relationship restored", "contact-user", "BillingControl.Contacts"));
        Assert.Equal(inactive.Id, reactivated.Id);
        Assert.True(reactivated.IsActive);
        Assert.Equal(clock.Today, reactivated.EffectiveFrom);
        Assert.Null(reactivated.EffectiveTo);
        Assert.Equal(4, reactivated.History.Count);

        await service.SetCustomerLinkActiveAsync(reactivated.Id, new(
            reactivated.Version, false, "Prepare duplicate-pair test", "contact-user", "BillingControl.Contacts"));
        var duplicate = await Assert.ThrowsAsync<BusinessException>(() => service.AddCustomerLinkAsync(new(
            contactA.Id, customerA.Id, null, null, "contact-user", "BillingControl.Contacts")));
        Assert.Contains("reactivate", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        var inactiveContact = await service.SetContactActiveAsync(contactA.Id, new(
            contactA.Version, false, "Contact inactive", "contact-user", "BillingControl.Contacts"));
        var inactiveLink = await service.GetContactDetailsAsync(contactA.Id);
        var durable = inactiveLink!.CustomerLinks.Single(x => x.Id == reactivated.Id);
        await Assert.ThrowsAsync<BusinessException>(() => service.SetCustomerLinkActiveAsync(durable.Id, new(
            durable.Version, true, "Attempt while Contact inactive", "contact-user", "BillingControl.Contacts")));
        Assert.False(inactiveContact.IsActive);

        var customer = await db.Customers.SingleAsync(x => x.Id == customerA.Id);
        customer.IsActive = false;
        await db.SaveChangesAsync();
        var afterContact = await service.GetContactDetailsAsync(contactA.Id);
        var inactiveDurable = afterContact!.CustomerLinks.Single(x => x.Id == reactivated.Id);
        await Assert.ThrowsAsync<BusinessException>(() => service.SetCustomerLinkActiveAsync(inactiveDurable.Id, new(
            inactiveDurable.Version, true, "Attempt while Customer inactive", "contact-user", "BillingControl.Contacts")));
        Assert.DoesNotContain((await service.GetActiveCustomerOptionsAsync()).Select(x => x.Id), x => x == customerA.Id);
        _ = linkB;
        _ = linkC;
    }

    [PostgresFact]
    public async Task ContactServiceSerializesEndpointOwnershipAndLeavesFinancialWorkflowUntouched()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var billing = await new BillingService(db).Generate(
            engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var beforeBillingVersion = await db.BillingRecords.Where(x => x.Id == billing.Id).Select(x => x.Version).SingleAsync();
        var beforeWorkVersion = await db.WorkItems.Where(x => x.Id == billing.WorkItem.Id).Select(x => x.Version).SingleAsync();
        var beforeEngagementVersion = await db.Engagements.Where(x => x.Id == engagementId).Select(x => x.Version).SingleAsync();
        var beforeBillingCount = await db.BillingRecords.CountAsync();
        var beforeWorkCount = await db.WorkItems.CountAsync();
        var beforeAssignments = await db.WorkerAssignments.CountAsync();
        var beforeRequests = await db.DocumentRequests.CountAsync();
        var beforeInvoices = await db.Invoices.CountAsync();
        var beforeReceipts = await db.CustomerReceipts.CountAsync();
        var beforePayments = await db.WorkerPayments.CountAsync();
        var customerId = await db.Engagements.Where(x => x.Id == engagementId).Select(x => x.CustomerId).SingleAsync();

        var setupService = new ContactService(db, ContactTestClock());
        var firstContact = await setupService.CreateContactAsync(NewContact("Concurrent PIC A"));
        var secondContact = await setupService.CreateContactAsync(NewContact("Concurrent PIC B"));

        async Task<bool> Attempt(AppDbContext context, int contactId)
        {
            try
            {
                await new ContactService(context, ContactTestClock()).AddAddressAsync(
                    NewAddress(contactId, "+60 12-000 0000"));
                return true;
            }
            catch (BusinessException)
            {
                return false;
            }
        }

        await using var firstContext = Db();
        await using var secondContext = Db();
        var results = await Task.WhenAll(
            Attempt(firstContext, firstContact.Id),
            Attempt(secondContext, secondContact.Id));

        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, await db.ContactWhatsAppAddresses.CountAsync(x => x.NormalizedE164 == "+60120000000" && x.IsActive));

        var firstAddress = (await setupService.GetContactDetailsAsync(firstContact.Id))!.Addresses.SingleOrDefault();
        var secondAddress = (await setupService.GetContactDetailsAsync(secondContact.Id))!.Addresses.SingleOrDefault();
        var owningContactId = firstAddress is not null ? firstContact.Id : secondContact.Id;
        var owningAddress = firstAddress ?? secondAddress;
        Assert.NotNull(owningAddress);
        await setupService.RecordConsentAsync(owningAddress!.Id,
            Consent(owningAddress.Version, ContactWhatsAppConsentState.OptedIn, "client-email", "isolation-evidence"));
        await setupService.AddCustomerLinkAsync(new(
            owningContactId, customerId, "PIC", null, "contact-user", "BillingControl.Contacts"));

        Assert.Equal(beforeBillingVersion, await db.BillingRecords.Where(x => x.Id == billing.Id).Select(x => x.Version).SingleAsync());
        Assert.Equal(beforeWorkVersion, await db.WorkItems.Where(x => x.Id == billing.WorkItem.Id).Select(x => x.Version).SingleAsync());
        Assert.Equal(beforeEngagementVersion, await db.Engagements.Where(x => x.Id == engagementId).Select(x => x.Version).SingleAsync());
        Assert.Equal(beforeBillingCount, await db.BillingRecords.CountAsync());
        Assert.Equal(beforeWorkCount, await db.WorkItems.CountAsync());
        Assert.Equal(beforeAssignments, await db.WorkerAssignments.CountAsync());
        Assert.Equal(beforeRequests, await db.DocumentRequests.CountAsync());
        Assert.Equal(beforeInvoices, await db.Invoices.CountAsync());
        Assert.Equal(beforeReceipts, await db.CustomerReceipts.CountAsync());
        Assert.Equal(beforePayments, await db.WorkerPayments.CountAsync());
        Assert.Equal(WorkStatus.Upcoming, await db.WorkItems.Where(x => x.Id == billing.WorkItem.Id).Select(x => x.Status).SingleAsync());
    }
}
