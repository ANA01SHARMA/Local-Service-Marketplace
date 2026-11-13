using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services;

namespace LocalServicesMarketplace.Tests;

/// <summary>Fast unit tests for the pure booking rules (no database).</summary>
public class BookingPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc);
    private static readonly MarketplaceOptions Options = new() { CancellationWindowHours = 2, MaxReschedules = 2, AllowEarlyCompletion = false };

    private static Booking BookingAt(TimeSpan fromNow, BookingStatus status = BookingStatus.Confirmed, int reschedules = 0) => new()
    {
        Status = status,
        ScheduledStartUtc = Now + fromNow,
        ScheduledEndUtc = Now + fromNow + TimeSpan.FromHours(1),
        RescheduleCount = reschedules
    };

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Confirmed)]
    public void Customer_can_cancel_an_active_booking_more_than_two_hours_ahead(BookingStatus status) =>
        Assert.True(BookingPolicy.CanCustomerCancel(BookingAt(TimeSpan.FromHours(3), status), Now, Options).Succeeded);

    [Fact]
    public void Customer_cannot_cancel_within_two_hours()
    {
        var result = BookingPolicy.CanCustomerCancel(BookingAt(TimeSpan.FromMinutes(119)), Now, Options);
        Assert.False(result.Succeeded);
        Assert.Contains("2 hours", result.Error);
    }

    [Fact]
    public void Exactly_two_hours_ahead_is_still_allowed() =>
        Assert.True(BookingPolicy.CanCustomerCancel(BookingAt(TimeSpan.FromHours(2)), Now, Options).Succeeded);

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    public void Finished_bookings_cannot_be_cancelled(BookingStatus status) =>
        Assert.False(BookingPolicy.CanCustomerCancel(BookingAt(TimeSpan.FromDays(2), status), Now, Options).Succeeded);

    [Fact]
    public void Reschedule_is_limited_by_count_and_window()
    {
        Assert.True(BookingPolicy.CanReschedule(BookingAt(TimeSpan.FromDays(1), reschedules: 1), Now, Options).Succeeded);
        Assert.False(BookingPolicy.CanReschedule(BookingAt(TimeSpan.FromDays(1), reschedules: 2), Now, Options).Succeeded);
        Assert.False(BookingPolicy.CanReschedule(BookingAt(TimeSpan.FromMinutes(30)), Now, Options).Succeeded);
    }

    [Fact]
    public void Only_pending_future_bookings_can_be_accepted_or_rejected()
    {
        Assert.True(BookingPolicy.CanRespond(BookingAt(TimeSpan.FromHours(5), BookingStatus.Pending), Now).Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, BookingPolicy.CanRespond(BookingAt(TimeSpan.FromHours(5), BookingStatus.Confirmed), Now).ErrorKind);
        Assert.False(BookingPolicy.CanRespond(BookingAt(TimeSpan.FromHours(-1), BookingStatus.Pending), Now).Succeeded);
    }

    [Fact]
    public void Completion_requires_confirmed_and_respects_early_completion_setting()
    {
        var future = BookingAt(TimeSpan.FromHours(5));
        Assert.False(BookingPolicy.CanComplete(future, Now, Options).Succeeded);
        Assert.True(BookingPolicy.CanComplete(future, Now, new MarketplaceOptions { AllowEarlyCompletion = true }).Succeeded);
        Assert.True(BookingPolicy.CanComplete(BookingAt(TimeSpan.FromHours(-2)), Now, Options).Succeeded);
        Assert.False(BookingPolicy.CanComplete(BookingAt(TimeSpan.FromHours(-2), BookingStatus.Pending), Now, Options).Succeeded);
    }

    [Fact]
    public void Reviews_need_a_completed_booking_and_only_one_per_booking()
    {
        Assert.False(BookingPolicy.CanReview(BookingAt(TimeSpan.FromHours(-2)), alreadyReviewed: false).Succeeded);
        Assert.True(BookingPolicy.CanReview(BookingAt(TimeSpan.FromHours(-2), BookingStatus.Completed), alreadyReviewed: false).Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, BookingPolicy.CanReview(BookingAt(TimeSpan.FromHours(-2), BookingStatus.Completed), alreadyReviewed: true).ErrorKind);
    }

    [Theory]
    [InlineData(499, 0.10, 49.90, 449.10)]
    [InlineData(249, 0.10, 24.90, 224.10)]
    [InlineData(1, 0.105, 0.11, 0.89)]
    public void Price_is_split_into_fee_and_earning(decimal price, decimal rate, decimal fee, decimal earning)
    {
        var (f, e) = BookingPolicy.SplitPrice(price, rate);
        Assert.Equal(fee, f);
        Assert.Equal(earning, e);
        Assert.Equal(price, f + e);
    }
}
