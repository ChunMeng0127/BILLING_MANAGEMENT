using System.Security.Claims;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.Staff)]
public sealed class WhatsAppConversationsController(WhatsAppConversationService conversations) : AppController
{
    private const string Source = "BillingControl.WhatsAppConversations";

    [HttpGet]
    public async Task<IActionResult> Index(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var rows = await conversations.GetConversationsAsync(includeInactive, cancellationToken);
        return View(new WhatsAppConversationIndexViewModel
        {
            IncludeInactive = includeInactive,
            Conversations = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default) =>
        View(await BuildCreateModelAsync(null, cancellationToken));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        WhatsAppConversationCreateViewModel form,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return View(await BuildCreateModelAsync(form, cancellationToken));

        try
        {
            var created = await conversations.CreateConversationAsync(
                new(
                    form.Kind,
                    form.ProviderName,
                    form.BusinessEndpointKey,
                    form.ProviderAccountReference,
                    form.ProviderConversationKey,
                    form.DirectContactWhatsAppAddressId,
                    Actor(),
                    Source,
                    form.Reason),
                cancellationToken);
            TempData["Success"] = "WhatsApp conversation created. Provider and routing identity were recorded only.";
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildCreateModelAsync(form, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return NotFound();

        var model = await BuildDetailsModelAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddParticipant(
        WhatsAppConversationParticipantForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.AddParticipantAsync(
                form.ConversationId,
                new(
                    form.ExpectedConversationVersion,
                    form.ParticipantKind,
                    form.ContactId,
                    form.ContactWhatsAppAddressId,
                    form.BusinessPartyId,
                    form.ManagerId,
                    form.AppUserId,
                    form.ProviderParticipantKey,
                    form.NormalizedE164,
                    form.DisplayNameSnapshot,
                    form.Reason,
                    Actor(),
                    Source),
                cancellationToken);
            TempData["Success"] = "Participant recorded. Authorization version and review state were evaluated by the service.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateParticipant(
        WhatsAppConversationParticipantLifecycleForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0 || form.ParticipantId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.DeactivateParticipantAsync(
                form.ConversationId,
                form.ParticipantId,
                new(form.ExpectedConversationVersion, form.ExpectedParticipantVersion, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Participant deactivated. Review active Engagement scopes before any later authorization decision.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateParticipant(
        WhatsAppConversationParticipantLifecycleForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0 || form.ParticipantId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.ReactivateParticipantAsync(
                form.ConversationId,
                form.ParticipantId,
                new(form.ExpectedConversationVersion, form.ExpectedParticipantVersion, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Participant reactivated. Review active Engagement scopes before any later authorization decision.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordUnknownMembershipChange(
        WhatsAppConversationUnknownMembershipChangeForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.RecordUnknownMembershipChangeAsync(
                form.ConversationId,
                new(form.ExpectedConversationVersion, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Unknown membership change recorded. Authorization version increased and active scopes are now stale.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveScope(
        WhatsAppConversationScopeApprovalForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.ApproveEngagementScopeAsync(
                form.ConversationId,
                new(
                    form.ExpectedConversationVersion,
                    form.EngagementId,
                    form.ExpectedScopeVersion,
                    form.Reason,
                    Actor(),
                    Source),
                cancellationToken);
            TempData["Success"] = "Engagement scope approval recorded. Authorization version state was recalculated by the service.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeScope(
        WhatsAppConversationScopeRevocationForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0 || form.ScopeId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.RevokeEngagementScopeAsync(
                form.ConversationId,
                form.ScopeId,
                new(form.ExpectedConversationVersion, form.ExpectedScopeVersion, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Engagement scope revoked. The durable scope row remains available for audit history.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(
        WhatsAppConversationLifecycleForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            await conversations.DeactivateConversationAsync(
                form.ConversationId,
                new(form.ExpectedConversationVersion, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = "Conversation deactivated. Participant and scope changes are restricted while inactive.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(
        WhatsAppConversationLifecycleForm form,
        CancellationToken cancellationToken = default)
    {
        if (form.ConversationId <= 0) return NotFound();
        if (!ModelState.IsValid)
            return DetailError(form.ConversationId, ValidationMessage());

        try
        {
            var result = await conversations.ReactivateConversationAsync(
                form.ConversationId,
                new(form.ExpectedConversationVersion, form.Reason, Actor(), Source),
                cancellationToken);
            TempData["Success"] = result.Status == WhatsAppConversationStatus.Active
                ? "Conversation reactivated as Active."
                : "Conversation reactivated as NeedsAuthorizationReview; review active Engagement scopes before any later authorization decision.";
        }
        catch (BusinessException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = form.ConversationId });
    }

    private async Task<WhatsAppConversationCreateViewModel> BuildCreateModelAsync(
        WhatsAppConversationCreateViewModel? form,
        CancellationToken cancellationToken)
    {
        var options = await conversations.GetActiveDirectContactWhatsAppAddressOptionsAsync(cancellationToken);
        return new WhatsAppConversationCreateViewModel
        {
            Kind = form?.Kind ?? WhatsAppConversationKind.Direct,
            ProviderName = form?.ProviderName ?? "",
            BusinessEndpointKey = form?.BusinessEndpointKey ?? "",
            ProviderAccountReference = form?.ProviderAccountReference,
            ProviderConversationKey = form?.ProviderConversationKey ?? "",
            DirectContactWhatsAppAddressId = form?.DirectContactWhatsAppAddressId,
            Reason = form?.Reason,
            DirectAddressOptions = options
        };
    }

    private async Task<WhatsAppConversationDetailsViewModel?> BuildDetailsModelAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetConversationDetailsAsync(id, cancellationToken);
        if (conversation is null) return null;

        return new WhatsAppConversationDetailsViewModel
        {
            Conversation = conversation,
            Options = await conversations.GetManagementOptionsAsync(cancellationToken)
        };
    }

    private string Actor() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.Identity?.Name
        ?? throw new InvalidOperationException("The authenticated staff identity is missing.");

    private IActionResult DetailError(int conversationId, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Details), new { id = conversationId });
    }

    private string ValidationMessage() =>
        ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
        ?? "Some values are missing or invalid. Check the form and try again.";
}
