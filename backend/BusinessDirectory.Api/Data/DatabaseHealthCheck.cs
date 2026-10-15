using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BusinessDirectory.Api.Data;

/// <summary>Readiness check: can the API reach PostgreSQL? Never leaks error details to callers.</summary>
public sealed class DatabaseHealthCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var dataSource = services.GetService<NpgsqlDataSource>();
        if (dataSource is null)
        {
            return HealthCheckResult.Unhealthy("Database is not configured.");
        }

        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.");
        }
    }
}
