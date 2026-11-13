using System.Diagnostics;

namespace LocalServicesMarketplace.Middleware;

/// <summary>Measures every request, adds an X-Response-Time-ms header and logs slow requests.</summary>
public class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
{
    private const int SlowRequestMs = 1000;

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Response-Time-ms"] = stopwatch.ElapsedMilliseconds.ToString();
            return Task.CompletedTask;
        });

        await next(context);

        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;
        if (elapsed >= SlowRequestMs)
            logger.LogWarning("Slow request {Method} {Path} -> {Status} in {Elapsed} ms", context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed);
        else if (!context.Request.Path.StartsWithSegments("/lib") && !context.Request.Path.StartsWithSegments("/css") && !context.Request.Path.StartsWithSegments("/js"))
            logger.LogDebug("{Method} {Path} -> {Status} in {Elapsed} ms", context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed);
    }
}
