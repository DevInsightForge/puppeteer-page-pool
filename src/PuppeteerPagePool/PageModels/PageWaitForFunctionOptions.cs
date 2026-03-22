namespace PuppeteerPagePool.PageModels;

public sealed class PageWaitForFunctionOptions
{
    public TimeSpan? Timeout { get; set; }

    public TimeSpan? PollingInterval { get; set; }
}
