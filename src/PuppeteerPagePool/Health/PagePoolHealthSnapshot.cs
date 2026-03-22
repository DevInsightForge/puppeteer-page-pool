namespace PuppeteerPagePool;

/// <summary>
/// Represents current pool counters and readiness state.
/// </summary>
/// <param name="PoolSize">Configured maximum number of pooled pages.</param>
/// <param name="AvailablePages">Number of pages currently available for lease.</param>
/// <param name="LeasedPages">Number of pages currently leased.</param>
/// <param name="WaitingRequests">Number of callers currently waiting for a page.</param>
/// <param name="BrowserConnected">Indicates whether the underlying browser session is connected.</param>
/// <param name="AcceptingLeases">Indicates whether the pool currently accepts new leases.</param>
/// <param name="ReplacementCount">Total number of page replacements since startup.</param>
/// <param name="BrowserRestartCount">Total number of browser rebuilds since startup.</param>
public sealed record PagePoolHealthSnapshot(
    int PoolSize,
    int AvailablePages,
    int LeasedPages,
    int WaitingRequests,
    bool BrowserConnected,
    bool AcceptingLeases,
    int ReplacementCount,
    int BrowserRestartCount);
