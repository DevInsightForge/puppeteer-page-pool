namespace PuppeteerPagePool;

public sealed record PagePoolHealthSnapshot(
    int PoolSize,
    int AvailablePages,
    int LeasedPages,
    int WaitingRequests,
    bool BrowserConnected,
    bool AcceptingLeases,
    int ReplacementCount,
    int BrowserRestartCount);
