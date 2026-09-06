using System.Data;
using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillingControl.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController(UserManager<AppUser> users, AppDbContext db) : AppController
{
    public async Task<IActionResult> Index()
    {
        var all = await users.Users.OrderBy(x => x.Email).ToListAsync();
        var roles = new Dictionary<string, string>(); foreach (var u in all) roles[u.Id] = string.Join(", ", await users.GetRolesAsync(u)); ViewBag.Roles = roles; return View(all);
    }
    public IActionResult Create() => View(new UserForm());
    [HttpPost]
    public async Task<IActionResult> Create(UserForm form)
    {
        if (form.Role is not ("Admin" or "User")) ModelState.AddModelError("Role", "Select Admin or User.");
        if (!ModelState.IsValid) return View(form);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = new AppUser { Email = form.Email, UserName = form.Email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, form.Password);
        if (!result.Succeeded) { foreach (var e in result.Errors) ModelState.AddModelError("", e.Description); return View(form); }
        Seed.Check(await users.AddToRoleAsync(user, form.Role)); await tx.CommitAsync(); TempData["Success"] = "User created."; return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public async Task<IActionResult> Update(string id, string role, bool active)
    {
        Finance.Require(role is "Admin" or "User", "Invalid role.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await users.FindByIdAsync(id); if (user == null) return NotFound();
        Finance.Require(id != users.GetUserId(User) || (active && role == "Admin"), "You cannot disable or demote your own administrator account.");
        if (await users.IsInRoleAsync(user, "Admin") && (!active || role != "Admin")) Finance.Require((await users.GetUsersInRoleAsync("Admin")).Count(x => x.IsActive) > 1, "Keep at least one active administrator.");
        user.IsActive = active; Seed.Check(await users.UpdateAsync(user));
        var old = await users.GetRolesAsync(user);
        if (!old.Contains(role)) { Seed.Check(await users.RemoveFromRolesAsync(user, old)); Seed.Check(await users.AddToRoleAsync(user, role)); }
        Seed.Check(await users.UpdateSecurityStampAsync(user)); await tx.CommitAsync(); TempData["Success"] = "Access updated; existing sessions revoked."; return RedirectToAction(nameof(Index));
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