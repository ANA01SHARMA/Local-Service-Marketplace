using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Controllers;

[Authorize(Roles = AppRoles.Customer)]
public class CustomerController(
    IBookingService bookings,
    IReviewService reviews,
    ICategoryService categories,
    ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Overview";
        var userId = User.GetUserId();
        var counts = await bookings.CountByStatusAsync(BookingScope.Customer, userId, ct);
        var upcoming = await bookings.QueryAsync(BookingScope.Customer, userId, new BookingQuery { Status = "upcoming", PageSize = 4 }, ct);
        var recent = await bookings.QueryAsync(BookingScope.Customer, userId, new BookingQuery { Status = "recent", PageSize = 6 }, ct);

        var model = new CustomerDashboardViewModel
        {
            FullName = User.DisplayName(),
            TotalBookings = counts.Values.Sum(),
            PendingBookings = counts[BookingStatus.Pending],
            ConfirmedBookings = counts[BookingStatus.Confirmed],
            CompletedBookings = counts[BookingStatus.Completed],
            CancelledBookings = counts[BookingStatus.Cancelled] + counts[BookingStatus.Rejected],
            TotalSpent = await db.Bookings.Where(b => b.CustomerId == userId && b.Status == BookingStatus.Completed).SumAsync(b => (decimal?)b.Price, ct) ?? 0,
            AwaitingReview = await db.Bookings.CountAsync(b => b.CustomerId == userId && b.Status == BookingStatus.Completed && b.Review == null, ct),
            Upcoming = upcoming.Items,
            RecentActivity = recent.Items,
            Categories = (await categories.GetActiveTilesAsync(ct)).Take(6).ToList()
        };
        return View(model);
    }

    /// <summary>My bookings, upcoming bookings and booking history share this action (?status=...).</summary>
    public async Task<IActionResult> Bookings([FromQuery] BookingQuery query, CancellationToken ct)
    {
        ViewData["Title"] = (query.Status ?? "all") switch
        {
            "upcoming" => "Upcoming bookings",
            "history" => "Booking history",
            _ => "My bookings"
        };
        ViewBag.Counts = await bookings.CountByStatusAsync(BookingScope.Customer, User.GetUserId(), ct);
        ViewData["Query"] = query;
        return View(await bookings.QueryAsync(BookingScope.Customer, User.GetUserId(), query, ct));
    }

    public async Task<IActionResult> Reviews(CancellationToken ct)
    {
        ViewData["Title"] = "My reviews";
        ViewBag.AwaitingReview = (await bookings.QueryAsync(BookingScope.Customer, User.GetUserId(), new BookingQuery { Status = "unreviewed", PageSize = 20 }, ct)).Items;
        return View(await reviews.GetCustomerReviewsAsync(User.GetUserId(), ct));
    }
}
