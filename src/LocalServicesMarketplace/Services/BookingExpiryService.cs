using LocalServicesMarketplace.Services.Interfaces;

namespace LocalServicesMarketplace.Services;

/// <summary>
/// Hosted background service: every few minutes, auto-cancels Pending requests whose appointment
/// time has passed without the provider responding (so they don't stay "pending" forever).
/// </summary>
public class BookingExpiryService(IServiceScopeFactory scopeFactory, ILogger<BookingExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give migrations / seeding a head start.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var expired = await bookings.ExpireStalePendingBookingsAsync(stoppingToken);
                if (expired > 0) logger.LogInformation("Expired {Count} stale pending bookings", expired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Booking expiry run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
