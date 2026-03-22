namespace PuppeteerPagePool.PageModels;

public sealed class PageWaitForSelectorOptions
{
    public TimeSpan? Timeout { get; set; }

    public bool Visible { get; set; }

    public bool Hidden { get; set; }
}
