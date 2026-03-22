using PuppeteerPagePool.Configuration;
using PuppeteerPagePool.PageModels;
using PuppeteerSharp;
using PuppeteerSharp.Input;
using PuppeteerSharp.Media;

namespace PuppeteerPagePool.Internal;

internal static class PuppeteerOptionMapper
{
    public static WaitUntilNavigation[] ToWaitUntilNavigation(PagePoolNavigationWaitUntil[] waitConditions)
    {
        var waitUntil = new WaitUntilNavigation[waitConditions.Length];
        for (var index = 0; index < waitConditions.Length; index++)
        {
            waitUntil[index] = waitConditions[index] switch
            {
                PagePoolNavigationWaitUntil.Load => WaitUntilNavigation.Load,
                PagePoolNavigationWaitUntil.DOMContentLoaded => WaitUntilNavigation.DOMContentLoaded,
                PagePoolNavigationWaitUntil.Networkidle0 => WaitUntilNavigation.Networkidle0,
                PagePoolNavigationWaitUntil.Networkidle2 => WaitUntilNavigation.Networkidle2,
                _ => throw new ArgumentOutOfRangeException(nameof(waitConditions))
            };
        }

        return waitUntil;
    }

    public static NavigationOptions ToNavigationOptions(PageNavigationOptions? options)
    {
        return new NavigationOptions
        {
            Timeout = ToTimeoutMilliseconds(options?.Timeout),
            WaitUntil = options is null ? null : ToWaitUntilNavigation(options.WaitUntil)
        };
    }

    public static NavigationOptions ToNavigationOptions(PageContentOptions? options)
    {
        return new NavigationOptions
        {
            Timeout = ToTimeoutMilliseconds(options?.Timeout),
            WaitUntil = options is null ? null : ToWaitUntilNavigation(options.WaitUntil)
        };
    }

    public static WaitForSelectorOptions ToWaitForSelectorOptions(PageWaitForSelectorOptions? options)
    {
        return new WaitForSelectorOptions
        {
            Timeout = ToTimeoutMilliseconds(options?.Timeout),
            Visible = options?.Visible ?? false,
            Hidden = options?.Hidden ?? false
        };
    }

    public static WaitForFunctionOptions ToWaitForFunctionOptions(PageWaitForFunctionOptions? options)
    {
        return new WaitForFunctionOptions
        {
            Timeout = ToTimeoutMilliseconds(options?.Timeout),
            PollingInterval = options?.PollingInterval is null
                ? null
                : (int)Math.Round(options.PollingInterval.Value.TotalMilliseconds)
        };
    }

    public static WaitForNetworkIdleOptions ToWaitForNetworkIdleOptions(PageWaitForNetworkIdleOptions? options)
    {
        return new WaitForNetworkIdleOptions
        {
            Timeout = ToTimeoutMilliseconds(options?.Timeout),
            IdleTime = ToTimeoutMilliseconds(options?.IdleTime)
        };
    }

    public static ClickOptions ToClickOptions(PageClickOptions? options)
    {
        return new ClickOptions
        {
            Button = options?.Button switch
            {
                PageMouseButton.Left => MouseButton.Left,
                PageMouseButton.Middle => MouseButton.Middle,
                PageMouseButton.Right => MouseButton.Right,
                null => MouseButton.Left,
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            },
            Count = options?.ClickCount ?? 1,
            Delay = options?.DelayMilliseconds ?? 0
        };
    }

    public static TypeOptions ToTypeOptions(PageTypeOptions? options)
    {
        return new TypeOptions
        {
            Delay = options?.DelayMilliseconds ?? 0
        };
    }

    public static ScreenshotOptions ToScreenshotOptions(PageScreenshotOptions? options)
    {
        return new ScreenshotOptions
        {
            Type = options?.Format switch
            {
                PageScreenshotFormat.Png => ScreenshotType.Png,
                PageScreenshotFormat.Jpeg => ScreenshotType.Jpeg,
                PageScreenshotFormat.Webp => ScreenshotType.Webp,
                null => ScreenshotType.Png,
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            },
            FullPage = options?.FullPage ?? false,
            Quality = options?.Quality,
            OmitBackground = options?.OmitBackground ?? false,
            CaptureBeyondViewport = options?.CaptureBeyondViewport ?? true
        };
    }

    public static PdfOptions ToPdfOptions(PagePdfOptions? options)
    {
        var pdfOptions = new PdfOptions
        {
            Format = options?.Format switch
            {
                PagePdfFormat.Letter => PaperFormat.Letter,
                PagePdfFormat.Legal => PaperFormat.Legal,
                PagePdfFormat.Tabloid => PaperFormat.Tabloid,
                PagePdfFormat.Ledger => PaperFormat.Ledger,
                PagePdfFormat.A0 => PaperFormat.A0,
                PagePdfFormat.A1 => PaperFormat.A1,
                PagePdfFormat.A2 => PaperFormat.A2,
                PagePdfFormat.A3 => PaperFormat.A3,
                PagePdfFormat.A4 => PaperFormat.A4,
                PagePdfFormat.A5 => PaperFormat.A5,
                PagePdfFormat.A6 => PaperFormat.A6,
                null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            },
            Width = options?.Width,
            Height = options?.Height,
            Landscape = options?.Landscape ?? false,
            PrintBackground = options?.PrintBackground ?? true,
            PreferCSSPageSize = options?.PreferCssPageSize ?? false
        };

        if (options?.Scale is not null)
        {
            pdfOptions.Scale = options.Scale.Value;
        }

        if (options?.Margin is not null)
        {
            pdfOptions.MarginOptions = new MarginOptions
            {
                Top = options.Margin.Top,
                Right = options.Margin.Right,
                Bottom = options.Margin.Bottom,
                Left = options.Margin.Left
            };
        }

        return pdfOptions;
    }

    public static int? ToTimeoutMilliseconds(TimeSpan? timeout)
    {
        if (timeout is null)
        {
            return null;
        }

        return (int)Math.Round(timeout.Value.TotalMilliseconds);
    }
}
