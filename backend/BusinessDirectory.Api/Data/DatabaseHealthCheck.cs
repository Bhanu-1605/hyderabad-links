using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BusinessDirectory.Api.Data;

/// <summary>
/// Readiness check: can the API reach SQL Server?
/// Never leaks error details to callers.
/// </summary>
public sealed class DatabaseHealthCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var dataSource = services.GetService<SqlDatabase>();

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
