using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Reporting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LocalServicesMarketplace.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<User, Role, int>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ProviderVerificationDocument> ProviderVerificationDocuments => Set<ProviderVerificationDocument>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    // SQL Server views (read-only, keyless)
    public DbSet<TopRatedProviderView> TopRatedProviders => Set<TopRatedProviderView>();
    public DbSet<MonthlyBookingStatistic> MonthlyBookingStatistics => Set<MonthlyBookingStatistic>();
    public DbSet<ProviderPerformance> ProviderPerformance => Set<ProviderPerformance>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every DateTime in the model is UTC. SQL Server's datetime2 has no kind, so
        // stamp DateTimeKind.Utc on the way out of the database.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        // Money defaults to decimal(18,2); entity columns that need another size declare it explicitly.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Friendly table names for the Identity schema ("Users", "Roles", ...)
        builder.Entity<User>().ToTable("Users");
        builder.Entity<Role>().ToTable("Roles");
        builder.Entity<IdentityUserRole<int>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<int>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<int>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<int>>().ToTable("UserTokens");

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Views
        builder.Entity<TopRatedProviderView>().HasNoKey().ToView("TopRatedProviders");
        builder.Entity<MonthlyBookingStatistic>().HasNoKey().ToView("MonthlyBookingStatistics");
        builder.Entity<ProviderPerformance>().HasNoKey().ToView("ProviderPerformance");
        foreach (var type in new[] { typeof(TopRatedProviderView), typeof(MonthlyBookingStatistic), typeof(ProviderPerformance) })
        {
            foreach (var property in builder.Entity(type).Metadata.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
