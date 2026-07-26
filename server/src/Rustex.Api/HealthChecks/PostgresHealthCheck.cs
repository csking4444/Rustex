using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.HealthChecks;

public class PostgresHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public PostgresHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Cannot reach Postgres");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postgres check threw", ex);
        }
    }
}
