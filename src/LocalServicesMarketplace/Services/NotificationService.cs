using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Services;

public class NotificationService(ApplicationDbContext db, TimeProvider clock) : INotificationService
{
    private const int PageSize = 15;

    public async Task NotifyAsync(int userId, string title, string message, NotificationType type, string? link = null, CancellationToken ct = default)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title.Truncate(120),
            Message = message.Truncate(500),
            Type = type,
            Link = link,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task NotifyAdminsAsync(string title, string message, NotificationType type, string? link = null, CancellationToken ct = default)
    {
        var adminIds = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              where r.Name == AppRoles.Admin
                              select ur.UserId).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var id in adminIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = id, Title = title.Truncate(120), Message = message.Truncate(500),
                Type = type, Link = link, CreatedAtUtc = now
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<Notification>> GetAsync(int userId, bool unreadOnly, int page, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new PagedResult<Notification> { Items = items, Page = page, PageSize = PageSize, TotalCount = total };
    }

    public Task<int> CountUnreadAsync(int userId, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task<IReadOnlyList<Notification>> GetLatestAsync(int userId, int take, CancellationToken ct = default) =>
        await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id).Take(take).ToListAsync(ct);

    public async Task<string?> MarkReadAsync(int userId, int notificationId, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, ct);
        if (n is null) return null;
        if (!n.IsRead)
        {
            n.IsRead = true;
            await db.SaveChangesAsync(ct);
        }
        return n.Link;
    }

    public Task MarkAllReadAsync(int userId, CancellationToken ct = default) =>
        db.Notifications.Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
}

public class ActivityLogger(ApplicationDbContext db, TimeProvider clock, ILogger<ActivityLogger> logger) : IActivityLogger
{
    public async Task LogAsync(int? userId, string action, string details, string icon = "bi-activity", CancellationToken ct = default)
    {
        logger.LogInformation("Activity {Action} by {UserId}: {Details}", action, userId, details);
        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            Action = action.Truncate(60),
            Details = details.Truncate(500),
            Icon = icon,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }
}
