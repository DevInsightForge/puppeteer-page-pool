namespace PuppeteerPagePool.Health;

public sealed record PagePoolHealthSnapshot(
    int PoolSize,
    int AvailablePages,
    int LeasedPages,
    int WaitingRequests,
    bool BrowserConnected,
    bool AcceptingLeases,
    TimeSpan Uptime = default,
    DateTime LastHealthCheck = default);
