using PuppeteerPagePool.Configuration;

namespace PuppeteerPagePool.PageModels;

public sealed class PageNavigationOptions
{
    public TimeSpan? Timeout { get; set; }

    public PagePoolNavigationWaitUntil[] WaitUntil { get; set; } = [PagePoolNavigationWaitUntil.Load];
}
