using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;

namespace LocalServicesMarketplace.Services;

/// <summary>
/// Pure booking rules (no database access) so they can be unit tested and reused by the
/// MVC views (to decide which buttons to show) and the services (to enforce them).
/// </summary>
public static class BookingPolicy
{
    public static bool IsActive(BookingStatus status) => status is BookingStatus.Pending or BookingStatus.Confirmed;

    /// <summary>Customers may cancel Pending/Confirmed bookings, but not inside the cancellation window.</summary>
    public static ServiceResult CanCustomerCancel(Booking booking, DateTime nowUtc, MarketplaceOptions options)
    {
        if (!IsActive(booking.Status))
            return ServiceResult.Fail($"A {booking.Status.ToString().ToLowerInvariant()} booking cannot be cancelled.");
        if (booking.ScheduledStartUtc - nowUtc < TimeSpan.FromHours(options.CancellationWindowHours))
            return ServiceResult.Fail($"Bookings cannot be cancelled within {options.CancellationWindowHours} hours of the appointment.");
        return ServiceResult.Ok();
    }

    public static ServiceResult CanReschedule(Booking booking, DateTime nowUtc, MarketplaceOptions options)
    {
        if (!IsActive(booking.Status))
            return ServiceResult.Fail("Only pending or confirmed bookings can be rescheduled.");
        if (booking.RescheduleCount >= options.MaxReschedules)
            return ServiceResult.Fail($"This booking has already been rescheduled {options.MaxReschedules} times.");
        if (booking.ScheduledStartUtc - nowUtc < TimeSpan.FromHours(options.CancellationWindowHours))
            return ServiceResult.Fail($"Bookings cannot be rescheduled within {options.CancellationWindowHours} hours of the appointment.");
        return ServiceResult.Ok();
    }

    public static ServiceResult CanRespond(Booking booking, DateTime nowUtc)
    {
        if (booking.Status != BookingStatus.Pending)
            return ServiceResult.Conflict($"This booking is already {booking.Status.ToString().ToLowerInvariant()}.");
        if (booking.ScheduledStartUtc <= nowUtc)
            return ServiceResult.Fail("The appointment time has already passed.");
        return ServiceResult.Ok();
    }

    public static ServiceResult CanComplete(Booking booking, DateTime nowUtc, MarketplaceOptions options)
    {
        if (booking.Status != BookingStatus.Confirmed)
            return ServiceResult.Conflict("Only confirmed bookings can be marked as completed.");
        if (!options.AllowEarlyCompletion && booking.ScheduledStartUtc > nowUtc)
            return ServiceResult.Fail("A job can only be completed after its scheduled start time.");
        return ServiceResult.Ok();
    }

    public static ServiceResult CanReview(Booking booking, bool alreadyReviewed)
    {
        if (booking.Status != BookingStatus.Completed)
            return ServiceResult.Fail("You can only review a booking after it has been completed.");
        if (alreadyReviewed)
            return ServiceResult.Conflict("You have already reviewed this booking.");
        return ServiceResult.Ok();
    }

    /// <summary>Splits the price into platform commission and provider earning (rounded to paise).</summary>
    public static (decimal PlatformFee, decimal ProviderEarning) SplitPrice(decimal price, decimal commissionRate)
    {
        var fee = Math.Round(price * commissionRate, 2, MidpointRounding.AwayFromZero);
        return (fee, price - fee);
    }
}
