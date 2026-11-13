using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace LocalServicesMarketplace.Tests;

/// <summary>
/// Starts a throw-away SQL Server 2022 container (Docker required) and applies the real EF Core
/// migrations, including the views and stored procedures, so tests exercise the production SQL.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "LsmTests" }.ConnectionString;
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public static MarketplaceOptions DefaultOptions() => new() { CommissionRate = 0.10m, CancellationWindowHours = 2, MinBookingLeadMinutes = 60, MaxReschedules = 2, AllowEarlyCompletion = true };

    public static BookingService CreateBookingService(ApplicationDbContext db, MarketplaceOptions? options = null)
    {
        var (opts, tz, notifications, activity) = Dependencies(db, options);
        return new BookingService(db, notifications, activity, tz, opts, NullLogger<BookingService>.Instance);
    }

    public static ReviewService CreateReviewService(ApplicationDbContext db)
    {
        var (_, _, notifications, activity) = Dependencies(db, null);
        return new ReviewService(db, notifications, activity, TimeProvider.System);
    }

    private static (IOptions<MarketplaceOptions>, TimeZoneService, NotificationService, ActivityLogger) Dependencies(ApplicationDbContext db, MarketplaceOptions? options)
    {
        var opts = Options.Create(options ?? DefaultOptions());
        var tz = new TimeZoneService(opts, TimeProvider.System, NullLogger<TimeZoneService>.Instance);
        return (opts, tz, new NotificationService(db, TimeProvider.System), new ActivityLogger(db, TimeProvider.System, NullLogger<ActivityLogger>.Instance));
    }
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

/// <summary>Creates isolated rows for each test (unique names), so tests can share one database.</summary>
public static class TestData
{
    public static async Task<User> CustomerAsync(ApplicationDbContext db, string city = "New Delhi")
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var user = new User
        {
            UserName = $"c{id}@test.local", NormalizedUserName = $"C{id}@TEST.LOCAL", Email = $"c{id}@test.local", NormalizedEmail = $"C{id}@TEST.LOCAL",
            FullName = $"Customer {id}", City = city, SecurityStamp = Guid.NewGuid().ToString(), CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<(Provider Provider, Service Service, List<TimeSlot> Slots)> ProviderAsync(
        ApplicationDbContext db, IEnumerable<DateTime> slotStartsUtc, VerificationStatus status = VerificationStatus.Approved, decimal price = 500m)
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var category = new Category { Name = $"Cat {id}", Slug = $"cat-{id}", CreatedAtUtc = DateTime.UtcNow };
        var user = new User
        {
            UserName = $"p{id}@test.local", NormalizedUserName = $"P{id}@TEST.LOCAL", Email = $"p{id}@test.local", NormalizedEmail = $"P{id}@TEST.LOCAL",
            FullName = $"Provider {id}", City = "New Delhi", SecurityStamp = Guid.NewGuid().ToString(), CreatedAtUtc = DateTime.UtcNow
        };
        var provider = new Provider { User = user, Category = category, City = "New Delhi", VerificationStatus = status, IsAcceptingBookings = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var service = new Service { Provider = provider, Category = category, Title = "Test service", Price = price, DurationMinutes = 60, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var slots = slotStartsUtc.Select(s => new TimeSlot { Provider = provider, StartUtc = s, EndUtc = s.AddHours(1), IsActive = true, CreatedAtUtc = DateTime.UtcNow }).ToList();
        db.AddRange(category, user, provider, service);
        db.TimeSlots.AddRange(slots);
        await db.SaveChangesAsync();
        return (provider, service, slots);
    }

    /// <summary>Whole-hour UTC time a number of hours from now (keeps slots aligned like real ones).</summary>
    public static DateTime HoursFromNow(double hours)
    {
        var t = DateTime.UtcNow.AddHours(hours);
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
    }
}
