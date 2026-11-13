using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Infrastructure;

// Strongly typed configuration bound from appsettings.json / environment variables (Options pattern).

public class MarketplaceOptions
{
    public const string Section = "Marketplace";

    /// <summary>Platform commission as a fraction of the booking price (0.10 = 10%).</summary>
    [Range(0, 0.5)]
    public decimal CommissionRate { get; set; } = 0.10m;

    /// <summary>Customers cannot cancel or reschedule within this many hours of the appointment.</summary>
    [Range(0, 72)]
    public int CancellationWindowHours { get; set; } = 2;

    /// <summary>Slots starting sooner than this cannot be booked.</summary>
    [Range(0, 1440)]
    public int MinBookingLeadMinutes { get; set; } = 60;

    [Range(0, 10)]
    public int MaxReschedules { get; set; } = 2;

    /// <summary>
    /// When false, a confirmed job can only be marked completed after its start time.
    /// Defaults to true so a live demo does not have to wait for the appointment time.
    /// </summary>
    public bool AllowEarlyCompletion { get; set; } = true;

    /// <summary>IANA / Windows time zone used to display UTC timestamps.</summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    [Required]
    public string Culture { get; set; } = "en-IN";

    public string[] Cities { get; set; } = [];
}

public class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = "LocalServicesMarketplace";
    [Required] public string Audience { get; set; } = "LocalServicesMarketplace.Api";

    [Required, MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters.")]
    public string Key { get; set; } = string.Empty;

    [Range(5, 1440)]
    public int ExpiryMinutes { get; set; } = 120;
}

public class SeedOptions
{
    public const string Section = "Seed";

    public bool Enabled { get; set; }
    public bool SampleData { get; set; } = true;
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string DemoUserPassword { get; set; } = "Demo@12345";

    /// <summary>Random seed for sample data. 0 = a new random dataset on every fresh database; any other value reproduces one.</summary>
    public int RandomSeed { get; set; }
}

public class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool EnableSsl { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@localservices.test";
    public string FromName { get; set; } = "Local Services Marketplace";
    public string SupportAddress { get; set; } = "support@localservices.test";
}

public class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Root folder for uploads. "public" is served at /uploads, "private" never is.</summary>
    public string Root { get; set; } = "storage";

    public long MaxProfileImageBytes { get; set; } = 2 * 1024 * 1024;
    public long MaxDocumentBytes { get; set; } = 5 * 1024 * 1024;
}

public class AppOptions
{
    public const string Section = "App";

    public string Name { get; set; } = "Local Services Marketplace";
    public string BrandShort { get; set; } = "Kaarigar";
    public string? PublicBaseUrl { get; set; }
}

public class DatabaseOptions
{
    public const string Section = "Database";
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
