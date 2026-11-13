using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LocalServicesMarketplace.Middleware;

/// <summary>
/// Catches unhandled exceptions for the whole pipeline.
///   /api/*  -> RFC 7807 problem+json with a trace id (details only in Development)
///   others  -> re-executes /Home/Error so users see the branded error page
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to report.
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            logger.LogError(ex, "Unhandled exception for {Method} {Path} (trace {TraceId})", context.Request.Method, context.Request.Path, traceId);

            if (context.Response.HasStarted) throw;

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = env.IsDevelopment() ? ex.Message : "Please try again later.",
                    Instance = context.Request.Path
                };
                problem.Extensions["traceId"] = traceId;
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
                return;
            }

            context.Items["ErrorTraceId"] = traceId;
            context.Items["ErrorMessage"] = env.IsDevelopment() ? ex.ToString() : null;
            var originalPath = context.Request.Path;
            var originalMethod = context.Request.Method;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            // Re-execute the pipeline for the error page (same approach as the built-in UseExceptionHandler).
            context.SetEndpoint(null);
            context.Features.Get<Microsoft.AspNetCore.Http.Features.IRouteValuesFeature>()?.RouteValues.Clear();
            context.Request.Path = "/Home/Error";
            context.Request.Method = HttpMethods.Get;
            try
            {
                await next(context);
            }
            finally
            {
                context.Request.Path = originalPath;
                context.Request.Method = originalMethod;
            }
        }
    }
}
