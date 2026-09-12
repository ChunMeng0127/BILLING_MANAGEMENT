using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using DomainRecord = BillingControl.Models.Record;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static string ContactToken(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task<Contact> AddContactAsync(AppDbContext db, string prefix, bool isActive = true)
    {
        var contact = new Contact { Name = ContactToken(prefix), IsActive = isActive };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    private static async Task<Customer> AddContactCustomerAsync(AppDbContext db, string prefix)
    {
        var customer = new Customer { Name = ContactToken(prefix) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<ContactWhatsAppAddress> AddContactAddressAsync(
        AppDbContext db,
        int contactId,
        string number,
        bool isActive = true,
        bool isPrimary = false,
        ContactWhatsAppConsentState consentState = ContactWhatsAppConsentState.Unknown,
        DateTime? consentRecordedAt = null,
        string? consentSource = null,
        string? consentEvidenceReference = null,
        DateTime? lastOptOutAt = null,
        string? lastOptOutReason = null)
    {
        var address = new ContactWhatsAppAddress
        {
            ContactId = contactId,
            NormalizedE164 = number,
            IsActive = isActive,
            IsPrimary = isPrimary,
            ConsentState = consentState,
            ConsentRecordedAt = consentRecordedAt,
            ConsentSource = consentSource,
            ConsentEvidenceReference = consentEvidenceReference,
            LastOptOutAt = lastOptOutAt,
            LastOptOutReason = lastOptOutReason
        };
        db.ContactWhatsAppAddresses.Add(address);
        await db.SaveChangesAsync();
        return address;
    }

    private static async Task<ContactCustomerLink> AddContactLinkAsync(
        AppDbContext db,
        int contactId,
        int customerId,
        bool isActive = true,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        string? role = null,
        string? note = null)
    {
        var link = new ContactCustomerLink
        {
            ContactId = contactId,
            CustomerId = customerId,
            IsActive = isActive,
            EffectiveFrom = effectiveFrom ?? new DateOnly(2026, 1, 1),
            EffectiveTo = effectiveTo,
            Role = role,
            Note = note
        };
        db.ContactCustomerLinks.Add(link);
        await db.SaveChangesAsync();
        return link;
    }

    private static async Task AssertContactAddressSaveFailsAsync(
        int contactId,
        string number,
        Action<ContactWhatsAppAddress>? configure = null)
    {
        await using var db = Db();
        var address = new ContactWhatsAppAddress { ContactId = contactId, NormalizedE164 = number };
        configure?.Invoke(address);
        db.ContactWhatsAppAddresses.Add(address);
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    private static async Task AssertContactLinkSaveFailsAsync(
        int contactId,
        int customerId,
        Action<ContactCustomerLink>? configure = null)
    {
        await using var db = Db();
        var link = new ContactCustomerLink
        {
            ContactId = contactId,
            CustomerId = customerId,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        configure?.Invoke(link);
        db.ContactCustomerLinks.Add(link);
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task Phase4AMigrationAppliesAndContactModelHasOnlyApprovedRelationships()
    {
        await using var db = await Fresh();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260912082332_Phase4AContactPersistence", applied);

        var tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('Contacts', 'ContactWhatsAppAddresses', 'ContactCustomerLinks', 'ContactStatusHistories', 'ContactWhatsAppAddressHistories', 'ContactCustomerLinkHistories')
            """).ToListAsync();
        Assert.Equal(6, tables.Count);

        var contactEntity = db.Model.FindEntityType(typeof(Contact))!;
        Assert.Empty(contactEntity.GetForeignKeys());

        var forbiddenTypes = new[]
        {
            typeof(AppUser), typeof(BusinessParty), typeof(Manager), typeof(Engagement), typeof(WorkItem), typeof(DocumentRequest)
        };
        var addressForeignKeys = db.Model.FindEntityType(typeof(ContactWhatsAppAddress))!.GetForeignKeys();
        Assert.DoesNotContain(addressForeignKeys, fk => forbiddenTypes.Contains(fk.PrincipalEntityType.ClrType));
        var linkForeignKeys = db.Model.FindEntityType(typeof(ContactCustomerLink))!.GetForeignKeys();
        Assert.DoesNotContain(linkForeignKeys, fk => forbiddenTypes.Contains(fk.PrincipalEntityType.ClrType));
        Assert.All(addressForeignKeys.Concat(linkForeignKeys), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [PostgresFact]
    public async Task ContactAndAddressPersistWithSafeDefaultsAndRelationships()
    {
        await using var db = await Fresh();
        var contact = new Contact { Name = "Alice Tan", PreferredLanguage = "en-MY" };
        var customer = new Customer { Name = "Contact persistence customer" };
        db.AddRange(contact, customer);
        await db.SaveChangesAsync();

        var address = new ContactWhatsAppAddress { ContactId = contact.Id, NormalizedE164 = "+60123456789" };
        var link = new ContactCustomerLink { ContactId = contact.Id, CustomerId = customer.Id, EffectiveFrom = new(2026, 1, 1) };
        db.AddRange(address, link);
        await db.SaveChangesAsync();

        var occurred = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        db.AddRange(
            new ContactStatusHistory
            {
                ContactId = contact.Id, PreviousIsActive = null, NewIsActive = true,
                Action = "Created", Actor = "tester", Source = "integration", OccurredAt = occurred
            },
            new ContactWhatsAppAddressHistory
            {
                ContactWhatsAppAddressId = address.Id, PreviousConsentState = null,
                NewConsentState = ContactWhatsAppConsentState.Unknown, PreviousIsActive = null,
                NewIsActive = true, PreviousIsPrimary = null, NewIsPrimary = false,
                PreviousProviderWaId = null, NewProviderWaId = null,
                Action = "Created", Actor = "tester", Source = "integration", OccurredAt = occurred
            },
            new ContactCustomerLinkHistory
            {
                ContactCustomerLinkId = link.Id, PreviousIsActive = null, NewIsActive = true,
                PreviousEffectiveFrom = null, NewEffectiveFrom = new(2026, 1, 1),
                PreviousEffectiveTo = null, NewEffectiveTo = null,
                PreviousRole = null, NewRole = null, PreviousNote = null, NewNote = null,
                Action = "Created", Actor = "tester", Source = "integration", OccurredAt = occurred
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var savedContact = await db.Contacts.Include(x => x.Addresses).Include(x => x.CustomerLinks).AsNoTracking().SingleAsync(x => x.Id == contact.Id);
        var savedAddress = savedContact.Addresses.Single();
        var savedLink = savedContact.CustomerLinks.Single();
        Assert.False(typeof(Master).IsAssignableFrom(typeof(Contact)));
        Assert.True(savedContact.IsActive);
        Assert.Equal("en-MY", savedContact.PreferredLanguage);
        Assert.False(savedAddress.IsPrimary);
        Assert.True(savedAddress.IsActive);
        Assert.Equal(ContactWhatsAppConsentState.Unknown, savedAddress.ConsentState);
        Assert.Equal(customer.Id, savedLink.CustomerId);
        Assert.Null(savedLink.EffectiveTo);
        Assert.Single(await db.ContactStatusHistories.AsNoTracking().Where(x => x.ContactId == contact.Id).ToListAsync());
        Assert.Single(await db.ContactWhatsAppAddressHistories.AsNoTracking().Where(x => x.ContactWhatsAppAddressId == address.Id).ToListAsync());
        Assert.Single(await db.ContactCustomerLinkHistories.AsNoTracking().Where(x => x.ContactCustomerLinkId == link.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task ContactTextConstraintsRejectBlankNameAndPreferredLanguage()
    {
        await using var db = await Fresh();

        await using (var blankName = Db())
        {
            blankName.Contacts.Add(new Contact { Name = " " });
            await AssertDocumentPersistenceFailure(() => blankName.SaveChangesAsync());
        }

        await using (var blankLanguage = Db())
        {
            blankLanguage.Contacts.Add(new Contact { Name = "Valid name", PreferredLanguage = " " });
            await AssertDocumentPersistenceFailure(() => blankLanguage.SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task ContactAddressRequiresCanonicalInternationalE164()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "e164-contact");
        var accepted = await AddContactAddressAsync(db, contact.Id, "+60123456789");
        Assert.Equal("+60123456789", accepted.NormalizedE164);

        var invalidNumbers = new[]
        {
            "60123456789",
            "+0060123456789",
            "+60 123456789",
            "+60-123-456789",
            "+60(123456789)",
            "+1234567890123456"
        };
        for (var index = 0; index < invalidNumbers.Length; index++)
            await AssertContactAddressSaveFailsAsync(contact.Id, invalidNumbers[index]);
    }

    [PostgresFact]
    public async Task ContactAddressEndpointUniquenessSupportsInactiveHistoryButOneActiveOwner()
    {
        await using var db = await Fresh();
        var first = await AddContactAsync(db, "endpoint-first");
        var second = await AddContactAsync(db, "endpoint-second");
        var third = await AddContactAsync(db, "endpoint-third");
        const string number = "+60111111111";

        var firstInactive = await AddContactAddressAsync(db, first.Id, number, isActive: false);
        await AssertContactAddressSaveFailsAsync(first.Id, number, a => a.IsActive = false);
        var secondInactive = await AddContactAddressAsync(db, second.Id, number, isActive: false);
        Assert.NotEqual(firstInactive.Id, secondInactive.Id);

        var active = await AddContactAddressAsync(db, first.Id, "+60222222222");
        await AssertContactAddressSaveFailsAsync(second.Id, "+60222222222");
        var inactiveDuplicate = await AddContactAddressAsync(db, third.Id, "+60222222222", isActive: false);
        Assert.False(inactiveDuplicate.IsActive);

        await using (var reactivation = Db())
        {
            var historical = await reactivation.ContactWhatsAppAddresses.SingleAsync(x => x.Id == inactiveDuplicate.Id);
            historical.IsActive = true;
            await AssertDocumentPersistenceFailure(() => reactivation.SaveChangesAsync());
        }

        Assert.Equal(2, await db.ContactWhatsAppAddresses.CountAsync(x => x.NormalizedE164 == number));
        Assert.Equal(active.Id, await db.ContactWhatsAppAddresses.Where(x => x.NormalizedE164 == "+60222222222" && x.IsActive).Select(x => x.Id).SingleAsync());
    }

    [PostgresFact]
    public async Task ContactAddressPrimaryConstraintAllowsZeroButOnlyOneActivePrimary()
    {
        await using var db = await Fresh();
        var zeroPrimary = await AddContactAsync(db, "zero-primary");
        var ordinary = await AddContactAddressAsync(db, zeroPrimary.Id, "+60333333333");
        Assert.False(ordinary.IsPrimary);

        var onePrimary = await AddContactAsync(db, "one-primary");
        await AddContactAddressAsync(db, onePrimary.Id, "+60444444444", isPrimary: true);
        await AssertContactAddressSaveFailsAsync(onePrimary.Id, "+60555555555", a => a.IsPrimary = true);

        var inactivePrimary = await AddContactAsync(db, "inactive-primary");
        await AssertContactAddressSaveFailsAsync(inactivePrimary.Id, "+60666666666", a =>
        {
            a.IsActive = false;
            a.IsPrimary = true;
        });
    }

    [PostgresFact]
    public async Task ContactCustomerLinksEnforcePairUniquenessAndEffectiveState()
    {
        await using var db = await Fresh();
        var firstContact = await AddContactAsync(db, "link-contact-first");
        var secondContact = await AddContactAsync(db, "link-contact-second");
        var firstCustomer = await AddContactCustomerAsync(db, "link-customer-first");
        var secondCustomer = await AddContactCustomerAsync(db, "link-customer-second");
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 6, 30);

        var firstLink = await AddContactLinkAsync(db, firstContact.Id, firstCustomer.Id, effectiveFrom: from, role: "PIC");
        var secondCustomerLink = await AddContactLinkAsync(db, firstContact.Id, secondCustomer.Id, effectiveFrom: from);
        var secondContactLink = await AddContactLinkAsync(db, secondContact.Id, firstCustomer.Id, effectiveFrom: from);
        Assert.Equal(2, await db.ContactCustomerLinks.CountAsync(x => x.ContactId == firstContact.Id));
        Assert.Equal(2, await db.ContactCustomerLinks.CountAsync(x => x.CustomerId == firstCustomer.Id));
        Assert.Equal("PIC", firstLink.Role);
        Assert.NotEqual(secondCustomerLink.Id, secondContactLink.Id);

        await AssertContactLinkSaveFailsAsync(firstContact.Id, firstCustomer.Id, link => link.EffectiveFrom = from);
        await AssertContactLinkSaveFailsAsync(secondContact.Id, secondCustomer.Id, link => link.EffectiveTo = to);
        await AssertContactLinkSaveFailsAsync(secondContact.Id, secondCustomer.Id, link =>
        {
            link.IsActive = false;
            link.EffectiveTo = null;
        });
        await AssertContactLinkSaveFailsAsync(secondContact.Id, secondCustomer.Id, link =>
        {
            link.IsActive = false;
            link.EffectiveTo = new DateOnly(2025, 12, 31);
        });
        await AssertContactLinkSaveFailsAsync(secondContact.Id, secondCustomer.Id, link => link.Role = " ");

        var inactive = await AddContactLinkAsync(db, secondContact.Id, secondCustomer.Id, isActive: false, effectiveFrom: from, effectiveTo: to);
        Assert.False(inactive.IsActive);
        Assert.Equal(to, inactive.EffectiveTo);

        await using (var reactivate = Db())
        {
            var row = await reactivate.ContactCustomerLinks.SingleAsync(x => x.Id == inactive.Id);
            row.IsActive = true;
            row.EffectiveTo = null;
            await reactivate.SaveChangesAsync();
        }
        Assert.Equal(4, await db.ContactCustomerLinks.CountAsync());
        Assert.Equal(1, await db.ContactCustomerLinks.CountAsync(x => x.Id == inactive.Id && x.IsActive && x.EffectiveTo == null));
    }

    [PostgresFact]
    public async Task ContactAddressConsentStatesEnforceCurrentEvidenceWithoutErasingOptOutFacts()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "consent-contact");
        var recorded = new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc);
        var optOutAt = new DateTime(2026, 9, 11, 1, 0, 0, DateTimeKind.Utc);

        var unknown = await AddContactAddressAsync(db, contact.Id, "+60777777777", lastOptOutAt: optOutAt, lastOptOutReason: "Earlier opt-out");
        Assert.Equal(ContactWhatsAppConsentState.Unknown, unknown.ConsentState);
        Assert.Null(unknown.ConsentRecordedAt);
        Assert.Null(unknown.ConsentSource);
        Assert.Null(unknown.ConsentEvidenceReference);

        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777778", a => a.ConsentRecordedAt = recorded);
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777779", a => a.ConsentSource = "manual");
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777780", a => a.ConsentEvidenceReference = "evidence");
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777781", a => a.ConsentState = (ContactWhatsAppConsentState)99);

        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777782", a => a.ConsentState = ContactWhatsAppConsentState.OptedIn);
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777783", a =>
        {
            a.ConsentState = ContactWhatsAppConsentState.OptedIn;
            a.ConsentRecordedAt = recorded;
            a.ConsentSource = "manual";
        });
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777784", a =>
        {
            a.ConsentState = ContactWhatsAppConsentState.OptedIn;
            a.ConsentRecordedAt = recorded;
            a.ConsentSource = "manual";
            a.ConsentEvidenceReference = " ";
        });

        var optedIn = await AddContactAddressAsync(db, contact.Id, "+60777777785", consentState: ContactWhatsAppConsentState.OptedIn, consentRecordedAt: recorded, consentSource: "manual", consentEvidenceReference: "evidence", lastOptOutAt: optOutAt, lastOptOutReason: "Earlier opt-out");
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, optedIn.ConsentState);

        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777786", a =>
        {
            a.ConsentState = ContactWhatsAppConsentState.DoNotWhatsApp;
            a.ConsentRecordedAt = recorded;
            a.ConsentSource = "manual";
        });
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777787", a =>
        {
            a.ConsentState = ContactWhatsAppConsentState.DoNotWhatsApp;
            a.ConsentSource = "manual";
            a.LastOptOutAt = optOutAt;
            a.LastOptOutReason = "Opted out";
        });
        await AssertContactAddressSaveFailsAsync(contact.Id, "+60777777788", a =>
        {
            a.ConsentState = ContactWhatsAppConsentState.DoNotWhatsApp;
            a.ConsentRecordedAt = recorded;
            a.LastOptOutAt = optOutAt;
            a.LastOptOutReason = "Opted out";
        });

        var optedOut = await AddContactAddressAsync(db, contact.Id, "+60777777789", consentState: ContactWhatsAppConsentState.DoNotWhatsApp, consentRecordedAt: recorded, consentSource: "client-message", lastOptOutAt: optOutAt, lastOptOutReason: "Client opted out");
        Assert.Null(optedOut.ConsentEvidenceReference);

        await using (var reconsent = Db())
        {
            var row = await reconsent.ContactWhatsAppAddresses.SingleAsync(x => x.Id == optedOut.Id);
            row.ConsentState = ContactWhatsAppConsentState.OptedIn;
            row.ConsentRecordedAt = recorded.AddDays(1);
            row.ConsentSource = "new-consent";
            row.ConsentEvidenceReference = "new-evidence";
            await reconsent.SaveChangesAsync();
        }
        var retained = await db.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == optedOut.Id);
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, retained.ConsentState);
        Assert.Equal(optOutAt, retained.LastOptOutAt);
        Assert.Equal("Client opted out", retained.LastOptOutReason);
    }

    [PostgresFact]
    public async Task ContactAddressHistorySnapshotsConsentEvidenceAcrossTransitions()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "consent-history-contact");
        var optedInAAt = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);
        var optOutAt = new DateTime(2026, 9, 12, 4, 0, 0, DateTimeKind.Utc);
        var optedInBAt = new DateTime(2026, 9, 12, 5, 0, 0, DateTimeKind.Utc);
        var address = await AddContactAddressAsync(
            db,
            contact.Id,
            "+60912345678",
            consentState: ContactWhatsAppConsentState.OptedIn,
            consentRecordedAt: optedInAAt,
            consentSource: "source-A",
            consentEvidenceReference: "evidence-A");

        var creationHistory = new ContactWhatsAppAddressHistory
        {
            ContactWhatsAppAddressId = address.Id,
            PreviousConsentState = null,
            NewConsentState = ContactWhatsAppConsentState.OptedIn,
            NewConsentRecordedAt = optedInAAt,
            NewConsentSource = "source-A",
            NewConsentEvidenceReference = "evidence-A",
            PreviousIsActive = null,
            NewIsActive = true,
            PreviousIsPrimary = null,
            NewIsPrimary = false,
            Action = "Created",
            Actor = "tester",
            Source = "integration",
            OccurredAt = optedInAAt
        };
        db.ContactWhatsAppAddressHistories.Add(creationHistory);
        await db.SaveChangesAsync();

        ContactWhatsAppAddressHistory optOutHistory;
        await using (var optOut = Db())
        {
            var row = await optOut.ContactWhatsAppAddresses.SingleAsync(x => x.Id == address.Id);
            row.ConsentState = ContactWhatsAppConsentState.DoNotWhatsApp;
            row.ConsentRecordedAt = optOutAt;
            row.ConsentSource = "source-opt-out";
            row.ConsentEvidenceReference = null;
            row.LastOptOutAt = optOutAt;
            row.LastOptOutReason = "Client opted out";

            optOutHistory = new ContactWhatsAppAddressHistory
            {
                ContactWhatsAppAddressId = address.Id,
                PreviousConsentState = ContactWhatsAppConsentState.OptedIn,
                PreviousConsentRecordedAt = optedInAAt,
                PreviousConsentSource = "source-A",
                PreviousConsentEvidenceReference = "evidence-A",
                NewConsentState = ContactWhatsAppConsentState.DoNotWhatsApp,
                NewConsentRecordedAt = optOutAt,
                NewConsentSource = "source-opt-out",
                NewLastOptOutAt = optOutAt,
                NewLastOptOutReason = "Client opted out",
                PreviousIsActive = true,
                NewIsActive = true,
                PreviousIsPrimary = false,
                NewIsPrimary = false,
                Action = "ConsentChanged",
                Actor = "tester",
                Source = "integration",
                OccurredAt = optOutAt
            };
            optOut.ContactWhatsAppAddressHistories.Add(optOutHistory);
            await optOut.SaveChangesAsync();
        }

        ContactWhatsAppAddressHistory reOptInHistory;
        await using (var reOptIn = Db())
        {
            var row = await reOptIn.ContactWhatsAppAddresses.SingleAsync(x => x.Id == address.Id);
            row.ConsentState = ContactWhatsAppConsentState.OptedIn;
            row.ConsentRecordedAt = optedInBAt;
            row.ConsentSource = "source-B";
            row.ConsentEvidenceReference = "evidence-B";

            reOptInHistory = new ContactWhatsAppAddressHistory
            {
                ContactWhatsAppAddressId = address.Id,
                PreviousConsentState = ContactWhatsAppConsentState.DoNotWhatsApp,
                PreviousConsentRecordedAt = optOutAt,
                PreviousConsentSource = "source-opt-out",
                PreviousLastOptOutAt = optOutAt,
                PreviousLastOptOutReason = "Client opted out",
                NewConsentState = ContactWhatsAppConsentState.OptedIn,
                NewConsentRecordedAt = optedInBAt,
                NewConsentSource = "source-B",
                NewConsentEvidenceReference = "evidence-B",
                NewLastOptOutAt = optOutAt,
                NewLastOptOutReason = "Client opted out",
                PreviousIsActive = true,
                NewIsActive = true,
                PreviousIsPrimary = false,
                NewIsPrimary = false,
                Action = "ConsentChanged",
                Actor = "tester",
                Source = "integration",
                OccurredAt = optedInBAt
            };
            reOptIn.ContactWhatsAppAddressHistories.Add(reOptInHistory);
            await reOptIn.SaveChangesAsync();
        }

        var histories = await db.ContactWhatsAppAddressHistories.AsNoTracking()
            .Where(x => x.ContactWhatsAppAddressId == address.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(3, histories.Count);
        var savedCreation = histories.Single(x => x.Id == creationHistory.Id);
        Assert.Equal(optedInAAt, savedCreation.NewConsentRecordedAt);
        Assert.Equal("source-A", savedCreation.NewConsentSource);
        Assert.Equal("evidence-A", savedCreation.NewConsentEvidenceReference);

        var savedOptOut = histories.Single(x => x.Id == optOutHistory.Id);
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, savedOptOut.PreviousConsentState);
        Assert.Equal("evidence-A", savedOptOut.PreviousConsentEvidenceReference);
        Assert.Equal(ContactWhatsAppConsentState.DoNotWhatsApp, savedOptOut.NewConsentState);
        Assert.Equal(optOutAt, savedOptOut.NewConsentRecordedAt);
        Assert.Equal("source-opt-out", savedOptOut.NewConsentSource);
        Assert.Equal(optOutAt, savedOptOut.NewLastOptOutAt);
        Assert.Equal("Client opted out", savedOptOut.NewLastOptOutReason);

        var savedReOptIn = histories.Single(x => x.Id == reOptInHistory.Id);
        Assert.Equal(ContactWhatsAppConsentState.DoNotWhatsApp, savedReOptIn.PreviousConsentState);
        Assert.Equal(optOutAt, savedReOptIn.PreviousLastOptOutAt);
        Assert.Equal("Client opted out", savedReOptIn.PreviousLastOptOutReason);
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, savedReOptIn.NewConsentState);
        Assert.Equal(optedInBAt, savedReOptIn.NewConsentRecordedAt);
        Assert.Equal("source-B", savedReOptIn.NewConsentSource);
        Assert.Equal("evidence-B", savedReOptIn.NewConsentEvidenceReference);
        Assert.Equal(optOutAt, savedReOptIn.NewLastOptOutAt);
        Assert.Equal("Client opted out", savedReOptIn.NewLastOptOutReason);

        var current = await db.ContactWhatsAppAddresses.AsNoTracking().SingleAsync(x => x.Id == address.Id);
        Assert.Equal(ContactWhatsAppConsentState.OptedIn, current.ConsentState);
        Assert.Equal("evidence-B", current.ConsentEvidenceReference);
        Assert.Equal(optOutAt, current.LastOptOutAt);
        Assert.Equal("Client opted out", current.LastOptOutReason);

        async Task AssertInvalidHistoryAsync(Action<ContactWhatsAppAddressHistory> configure)
        {
            await using var invalid = Db();
            var history = new ContactWhatsAppAddressHistory
            {
                ContactWhatsAppAddressId = address.Id,
                NewConsentState = ContactWhatsAppConsentState.Unknown,
                NewIsActive = true,
                NewIsPrimary = false,
                Action = "Invalid",
                Actor = "tester",
                Source = "integration",
                OccurredAt = DateTime.UtcNow
            };
            configure(history);
            invalid.ContactWhatsAppAddressHistories.Add(history);
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }

        await AssertInvalidHistoryAsync(history =>
        {
            history.NewConsentState = ContactWhatsAppConsentState.OptedIn;
            history.NewConsentRecordedAt = optedInAAt;
            history.NewConsentSource = "missing-evidence";
        });
        await AssertInvalidHistoryAsync(history =>
        {
            history.NewConsentState = ContactWhatsAppConsentState.DoNotWhatsApp;
            history.NewConsentRecordedAt = optOutAt;
            history.NewConsentSource = "missing-opt-out-facts";
        });
        await AssertInvalidHistoryAsync(history =>
        {
            history.PreviousConsentState = ContactWhatsAppConsentState.OptedIn;
            history.PreviousConsentRecordedAt = optedInAAt;
            history.PreviousConsentSource = "source-A";
        });
        await AssertInvalidHistoryAsync(history => history.PreviousConsentSource = "state-is-null");

        await using (var immutable = Db())
        {
            var history = await immutable.ContactWhatsAppAddressHistories.SingleAsync(x => x.Id == reOptInHistory.Id);
            history.NewConsentEvidenceReference = "tampered-evidence";
            await Assert.ThrowsAsync<InvalidOperationException>(() => immutable.SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task ContactAndLinkIdentityFieldsAndPhysicalDeletesAreProtected()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "immutability-contact");
        var otherContact = await AddContactAsync(db, "immutability-other-contact");
        var customer = await AddContactCustomerAsync(db, "immutability-customer");
        var otherCustomer = await AddContactCustomerAsync(db, "immutability-other-customer");
        var address = await AddContactAddressAsync(db, contact.Id, "+60888888888");
        var link = await AddContactLinkAsync(db, contact.Id, customer.Id, effectiveFrom: new(2026, 1, 1));

        await using (var addressContact = Db())
        {
            var row = await addressContact.ContactWhatsAppAddresses.SingleAsync(x => x.Id == address.Id);
            row.ContactId = otherContact.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => addressContact.SaveChangesAsync());
        }
        await using (var addressNumber = Db())
        {
            var row = await addressNumber.ContactWhatsAppAddresses.SingleAsync(x => x.Id == address.Id);
            row.NormalizedE164 = "+60999999999";
            await Assert.ThrowsAsync<InvalidOperationException>(() => addressNumber.SaveChangesAsync());
        }
        await using (var linkContact = Db())
        {
            var row = await linkContact.ContactCustomerLinks.SingleAsync(x => x.Id == link.Id);
            row.ContactId = otherContact.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => linkContact.SaveChangesAsync());
        }
        await using (var linkCustomer = Db())
        {
            var row = await linkCustomer.ContactCustomerLinks.SingleAsync(x => x.Id == link.Id);
            row.CustomerId = otherCustomer.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => linkCustomer.SaveChangesAsync());
        }
        await using (var physicalDelete = Db())
        {
            var row = await physicalDelete.Contacts.SingleAsync(x => x.Id == contact.Id);
            physicalDelete.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => physicalDelete.SaveChangesAsync());
        }

        async Task AssertDirectDeleteFailsAsync(string table, int id)
        {
            await using var direct = Db();
            var sql = $"DELETE FROM \"{table}\" WHERE \"Id\" = {id}";
            await AssertDocumentPersistenceFailure(() => direct.Database.ExecuteSqlRawAsync(sql));
        }

        await AssertDirectDeleteFailsAsync("Contacts", contact.Id);
        await AssertDirectDeleteFailsAsync("Customers", customer.Id);
    }

    [PostgresFact]
    public async Task ContactHistoriesAreAppendOnlyAndValidateRequiredText()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "history-contact");
        var customer = await AddContactCustomerAsync(db, "history-customer");
        var address = await AddContactAddressAsync(db, contact.Id, "+60101010101");
        var link = await AddContactLinkAsync(db, contact.Id, customer.Id, effectiveFrom: new(2026, 1, 1));
        var occurred = new DateTime(2026, 9, 12, 2, 0, 0, DateTimeKind.Utc);
        var statusHistory = new ContactStatusHistory
        {
            ContactId = contact.Id, PreviousIsActive = null, NewIsActive = true,
            Action = "Created", Actor = "tester", Source = "integration", OccurredAt = occurred
        };
        var addressHistory = new ContactWhatsAppAddressHistory
        {
            ContactWhatsAppAddressId = address.Id, PreviousConsentState = null,
            NewConsentState = ContactWhatsAppConsentState.Unknown, PreviousIsActive = null,
            NewIsActive = true, PreviousIsPrimary = null, NewIsPrimary = false,
            Action = "Created", Actor = "tester", Source = "integration", OccurredAt = occurred
        };
        var linkHistory = new ContactCustomerLinkHistory
        {
            ContactCustomerLinkId = link.Id, PreviousIsActive = null, NewIsActive = true,
            PreviousEffectiveFrom = null, NewEffectiveFrom = new(2026, 1, 1),
            PreviousEffectiveTo = null, NewEffectiveTo = null,
            Action = "Created", Actor = "tester", Source = "integration", OccurredAt = occurred
        };
        db.AddRange(statusHistory, addressHistory, linkHistory);
        await db.SaveChangesAsync();

        async Task AssertHistoryModificationFails<T>(Func<AppDbContext, IQueryable<T>> query, Action<T> mutate) where T : DomainRecord
        {
            await using var context = Db();
            var row = await query(context).SingleAsync();
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertHistoryModificationFails(c => c.ContactStatusHistories.Where(x => x.Id == statusHistory.Id), x => x.Action = "Changed");
        await AssertHistoryModificationFails(c => c.ContactWhatsAppAddressHistories.Where(x => x.Id == addressHistory.Id), x => x.Action = "Changed");
        await AssertHistoryModificationFails(c => c.ContactCustomerLinkHistories.Where(x => x.Id == linkHistory.Id), x => x.Action = "Changed");

        foreach (var (table, id) in new[]
        {
            ("ContactStatusHistories", statusHistory.Id),
            ("ContactWhatsAppAddressHistories", addressHistory.Id),
            ("ContactCustomerLinkHistories", linkHistory.Id)
        })
        {
            await using var direct = Db();
            var sql = $"UPDATE \"{table}\" SET \"Action\" = ' ' WHERE \"Id\" = {id}";
            await AssertDocumentPersistenceFailure(() => direct.Database.ExecuteSqlRawAsync(sql));
        }

        await using (var deleteHistory = Db())
        {
            var row = await deleteHistory.ContactStatusHistories.SingleAsync(x => x.Id == statusHistory.Id);
            deleteHistory.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteHistory.SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task ContactPersistenceDoesNotChangeExistingBillingAndWorkflowBoundary()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var billing = new BillingService(db);
        var billingRecord = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var contact = await AddContactAsync(db, "boundary-contact");
        var customer = await db.Engagements.Where(x => x.Id == engagementId).Select(x => x.Customer).SingleAsync();
        await AddContactLinkAsync(db, contact.Id, customer.Id, effectiveFrom: new(2026, 1, 1));

        Assert.Equal(1, await db.BillingRecords.CountAsync());
        Assert.Equal(billingRecord.WorkItem.Id, await db.WorkItems.Select(x => x.Id).SingleAsync());
        Assert.Equal(WorkStatus.Upcoming, await db.WorkItems.Select(x => x.Status).SingleAsync());
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(DocumentRequest))!.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Contact));
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(WorkItem))!.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Contact));
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(WorkerAssignment))!.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Contact));
    }
}
