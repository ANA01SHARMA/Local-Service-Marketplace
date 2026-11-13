using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Data.Seed;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Middleware;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services;
using LocalServicesMarketplace.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- configuration (Options pattern)
builder.Services.AddOptions<MarketplaceOptions>().Bind(config.GetSection(MarketplaceOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SeedOptions>().Bind(config.GetSection(SeedOptions.Section));
builder.Services.AddOptions<SmtpOptions>().Bind(config.GetSection(SmtpOptions.Section));
builder.Services.AddOptions<StorageOptions>().Bind(config.GetSection(StorageOptions.Section));
builder.Services.AddOptions<AppOptions>().Bind(config.GetSection(AppOptions.Section));
builder.Services.AddOptions<DatabaseOptions>().Bind(config.GetSection(DatabaseOptions.Section));

var storageRoot = config["Storage:Root"] ?? "storage";
storageRoot = Path.IsPathRooted(storageRoot) ? storageRoot : Path.Combine(builder.Environment.ContentRootPath, storageRoot);

// ---------------------------------------------------------------- data (EF Core + SQL Server)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured."),
        sql => sql.CommandTimeout(30)));

// ---------------------------------------------------------------- identity & authentication
builder.Services.AddIdentity<User, Role>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

// Re-validate the security stamp often so blocking / password changes take effect quickly.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "lsm.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // API callers get status codes, not redirects to the login page.
    options.Events.OnRedirectToLogin = ctx => ApiAwareRedirect(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = ctx => ApiAwareRedirect(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status403Forbidden);
});

const string SmartScheme = "CookieOrBearer";
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(options =>
    {
        // Requests with "Authorization: Bearer ..." use JWT, everything else uses the Identity cookie.
        options.DefaultScheme = SmartScheme;
        options.DefaultAuthenticateScheme = SmartScheme;
        options.DefaultChallengeScheme = SmartScheme;
        options.DefaultForbidScheme = SmartScheme;
    })
    .AddPolicyScheme(SmartScheme, "Cookie or JWT bearer", options =>
    {
        options.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.ApplicationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(string.IsNullOrEmpty(jwt.Key) ? new string('x', 32) : jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

// Persist Data Protection keys (cookies, antiforgery, reset tokens) so restarts don't log everyone out.
builder.Services.AddDataProtection()
    .SetApplicationName("LocalServicesMarketplace")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storageRoot, "keys")));

// ---------------------------------------------------------------- MVC, API, session, misc framework services
builder.Services.AddControllersWithViews(options =>
    {
        // CSRF protection for every unsafe MVC request (API controllers opt out via [IgnoreAntiforgeryToken]).
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "lsm.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Brute-force protection for login / register / password reset endpoints.
    options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var culture = config["Marketplace:Culture"] ?? "en-IN";
    options.DefaultRequestCulture = new RequestCulture(culture);
    options.SupportedCultures = [new CultureInfo(culture)];
    options.SupportedUICultures = [new CultureInfo(culture)];
});

// ---------------------------------------------------------------- application services (DI)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITimeZoneService, TimeZoneService>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddTransient<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IActivityLogger, ActivityLogger>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IProviderService, ProviderService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddHostedService<BookingExpiryService>();

var app = builder.Build();

// ---------------------------------------------------------------- database migrate + seed
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

// ---------------------------------------------------------------- HTTP pipeline (order matters)
app.UseLsmExceptionHandling();          // custom: catches everything below
app.UseRequestTiming();                 // custom: X-Response-Time-ms + slow request logging
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}"));
app.UseRequestLocalization();

// Runtime uploads (profile photos). Verification documents live in storage/private and are never served here.
var publicUploads = app.Services.GetRequiredService<IFileStorageService>().PublicRoot;
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(publicUploads),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});

app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAccountStatus();                 // custom: signs out / rejects blocked accounts
app.UseAuthorization();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.MapStaticAssets();
app.MapHealthChecks("/health");
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json - importable into Postman
}

// Convention-based routes (MVC)
app.MapControllerRoute(
        name: "provider-profile",
        pattern: "providers/{id:int}",
        defaults: new { controller = "Services", action = "Provider" })
    .WithStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Attribute-routed REST API controllers
app.MapControllers();

app.Run();

static Task ApiAwareRedirect(HttpContext context, string redirectUri, int apiStatus)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = apiStatus;
        return Task.CompletedTask;
    }
    context.Response.Redirect(redirectUri);
    return Task.CompletedTask;
}

/// <summary>Exposed so integration tests can reference the entry point.</summary>
public partial class Program;
