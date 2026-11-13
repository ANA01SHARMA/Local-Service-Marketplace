using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Services;

public class AccountService(
    ApplicationDbContext db,
    UserManager<User> userManager,
    IFileStorageService files,
    INotificationService notifications,
    IActivityLogger activity,
    TimeProvider clock) : IAccountService
{
    public async Task<ServiceResult<User>> RegisterCustomerAsync(RegisterCustomerViewModel model, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await CreateUserAsync(model, AppRoles.Customer);
        if (!created.Succeeded) return created;
        await tx.CommitAsync(ct);

        var user = created.Value!;
        await notifications.NotifyAsync(user.Id, "Welcome to Kaarigar",
            "Find a verified professional, pick a time that suits you and book in under a minute.",
            NotificationType.Account, "/Services", ct);
        await activity.LogAsync(user.Id, "Customer registered", $"{user.FullName} joined from {user.City}", "bi-person-plus", ct);
        return created;
    }

    public async Task<ServiceResult<User>> RegisterProviderAsync(RegisterProviderViewModel model, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == model.CategoryId && c.IsActive, ct))
            return ServiceResult<User>.Fail("Choose a valid service category.");

        // User + role + provider profile are created atomically.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await CreateUserAsync(model, AppRoles.Provider);
        if (!created.Succeeded) return created;

        var user = created.Value!;
        var now = clock.GetUtcNow().UtcDateTime;
        db.Providers.Add(new Provider
        {
            UserId = user.Id,
            CategoryId = model.CategoryId!.Value,
            ExperienceYears = model.ExperienceYears,
            Headline = model.Headline?.Trim(),
            City = model.City.Trim(),
            VerificationStatus = VerificationStatus.NotSubmitted,
            IsAcceptingBookings = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await notifications.NotifyAsync(user.Id, "Welcome aboard",
            "Complete your profile, add services and upload documents so we can verify you.",
            NotificationType.Verification, "/Provider", ct);
        await activity.LogAsync(user.Id, "Provider registered", $"{user.FullName} applied as a professional in {user.City}", "bi-person-badge", ct);
        return created;
    }

    public async Task<ServiceResult> UpdateProfileAsync(int userId, ProfileViewModel model, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult.NotFound("User not found.");

        if (model.Photo is { Length: > 0 })
        {
            var saved = await files.SaveProfileImageAsync(model.Photo, ct);
            if (!saved.Succeeded) return ServiceResult.Fail(saved.Error!);
            files.DeletePublicFile(user.ProfileImagePath);
            user.ProfileImagePath = saved.Value;
        }

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = model.PhoneNumber?.Trim();
        user.City = model.City?.Trim();
        user.Address = model.Address?.Trim();

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? ServiceResult.Ok()
            : ServiceResult.Fail(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public async Task<string> GetPrimaryRoleAsync(User user)
    {
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains(AppRoles.Admin)) return AppRoles.Admin;
        if (roles.Contains(AppRoles.Provider)) return AppRoles.Provider;
        return AppRoles.Customer;
    }

    private async Task<ServiceResult<User>> CreateUserAsync(RegisterCustomerViewModel model, string role)
    {
        // Public registration can only ever create Customer or Provider accounts.
        if (role is not (AppRoles.Customer or AppRoles.Provider))
            return ServiceResult<User>.Forbidden("That account type cannot be registered.");

        var email = model.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return ServiceResult<User>.Conflict("An account with this e-mail already exists. Try signing in instead.");

        var user = new User
        {
            UserName = email,
            Email = email,
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            City = model.City.Trim(),
            EmailConfirmed = true, // no e-mail confirmation step in the demo
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };

        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            return ServiceResult<User>.Fail(string.Join(" ", result.Errors.Select(e => e.Description)));

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
            return ServiceResult<User>.Fail(string.Join(" ", roleResult.Errors.Select(e => e.Description)));

        return ServiceResult<User>.Ok(user);
    }
}
