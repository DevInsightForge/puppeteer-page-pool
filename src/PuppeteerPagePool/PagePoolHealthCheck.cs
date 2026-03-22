using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PuppeteerPagePool;

public sealed class PagePoolHealthCheck : IHealthCheck
{
    private readonly IPagePool _pagePool;

    public PagePoolHealthCheck(IPagePool pagePool)
    {
        _pagePool = pagePool;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await _pagePool.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (!snapshot.BrowserConnected)
        {
            return HealthCheckResult.Unhealthy("Browser is not connected.");
        }

        if (!snapshot.AcceptingLeases)
        {
            return HealthCheckResult.Degraded("Pool is not accepting leases.");
        }

        return HealthCheckResult.Healthy("Pool is ready.");
    }
}
