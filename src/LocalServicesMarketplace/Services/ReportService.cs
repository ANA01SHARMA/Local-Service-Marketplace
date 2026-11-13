using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Reporting;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Services;

/// <summary>
/// Reporting reads from the SQL Server VIEWS (TopRatedProviders, MonthlyBookingStatistics,
/// ProviderPerformance), the GetMonthlyBookingReport STORED PROCEDURE, and explicit LINQ JOIN queries.
/// </summary>
public class ReportService(
    ApplicationDbContext db,
    IBookingService bookings,
    IProviderService providers,
    ICategoryService categories,
    ITimeZoneService tz) : IReportService
{
    public async Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken ct = default)
    {
        var roleCounts = await (from ur in db.UserRoles
                                join r in db.Roles on ur.RoleId equals r.Id
                                group ur by r.Name into g
                                select new { Role = g.Key, Count = g.Count() }).ToListAsync(ct);
        var statusCounts = await bookings.CountByStatusAsync(BookingScope.Admin, null, ct);

        // Last 12 months from the MonthlyBookingStatistics view, with empty months filled in.
        var today = tz.LocalToday;
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var firstKey = first.Year * 100 + first.Month;
        var stats = await db.MonthlyBookingStatistics.AsNoTracking()
            .Where(m => m.Year * 100 + m.Month >= firstKey)
            .ToListAsync(ct);
        var monthly = Enumerable.Range(0, 12).Select(i =>
        {
            var m = first.AddMonths(i);
            return stats.FirstOrDefault(s => s.Year == m.Year && s.Month == m.Month)
                   ?? new MonthlyBookingStatistic { Year = m.Year, Month = m.Month };
        }).ToList();

        var recentUsers = await (from u in db.Users.AsNoTracking()
                                 join ur in db.UserRoles on u.Id equals ur.UserId
                                 join r in db.Roles on ur.RoleId equals r.Id
                                 orderby u.CreatedAtUtc descending
                                 select new UserListItemVm
                                 {
                                     Id = u.Id, FullName = u.FullName, Email = u.Email ?? "", City = u.City, Role = r.Name!,
                                     CreatedAtUtc = u.CreatedAtUtc, IsBlocked = u.IsBlocked, IsSampleData = u.IsSampleData,
                                     ProfileImagePath = u.ProfileImagePath
                                 }).Take(6).ToListAsync(ct);

        var pending = await AdminService.ProjectProviders(db.Providers.AsNoTracking()
                .Where(p => p.VerificationStatus == VerificationStatus.Pending)
                .OrderBy(p => p.VerificationSubmittedAtUtc).Take(5))
            .ToListAsync(ct);

        var recentBookings = await bookings.QueryAsync(BookingScope.Admin, null, new BookingQuery { Status = "recent", PageSize = 6 }, ct);

        return new AdminDashboardViewModel
        {
            TotalUsers = await db.Users.CountAsync(ct),
            TotalCustomers = roleCounts.FirstOrDefault(r => r.Role == AppRoles.Customer)?.Count ?? 0,
            TotalProviders = roleCounts.FirstOrDefault(r => r.Role == AppRoles.Provider)?.Count ?? 0,
            PendingApprovals = await db.Providers.CountAsync(p => p.VerificationStatus == VerificationStatus.Pending, ct),
            TotalBookings = statusCounts.Values.Sum(),
            CompletedBookings = statusCounts[BookingStatus.Completed],
            CancelledBookings = statusCounts[BookingStatus.Cancelled],
            TotalRevenue = await db.Bookings.Where(b => b.Status == BookingStatus.Completed).SumAsync(b => (decimal?)b.PlatformFee, ct) ?? 0,
            OpenComplaints = await db.Complaints.CountAsync(c => c.Status == ComplaintStatus.Open || c.Status == ComplaintStatus.InProgress, ct),
            Monthly = monthly,
            StatusDistribution = statusCounts,
            TopRated = await GetTopRatedAsync(5, ct),
            RecentUsers = recentUsers,
            PendingProviders = pending,
            RecentBookings = recentBookings.Items,
            Activities = await db.ActivityLogs.AsNoTracking().Include(a => a.User)
                .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).Take(8).ToListAsync(ct)
        };
    }

    /// <summary>Reads the dbo.TopRatedProviders view.</summary>
    public async Task<IReadOnlyList<TopRatedProviderView>> GetTopRatedAsync(int take, CancellationToken ct = default) =>
        await db.TopRatedProviders.AsNoTracking()
            .OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.TotalReviews).ThenBy(p => p.ProviderName)
            .Take(take).ToListAsync(ct);

    /// <summary>Executes the dbo.GetMonthlyBookingReport stored procedure (always 12 rows).</summary>
    public async Task<IReadOnlyList<MonthlyBookingReportRow>> GetMonthlyBookingReportAsync(int year, CancellationToken ct = default) =>
        await db.Database.SqlQuery<MonthlyBookingReportRow>($"EXEC dbo.GetMonthlyBookingReport @Year = {year}").ToListAsync(ct);

    public async Task<ReportsViewModel> GetReportsAsync(int year, CancellationToken ct = default)
    {
        var years = await db.MonthlyBookingStatistics.AsNoTracking().Select(m => m.Year).Distinct().OrderByDescending(y => y).ToListAsync(ct);
        if (!years.Contains(tz.LocalToday.Year)) years.Insert(0, tz.LocalToday.Year);
        if (!years.Contains(year)) year = years[0];

        // Customer report: Bookings JOIN Users, grouped per customer.
        var topCustomers = await (from b in db.Bookings.AsNoTracking()
                                  join u in db.Users on b.CustomerId equals u.Id
                                  group b by new { u.Id, u.FullName, u.Email, u.City } into g
                                  select new CustomerReportRow
                                  {
                                      CustomerId = g.Key.Id,
                                      CustomerName = g.Key.FullName,
                                      Email = g.Key.Email ?? "",
                                      City = g.Key.City,
                                      TotalBookings = g.Count(),
                                      CompletedBookings = g.Count(x => x.Status == BookingStatus.Completed),
                                      CancelledBookings = g.Count(x => x.Status == BookingStatus.Cancelled),
                                      TotalSpent = g.Sum(x => x.Status == BookingStatus.Completed ? x.Price : 0m),
                                      LastBookingUtc = g.Max(x => (DateTime?)x.CreatedAtUtc)
                                  })
            .OrderByDescending(r => r.TotalSpent).ThenByDescending(r => r.TotalBookings)
            .Take(10).ToListAsync(ct);

        // Revenue by category: Bookings JOIN Services JOIN Categories.
        var categoryRevenue = await (from b in db.Bookings.AsNoTracking()
                                     where b.Status == BookingStatus.Completed
                                     join s in db.Services on b.ServiceId equals s.Id
                                     join c in db.Categories on s.CategoryId equals c.Id
                                     group b by c.Name into g
                                     select new CategoryRevenueRow
                                     {
                                         CategoryName = g.Key,
                                         CompletedBookings = g.Count(),
                                         GrossValue = g.Sum(x => x.Price),
                                         PlatformRevenue = g.Sum(x => x.PlatformFee)
                                     })
            .OrderByDescending(r => r.PlatformRevenue).ToListAsync(ct);

        return new ReportsViewModel
        {
            Year = year,
            Years = years,
            MonthlyReport = await GetMonthlyBookingReportAsync(year, ct),
            ProviderPerformance = await db.ProviderPerformance.AsNoTracking()
                .Where(p => p.VerificationStatus == VerificationStatus.Approved)
                .OrderByDescending(p => p.TotalEarnings).ThenByDescending(p => p.CompletedJobs)
                .ToListAsync(ct),
            TopCustomers = topCustomers,
            CategoryRevenue = categoryRevenue
        };
    }

    public Task<ProviderPerformance?> GetProviderPerformanceAsync(int providerId, CancellationToken ct = default) =>
        db.ProviderPerformance.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderId == providerId, ct);

    public async Task<HomeViewModel> GetHomeAsync(CancellationToken ct = default)
    {
        var testimonials = await db.Reviews.AsNoTracking()
            .Where(r => r.Rating >= 4 && r.Comment != null && r.Comment.Length > 60)
            .OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAtUtc)
            .Take(12)
            .Select(r => new TestimonialVm
            {
                CustomerName = r.Customer.FullName,
                City = r.Customer.City,
                Rating = r.Rating,
                Comment = r.Comment!,
                ProviderName = r.Provider.User.FullName,
                ServiceTitle = r.Booking.Service.Title
            }).ToListAsync(ct);

        return new HomeViewModel
        {
            Categories = await categories.GetActiveTilesAsync(ct),
            // Ranked by the TopRatedProviders view, then hydrated with live availability.
            FeaturedProviders = await providers.GetCardsAsync((await GetTopRatedAsync(6, ct)).Select(p => p.ProviderId), ct),
            NextAvailable = await providers.GetNextAvailableAsync(4, ct),
            Testimonials = testimonials.GroupBy(t => t.ProviderName).Select(g => g.First()).Take(3).ToList(),
            Cities = await categories.GetCitiesAsync(ct),
            VerifiedProviders = await db.Providers.CountAsync(p => p.VerificationStatus == VerificationStatus.Approved && !p.User.IsBlocked, ct),
            CompletedJobs = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Completed, ct),
            AverageRating = await db.Reviews.AverageAsync(r => (decimal?)r.Rating, ct) ?? 0
        };
    }
}
