using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Services;

public class CategoryService(
    ApplicationDbContext db,
    IActivityLogger activity,
    IMemoryCache cache,
    IWebHostEnvironment env,
    IOptions<MarketplaceOptions> options,
    TimeProvider clock) : ICategoryService
{
    private const string TilesCacheKey = "categories:tiles";

    public async Task<IReadOnlyList<CategoryTileVm>> GetActiveTilesAsync(CancellationToken ct = default)
    {
        // Cached briefly: the category grid is on almost every public page.
        return await cache.GetOrCreateAsync(TilesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return (IReadOnlyList<CategoryTileVm>)await db.Categories.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .Select(c => new CategoryTileVm
                {
                    Id = c.Id, Name = c.Name, Slug = c.Slug, Icon = c.Icon, ImagePath = c.ImagePath, Description = c.Description,
                    ProviderCount = db.Providers.Count(p => p.VerificationStatus == VerificationStatus.Approved && !p.User.IsBlocked
                        && (p.CategoryId == c.Id || p.Services.Any(s => s.IsActive && s.CategoryId == c.Id))),
                    StartingPrice = c.Services
                        .Where(s => s.IsActive && s.Provider.VerificationStatus == VerificationStatus.Approved)
                        .Min(s => (decimal?)s.Price)
                })
                .ToListAsync(ct);
        }) ?? [];
    }

    public async Task<IReadOnlyList<Category>> GetActiveAsync(CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<CategoryAdminVm>> GetAllForAdminAsync(CancellationToken ct = default) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryAdminVm
            {
                Category = c,
                ServiceCount = c.Services.Count,
                ProviderCount = c.Providers.Count,
                BookingCount = c.Services.SelectMany(s => s.Bookings).Count()
            }).ToListAsync(ct);

    public Task<Category?> GetAsync(int id, CancellationToken ct = default) =>
        db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ServiceResult<Category>> SaveAsync(CategoryInputModel input, int adminId, CancellationToken ct = default)
    {
        var name = input.Name.Trim();
        var slug = input.Slug.Trim().ToLowerInvariant();
        if (await db.Categories.AnyAsync(c => c.Id != input.Id && (c.Slug == slug || c.Name == name), ct))
            return ServiceResult<Category>.Conflict("Another category already uses this name or slug.");

        Category category;
        if (input.Id is > 0)
        {
            var existing = await db.Categories.FirstOrDefaultAsync(c => c.Id == input.Id, ct);
            if (existing is null) return ServiceResult<Category>.NotFound("Category not found.");
            category = existing;
        }
        else
        {
            category = new Category { CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
            db.Categories.Add(category);
        }

        category.Name = name;
        category.Slug = slug;
        category.Description = input.Description?.Trim();
        category.Icon = input.Icon.Trim();
        category.DisplayOrder = input.DisplayOrder;
        category.IsActive = input.IsActive;
        var illustration = $"/images/categories/{slug}.svg";
        category.ImagePath = File.Exists(Path.Combine(env.WebRootPath, "images", "categories", $"{slug}.svg")) ? illustration : null;

        await db.SaveChangesAsync(ct);
        cache.Remove(TilesCacheKey);
        await activity.LogAsync(adminId, input.Id is > 0 ? "Category updated" : "Category created", $"Category \"{category.Name}\"", "bi-grid", ct);
        return ServiceResult<Category>.Ok(category);
    }

    public async Task<ServiceResult> DeleteAsync(int id, int adminId, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return ServiceResult.NotFound("Category not found.");
        var inUse = await db.Services.AnyAsync(s => s.CategoryId == id, ct) || await db.Providers.AnyAsync(p => p.CategoryId == id, ct);
        if (inUse)
            return ServiceResult.Conflict($"\"{category.Name}\" is used by providers or services. Deactivate it instead of deleting.");

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        cache.Remove(TilesCacheKey);
        await activity.LogAsync(adminId, "Category deleted", $"Category \"{category.Name}\"", "bi-trash", ct);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<string>> GetCitiesAsync(CancellationToken ct = default)
    {
        var fromDb = await db.Providers.AsNoTracking()
            .Where(p => p.VerificationStatus == VerificationStatus.Approved)
            .Select(p => p.City).Distinct().ToListAsync(ct);
        return options.Value.Cities.Concat(fromDb)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c).ToList();
    }
}

public class ComplaintService(
    ApplicationDbContext db,
    INotificationService notifications,
    IActivityLogger activity,
    ITimeZoneService tz) : IComplaintService
{
    private const int PageSize = 12;

    public async Task<ServiceResult<int>> CreateAsync(int userId, ComplaintInputModel input, CancellationToken ct = default)
    {
        if (input.BookingId.HasValue)
        {
            var allowed = await db.Bookings.AnyAsync(b => b.Id == input.BookingId && (b.CustomerId == userId || b.Provider.UserId == userId), ct);
            if (!allowed) return ServiceResult<int>.Forbidden("You can only report issues about your own bookings.");
        }
        var openCount = await db.Complaints.CountAsync(c => c.ComplainantId == userId && c.Status == ComplaintStatus.Open, ct);
        if (openCount >= 5) return ServiceResult<int>.Fail("You already have 5 open issues. Wait for the team to respond before opening more.");

        var complaint = new Complaint
        {
            ComplainantId = userId,
            BookingId = input.BookingId,
            Subject = input.Subject.Trim(),
            Description = input.Description.Trim(),
            Status = ComplaintStatus.Open,
            CreatedAtUtc = tz.UtcNow
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAdminsAsync("New complaint", complaint.Subject, NotificationType.Complaint, "/Admin/Complaints", ct);
        await activity.LogAsync(userId, "Complaint opened", complaint.Subject, "bi-life-preserver", ct);
        return ServiceResult<int>.Ok(complaint.Id);
    }

    public async Task<IReadOnlyList<ComplaintListItemVm>> GetForUserAsync(int userId, CancellationToken ct = default) =>
        await Project(db.Complaints.AsNoTracking().Where(c => c.ComplainantId == userId).OrderByDescending(c => c.CreatedAtUtc)).ToListAsync(ct);

    public async Task<PagedResult<ComplaintListItemVm>> GetAllAsync(ComplaintStatus? status, int page, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var q = db.Complaints.AsNoTracking();
        if (status.HasValue) q = q.Where(c => c.Status == status);
        var total = await q.CountAsync(ct);
        var items = await Project(q.OrderBy(c => c.Status).ThenByDescending(c => c.CreatedAtUtc).Skip((page - 1) * PageSize).Take(PageSize)).ToListAsync(ct);
        return new PagedResult<ComplaintListItemVm> { Items = items, Page = page, PageSize = PageSize, TotalCount = total };
    }

    public async Task<ServiceResult> UpdateAsync(int adminId, ComplaintUpdateInput input, CancellationToken ct = default)
    {
        var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.Id == input.Id, ct);
        if (complaint is null) return ServiceResult.NotFound("Complaint not found.");
        if (input.Status is ComplaintStatus.Resolved or ComplaintStatus.Dismissed && string.IsNullOrWhiteSpace(input.AdminResponse))
            return ServiceResult.Fail("Add a response for the user before closing the complaint.");

        complaint.Status = input.Status;
        complaint.AdminResponse = input.AdminResponse?.Trim();
        if (input.Status is ComplaintStatus.Resolved or ComplaintStatus.Dismissed)
        {
            complaint.ResolvedAtUtc = tz.UtcNow;
            complaint.ResolvedById = adminId;
        }
        else
        {
            complaint.ResolvedAtUtc = null;
            complaint.ResolvedById = null;
        }
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(complaint.ComplainantId, $"Your issue is {input.Status.Label().ToLowerInvariant()}",
            string.IsNullOrWhiteSpace(complaint.AdminResponse) ? complaint.Subject : complaint.AdminResponse!,
            NotificationType.Complaint, "/Support", ct);
        await activity.LogAsync(adminId, "Complaint updated", $"#{complaint.Id} {complaint.Subject} → {input.Status.Label()}", "bi-life-preserver", ct);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<(int Id, string Label)>> GetBookingOptionsAsync(int userId, CancellationToken ct = default)
    {
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.CustomerId == userId || b.Provider.UserId == userId)
            .OrderByDescending(b => b.ScheduledStartUtc).Take(25)
            .Select(b => new { b.Id, b.Service.Title, b.ScheduledStartUtc })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, $"{Booking.FormatReference(r.Id)} – {r.Title} ({tz.Format(r.ScheduledStartUtc, "dd MMM yyyy")})")).ToList();
    }

    private IQueryable<ComplaintListItemVm> Project(IQueryable<Complaint> q) => q.Select(c => new ComplaintListItemVm
    {
        Id = c.Id,
        Subject = c.Subject,
        Description = c.Description,
        ComplainantName = c.Complainant.FullName,
        ComplainantEmail = c.Complainant.Email ?? string.Empty,
        ComplainantRole = db.Providers.Any(p => p.UserId == c.ComplainantId) ? AppRoles.Provider : AppRoles.Customer,
        BookingId = c.BookingId,
        Status = c.Status,
        AdminResponse = c.AdminResponse,
        CreatedAtUtc = c.CreatedAtUtc,
        ResolvedAtUtc = c.ResolvedAtUtc,
        IsSampleData = c.IsSampleData
    });
}
