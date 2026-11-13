using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LocalServicesMarketplace.Tests;

/// <summary>
/// End-to-end booking rules against a real SQL Server: the CreateBooking stored procedure,
/// the filtered unique index, transactions and the rating stored procedure.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class BookingIntegrationTests(SqlServerFixture fixture)
{
    private static CreateBookingCommand Command(Provider p, Service s, TimeSlot slot) =>
        new(p.Id, s.Id, slot.Id, "12 Test Road, Saket", "New Delhi", null);

    [Fact]
    public async Task Concurrent_requests_for_the_same_slot_create_exactly_one_booking()
    {
        await using var setup = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(setup, [TestData.HoursFromNow(30)]);
        var customers = new List<User>();
        for (var i = 0; i < 8; i++) customers.Add(await TestData.CustomerAsync(setup));

        // Eight customers race for the same slot, each on its own connection.
        var attempts = customers.Select(async c =>
        {
            await using var db = fixture.CreateDbContext();
            return await SqlServerFixture.CreateBookingService(db).CreateAsync(c.Id, Command(provider, service, slots[0]));
        });
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal(ServiceErrorKind.Conflict, r.ErrorKind));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(1, await verify.Bookings.CountAsync(b => b.TimeSlotId == slots[0].Id));
    }

    [Fact]
    public async Task Booking_snapshots_price_and_splits_the_platform_fee()
    {
        await using var db = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(db, [TestData.HoursFromNow(26)], price: 799m);
        var customer = await TestData.CustomerAsync(db);

        var result = await SqlServerFixture.CreateBookingService(db).CreateAsync(customer.Id, Command(provider, service, slots[0]));

        Assert.True(result.Succeeded, result.Error);
        var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == result.Value);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(799m, booking.Price);
        Assert.Equal(79.90m, booking.PlatformFee);
        Assert.Equal(719.10m, booking.ProviderEarning);
        Assert.Equal(slots[0].StartUtc, booking.ScheduledStartUtc);
    }

    [Fact]
    public async Task A_customer_cannot_hold_two_bookings_at_the_same_time()
    {
        await using var db = fixture.CreateDbContext();
        var start = TestData.HoursFromNow(40);
        var a = await TestData.ProviderAsync(db, [start]);
        var b = await TestData.ProviderAsync(db, [start]);
        var customer = await TestData.CustomerAsync(db);
        var service = SqlServerFixture.CreateBookingService(db);

        Assert.True((await service.CreateAsync(customer.Id, Command(a.Provider, a.Service, a.Slots[0]))).Succeeded);
        var second = await service.CreateAsync(customer.Id, Command(b.Provider, b.Service, b.Slots[0]));

        Assert.False(second.Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, second.ErrorKind);
    }

    [Fact]
    public async Task Providers_awaiting_verification_cannot_be_booked()
    {
        await using var db = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(db, [TestData.HoursFromNow(30)], VerificationStatus.Pending);
        var customer = await TestData.CustomerAsync(db);

        var result = await SqlServerFixture.CreateBookingService(db).CreateAsync(customer.Id, Command(provider, service, slots[0]));

        Assert.False(result.Succeeded);
        Assert.Contains("not accepting bookings", result.Error);
    }

    [Fact]
    public async Task Cancelling_within_two_hours_is_refused()
    {
        await using var db = fixture.CreateDbContext();
        // 90 minutes ahead: bookable (lead time is 60 min) but inside the 2-hour cancellation window.
        var start = DateTime.UtcNow.AddMinutes(90);
        var (provider, service, slots) = await TestData.ProviderAsync(db, [start]);
        var customer = await TestData.CustomerAsync(db);
        var bookings = SqlServerFixture.CreateBookingService(db);
        var created = await bookings.CreateAsync(customer.Id, Command(provider, service, slots[0]));
        Assert.True(created.Succeeded, created.Error);

        var cancel = await bookings.CancelAsync(customer.Id, created.Value, "changed my mind", asAdmin: false);

        Assert.False(cancel.Succeeded);
        Assert.Contains("2 hours", cancel.Error);
    }

    [Fact]
    public async Task Cancelling_releases_the_slot_for_someone_else()
    {
        await using var db = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(db, [TestData.HoursFromNow(50)]);
        var first = await TestData.CustomerAsync(db);
        var second = await TestData.CustomerAsync(db);
        var bookings = SqlServerFixture.CreateBookingService(db);

        var created = await bookings.CreateAsync(first.Id, Command(provider, service, slots[0]));
        Assert.False((await bookings.CancelAsync(second.Id, created.Value, null, asAdmin: false)).Succeeded); // not their booking
        Assert.True((await bookings.CancelAsync(first.Id, created.Value, null, asAdmin: false)).Succeeded);

        var available = await bookings.GetAvailableSlotsAsync(provider.Id, 14);
        Assert.Contains(available.SelectMany(d => d.Slots), s => s.Id == slots[0].Id);
        Assert.True((await bookings.CreateAsync(second.Id, Command(provider, service, slots[0]))).Succeeded);
    }

    [Fact]
    public async Task Only_the_assigned_provider_can_accept_a_request()
    {
        await using var db = fixture.CreateDbContext();
        var mine = await TestData.ProviderAsync(db, [TestData.HoursFromNow(30)]);
        var other = await TestData.ProviderAsync(db, [TestData.HoursFromNow(30)]);
        var customer = await TestData.CustomerAsync(db);
        var bookings = SqlServerFixture.CreateBookingService(db);
        var created = await bookings.CreateAsync(customer.Id, Command(mine.Provider, mine.Service, mine.Slots[0]));

        var intruder = await bookings.AcceptAsync(other.Provider.UserId, created.Value);
        var owner = await bookings.AcceptAsync(mine.Provider.UserId, created.Value);

        Assert.Equal(ServiceErrorKind.Forbidden, intruder.ErrorKind);
        Assert.True(owner.Succeeded, owner.Error);
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == created.Value)).Status);
    }

    [Fact]
    public async Task Reviews_require_completion_are_unique_and_update_the_rating()
    {
        await using var db = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(db, [TestData.HoursFromNow(28)]);
        var customer = await TestData.CustomerAsync(db);
        var bookings = SqlServerFixture.CreateBookingService(db);
        var reviews = SqlServerFixture.CreateReviewService(db);
        var bookingId = (await bookings.CreateAsync(customer.Id, Command(provider, service, slots[0]))).Value;

        Assert.False((await reviews.SubmitAsync(customer.Id, bookingId, 5, "Too early")).Succeeded);

        Assert.True((await bookings.AcceptAsync(provider.UserId, bookingId)).Succeeded);
        Assert.True((await bookings.CompleteAsync(provider.UserId, bookingId)).Succeeded);
        var stranger = await TestData.CustomerAsync(db);
        Assert.Equal(ServiceErrorKind.Forbidden, (await reviews.SubmitAsync(stranger.Id, bookingId, 1, "Not my booking")).ErrorKind);

        var review = await reviews.SubmitAsync(customer.Id, bookingId, 4, "Good work");
        Assert.True(review.Succeeded, review.Error);
        var duplicate = await reviews.SubmitAsync(customer.Id, bookingId, 5, "Again");
        Assert.Equal(ServiceErrorKind.Conflict, duplicate.ErrorKind);

        // dbo.CalculateProviderAverageRating ran after the review.
        var stored = await db.Providers.AsNoTracking().SingleAsync(p => p.Id == provider.Id);
        Assert.Equal(4.00m, stored.AverageRating);
        Assert.Equal(1, stored.TotalReviews);
    }

    [Fact]
    public async Task Rescheduling_rechecks_availability()
    {
        await using var db = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(db, [TestData.HoursFromNow(30), TestData.HoursFromNow(32), TestData.HoursFromNow(34)]);
        var alice = await TestData.CustomerAsync(db);
        var bob = await TestData.CustomerAsync(db);
        var bookings = SqlServerFixture.CreateBookingService(db);
        var aliceBooking = (await bookings.CreateAsync(alice.Id, Command(provider, service, slots[0]))).Value;
        await bookings.CreateAsync(bob.Id, Command(provider, service, slots[1]));
        await bookings.AcceptAsync(provider.UserId, aliceBooking);

        var clash = await bookings.RescheduleAsync(alice.Id, aliceBooking, slots[1].Id);
        var moved = await bookings.RescheduleAsync(alice.Id, aliceBooking, slots[2].Id);

        Assert.Equal(ServiceErrorKind.Conflict, clash.ErrorKind);
        Assert.True(moved.Succeeded, moved.Error);
        await using var verify = fixture.CreateDbContext();
        var booking = await verify.Bookings.SingleAsync(b => b.Id == aliceBooking);
        Assert.Equal(slots[2].Id, booking.TimeSlotId);
        Assert.Equal(BookingStatus.Pending, booking.Status); // provider must accept the new time
        Assert.Equal(1, booking.RescheduleCount);
    }

    [Fact]
    public async Task A_slot_completed_early_is_not_offered_again()
    {
        await using var db = fixture.CreateDbContext();
        var (provider, service, slots) = await TestData.ProviderAsync(db, [TestData.HoursFromNow(36)]);
        var customer = await TestData.CustomerAsync(db);
        var other = await TestData.CustomerAsync(db);
        var bookings = SqlServerFixture.CreateBookingService(db);
        var id = (await bookings.CreateAsync(customer.Id, Command(provider, service, slots[0]))).Value;
        await bookings.AcceptAsync(provider.UserId, id);
        await bookings.CompleteAsync(provider.UserId, id);

        Assert.DoesNotContain((await bookings.GetAvailableSlotsAsync(provider.Id)).SelectMany(d => d.Slots), s => s.Id == slots[0].Id);
        Assert.Equal(ServiceErrorKind.Conflict, (await bookings.CreateAsync(other.Id, Command(provider, service, slots[0]))).ErrorKind);
    }
}
