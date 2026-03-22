using PuppeteerPagePool.Configuration;

namespace PuppeteerPagePool.PageModels;

public sealed class PageContentOptions
{
    public TimeSpan? Timeout { get; set; }

    public PagePoolNavigationWaitUntil[] WaitUntil { get; set; } = [PagePoolNavigationWaitUntil.Load];
}
