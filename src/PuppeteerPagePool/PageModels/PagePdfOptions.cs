namespace PuppeteerPagePool.PageModels;

public sealed class PagePdfOptions
{
    public PagePdfFormat? Format { get; set; }

    public string? Width { get; set; }

    public string? Height { get; set; }

    public bool Landscape { get; set; }

    public bool PrintBackground { get; set; } = true;

    public bool PreferCssPageSize { get; set; }

    public decimal? Scale { get; set; }

    public PagePdfMarginOptions? Margin { get; set; }
}
