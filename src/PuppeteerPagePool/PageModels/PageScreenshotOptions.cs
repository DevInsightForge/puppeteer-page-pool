namespace PuppeteerPagePool.PageModels;

public sealed class PageScreenshotOptions
{
    public PageScreenshotFormat Format { get; set; } = PageScreenshotFormat.Png;

    public bool FullPage { get; set; }

    public int? Quality { get; set; }

    public bool OmitBackground { get; set; }

    public bool CaptureBeyondViewport { get; set; } = true;
}
