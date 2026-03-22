using Microsoft.Extensions.Diagnostics.HealthChecks;
using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Health;

/// <summary>
/// Reports readiness for the registered page pool instance.
/// </summary>
public sealed class PagePoolHealthCheck : IHealthCheck
{
    private readonly IPagePool _pagePool;

    /// <summary>
    /// Initializes a new instance of the <see cref="PagePoolHealthCheck"/> class.
    /// </summary>
    /// <param name="pagePool">The registered page pool.</param>
    public PagePoolHealthCheck(IPagePool pagePool)
    {
        _pagePool = pagePool;
    }

    /// <summary>
    /// Executes the pool readiness check.
    /// </summary>
    /// <param name="context">Health check context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A health check result for browser connectivity and lease readiness.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_pagePool is not PagePool concretePool)
        {
            return HealthCheckResult.Unhealthy($"Registered {nameof(IPagePool)} implementation is unsupported.");
        }

        var snapshot = await _pagePool.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (!snapshot.BrowserConnected)
        {
            return HealthCheckResult.Unhealthy("Browser is not connected.");
        }

        if (!await concretePool.IsBrowserHealthyAsync(cancellationToken).ConfigureAwait(false))
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
