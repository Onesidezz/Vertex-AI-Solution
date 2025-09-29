using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocumentProcessingAPI.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Qdrant vector database
/// </summary>
public class QdrantHealthCheck : IHealthCheck
{
    private readonly IVectorDatabaseService _vectorDatabaseService;

    public QdrantHealthCheck(IVectorDatabaseService vectorDatabaseService)
    {
        _vectorDatabaseService = vectorDatabaseService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _vectorDatabaseService.IsCollectionHealthyAsync();

            if (isHealthy)
            {
                var stats = await _vectorDatabaseService.GetCollectionStatsAsync();
                return HealthCheckResult.Healthy($"Qdrant is healthy. Vector count: {stats.VectorCount}");
            }

            return HealthCheckResult.Unhealthy("Qdrant collection is not healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Qdrant health check failed: {ex.Message}", ex);
        }
    }
}