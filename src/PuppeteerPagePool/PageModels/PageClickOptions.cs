namespace PuppeteerPagePool.PageModels;

public sealed class PageClickOptions
{
    public PageMouseButton Button { get; set; } = PageMouseButton.Left;

    public int ClickCount { get; set; } = 1;

    public int? DelayMilliseconds { get; set; }
}
