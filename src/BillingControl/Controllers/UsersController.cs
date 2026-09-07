using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class UsersController(UserManager<AppUser> users, AppDbContext db, AccessScope access) : AppController
{
    private async Task Choices()
    {
        ViewBag.Firms = await db.BusinessParties.OrderBy(x => x.Name).ToListAsync();
        ViewBag.Managers = await db.Managers.OrderBy(x => x.Name).ToListAsync();
        ViewBag.Workers = await db.Workers.OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<IActionResult> Index()
    {
        var all = await users.Users.Include(x => x.BusinessParty).Include(x => x.Manager).Include(x => x.Worker).OrderBy(x => x.Email).ToListAsync();
        var roles = new Dictionary<string, string>();
        foreach (var u in all) roles[u.Id] = (await users.GetRolesAsync(u)).SingleOrDefault() ?? "Invalid";
        ViewBag.Roles = roles;
        await Choices();
        return View(all);
    }

    public async Task<IActionResult> Create()
    {
        await Choices();
        return View(new UserForm());
    }

    [HttpPost]
    public async Task<IActionResult> Create(UserForm form)
    {
        if (!AppRoles.All.Contains(form.Role)) ModelState.AddModelError("Role", "Select a valid role.");
        if (!ModelState.IsValid) { await Choices(); return View(form); }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = new AppUser { Email = form.Email, UserName = form.Email, EmailConfirmed = true };
        try { await access.ApplyUserLinkAsync(user, form.Role, form.BusinessPartyId, form.ManagerId, form.WorkerId); }
        catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); await Choices(); return View(form); }
        var result = await users.CreateAsync(user, form.Password);
        if (!result.Succeeded) { foreach (var e in result.Errors) ModelState.AddModelError("", e.Description); await Choices(); return View(form); }
        Seed.Check(await users.AddToRoleAsync(user, form.Role));
        await tx.CommitAsync();
        TempData["Success"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Update(string id, string role, bool active, int? businessPartyId, int? managerId, int? workerId)
    {
        Finance.Require(AppRoles.All.Contains(role), "Invalid role.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await users.FindByIdAsync(id); if (user == null) return NotFound();
        Finance.Require(id != users.GetUserId(User) || (active && role == AppRoles.Admin), "You cannot disable or demote your own administrator account.");
        if (await users.IsInRoleAsync(user, AppRoles.Admin) && (!active || role != AppRoles.Admin)) Finance.Require((await users.GetUsersInRoleAsync(AppRoles.Admin)).Count(x => x.IsActive) > 1, "Keep at least one active administrator.");
        await access.ApplyUserLinkAsync(user, role, businessPartyId, managerId, workerId);
        user.IsActive = active;
        Seed.Check(await users.UpdateAsync(user));
        var old = await users.GetRolesAsync(user);
        if (!old.Contains(role))
        {
            Seed.Check(await users.RemoveFromRolesAsync(user, old));
            Seed.Check(await users.AddToRoleAsync(user, role));
        }
        Seed.Check(await users.UpdateSecurityStampAsync(user));
        await tx.CommitAsync();
        TempData["Success"] = "Access and entity scope updated; existing sessions revoked.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(string id, string password)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(password), "Enter a new password.");
        var user = await users.FindByIdAsync(id); if (user == null) return NotFound();
        var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password);
        if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(x => x.Description)));
        Seed.Check(await users.ResetAccessFailedCountAsync(user)); Seed.Check(await users.SetLockoutEndDateAsync(user, null));
        TempData["Success"] = "Password reset and login lockout cleared."; return RedirectToAction(nameof(Index));
    }
}
