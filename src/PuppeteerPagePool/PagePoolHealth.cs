using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PuppeteerPagePool;

/// <summary>
/// Represents the current externally visible health and capacity state of the pool.
/// </summary>
public sealed record PagePoolHealthSnapshot(
    int PoolSize,
    int AvailablePages,
    int LeasedPages,
    int WaitingRequests,
    bool BrowserConnected,
    bool AcceptingLeases);

/// <summary>
/// Implements a readiness health check for the registered page pool.
/// </summary>
/// <remarks>
/// Initializes a new health check for the supplied page pool.
/// </remarks>
internal sealed class PagePoolHealthCheck(PagePool pagePool) : IHealthCheck
{


    /// <summary>
    /// Evaluates browser connectivity and lease readiness.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await pagePool.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (!snapshot.BrowserConnected)
        {
            return HealthCheckResult.Unhealthy("Browser is not connected.");
        }

        if (!await pagePool.IsHealthyAsync(cancellationToken).ConfigureAwait(false))
        {
            return HealthCheckResult.Unhealthy("Browser is unresponsive.");
        }

        if (!snapshot.AcceptingLeases)
        {
            return HealthCheckResult.Degraded("Pool is not accepting leases.");
        }

        return HealthCheckResult.Healthy("Pool is ready.");
    }
}
