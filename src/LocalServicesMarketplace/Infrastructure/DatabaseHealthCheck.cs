using LocalServicesMarketplace.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LocalServicesMarketplace.Infrastructure;

/// <summary>Used by GET /health (and the Docker healthcheck).</summary>
public class DatabaseHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("SQL Server reachable")
            : HealthCheckResult.Unhealthy("SQL Server unreachable");
}
