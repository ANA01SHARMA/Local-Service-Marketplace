using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LocalServicesMarketplace.Middleware;

/// <summary>
/// Enforces account blocking for already signed-in users (cookie or JWT):
/// a blocked user is signed out (MVC) or receives 403 (API) on their next request.
/// The status is cached for a few seconds to avoid a database hit per request.
/// </summary>
public class AccountStatusMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, SignInManager<User> signInManager)
    {
        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.TryGetUserId() : null;
        if (userId is null)
        {
            await next(context);
            return;
        }

        var blocked = await cache.GetOrCreateAsync($"user-blocked:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            // A deleted user counts as blocked.
            return await db.Users.Where(u => u.Id == userId).Select(u => (bool?)u.IsBlocked).FirstOrDefaultAsync() ?? true;
        });

        if (!blocked)
        {
            await next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { title = "This account has been blocked.", status = 403 });
            return;
        }

        await signInManager.SignOutAsync();
        context.Response.Redirect("/Account/Login?blocked=true");
    }
}

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseLsmExceptionHandling(this IApplicationBuilder app) => app.UseMiddleware<ExceptionHandlingMiddleware>();
    public static IApplicationBuilder UseRequestTiming(this IApplicationBuilder app) => app.UseMiddleware<RequestTimingMiddleware>();
    public static IApplicationBuilder UseAccountStatus(this IApplicationBuilder app) => app.UseMiddleware<AccountStatusMiddleware>();
}
