using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Data.Seed;

/// <summary>
/// Runs at startup:
///   1. applies EF Core migrations (creates tables, indexes, views and stored procedures)
///   2. seeds reference data that the app needs in every environment (roles, categories)
///   3. DEVELOPMENT ONLY + Seed:Enabled: the initial admin (credentials from configuration, never hard-coded)
///      and, if Seed:SampleData, clearly-flagged demo customers / providers / bookings.
/// </summary>
public class DatabaseInitializer(
    ApplicationDbContext db,
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    IOptions<SeedOptions> seedOptions,
    IOptions<DatabaseOptions> dbOptions,
    IWebHostEnvironment env,
    IServiceProvider services,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (dbOptions.Value.ApplyMigrationsOnStartup)
        {
            await MigrateWithRetryAsync(ct);
        }

        await SeedRolesAsync();
        await SeedCategoriesAsync(ct);

        var seed = seedOptions.Value;
        if (!env.IsDevelopment() || !seed.Enabled)
        {
            logger.LogInformation("Development seeding skipped (environment {Env}, Seed:Enabled={Enabled})", env.EnvironmentName, seed.Enabled);
            return;
        }

        await SeedAdminAsync(seed);
        if (seed.SampleData)
        {
            var sample = ActivatorUtilities.CreateInstance<SampleDataSeeder>(services);
            await sample.SeedAsync(seed.DemoUserPassword, ct);
        }
    }

    private async Task MigrateWithRetryAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                    logger.LogInformation("Applying {Count} migration(s): {Names}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(ct);
                return;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (attempt < 10 && IsTransientConnectionError(ex))
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/10): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(3 * attempt, 15)), ct);
            }
        }
    }

    // Errors that mean "SQL Server is not up yet" (container still starting), not a broken migration.
    private static bool IsTransientConnectionError(Microsoft.Data.SqlClient.SqlException ex) =>
        ex.Class >= 20 || ex.Number is -2 or -1 or 2 or 53 or 233 or 4060 or 18456 or 10054 or 10060 or 10061 or 40613;

    private async Task SeedRolesAsync()
    {
        var descriptions = new Dictionary<string, string>
        {
            [AppRoles.Admin] = "Platform administrator",
            [AppRoles.Customer] = "Books services",
            [AppRoles.Provider] = "Offers services"
        };
        foreach (var (name, description) in descriptions)
        {
            if (!await roleManager.RoleExistsAsync(name))
                await roleManager.CreateAsync(new Role(name) { Description = description });
        }
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.Categories.Select(c => c.Slug).ToListAsync(ct);
        var added = 0;
        foreach (var (name, slug, icon, description, order) in SeedData.Categories)
        {
            if (existing.Contains(slug)) continue;
            db.Categories.Add(new Category
            {
                Name = name, Slug = slug, Icon = icon, Description = description, DisplayOrder = order,
                ImagePath = $"/images/categories/{slug}.svg", IsActive = true, CreatedAtUtc = now
            });
            added++;
        }
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} service categories", added);
        }
    }

    private async Task SeedAdminAsync(SeedOptions seed)
    {
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            logger.LogWarning("Seed:AdminEmail / Seed:AdminPassword not configured - no admin account was created.");
            return;
        }
        if (await userManager.FindByEmailAsync(seed.AdminEmail) is not null) return;

        var admin = new User
        {
            UserName = seed.AdminEmail,
            Email = seed.AdminEmail,
            EmailConfirmed = true,
            FullName = "Platform Admin",
            City = "New Delhi",
            MustChangePassword = true,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        var result = await userManager.CreateAsync(admin, seed.AdminPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create the seed admin: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        logger.LogInformation("Seeded development admin account {Email}. Change its password after first login.", seed.AdminEmail);
    }
}
