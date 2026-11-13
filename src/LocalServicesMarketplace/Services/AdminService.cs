using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Services;

public class AdminService(
    ApplicationDbContext db,
    UserManager<User> userManager,
    INotificationService notifications,
    IActivityLogger activity,
    ITimeZoneService tz) : IAdminService
{
    private const int PageSize = 15;

    // ------------------------------------------------------------------ users

    public async Task<PagedResult<UserListItemVm>> GetUsersAsync(AdminUserQuery query, CancellationToken ct = default)
    {
        // Explicit LINQ join over the Identity tables (Users -> UserRoles -> Roles).
        var q = from u in db.Users.AsNoTracking()
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                select new { User = u, Role = r.Name! };

        if (!string.IsNullOrWhiteSpace(query.Role)) q = q.Where(x => x.Role == query.Role);
        if (query.Status == "blocked") q = q.Where(x => x.User.IsBlocked);
        else if (query.Status == "active") q = q.Where(x => !x.User.IsBlocked);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(x => x.User.FullName.Contains(term) || x.User.Email!.Contains(term)
                             || (x.User.PhoneNumber != null && x.User.PhoneNumber.Contains(term))
                             || (x.User.City != null && x.User.City.Contains(term)));
        }

        var page = Math.Max(1, query.Page);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.User.CreatedAtUtc)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(x => new UserListItemVm
            {
                Id = x.User.Id,
                FullName = x.User.FullName,
                Email = x.User.Email ?? string.Empty,
                PhoneNumber = x.User.PhoneNumber,
                City = x.User.City,
                Role = x.Role,
                IsBlocked = x.User.IsBlocked,
                BlockedReason = x.User.BlockedReason,
                CreatedAtUtc = x.User.CreatedAtUtc,
                LastLoginAtUtc = x.User.LastLoginAtUtc,
                IsSampleData = x.User.IsSampleData,
                ProfileImagePath = x.User.ProfileImagePath,
                BookingCount = x.Role == AppRoles.Provider
                    ? db.Bookings.Count(b => b.Provider.UserId == x.User.Id)
                    : db.Bookings.Count(b => b.CustomerId == x.User.Id)
            }).ToListAsync(ct);

        return new PagedResult<UserListItemVm> { Items = items, Page = page, PageSize = PageSize, TotalCount = total };
    }

    public async Task<ServiceResult> SetBlockedAsync(int adminId, int userId, bool blocked, string? reason, CancellationToken ct = default)
    {
        if (adminId == userId) return ServiceResult.Fail("You cannot block your own account.");
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult.NotFound("User not found.");
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin)) return ServiceResult.Forbidden("Administrator accounts cannot be blocked here.");
        if (blocked && string.IsNullOrWhiteSpace(reason)) return ServiceResult.Fail("Give a reason for blocking this account.");

        user.IsBlocked = blocked;
        user.BlockedReason = blocked ? reason!.Trim().Truncate(300) : null;
        user.LockoutEnabled = true;
        user.LockoutEnd = blocked ? DateTimeOffset.MaxValue : null;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return ServiceResult.Fail(string.Join(" ", update.Errors.Select(e => e.Description)));

        // Rotating the security stamp invalidates existing cookies/tokens on the next validation.
        await userManager.UpdateSecurityStampAsync(user);

        if (!blocked)
        {
            await notifications.NotifyAsync(user.Id, "Account restored", "Your account has been unblocked. Welcome back.", NotificationType.Account, null, ct);
        }
        await activity.LogAsync(adminId, blocked ? "User blocked" : "User unblocked",
            $"{user.FullName} ({user.Email}){(blocked ? $": {user.BlockedReason}" : "")}", blocked ? "bi-person-slash" : "bi-person-check", ct);
        return ServiceResult.Ok();
    }

    // ------------------------------------------------------------------ providers & verification

    public async Task<PagedResult<ProviderAdminListItemVm>> GetProvidersAsync(AdminProviderQuery query, CancellationToken ct = default)
    {
        var q = db.Providers.AsNoTracking();
        if (query.Status.HasValue) q = q.Where(p => p.VerificationStatus == query.Status);
        if (query.CategoryId.HasValue) q = q.Where(p => p.CategoryId == query.CategoryId);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(p => p.User.FullName.Contains(term) || p.User.Email!.Contains(term) || p.City.Contains(term));
        }

        var page = Math.Max(1, query.Page);
        var total = await q.CountAsync(ct);
        var ordered = query.Status == VerificationStatus.Pending
            ? q.OrderBy(p => p.VerificationSubmittedAtUtc)
            : q.OrderByDescending(p => p.CreatedAtUtc);
        var items = await ProjectProviders(ordered.Skip((page - 1) * PageSize).Take(PageSize)).ToListAsync(ct);
        return new PagedResult<ProviderAdminListItemVm> { Items = items, Page = page, PageSize = PageSize, TotalCount = total };
    }

    internal static IQueryable<ProviderAdminListItemVm> ProjectProviders(IQueryable<Provider> q) => q.Select(p => new ProviderAdminListItemVm
    {
        Id = p.Id,
        UserId = p.UserId,
        Name = p.User.FullName,
        Email = p.User.Email ?? string.Empty,
        CategoryName = p.Category.Name,
        City = p.City,
        ExperienceYears = p.ExperienceYears,
        VerificationStatus = p.VerificationStatus,
        SubmittedAtUtc = p.VerificationSubmittedAtUtc,
        CreatedAtUtc = p.CreatedAtUtc,
        DocumentCount = p.VerificationDocuments.Count,
        ServiceCount = p.Services.Count(s => s.IsActive),
        AverageRating = p.AverageRating,
        TotalReviews = p.TotalReviews,
        IsBlocked = p.User.IsBlocked,
        IsSampleData = p.IsSampleData,
        ProfileImagePath = p.User.ProfileImagePath
    });

    public async Task<ProviderReviewViewModel?> GetProviderForReviewAsync(int providerId, CancellationToken ct = default)
    {
        var provider = await db.Providers.AsNoTracking().Include(p => p.User).Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == providerId, ct);
        if (provider is null) return null;

        return new ProviderReviewViewModel
        {
            Provider = provider,
            Documents = await db.ProviderVerificationDocuments.AsNoTracking().Include(d => d.ReviewedBy)
                .Where(d => d.ProviderId == providerId).OrderByDescending(d => d.UploadedAtUtc).ToListAsync(ct),
            Services = await db.Services.AsNoTracking().Include(s => s.Category)
                .Where(s => s.ProviderId == providerId).OrderBy(s => s.Title).ToListAsync(ct),
            Performance = await db.ProviderPerformance.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderId == providerId, ct)
        };
    }

    public async Task<ServiceResult> DecideProviderAsync(int adminId, int providerId, bool approve, string? note, CancellationToken ct = default)
    {
        var provider = await db.Providers.Include(p => p.User).Include(p => p.VerificationDocuments)
            .FirstOrDefaultAsync(p => p.Id == providerId, ct);
        if (provider is null) return ServiceResult.NotFound("Provider not found.");
        if (!approve && string.IsNullOrWhiteSpace(note)) return ServiceResult.Fail("Tell the provider why the application was rejected.");
        if (approve && provider.VerificationDocuments.All(d => d.Status == DocumentStatus.Rejected))
            return ServiceResult.Fail("At least one valid document is required before approval.");

        var now = tz.UtcNow;
        provider.VerificationStatus = approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        provider.VerificationNote = note?.Trim().Truncate(500);
        provider.VerifiedAtUtc = approve ? now : null;
        provider.UpdatedAtUtc = now;
        if (approve)
        {
            foreach (var doc in provider.VerificationDocuments.Where(d => d.Status == DocumentStatus.Pending))
            {
                doc.Status = DocumentStatus.Approved;
                doc.ReviewedAtUtc = now;
                doc.ReviewedById = adminId;
            }
        }
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(provider.UserId,
            approve ? "You're verified" : "Verification needs changes",
            approve ? "Your profile is approved. Customers can now find and book you."
                    : $"Your verification was not approved: {provider.VerificationNote}",
            NotificationType.Verification, "/Provider/Verification", ct);
        await activity.LogAsync(adminId, approve ? "Provider approved" : "Provider rejected",
            $"{provider.User.FullName}{(approve ? "" : $": {provider.VerificationNote}")}", approve ? "bi-patch-check" : "bi-patch-exclamation", ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReviewDocumentAsync(int adminId, int documentId, bool approve, string? note, CancellationToken ct = default)
    {
        var doc = await db.ProviderVerificationDocuments.Include(d => d.Provider).FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return ServiceResult.NotFound("Document not found.");
        if (!approve && string.IsNullOrWhiteSpace(note)) return ServiceResult.Fail("Add a note explaining why the document was rejected.");

        doc.Status = approve ? DocumentStatus.Approved : DocumentStatus.Rejected;
        doc.ReviewNote = note?.Trim().Truncate(500);
        doc.ReviewedAtUtc = tz.UtcNow;
        doc.ReviewedById = adminId;
        await db.SaveChangesAsync(ct);

        if (!approve)
        {
            await notifications.NotifyAsync(doc.Provider.UserId, "Document rejected",
                $"Your {doc.DocumentType.Label().ToLowerInvariant()} was rejected: {doc.ReviewNote}",
                NotificationType.Verification, "/Provider/Verification", ct);
        }
        return ServiceResult.Ok();
    }

    // ------------------------------------------------------------------ services moderation

    public async Task<PagedResult<AdminServiceListItemVm>> GetServicesAsync(string? q, int? categoryId, int page, CancellationToken ct = default)
    {
        var query = db.Services.AsNoTracking();
        if (categoryId.HasValue) query = query.Where(s => s.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(s => s.Title.Contains(term) || s.Provider.User.FullName.Contains(term));
        }
        page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Category.DisplayOrder).ThenBy(s => s.Title)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(s => new AdminServiceListItemVm
            {
                Id = s.Id, Title = s.Title, ProviderName = s.Provider.User.FullName, ProviderId = s.ProviderId,
                CategoryName = s.Category.Name, Price = s.Price, PriceUnit = s.PriceUnit, IsActive = s.IsActive,
                BookingCount = s.Bookings.Count, CreatedAtUtc = s.CreatedAtUtc
            }).ToListAsync(ct);
        return new PagedResult<AdminServiceListItemVm> { Items = items, Page = page, PageSize = PageSize, TotalCount = total };
    }

    public async Task<ServiceResult> SetServiceActiveAsync(int adminId, int serviceId, bool active, CancellationToken ct = default)
    {
        var service = await db.Services.Include(s => s.Provider).FirstOrDefaultAsync(s => s.Id == serviceId, ct);
        if (service is null) return ServiceResult.NotFound("Service not found.");
        service.IsActive = active;
        service.UpdatedAtUtc = tz.UtcNow;
        await db.SaveChangesAsync(ct);
        if (!active)
        {
            await notifications.NotifyAsync(service.Provider.UserId, "Service hidden by admin",
                $"\"{service.Title}\" was hidden from customers by the platform team.", NotificationType.Account, "/Provider/Services", ct);
        }
        await activity.LogAsync(adminId, active ? "Service shown" : "Service hidden", service.Title, "bi-eye", ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteServiceAsync(int adminId, int serviceId, CancellationToken ct = default)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId, ct);
        if (service is null) return ServiceResult.NotFound("Service not found.");
        if (await db.Bookings.AnyAsync(b => b.ServiceId == serviceId, ct))
            return ServiceResult.Conflict("This service has bookings. Hide it instead of deleting it.");
        db.Services.Remove(service);
        await db.SaveChangesAsync(ct);
        await activity.LogAsync(adminId, "Service deleted", service.Title, "bi-trash", ct);
        return ServiceResult.Ok();
    }
}
