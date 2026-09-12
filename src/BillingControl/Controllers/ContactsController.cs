using System.Security.Claims;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.Staff)]
public sealed class ContactsController(ContactService contacts) : AppController
{
    private const string Source = "BillingControl.Contacts";

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var rows = await contacts.GetContactsAsync(search, includeInactive, cancellationToken);
        return View(new ContactListViewModel
        {
            Search = search,
            IncludeInactive = includeInactive,
            Contacts = rows
        });
    }

    [HttpGet]
    public IActionResult Create() => View(new ContactCreateViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        ContactCreateViewModel form,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return View(form);

        try
        {
            var created = await contacts.CreateContactAsync(
                new(form.Name, form.PreferredLanguage, Actor(), Source), cancellationToken);
            TempData["Success"] = "Contact created.";
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }
        catch (BusinessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(form);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return NotFound();
        var contact = await contacts.GetContactDetailsAsync(id, cancellationToken);
        return contact is null
            ? NotFound()
            : View(new ContactEditViewModel
            {
                Id = contact.Id,
                ExpectedVersion = contact.Version,
                Name = contact.Name,
                PreferredLanguage = contact.PreferredLanguage
            });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        ContactEditViewModel form,
        CancellationToken cancellationToken = default)
    {
        if (form.Id <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.Id, ValidationMessage());

        try
        {
            await contacts.UpdateContactAsync(
                form.Id,
                new(form.ExpectedVersion, form.Name, form.PreferredLanguage, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Contact profile updated.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return NotFound();
        var contact = await contacts.GetContactDetailsAsync(id, cancellationToken);
        if (contact is null) return NotFound();

        return View(new ContactDetailsViewModel
        {
            Contact = contact,
            CustomerOptions = await contacts.GetActiveCustomerOptionsAsync(cancellationToken)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        ContactActiveForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.SetContactActiveAsync(
                form.ContactId,
                new(form.ExpectedVersion, form.IsActive, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = form.IsActive ? "Contact activated." : "Contact deactivated.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAddress(
        AddAddressForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.AddAddressAsync(
                new(form.ContactId, form.PhoneNumber, Actor(), Source), cancellationToken);
            TempData["Success"] = "WhatsApp address added.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAddressPrimary(
        AddressPrimaryForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0 || form.AddressId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.SetAddressPrimaryAsync(
                form.ContactId,
                form.AddressId,
                new(form.ExpectedVersion, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Primary WhatsApp address updated.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAddressActive(
        AddressActiveForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0 || form.AddressId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.SetAddressActiveAsync(
                form.AddressId,
                new(form.ExpectedVersion, form.IsActive, form.MakePrimary, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = form.IsActive ? "WhatsApp address activated." : "WhatsApp address deactivated.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordConsent(
        ConsentForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0 || form.AddressId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.RecordConsentAsync(
                form.AddressId,
                new(form.ExpectedVersion, form.ConsentState, form.ConsentSource,
                    form.ConsentEvidenceReference, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = form.ConsentState switch
            {
                ContactWhatsAppConsentState.OptedIn => "Opted-in consent recorded.",
                ContactWhatsAppConsentState.DoNotWhatsApp => "Do Not WhatsApp record saved.",
                _ => "Consent reset to Unknown."
            };
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCustomerLink(
        AddCustomerLinkForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.AddCustomerLinkAsync(
                new(form.ContactId, form.CustomerId, form.Role, form.Note, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Customer relationship added.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCustomerLink(
        EditCustomerLinkForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0 || form.LinkId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.UpdateCustomerLinkAsync(
                form.LinkId,
                new(form.ExpectedVersion, form.Role, form.Note, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Customer relationship updated.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCustomerLinkActive(
        CustomerLinkActiveForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ContactId <= 0 || form.LinkId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ContactId, ValidationMessage());

        try
        {
            await contacts.SetCustomerLinkActiveAsync(
                form.LinkId,
                new(form.ExpectedVersion, form.IsActive, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = form.IsActive ? "Customer relationship activated." : "Customer relationship deactivated.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ContactId });
    }

    private string Actor() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.Identity?.Name
        ?? throw new InvalidOperationException("The authenticated staff identity is missing.");

    private IActionResult DetailError(int contactId, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Details), new { id = contactId });
    }

    private string ValidationMessage() =>
        ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
        ?? "Some values are missing or invalid. Check the form and try again.";
}
