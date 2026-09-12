using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingControl.Services;

public sealed record ContactCreateInput(
    string Name,
    string? PreferredLanguage,
    string Actor,
    string Source);

public sealed record ContactUpdateInput(
    long ExpectedVersion,
    string Name,
    string? PreferredLanguage,
    string Actor,
    string Source);

public sealed record ContactActiveStateInput(
    long ExpectedVersion,
    bool IsActive,
    string Reason,
    string Actor,
    string Source);

public sealed record ContactAddressCreateInput(
    int ContactId,
    string PhoneNumber,
    string Actor,
    string Source);

public sealed record ContactAddressPrimaryInput(
    long ExpectedVersion,
    string Actor,
    string Source);

public sealed record ContactAddressActiveInput(
    long ExpectedVersion,
    bool IsActive,
    bool MakePrimary,
    string Reason,
    string Actor,
    string Source);

public sealed record ContactConsentInput(
    long ExpectedVersion,
    ContactWhatsAppConsentState ConsentState,
    string? ConsentSource,
    string? ConsentEvidenceReference,
    string Reason,
    string Actor,
    string Source);

public sealed record ContactCustomerLinkCreateInput(
    int ContactId,
    int CustomerId,
    string? Role,
    string? Note,
    string Actor,
    string Source);

public sealed record ContactCustomerLinkUpdateInput(
    long ExpectedVersion,
    string? Role,
    string? Note,
    string Actor,
    string Source);

public sealed record ContactCustomerLinkActiveInput(
    long ExpectedVersion,
    bool IsActive,
    string Reason,
    string Actor,
    string Source);

public sealed record ContactSummaryReadModel(
    int Id,
    string Name,
    string? PreferredLanguage,
    bool IsActive,
    long Version,
    int ActiveAddressCount,
    int ActiveCustomerLinkCount);

public sealed record ContactStatusHistoryReadModel(
    int Id,
    bool? PreviousIsActive,
    bool NewIsActive,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record ContactWhatsAppAddressHistoryReadModel(
    int Id,
    ContactWhatsAppConsentState? PreviousConsentState,
    ContactWhatsAppConsentState NewConsentState,
    DateTime? PreviousConsentRecordedAt,
    DateTime? NewConsentRecordedAt,
    string? PreviousConsentSource,
    string? NewConsentSource,
    string? PreviousConsentEvidenceReference,
    string? NewConsentEvidenceReference,
    DateTime? PreviousLastOptOutAt,
    DateTime? NewLastOptOutAt,
    string? PreviousLastOptOutReason,
    string? NewLastOptOutReason,
    bool? PreviousIsActive,
    bool NewIsActive,
    bool? PreviousIsPrimary,
    bool NewIsPrimary,
    string? PreviousProviderWaId,
    string? NewProviderWaId,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record ContactCustomerLinkHistoryReadModel(
    int Id,
    bool? PreviousIsActive,
    bool NewIsActive,
    DateOnly? PreviousEffectiveFrom,
    DateOnly NewEffectiveFrom,
    DateOnly? PreviousEffectiveTo,
    DateOnly? NewEffectiveTo,
    string? PreviousRole,
    string? NewRole,
    string? PreviousNote,
    string? NewNote,
    string Action,
    string? Reason,
    string Actor,
    string Source,
    DateTime OccurredAt,
    long Version);

public sealed record ContactWhatsAppAddressReadModel(
    int Id,
    int ContactId,
    string NormalizedE164,
    string? ProviderWaId,
    bool IsPrimary,
    bool IsActive,
    ContactWhatsAppConsentState ConsentState,
    DateTime? ConsentRecordedAt,
    string? ConsentSource,
    string? ConsentEvidenceReference,
    DateTime? LastOptOutAt,
    string? LastOptOutReason,
    long Version,
    IReadOnlyList<ContactWhatsAppAddressHistoryReadModel> History);

public sealed record ContactCustomerLinkReadModel(
    int Id,
    int ContactId,
    int CustomerId,
    string CustomerName,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Role,
    string? Note,
    long Version,
    IReadOnlyList<ContactCustomerLinkHistoryReadModel> History);

public sealed record ContactReadModel(
    int Id,
    string Name,
    string? PreferredLanguage,
    bool IsActive,
    long Version,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ContactWhatsAppAddressReadModel> Addresses,
    IReadOnlyList<ContactCustomerLinkReadModel> CustomerLinks,
    IReadOnlyList<ContactStatusHistoryReadModel> StatusHistory);

public sealed record ActiveCustomerOptionReadModel(int Id, string Name);

/// <summary>
/// Application operations for Contact/PIC persistence. Provider identifiers remain
/// provider-owned data and are intentionally not writable through this service.
/// </summary>
public sealed class ContactService(AppDbContext db, BusinessClock clock)
{
    private const string ContactConflictMessage = "Another Contact change completed first. Refresh and retry.";
    private const string AddressConflictMessage = "Another WhatsApp address change completed first. Refresh and retry.";
    private const string LinkConflictMessage = "Another Contact/Customer link change completed first. Refresh and retry.";
    private const string AddressOwnershipConflictMessage = "That WhatsApp number is already owned by another Contact, or this durable address already exists. Refresh and retry.";
    private const string LinkUniquenessConflictMessage = "That Contact/Customer relationship already exists. Reactivate the existing durable link instead of adding another row.";

    public async Task<IReadOnlyList<ContactSummaryReadModel>> GetContactsAsync(
        string? search = null,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.Contacts.AsNoTracking();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedPhone = ContactValueObjects.TryNormalizeE164(term, out var phone)
                ? phone
                : null;
            query = normalizedPhone is null
                ? query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%"))
                : query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%") ||
                                   x.Addresses.Any(a => a.NormalizedE164 == normalizedPhone));
        }

        return await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new ContactSummaryReadModel(
                x.Id,
                x.Name,
                x.PreferredLanguage,
                x.IsActive,
                x.Version,
                x.Addresses.Count(a => a.IsActive),
                x.CustomerLinks.Count(l => l.IsActive)))
            .ToListAsync(cancellationToken);
    }

    public async Task<ContactReadModel?> GetContactDetailsAsync(
        int contactId,
        CancellationToken cancellationToken = default)
    {
        if (contactId <= 0)
            return null;

        var contact = await ContactDetailsQuery()
            .SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken);
        return contact is null ? null : ToReadModel(contact);
    }

    public async Task<IReadOnlyList<ActiveCustomerOptionReadModel>> GetActiveCustomerOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.Customers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new ActiveCustomerOptionReadModel(x.Id, x.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<ContactReadModel> CreateContactAsync(
        ContactCreateInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Contact details are required.");
        var name = NormalizeName(input.Name);
        var language = ContactValueObjects.NormalizePreferredLanguage(input.PreferredLanguage);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;

        var contactId = await InSerializableTransactionAsync(async () =>
        {
            var contact = new Contact
            {
                Name = name,
                PreferredLanguage = language,
                IsActive = true
            };
            var history = new ContactStatusHistory
            {
                Contact = contact,
                PreviousIsActive = null,
                NewIsActive = true,
                Action = "Created",
                Actor = actor,
                Source = source,
                OccurredAt = occurredAt
            };

            db.Contacts.Add(contact);
            db.ContactStatusHistories.Add(history);
            await db.SaveChangesAsync(cancellationToken);
            return contact.Id;
        }, ContactConflictMessage, cancellationToken);

        return await ReloadContactAsync(contactId, cancellationToken);
    }

    public async Task<ContactReadModel> UpdateContactAsync(
        int contactId,
        ContactUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Contact details are required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected Contact version is required.");
        var name = NormalizeName(input.Name);
        var language = ContactValueObjects.NormalizePreferredLanguage(input.PreferredLanguage);
        _ = NormalizeActor(input.Actor);
        _ = NormalizeSource(input.Source);

        await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("Contacts", contactId, cancellationToken,
                "The Contact was not found.");
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            EnsureVersion(contact.Version, input.ExpectedVersion, ContactConflictMessage);

            if (contact.Name != name || contact.PreferredLanguage != language)
            {
                contact.Name = name;
                contact.PreferredLanguage = language;
                await db.SaveChangesAsync(cancellationToken);
            }

            return true;
        }, ContactConflictMessage, cancellationToken);

        return await ReloadContactAsync(contactId, cancellationToken);
    }

    public async Task<ContactReadModel> SetContactActiveAsync(
        int contactId,
        ContactActiveStateInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Contact state details are required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected Contact version is required.");
        var reason = NormalizeReason(input.Reason);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;

        await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("Contacts", contactId, cancellationToken,
                "The Contact was not found.");
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            EnsureVersion(contact.Version, input.ExpectedVersion, ContactConflictMessage);

            if (contact.IsActive == input.IsActive)
                return true;

            var previous = contact.IsActive;
            contact.IsActive = input.IsActive;
            db.ContactStatusHistories.Add(new ContactStatusHistory
            {
                ContactId = contact.Id,
                PreviousIsActive = previous,
                NewIsActive = contact.IsActive,
                Action = input.IsActive ? "Activated" : "Deactivated",
                Reason = reason,
                Actor = actor,
                Source = source,
                OccurredAt = occurredAt
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, ContactConflictMessage, cancellationToken);

        return await ReloadContactAsync(contactId, cancellationToken);
    }

    public async Task<ContactWhatsAppAddressReadModel> AddAddressAsync(
        ContactAddressCreateInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Address details are required.");
        Finance.Require(input.ContactId > 0, "A valid Contact is required.");
        var normalizedNumber = ContactValueObjects.NormalizeE164(input.PhoneNumber);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;

        var addressId = await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("Contacts", input.ContactId, cancellationToken,
                "The Contact was not found.");
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == input.ContactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            Finance.Require(contact.IsActive, "An address can only be added to an active Contact.");

            var hasActiveAddress = await db.ContactWhatsAppAddresses
                .AnyAsync(x => x.ContactId == contact.Id && x.IsActive, cancellationToken);
            var address = new ContactWhatsAppAddress
            {
                ContactId = contact.Id,
                NormalizedE164 = normalizedNumber,
                IsActive = true,
                IsPrimary = !hasActiveAddress,
                ConsentState = ContactWhatsAppConsentState.Unknown
            };
            db.ContactWhatsAppAddresses.Add(address);
            db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                address,
                previous: null,
                next: Snapshot(address),
                action: "Created",
                reason: null,
                actor,
                source,
                occurredAt));
            await db.SaveChangesAsync(cancellationToken);
            return address.Id;
        }, AddressOwnershipConflictMessage, cancellationToken);

        return await ReloadAddressAsync(addressId, cancellationToken);
    }

    public async Task<ContactReadModel> SetAddressPrimaryAsync(
        int contactId,
        int addressId,
        ContactAddressPrimaryInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Primary-address details are required.");
        Finance.Require(contactId > 0 && addressId > 0, "A valid Contact and address are required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected address version is required.");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;

        await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("Contacts", contactId, cancellationToken,
                "The Contact was not found.");
            await LockAddressSetAsync(contactId, cancellationToken);
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            Finance.Require(contact.IsActive, "A primary address can only be selected for an active Contact.");

            var address = await db.ContactWhatsAppAddresses
                .SingleOrDefaultAsync(x => x.Id == addressId && x.ContactId == contactId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp address was not found for this Contact.");
            EnsureVersion(address.Version, input.ExpectedVersion, AddressConflictMessage);
            Finance.Require(address.IsActive, "Only an active WhatsApp address can be primary.");

            if (address.IsPrimary)
                return true;

            var addresses = await db.ContactWhatsAppAddresses
                .Where(x => x.ContactId == contactId && x.IsActive)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var existing in addresses.Where(x => x.IsPrimary && x.Id != address.Id))
            {
                var previous = Snapshot(existing);
                existing.IsPrimary = false;
                db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                    existing, previous, Snapshot(existing), "PrimaryCleared", null, actor, source, occurredAt));
            }

            var targetPrevious = Snapshot(address);
            address.IsPrimary = true;
            db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                address, targetPrevious, Snapshot(address), "PrimarySet", null, actor, source, occurredAt));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, AddressConflictMessage, cancellationToken);

        return await ReloadContactAsync(contactId, cancellationToken);
    }

    public async Task<ContactWhatsAppAddressReadModel> SetAddressActiveAsync(
        int addressId,
        ContactAddressActiveInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Address state details are required.");
        Finance.Require(addressId > 0, "A valid WhatsApp address is required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected address version is required.");
        var reason = NormalizeReason(input.Reason);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;

        await InSerializableTransactionAsync(async () =>
        {
            var contactId = await GetAddressContactIdAsync(addressId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp address was not found.");
            await LockRowAsync("Contacts", contactId, cancellationToken,
                "The Contact was not found.");
            await LockAddressSetAsync(contactId, cancellationToken);
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            var address = await db.ContactWhatsAppAddresses
                .SingleOrDefaultAsync(x => x.Id == addressId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp address was not found.");
            EnsureVersion(address.Version, input.ExpectedVersion, AddressConflictMessage);

            if (!input.IsActive)
            {
                if (!address.IsActive)
                    return true;

                var previous = Snapshot(address);
                address.IsActive = false;
                address.IsPrimary = false;
                db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                    address, previous, Snapshot(address), "Deactivated", reason, actor, source, occurredAt));
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }

            Finance.Require(contact.IsActive, "An address can only be activated for an active Contact.");
            if (address.IsActive && !input.MakePrimary)
                return true;

            var activeAddresses = await db.ContactWhatsAppAddresses
                .Where(x => x.ContactId == contactId && x.IsActive)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

            var changed = false;
            if (!address.IsActive)
            {
                var previous = Snapshot(address);
                address.IsActive = true;
                address.IsPrimary = false;
                changed = true;

                if (input.MakePrimary)
                    ClearOtherPrimaries(activeAddresses, address.Id, reason, actor, source, occurredAt, ref changed);

                if (input.MakePrimary)
                    address.IsPrimary = true;

                db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                    address, previous, Snapshot(address), "Activated", reason, actor, source, occurredAt));
            }
            else if (input.MakePrimary && !address.IsPrimary)
            {
                ClearOtherPrimaries(activeAddresses, address.Id, reason, actor, source, occurredAt, ref changed);
                var previous = Snapshot(address);
                address.IsPrimary = true;
                changed = true;
                db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                    address, previous, Snapshot(address), "PrimarySet", reason, actor, source, occurredAt));
            }

            if (changed)
                await db.SaveChangesAsync(cancellationToken);
            return true;
        }, AddressConflictMessage, cancellationToken);

        return await ReloadAddressAsync(addressId, cancellationToken);
    }

    public async Task<ContactWhatsAppAddressReadModel> RecordConsentAsync(
        int addressId,
        ContactConsentInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Consent details are required.");
        Finance.Require(addressId > 0, "A valid WhatsApp address is required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected address version is required.");
        Finance.Require(Enum.IsDefined(input.ConsentState), "The consent state is invalid.");
        var auditReason = NormalizeReason(input.Reason);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var consentSource = NormalizeOptionalText(input.ConsentSource, 254, "Consent source");
        var consentEvidence = NormalizeOptionalText(input.ConsentEvidenceReference, 254, "Consent evidence reference");
        var occurredAt = clock.UtcNow;

        await InSerializableTransactionAsync(async () =>
        {
            var contactId = await GetAddressContactIdAsync(addressId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp address was not found.");
            await LockRowAsync("Contacts", contactId, cancellationToken,
                "The Contact was not found.");
            await LockAddressSetAsync(contactId, cancellationToken);
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            var address = await db.ContactWhatsAppAddresses
                .SingleOrDefaultAsync(x => x.Id == addressId, cancellationToken)
                ?? throw new BusinessException("The WhatsApp address was not found.");
            EnsureVersion(address.Version, input.ExpectedVersion, AddressConflictMessage);

            var previous = Snapshot(address);
            switch (input.ConsentState)
            {
                case ContactWhatsAppConsentState.Unknown:
                    address.ConsentState = ContactWhatsAppConsentState.Unknown;
                    address.ConsentRecordedAt = null;
                    address.ConsentSource = null;
                    address.ConsentEvidenceReference = null;
                    break;

                case ContactWhatsAppConsentState.OptedIn:
                    Finance.Require(contact.IsActive && address.IsActive,
                        "Opt-in consent can only be recorded for an active Contact and active address.");
                    Finance.Require(!string.IsNullOrWhiteSpace(consentSource),
                        "Consent source is required for OptedIn.");
                    Finance.Require(!string.IsNullOrWhiteSpace(consentEvidence),
                        "Consent evidence reference is required for OptedIn.");
                    address.ConsentState = ContactWhatsAppConsentState.OptedIn;
                    address.ConsentRecordedAt = occurredAt;
                    address.ConsentSource = consentSource;
                    address.ConsentEvidenceReference = consentEvidence;
                    break;

                case ContactWhatsAppConsentState.DoNotWhatsApp:
                    Finance.Require(!string.IsNullOrWhiteSpace(consentSource),
                        "Consent source is required for DoNotWhatsApp.");
                    address.ConsentState = ContactWhatsAppConsentState.DoNotWhatsApp;
                    address.ConsentRecordedAt = occurredAt;
                    address.ConsentSource = consentSource;
                    address.ConsentEvidenceReference = consentEvidence;
                    address.LastOptOutAt = occurredAt;
                    address.LastOptOutReason = auditReason;
                    break;
            }

            var next = Snapshot(address);
            if (SnapshotsEqual(previous, next) &&
                input.ConsentState != ContactWhatsAppConsentState.DoNotWhatsApp)
                return true;

            db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                address,
                previous,
                next,
                input.ConsentState switch
                {
                    ContactWhatsAppConsentState.Unknown => "ConsentReset",
                    ContactWhatsAppConsentState.OptedIn => "ConsentRecorded",
                    ContactWhatsAppConsentState.DoNotWhatsApp => "OptedOut",
                    _ => "ConsentChanged"
                },
                auditReason,
                actor,
                source,
                occurredAt));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, AddressConflictMessage, cancellationToken);

        return await ReloadAddressAsync(addressId, cancellationToken);
    }

    public async Task<ContactCustomerLinkReadModel> AddCustomerLinkAsync(
        ContactCustomerLinkCreateInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Customer-link details are required.");
        Finance.Require(input.ContactId > 0 && input.CustomerId > 0,
            "A valid Contact and Customer are required.");
        var role = NormalizeOptionalText(input.Role, 120, "Role");
        var note = NormalizeOptionalText(input.Note, 2000, "Note");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;
        var effectiveFrom = clock.Today;

        var linkId = await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("Contacts", input.ContactId, cancellationToken,
                "The Contact was not found.");
            await LockRowAsync("Customers", input.CustomerId, cancellationToken,
                "The Customer was not found.");
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == input.ContactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == input.CustomerId, cancellationToken)
                ?? throw new BusinessException("The Customer was not found.");
            Finance.Require(contact.IsActive, "A customer link can only be added to an active Contact.");
            Finance.Require(customer.IsActive, "A customer link can only target an active Customer.");

            var existing = await db.ContactCustomerLinks
                .SingleOrDefaultAsync(x => x.ContactId == input.ContactId && x.CustomerId == input.CustomerId,
                    cancellationToken);
            Finance.Require(existing is null,
                existing is not null && !existing.IsActive
                    ? "This Contact/Customer link already exists but is inactive. Reactivate the existing durable link."
                    : "This Contact/Customer link already exists.");

            var link = new ContactCustomerLink
            {
                ContactId = contact.Id,
                CustomerId = customer.Id,
                IsActive = true,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
                Role = role,
                Note = note
            };
            db.ContactCustomerLinks.Add(link);
            db.ContactCustomerLinkHistories.Add(CreateLinkHistory(
                link,
                previous: null,
                next: Snapshot(link),
                action: "Created",
                reason: null,
                actor,
                source,
                occurredAt));
            await db.SaveChangesAsync(cancellationToken);
            return link.Id;
        }, LinkUniquenessConflictMessage, cancellationToken);

        return await ReloadLinkAsync(linkId, cancellationToken);
    }

    public async Task<ContactCustomerLinkReadModel> UpdateCustomerLinkAsync(
        int linkId,
        ContactCustomerLinkUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Customer-link details are required.");
        Finance.Require(linkId > 0, "A valid customer link is required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected customer-link version is required.");
        var role = NormalizeOptionalText(input.Role, 120, "Role");
        var note = NormalizeOptionalText(input.Note, 2000, "Note");
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;

        await InSerializableTransactionAsync(async () =>
        {
            await LockRowAsync("ContactCustomerLinks", linkId, cancellationToken,
                "The Contact/Customer link was not found.");
            var link = await db.ContactCustomerLinks.SingleOrDefaultAsync(x => x.Id == linkId, cancellationToken)
                ?? throw new BusinessException("The Contact/Customer link was not found.");
            EnsureVersion(link.Version, input.ExpectedVersion, LinkConflictMessage);

            if (link.Role == role && link.Note == note)
                return true;

            var previous = Snapshot(link);
            link.Role = role;
            link.Note = note;
            db.ContactCustomerLinkHistories.Add(CreateLinkHistory(
                link, previous, Snapshot(link), "MetadataUpdated", null, actor, source, occurredAt));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, LinkConflictMessage, cancellationToken);

        return await ReloadLinkAsync(linkId, cancellationToken);
    }

    public async Task<ContactCustomerLinkReadModel> SetCustomerLinkActiveAsync(
        int linkId,
        ContactCustomerLinkActiveInput input,
        CancellationToken cancellationToken = default)
    {
        input = RequireInput(input, "Customer-link state details are required.");
        Finance.Require(linkId > 0, "A valid customer link is required.");
        Finance.Require(input.ExpectedVersion > 0, "The expected customer-link version is required.");
        var reason = NormalizeReason(input.Reason);
        var actor = NormalizeActor(input.Actor);
        var source = NormalizeSource(input.Source);
        var occurredAt = clock.UtcNow;
        var today = clock.Today;

        await InSerializableTransactionAsync(async () =>
        {
            var identity = await db.ContactCustomerLinks.AsNoTracking()
                .Where(x => x.Id == linkId)
                .Select(x => new { x.ContactId, x.CustomerId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new BusinessException("The Contact/Customer link was not found.");
            await LockRowAsync("Contacts", identity.ContactId, cancellationToken,
                "The Contact was not found.");
            await LockRowAsync("Customers", identity.CustomerId, cancellationToken,
                "The Customer was not found.");
            await LockRowAsync("ContactCustomerLinks", linkId, cancellationToken,
                "The Contact/Customer link was not found.");

            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == identity.ContactId, cancellationToken)
                ?? throw new BusinessException("The Contact was not found.");
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == identity.CustomerId, cancellationToken)
                ?? throw new BusinessException("The Customer was not found.");
            var link = await db.ContactCustomerLinks.SingleOrDefaultAsync(x => x.Id == linkId, cancellationToken)
                ?? throw new BusinessException("The Contact/Customer link was not found.");
            EnsureVersion(link.Version, input.ExpectedVersion, LinkConflictMessage);

            if (link.IsActive == input.IsActive)
                return true;

            if (input.IsActive)
            {
                Finance.Require(contact.IsActive, "A link can only be activated for an active Contact.");
                Finance.Require(customer.IsActive, "A link can only be activated for an active Customer.");
            }

            var previous = Snapshot(link);
            link.IsActive = input.IsActive;
            if (input.IsActive)
            {
                link.EffectiveFrom = today;
                link.EffectiveTo = null;
            }
            else
            {
                link.EffectiveTo = today;
            }

            db.ContactCustomerLinkHistories.Add(CreateLinkHistory(
                link,
                previous,
                Snapshot(link),
                input.IsActive ? "Activated" : "Deactivated",
                reason,
                actor,
                source,
                occurredAt));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, LinkConflictMessage, cancellationToken);

        return await ReloadLinkAsync(linkId, cancellationToken);
    }

    private IQueryable<Contact> ContactDetailsQuery() => db.Contacts
        .AsNoTracking()
        .AsSplitQuery()
        .Include(x => x.Addresses)
            .ThenInclude(x => x.History)
        .Include(x => x.CustomerLinks)
            .ThenInclude(x => x.Customer)
        .Include(x => x.CustomerLinks)
            .ThenInclude(x => x.History)
        .Include(x => x.StatusHistory);

    private async Task<ContactReadModel> ReloadContactAsync(
        int contactId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await GetContactDetailsAsync(contactId, cancellationToken)
            ?? throw new BusinessException("The Contact was not found.");
    }

    private async Task<ContactWhatsAppAddressReadModel> ReloadAddressAsync(
        int addressId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var contact = await ContactDetailsQuery()
            .Where(x => x.Addresses.Any(a => a.Id == addressId))
            .SingleOrDefaultAsync(cancellationToken);
        var address = contact?.Addresses.SingleOrDefault(x => x.Id == addressId);
        return address is null
            ? throw new BusinessException("The WhatsApp address was not found.")
            : ToReadModel(address);
    }

    private async Task<ContactCustomerLinkReadModel> ReloadLinkAsync(
        int linkId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var contact = await ContactDetailsQuery()
            .Where(x => x.CustomerLinks.Any(l => l.Id == linkId))
            .SingleOrDefaultAsync(cancellationToken);
        var link = contact?.CustomerLinks.SingleOrDefault(x => x.Id == linkId);
        return link is null
            ? throw new BusinessException("The Contact/Customer link was not found.")
            : ToReadModel(link);
    }

    private async Task<int?> GetAddressContactIdAsync(int addressId, CancellationToken cancellationToken) =>
        await db.ContactWhatsAppAddresses.AsNoTracking()
            .Where(x => x.Id == addressId)
            .Select(x => (int?)x.ContactId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task LockAddressSetAsync(int contactId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\" FROM \"ContactWhatsAppAddresses\" WHERE \"ContactId\" = @contact_id ORDER BY \"Id\" FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "contact_id";
        parameter.Value = contactId;
        command.Parameters.Add(parameter);
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) { }
    }

    private async Task LockRowAsync(
        string table,
        int id,
        CancellationToken cancellationToken,
        string notFoundMessage)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Id\" FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        Finance.Require(await command.ExecuteScalarAsync(cancellationToken) is not null, notFoundMessage);
    }

    private async Task<T> InSerializableTransactionAsync<T>(
        Func<Task<T>> action,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(conflictMessage, ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(conflictMessage, ex);
        }
        catch (Exception ex) when (FinancialTransaction.IsSerializationConflict(ex))
        {
            db.ChangeTracker.Clear();
            throw new BusinessException(conflictMessage, ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static void EnsureVersion(long actual, long expected, string conflictMessage) =>
        Finance.Require(actual == expected, conflictMessage);

    private static string NormalizeName(string? value)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(value), "Contact name is required.");
        var normalized = value!.Trim();
        Finance.Require(normalized.Length <= 160, "Contact name must be 160 characters or fewer.");
        return normalized;
    }

    private static T RequireInput<T>(T? input, string message)
        where T : class => input ?? throw new BusinessException(message);

    private static string? NormalizeOptionalText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        Finance.Require(normalized.Length <= maxLength, $"{label} must be {maxLength} characters or fewer.");
        return normalized;
    }

    private static string NormalizeActor(string? actor)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(actor), "An actor is required for Contact operations.");
        var normalized = actor!.Trim();
        Finance.Require(normalized.Length <= 254, "Actor must be 254 characters or fewer.");
        return normalized;
    }

    private static string NormalizeSource(string? source)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(source), "A source is required for Contact operations.");
        var normalized = source!.Trim();
        Finance.Require(normalized.Length <= 80, "Source must be 80 characters or fewer.");
        return normalized;
    }

    private static string NormalizeReason(string? reason)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(reason), "A reason is required for this Contact operation.");
        var normalized = reason!.Trim();
        Finance.Require(normalized.Length <= 2000, "Reason must be 2,000 characters or fewer.");
        return normalized;
    }

    private sealed record AddressSnapshot(
        ContactWhatsAppConsentState ConsentState,
        DateTime? ConsentRecordedAt,
        string? ConsentSource,
        string? ConsentEvidenceReference,
        DateTime? LastOptOutAt,
        string? LastOptOutReason,
        bool IsActive,
        bool IsPrimary,
        string? ProviderWaId);

    private sealed record LinkSnapshot(
        bool IsActive,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string? Role,
        string? Note);

    private static AddressSnapshot Snapshot(ContactWhatsAppAddress address) => new(
        address.ConsentState,
        address.ConsentRecordedAt,
        address.ConsentSource,
        address.ConsentEvidenceReference,
        address.LastOptOutAt,
        address.LastOptOutReason,
        address.IsActive,
        address.IsPrimary,
        address.ProviderWaId);

    private static LinkSnapshot Snapshot(ContactCustomerLink link) => new(
        link.IsActive,
        link.EffectiveFrom,
        link.EffectiveTo,
        link.Role,
        link.Note);

    private static bool SnapshotsEqual(AddressSnapshot left, AddressSnapshot right) =>
        left == right;

    private static ContactWhatsAppAddressHistory CreateAddressHistory(
        ContactWhatsAppAddress address,
        AddressSnapshot? previous,
        AddressSnapshot next,
        string action,
        string? reason,
        string actor,
        string source,
        DateTime occurredAt) => new()
        {
            ContactWhatsAppAddressId = address.Id,
            ContactWhatsAppAddress = address,
            PreviousConsentState = previous?.ConsentState,
            NewConsentState = next.ConsentState,
            PreviousConsentRecordedAt = previous?.ConsentRecordedAt,
            NewConsentRecordedAt = next.ConsentRecordedAt,
            PreviousConsentSource = previous?.ConsentSource,
            NewConsentSource = next.ConsentSource,
            PreviousConsentEvidenceReference = previous?.ConsentEvidenceReference,
            NewConsentEvidenceReference = next.ConsentEvidenceReference,
            PreviousLastOptOutAt = previous?.LastOptOutAt,
            NewLastOptOutAt = next.LastOptOutAt,
            PreviousLastOptOutReason = previous?.LastOptOutReason,
            NewLastOptOutReason = next.LastOptOutReason,
            PreviousIsActive = previous?.IsActive,
            NewIsActive = next.IsActive,
            PreviousIsPrimary = previous?.IsPrimary,
            NewIsPrimary = next.IsPrimary,
            PreviousProviderWaId = previous?.ProviderWaId,
            NewProviderWaId = next.ProviderWaId,
            Action = action,
            Reason = reason,
            Actor = actor,
            Source = source,
            OccurredAt = occurredAt
        };

    private static ContactCustomerLinkHistory CreateLinkHistory(
        ContactCustomerLink link,
        LinkSnapshot? previous,
        LinkSnapshot next,
        string action,
        string? reason,
        string actor,
        string source,
        DateTime occurredAt) => new()
        {
            ContactCustomerLinkId = link.Id,
            ContactCustomerLink = link,
            PreviousIsActive = previous?.IsActive,
            NewIsActive = next.IsActive,
            PreviousEffectiveFrom = previous?.EffectiveFrom,
            NewEffectiveFrom = next.EffectiveFrom,
            PreviousEffectiveTo = previous?.EffectiveTo,
            NewEffectiveTo = next.EffectiveTo,
            PreviousRole = previous?.Role,
            NewRole = next.Role,
            PreviousNote = previous?.Note,
            NewNote = next.Note,
            Action = action,
            Reason = reason,
            Actor = actor,
            Source = source,
            OccurredAt = occurredAt
        };

    private void ClearOtherPrimaries(
        IEnumerable<ContactWhatsAppAddress> addresses,
        int targetAddressId,
        string? reason,
        string actor,
        string source,
        DateTime occurredAt,
        ref bool changed)
    {
        foreach (var existing in addresses.Where(x => x.IsPrimary && x.Id != targetAddressId))
        {
            var previous = Snapshot(existing);
            existing.IsPrimary = false;
            changed = true;
            db.ContactWhatsAppAddressHistories.Add(CreateAddressHistory(
                existing, previous, Snapshot(existing), "PrimaryCleared", reason, actor, source, occurredAt));
        }
    }

    private static ContactSummaryReadModel ToSummary(Contact contact) => new(
        contact.Id,
        contact.Name,
        contact.PreferredLanguage,
        contact.IsActive,
        contact.Version,
        contact.Addresses.Count(x => x.IsActive),
        contact.CustomerLinks.Count(x => x.IsActive));

    private static ContactReadModel ToReadModel(Contact contact) => new(
        contact.Id,
        contact.Name,
        contact.PreferredLanguage,
        contact.IsActive,
        contact.Version,
        contact.CreatedAt,
        contact.UpdatedAt,
        contact.Addresses
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.IsPrimary)
            .ThenBy(x => x.NormalizedE164)
            .ThenBy(x => x.Id)
            .Select(ToReadModel)
            .ToArray(),
        contact.CustomerLinks
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Customer?.Name)
            .ThenBy(x => x.CustomerId)
            .ThenBy(x => x.Id)
            .Select(ToReadModel)
            .ToArray(),
        contact.StatusHistory
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Select(x => new ContactStatusHistoryReadModel(
                x.Id,
                x.PreviousIsActive,
                x.NewIsActive,
                x.Action,
                x.Reason,
                x.Actor,
                x.Source,
                x.OccurredAt,
                x.Version))
            .ToArray());

    private static ContactWhatsAppAddressReadModel ToReadModel(ContactWhatsAppAddress address) => new(
        address.Id,
        address.ContactId,
        address.NormalizedE164,
        address.ProviderWaId,
        address.IsPrimary,
        address.IsActive,
        address.ConsentState,
        address.ConsentRecordedAt,
        address.ConsentSource,
        address.ConsentEvidenceReference,
        address.LastOptOutAt,
        address.LastOptOutReason,
        address.Version,
        address.History
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Select(x => new ContactWhatsAppAddressHistoryReadModel(
                x.Id,
                x.PreviousConsentState,
                x.NewConsentState,
                x.PreviousConsentRecordedAt,
                x.NewConsentRecordedAt,
                x.PreviousConsentSource,
                x.NewConsentSource,
                x.PreviousConsentEvidenceReference,
                x.NewConsentEvidenceReference,
                x.PreviousLastOptOutAt,
                x.NewLastOptOutAt,
                x.PreviousLastOptOutReason,
                x.NewLastOptOutReason,
                x.PreviousIsActive,
                x.NewIsActive,
                x.PreviousIsPrimary,
                x.NewIsPrimary,
                x.PreviousProviderWaId,
                x.NewProviderWaId,
                x.Action,
                x.Reason,
                x.Actor,
                x.Source,
                x.OccurredAt,
                x.Version))
            .ToArray());

    private static ContactCustomerLinkReadModel ToReadModel(ContactCustomerLink link) => new(
        link.Id,
        link.ContactId,
        link.CustomerId,
        link.Customer?.Name ?? "",
        link.IsActive,
        link.EffectiveFrom,
        link.EffectiveTo,
        link.Role,
        link.Note,
        link.Version,
        link.History
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Select(x => new ContactCustomerLinkHistoryReadModel(
                x.Id,
                x.PreviousIsActive,
                x.NewIsActive,
                x.PreviousEffectiveFrom,
                x.NewEffectiveFrom,
                x.PreviousEffectiveTo,
                x.NewEffectiveTo,
                x.PreviousRole,
                x.NewRole,
                x.PreviousNote,
                x.NewNote,
                x.Action,
                x.Reason,
                x.Actor,
                x.Source,
                x.OccurredAt,
                x.Version))
            .ToArray());
}
