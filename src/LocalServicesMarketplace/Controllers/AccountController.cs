using System.Net;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Controllers;

public class AccountController(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IAccountService accounts,
    ICategoryService categories,
    IEmailService email,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<AccountController> logger) : Controller
{
    // ------------------------------------------------------------------ login / logout

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null, bool blocked = false)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        ViewData["Title"] = "Sign in";
        if (blocked) ModelState.AddModelError(string.Empty, "Your account has been blocked. Contact support if you think this is a mistake.");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Title"] = "Sign in";
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is { IsBlocked: true })
        {
            ModelState.AddModelError(string.Empty, "Your account has been blocked. Contact support if you think this is a mistake.");
            return View(model);
        }

        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Too many failed attempts. Try again in 5 minutes.");
            return View(model);
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "The e-mail or password is incorrect.");
            return View(model);
        }

        user!.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
        await userManager.UpdateAsync(user);
        logger.LogInformation("User {UserId} signed in", user.Id);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return LocalRedirect(model.ReturnUrl);

        return (await accounts.GetPrimaryRoleAsync(user)) switch
        {
            AppRoles.Admin => RedirectToAction("Index", "Admin"),
            AppRoles.Provider => RedirectToAction("Index", "Provider"),
            _ => RedirectToAction("Index", "Customer")
        };
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        HttpContext.Session.Clear();
        TempData["Success"] = "You have been signed out.";
        return RedirectToAction("Index", "Home");
    }

    // ------------------------------------------------------------------ registration

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewData["Title"] = "Create a customer account";
        await LoadCitiesAsync();
        return View(new RegisterCustomerViewModel());
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterCustomerViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Create a customer account";
        if (!ModelState.IsValid)
        {
            await LoadCitiesAsync();
            return View(model);
        }

        var result = await accounts.RegisterCustomerAsync(model, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadCitiesAsync();
            return View(model);
        }

        await signInManager.SignInAsync(result.Value!, isPersistent: false);
        TempData["Success"] = $"Welcome, {result.Value!.FullName}. Your account is ready.";
        return RedirectToAction("Index", "Customer");
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> RegisterProvider(int? categoryId)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewData["Title"] = "Join as a professional";
        await LoadProviderListsAsync();
        return View(new RegisterProviderViewModel { CategoryId = categoryId });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> RegisterProvider(RegisterProviderViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Join as a professional";
        if (!ModelState.IsValid)
        {
            await LoadProviderListsAsync();
            return View(model);
        }

        var result = await accounts.RegisterProviderAsync(model, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadProviderListsAsync();
            return View(model);
        }

        await signInManager.SignInAsync(result.Value!, isPersistent: false);
        TempData["Success"] = "Your professional account is ready. Finish the checklist below to get verified.";
        return RedirectToAction("Index", "Provider");
    }

    // ------------------------------------------------------------------ password reset

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        ViewData["Title"] = "Reset your password";
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Reset your password";
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not null && !user.IsBlocked)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var baseUrl = appOptions.Value.PublicBaseUrl?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var link = $"{baseUrl}{Url.Action(nameof(ResetPassword), new { email = user.Email, token })}";
            await email.SendAsync(user.Email!, "Reset your Kaarigar password",
                $"<p>Hi {WebUtility.HtmlEncode(user.FullName)},</p><p>Use the link below to choose a new password. It expires in one day.</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Reset my password</a></p><p>If you didn't ask for this, ignore this e-mail.</p>", ct);
        }

        // Same response whether or not the account exists (prevents account enumeration).
        TempData["Success"] = "If an account exists for that e-mail, a reset link is on its way. In the Docker demo, open Mailpit at http://localhost:8025.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token)) return RedirectToAction(nameof(ForgotPassword));
        ViewData["Title"] = "Choose a new password";
        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        ViewData["Title"] = "Choose a new password";
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null)
        {
            var result = await userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return View(model);
            }
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }
        TempData["Success"] = "Password updated. Sign in with your new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewData["Title"] = "Access denied";
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    // ------------------------------------------------------------------ profile (all roles)

    [HttpGet, Authorize]
    public async Task<IActionResult> Profile()
    {
        ViewData["Title"] = "My profile";
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        await LoadCitiesAsync();
        return View(new ProfileViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            City = user.City,
            Address = user.Address,
            ProfileImagePath = user.ProfileImagePath,
            MemberSinceUtc = user.CreatedAtUtc,
            Role = await accounts.GetPrimaryRoleAsync(user)
        });
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Profile(ProfileViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "My profile";
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (ModelState.IsValid)
        {
            var result = await accounts.UpdateProfileAsync(user.Id, model, ct);
            if (result.Succeeded)
            {
                await signInManager.RefreshSignInAsync(await userManager.GetUserAsync(User) ?? user);
                TempData["Success"] = "Profile saved.";
                return RedirectToAction(nameof(Profile));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        model.Email = user.Email ?? string.Empty;
        model.ProfileImagePath = user.ProfileImagePath;
        model.MemberSinceUtc = user.CreatedAtUtc;
        model.Role = await accounts.GetPrimaryRoleAsync(user);
        await LoadCitiesAsync();
        return View(model);
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Profile));
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Profile));
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Password changed.";
        return RedirectToAction(nameof(Profile));
    }

    // ------------------------------------------------------------------ helpers

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");

    private async Task LoadCitiesAsync() => ViewBag.Cities = await categories.GetCitiesAsync();

    private async Task LoadProviderListsAsync()
    {
        ViewBag.Categories = await categories.GetActiveAsync();
        await LoadCitiesAsync();
    }
}
