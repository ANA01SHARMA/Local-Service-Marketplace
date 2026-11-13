using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Reporting;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Services;

public class ReviewService(
    ApplicationDbContext db,
    INotificationService notifications,
    IActivityLogger activity,
    TimeProvider clock) : IReviewService
{
    public async Task<ServiceResult<Booking>> GetReviewableBookingAsync(int customerId, int bookingId, CancellationToken ct = default)
    {
        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Provider).ThenInclude(p => p.User)
            .Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return ServiceResult<Booking>.NotFound("Booking not found.");
        if (booking.CustomerId != customerId) return ServiceResult<Booking>.Forbidden("You can only review your own bookings.");

        var reviewed = await db.Reviews.AnyAsync(r => r.BookingId == bookingId, ct);
        var check = BookingPolicy.CanReview(booking, reviewed);
        return check.Succeeded ? ServiceResult<Booking>.Ok(booking) : ServiceResult<Booking>.Fail(check.Error!, check.ErrorKind);
    }

    public async Task<ServiceResult<int>> SubmitAsync(int customerId, int bookingId, int rating, string? comment, CancellationToken ct = default)
    {
        if (rating is < 1 or > 5) return ServiceResult<int>.Fail("Choose a rating from 1 to 5 stars.");

        var eligible = await GetReviewableBookingAsync(customerId, bookingId, ct);
        if (!eligible.Succeeded) return ServiceResult<int>.Fail(eligible.Error!, eligible.ErrorKind);
        var booking = eligible.Value!;

        var review = new Review
        {
            BookingId = booking.Id,
            CustomerId = customerId,
            ProviderId = booking.ProviderId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim().Truncate(1000),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Unique index on Reviews.BookingId: a double-submit raced us.
            return ServiceResult<int>.Conflict("You have already reviewed this booking.");
        }

        var result = await RecalculateProviderRatingAsync(booking.ProviderId, ct);

        await notifications.NotifyAsync(booking.Provider.UserId, "New review received",
            $"You received {rating} star{(rating == 1 ? "" : "s")} for {booking.Service.Title}. Your rating is now {result.AverageRating:0.0}.",
            NotificationType.Review, "/Provider/Reviews", ct);
        await activity.LogAsync(customerId, "Review posted", $"{rating}★ for {booking.Provider.User.FullName} ({booking.Reference})", "bi-star", ct);
        return ServiceResult<int>.Ok(review.Id);
    }

    /// <summary>Executes the dbo.CalculateProviderAverageRating stored procedure.</summary>
    public async Task<ProviderRatingResult> RecalculateProviderRatingAsync(int providerId, CancellationToken ct = default)
    {
        var rows = await db.Database
            .SqlQuery<ProviderRatingResult>($"EXEC dbo.CalculateProviderAverageRating @ProviderId = {providerId}")
            .ToListAsync(ct);
        return rows.FirstOrDefault() ?? new ProviderRatingResult { ProviderId = providerId };
    }

    public async Task<PagedResult<ReviewVm>> GetProviderReviewsAsync(int providerId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var q = db.Reviews.AsNoTracking().Where(r => r.ProviderId == providerId);
        var total = await q.CountAsync(ct);
        var items = await Project(q.OrderByDescending(r => r.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new PagedResult<ReviewVm> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<IReadOnlyList<ReviewVm>> GetCustomerReviewsAsync(int customerId, CancellationToken ct = default) =>
        await Project(db.Reviews.AsNoTracking().Where(r => r.CustomerId == customerId).OrderByDescending(r => r.CreatedAtUtc)).ToListAsync(ct);

    private static IQueryable<ReviewVm> Project(IQueryable<Review> q) => q.Select(r => new ReviewVm
    {
        Id = r.Id,
        CustomerName = r.Customer.FullName,
        Rating = r.Rating,
        Comment = r.Comment,
        CreatedAtUtc = r.CreatedAtUtc,
        ServiceTitle = r.Booking.Service.Title,
        ProviderName = r.Provider.User.FullName,
        ProviderId = r.ProviderId,
        BookingId = r.BookingId,
        IsSampleData = r.IsSampleData
    });
}
