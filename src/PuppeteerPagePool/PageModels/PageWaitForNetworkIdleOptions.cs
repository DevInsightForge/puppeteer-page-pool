namespace PuppeteerPagePool.PageModels;

public sealed class PageWaitForNetworkIdleOptions
{
    public TimeSpan? Timeout { get; set; }

    public TimeSpan? IdleTime { get; set; }
}
