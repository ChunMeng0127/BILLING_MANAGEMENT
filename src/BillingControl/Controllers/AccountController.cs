using BillingControl.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillingControl.Controllers;

public class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users) : Controller
{
    [AllowAnonymous, HttpGet] public IActionResult Login(string? returnUrl) => View(new LoginForm { ReturnUrl = returnUrl });
    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginForm model)
    {
        if (ModelState.IsValid)
        {
            var user = await users.FindByEmailAsync(model.Email);
            if (user is not null && user.IsActive)
            {
                var result = await signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
            }
            ModelState.AddModelError("", "Unable to sign in. Check your details, or wait 15 minutes if locked out.");
        }
        return View(model);
    }
    [HttpPost] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction(nameof(Login)); }
    [AllowAnonymous] public IActionResult Denied() { Response.StatusCode = 403; return View(); }
    [HttpGet] public IActionResult Password() => View();
    [HttpPost]
    public async Task<IActionResult> Password(string currentPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword)) { ModelState.AddModelError("", "Enter both passwords."); return View(); }
        var user = await users.GetUserAsync(User);
        var result = await users.ChangePasswordAsync(user!, currentPassword, newPassword);
        if (!result.Succeeded) { foreach (var e in result.Errors) ModelState.AddModelError("", e.Description); return View(); }
        await signIn.RefreshSignInAsync(user!); TempData["Success"] = "Password updated."; return RedirectToAction("Index", "Home");
    }
}
