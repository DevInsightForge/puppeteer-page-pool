# PuppeteerPagePool

`PuppeteerPagePool` is a .NET library for running PuppeteerSharp work through a bounded, reusable page pool.

It is designed for workloads such as:

- scraping and extraction
- HTML rendering
- PDF generation
- screenshot generation
- repeated browser automation where page reuse matters

The package is intentionally pool-centric:

- callers never receive raw `IPage`, `IBrowser`, or `IBrowserContext`
- page ownership stays inside the pool
- work is executed through callbacks
- page reset, recycle, and replacement are handled by the package
- health and throughput can be observed through a pool snapshot and health checks

## Why this package exists

Creating a new browser page for every operation is simple, but expensive under sustained load. A pool gives you:

- bounded concurrency
- deterministic reuse
- reduced browser and page churn
- central lifecycle management
- safer cleanup between tasks

This package does not try to expose the full PuppeteerSharp API. Instead, it exposes a focused `ILeasedPage` contract that covers common browser workloads while keeping pooled pages safe to reuse.

## Installation

```bash
dotnet add package PuppeteerPagePool
```

## Target frameworks

- `net8.0`
- `net10.0`

## Core concepts

### `IPagePool`

`IPagePool` is the main entry point. It leases a page, runs your callback, resets or recycles the page, and returns it to the pool.

```csharp
public interface IPagePool : IAsyncDisposable
{
    ValueTask ExecuteAsync(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
```

Important characteristics:

- the pool remains the owner of every page
- pages are not returned to callers
- callbacks are the only supported way to use a page
- when your callback ends, the lease ends

### `ILeasedPage`

`ILeasedPage` is the safe page abstraction you use inside the callback. It includes common operations for:

- navigation
- content loading
- script evaluation
- waiting and readiness
- interaction
- rendering and output
- readonly page state

It does not expose lifecycle-breaking methods such as `CloseAsync` or `DisposeAsync`.

If a leased page is used after the callback has returned, the package throws `PageLeaseExpiredException`.

## Quick start

### Register the pool

```csharp
using Microsoft.Extensions.DependencyInjection;
using PuppeteerPagePool;
using PuppeteerPagePool.Configuration;
using PuppeteerPagePool.DependencyInjection;

var services = new ServiceCollection();

services.AddPagePool(options =>
{
    options.PoolSize = 4;
    options.AcquireTimeout = TimeSpan.FromSeconds(20);
    options.ResetTargetUrl = "about:blank";
    options.Browser = PagePoolBrowser.Chrome;
});

await using var provider = services.BuildServiceProvider();
var pool = provider.GetRequiredService<IPagePool>();
```

### Execute work against a leased page

```csharp
await pool.ExecuteAsync(async (page, cancellationToken) =>
{
    await page.GoToAsync(
        "https://example.com",
        new PageNavigationOptions
        {
            WaitUntil = [PagePoolNavigationWaitUntil.DOMContentLoaded]
        },
        cancellationToken);

    var title = await page.GetTitleAsync(cancellationToken);
    Console.WriteLine(title);
});
```

### Return a result from the callback

```csharp
var html = await pool.ExecuteAsync(
    async (page, cancellationToken) =>
    {
        await page.GoToAsync("https://example.com", cancellationToken: cancellationToken);
        return await page.GetContentAsync(cancellationToken);
    });
```

## Dependency injection

Register the pool once per process, typically as part of app startup:

```csharp
services.AddPagePool(options =>
{
    options.PoolSize = 8;
    options.AcquireTimeout = TimeSpan.FromSeconds(30);
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
    options.WarmupOnStartup = true;
    options.ResetTargetUrl = "about:blank";
    options.ClearStorageOnReturn = true;
    options.JavaScriptEnabled = true;
    options.Browser = PagePoolBrowser.Chrome;
});
```

You can also configure advanced pooling behavior:

```csharp
services.AddPagePool(
    options =>
    {
        options.PoolSize = 8;
        options.ResetTargetUrl = "about:blank";
    },
    advanced =>
    {
        advanced.MaxPageUses = 500;
        advanced.MaxConsecutiveLeaseFailures = 3;
        advanced.OperationFailureHandling = PageOperationFailureHandling.ResetPage;
        advanced.ResetStrategy = PageResetStrategy.Navigate;
        advanced.ClearCookiesOnReturn = true;
        advanced.ValidatePageHealthBeforeLease = true;
    });
```

## Using a local browser

If you want the pool to launch a local browser:

```csharp
services.AddPagePool(options =>
{
    options.Browser = PagePoolBrowser.Chrome;
    options.LaunchOptions = new PagePoolLaunchOptions
    {
        Headless = true,
        TimeoutMilliseconds = 30_000,
        Args =
        [
            "--no-sandbox",
            "--disable-dev-shm-usage"
        ]
    };
});
```

If `ExecutablePath` is set, the package uses that executable and validates that it matches the configured browser family:

```csharp
services.AddPagePool(options =>
{
    options.Browser = PagePoolBrowser.Chrome;
    options.ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
});
```

If no executable path is provided and advanced browser download is enabled, the package can download a compatible browser build automatically.

## Using a remote browser

To attach to an existing browser instance instead of launching one:

```csharp
services.AddPagePool(options =>
{
    options.ConnectOptions = new PagePoolConnectOptions
    {
        BrowserWebSocketEndpoint = "ws://localhost:3000/devtools/browser/your-browser-id"
    };
});
```

Or:

```csharp
services.AddPagePool(options =>
{
    options.ConnectOptions = new PagePoolConnectOptions
    {
        BrowserUrl = "http://localhost:3000"
    };
});
```

## `ILeasedPage` API

The current leased page surface is intentionally limited to broadly useful, pool-safe operations.

### Readonly state

```csharp
bool IsClosed { get; }
string Url { get; }
ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default);
ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default);
```

### Navigation and content

```csharp
ValueTask GoToAsync(string url, PageNavigationOptions? options = null, CancellationToken cancellationToken = default);
ValueTask WaitForNavigationAsync(PageNavigationOptions? options = null, CancellationToken cancellationToken = default);
ValueTask SetContentAsync(string html, PageContentOptions? options = null, CancellationToken cancellationToken = default);
```

### Waiting and readiness

```csharp
ValueTask WaitForSelectorAsync(string selector, PageWaitForSelectorOptions? options = null, CancellationToken cancellationToken = default);
ValueTask WaitForFunctionAsync(string script, object?[]? arguments = null, PageWaitForFunctionOptions? options = null, CancellationToken cancellationToken = default);
ValueTask WaitForNetworkIdleAsync(PageWaitForNetworkIdleOptions? options = null, CancellationToken cancellationToken = default);
```

### Interaction

```csharp
ValueTask FocusAsync(string selector, CancellationToken cancellationToken = default);
ValueTask ClickAsync(string selector, PageClickOptions? options = null, CancellationToken cancellationToken = default);
ValueTask TypeAsync(string selector, string text, PageTypeOptions? options = null, CancellationToken cancellationToken = default);
```

### Script evaluation

```csharp
ValueTask EvaluateExpressionAsync(string script, CancellationToken cancellationToken = default);
ValueTask<TResult> EvaluateExpressionAsync<TResult>(string script, CancellationToken cancellationToken = default);
ValueTask EvaluateFunctionAsync(string script, object?[]? arguments = null, CancellationToken cancellationToken = default);
ValueTask<TResult> EvaluateFunctionAsync<TResult>(string script, object?[]? arguments = null, CancellationToken cancellationToken = default);
```

### Rendering and output

```csharp
ValueTask<byte[]> GetScreenshotAsync(PageScreenshotOptions? options = null, CancellationToken cancellationToken = default);
ValueTask<byte[]> GetPdfAsync(PagePdfOptions? options = null, CancellationToken cancellationToken = default);
```

## Page option models

The package uses its own option models instead of exposing PuppeteerSharp public types.

### `PageNavigationOptions`

Used by `GoToAsync` and `WaitForNavigationAsync`.

- `Timeout`
- `WaitUntil`

Example:

```csharp
await page.GoToAsync(
    "https://example.com",
    new PageNavigationOptions
    {
        Timeout = TimeSpan.FromSeconds(15),
        WaitUntil =
        [
            PagePoolNavigationWaitUntil.DOMContentLoaded
        ]
    },
    cancellationToken);
```

### `PageContentOptions`

Used by `SetContentAsync`.

- `Timeout`
- `WaitUntil`

### `PageWaitForSelectorOptions`

Used by `WaitForSelectorAsync`.

- `Timeout`
- `Visible`
- `Hidden`

Example:

```csharp
await page.WaitForSelectorAsync(
    ".result",
    new PageWaitForSelectorOptions
    {
        Timeout = TimeSpan.FromSeconds(10),
        Visible = true
    },
    cancellationToken);
```

### `PageWaitForFunctionOptions`

Used by `WaitForFunctionAsync`.

- `Timeout`
- `PollingInterval`

### `PageWaitForNetworkIdleOptions`

Used by `WaitForNetworkIdleAsync`.

- `Timeout`
- `IdleTime`

### `PageClickOptions`

Used by `ClickAsync`.

- `Button`
- `ClickCount`
- `DelayMilliseconds`

### `PageTypeOptions`

Used by `TypeAsync`.

- `DelayMilliseconds`

### `PageScreenshotOptions`

Used by `GetScreenshotAsync`.

- `Format`
- `FullPage`
- `Quality`
- `OmitBackground`
- `CaptureBeyondViewport`

Example:

```csharp
var bytes = await page.GetScreenshotAsync(
    new PageScreenshotOptions
    {
        Format = PageScreenshotFormat.Png,
        FullPage = true
    },
    cancellationToken);
```

### `PagePdfOptions`

Used by `GetPdfAsync`.

- `Format`
- `Width`
- `Height`
- `Landscape`
- `PrintBackground`
- `PreferCssPageSize`
- `Scale`
- `Margin`

Example:

```csharp
var bytes = await page.GetPdfAsync(
    new PagePdfOptions
    {
        Format = PagePdfFormat.A4,
        PrintBackground = true,
        PreferCssPageSize = true,
        Margin = new PagePdfMarginOptions
        {
            Top = "12mm",
            Right = "12mm",
            Bottom = "12mm",
            Left = "12mm"
        }
    },
    cancellationToken);
```

## Pool configuration

## `PagePoolOptions`

`PagePoolOptions` contains the main operational settings:

- `PoolSize`
  Maximum number of pages that can be leased concurrently.
- `AcquireTimeout`
  Maximum time to wait for an available page.
- `WarmupOnStartup`
  If `true`, the hosted service initializes the browser and warms the pool during startup.
- `ShutdownTimeout`
  Maximum time to wait for in-flight leases during shutdown.
- `ResetTargetUrl`
  Absolute URL used when reset strategy is `Navigate`.
- `ClearStorageOnReturn`
  Clears local storage and session storage during reset.
- `JavaScriptEnabled`
  Sets JavaScript enabled state on pooled pages.
- `Browser`
  Browser family to launch or validate.
- `ExecutablePath`
  Optional explicit browser executable.
- `LaunchOptions`
  Local launch configuration.
- `ConnectOptions`
  Remote browser connection configuration.

## `PagePoolAdvancedOptions`

Advanced options refine pooling policy, page reset, and browser handling:

- `MaxPageUses`
  Recycle a page after this many uses.
- `MaxConsecutiveLeaseFailures`
  Recycle a page after repeated callback failures.
- `OperationFailureHandling`
  Controls whether a callback failure resets the page or recycles it.
- `ResetStrategy`
  Controls how pages are reset after lease completion.
- `ResetContent`
  HTML content used when reset strategy is `SetContent`.
- `ClearCookiesOnReturn`
  Deletes cookies during reset.
- `ValidatePageHealthBeforeLease`
  Verifies the page is healthy before handing it to user code.
- `EnsureBrowserDownloaded`
  Downloads a browser if needed and no executable path is configured.
- `BrowserBuildId`
  Pins browser download to a specific build.
- `BrowserCachePath`
  Controls where downloaded browser binaries are stored.
- `BrowserHealthCheckTimeout`
  Timeout used for browser health probes.
- `ResetNavigationTimeout`
  Timeout used during reset navigation or reset content load.
- `ResetWaitConditions`
  Completion conditions for reset operations.
- `ConfigurePageAsync`
  Runs once for each newly created pooled page.
- `BeforeLeaseAsync`
  Runs before each lease is handed to user code.

### `PageOperationFailureHandling`

- `ResetPage`
  Try to reset the page after a failed callback and return it to the pool if reset succeeds.
- `RecyclePage`
  Replace the page immediately after a failed callback.

### `PageResetStrategy`

- `None`
  Skip navigation or HTML reset and only apply the remaining reset logic such as JS state, cookie cleanup, and storage cleanup.
- `Navigate`
  Reset by navigating to `ResetTargetUrl`.
- `SetContent`
  Reset by loading the configured `ResetContent`.

## Reset and recycle behavior

At the end of each callback, the pool decides whether to reuse or replace the page.

The page is typically reset and reused when:

- the callback completed successfully
- the page is still open
- the page generation is still current
- the page has not exceeded `MaxPageUses`
- the page has not exceeded `MaxConsecutiveLeaseFailures`
- reset succeeds

The page is replaced when:

- the page is closed
- the page is from an old browser generation
- the page is marked unhealthy
- the configured failure policy requires recycling
- reset fails
- page creation or replacement requires rebuilding browser state

This keeps failure handling explicit:

- callback failures are treated separately from page-health failures
- preparation failures before lease handoff are tracked separately
- reset failures are tracked separately
- browser failures can trigger a browser rebuild

## Lifecycle hooks

### `ConfigurePageAsync`

Runs once when a new pooled page is created. Use it for stable per-page initialization.

Example:

```csharp
services.AddPagePool(
    options =>
    {
        options.JavaScriptEnabled = true;
        options.ResetTargetUrl = "about:blank";
    },
    advanced =>
    {
        advanced.ConfigurePageAsync = async (page, cancellationToken) =>
        {
            await page.EvaluateFunctionAsync(
                "() => { window.__APP_POOL_READY__ = true; }",
                cancellationToken: cancellationToken);
        };
    });
```

### `BeforeLeaseAsync`

Runs every time just before a page is handed to the callback.

Example:

```csharp
services.AddPagePool(
    options =>
    {
        options.ResetTargetUrl = "about:blank";
    },
    advanced =>
    {
        advanced.BeforeLeaseAsync = async (page, cancellationToken) =>
        {
            await page.WaitForFunctionAsync(
                "() => document.readyState === 'complete' || document.readyState === 'interactive'",
                cancellationToken: cancellationToken);
        };
    });
```

## Usage examples

### Scraping example

```csharp
var result = await pool.ExecuteAsync(
    async (page, cancellationToken) =>
    {
        await page.GoToAsync(
            "https://example.com",
            new PageNavigationOptions
            {
                WaitUntil = [PagePoolNavigationWaitUntil.DOMContentLoaded],
                Timeout = TimeSpan.FromSeconds(20)
            },
            cancellationToken);

        await page.WaitForSelectorAsync(
            "h1",
            new PageWaitForSelectorOptions
            {
                Visible = true,
                Timeout = TimeSpan.FromSeconds(10)
            },
            cancellationToken);

        var title = await page.EvaluateFunctionAsync<string>(
            "() => document.querySelector('h1')?.textContent?.trim() ?? ''",
            cancellationToken: cancellationToken);

        var links = await page.EvaluateFunctionAsync<string[]>(
            @"() => Array.from(document.querySelectorAll('a'))
                .map(x => x.href)
                .filter(Boolean)",
            cancellationToken: cancellationToken);

        return new
        {
            title,
            links
        };
    });
```

### Render HTML and create a PDF

```csharp
var pdfBytes = await pool.ExecuteAsync(
    async (page, cancellationToken) =>
    {
        await page.SetContentAsync(
            """
            <!doctype html>
            <html>
            <head>
                <style>
                    body { font-family: Arial, sans-serif; padding: 24px; }
                    h1 { color: #222; }
                </style>
            </head>
            <body>
                <h1>Invoice</h1>
                <p>Generated by PuppeteerPagePool.</p>
            </body>
            </html>
            """,
            new PageContentOptions
            {
                WaitUntil = [PagePoolNavigationWaitUntil.Load]
            },
            cancellationToken);

        await page.WaitForNetworkIdleAsync(
            new PageWaitForNetworkIdleOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
                IdleTime = TimeSpan.FromMilliseconds(500)
            },
            cancellationToken);

        return await page.GetPdfAsync(
            new PagePdfOptions
            {
                Format = PagePdfFormat.A4,
                PrintBackground = true,
                PreferCssPageSize = true
            },
            cancellationToken);
    });
```

### Render HTML and create a screenshot

```csharp
var screenshotBytes = await pool.ExecuteAsync(
    async (page, cancellationToken) =>
    {
        await page.SetContentAsync(
            "<html><body><div style='padding:40px;font-size:32px'>Hello</div></body></html>",
            cancellationToken: cancellationToken);

        return await page.GetScreenshotAsync(
            new PageScreenshotOptions
            {
                Format = PageScreenshotFormat.Png,
                FullPage = true
            },
            cancellationToken);
    });
```

### Interaction example

```csharp
await pool.ExecuteAsync(async (page, cancellationToken) =>
{
    await page.GoToAsync("https://example.com/login", cancellationToken: cancellationToken);
    await page.WaitForSelectorAsync("#email", cancellationToken: cancellationToken);
    await page.TypeAsync("#email", "user@example.com", cancellationToken: cancellationToken);
    await page.TypeAsync("#password", "secret", cancellationToken: cancellationToken);
    await page.ClickAsync(
        "button[type=submit]",
        new PageClickOptions
        {
            Button = PageMouseButton.Left,
            ClickCount = 1
        },
        cancellationToken);
    await page.WaitForNavigationAsync(cancellationToken: cancellationToken);
});
```

## Health checks

If you use `Microsoft.Extensions.Diagnostics.HealthChecks`, register the built-in readiness check:

```csharp
services.AddHealthChecks()
    .AddPagePoolHealthCheck();
```

The health check reports:

- unhealthy when the browser is disconnected
- unhealthy when the browser is connected but unresponsive
- degraded when the pool is not accepting new leases
- healthy when the pool is ready

## Pool health snapshot

You can also inspect runtime state directly:

```csharp
var snapshot = await pool.GetSnapshotAsync();
```

`PagePoolHealthSnapshot` contains:

- `PoolSize`
- `AvailablePages`
- `LeasedPages`
- `WaitingRequests`
- `BrowserConnected`
- `AcceptingLeases`
- `ReplacementCount`
- `BrowserRestartCount`
- `CompletedLeaseCount`
- `OperationFailureCount`
- `PreparationFailureCount`
- `ResetFailureCount`

These counters are useful for:

- spotting saturation
- sizing `PoolSize`
- seeing whether callbacks are failing frequently
- seeing whether reset policy is too aggressive or too fragile
- identifying browser rebuild churn

## Exceptions

The package throws a small set of pool-specific exceptions:

- `PagePoolAcquireTimeoutException`
  No page became available before `AcquireTimeout`.
- `PagePoolDisposedException`
  The pool was disposed or stopped accepting leases.
- `PagePoolUnavailableException`
  Browser startup, validation, connection, or rebuild failed.
- `PageLeaseExpiredException`
  A leased page was used after the callback finished.

You should treat `PageLeaseExpiredException` as a usage bug. Do not store `ILeasedPage` references outside the callback.

## Recommended usage patterns

- Register one pool singleton per application process.
- Keep callbacks short and bounded.
- Return plain results from callbacks instead of retaining the leased page.
- Use `SetContentAsync` for server-side rendering scenarios.
- Use `GetPdfAsync` and `GetScreenshotAsync` for output generation inside the callback.
- Use `BeforeLeaseAsync` and `ConfigurePageAsync` for standardization that should be owned by the pool.
- Prefer fast reset targets such as `about:blank` when using navigation-based reset.

## Anti-patterns

- Do not cache `ILeasedPage` references.
- Do not start background work that keeps using a leased page after the callback returns.
- Do not assume the same logical page instance will be reused across operations.
- Do not use the pool as a browser orchestration layer for business workflows.

## Notes on browser download and executable validation

- If `ExecutablePath` is configured, the package validates that the executable exists and matches the configured browser family.
- If `ExecutablePath` is not configured and automatic download is enabled, the package can fetch a compatible browser.
- `ExecutablePath` cannot be combined with remote `ConnectOptions`.
- `ExecutablePath` cannot be combined with an advanced `BrowserBuildId`.

## Package boundaries

This package is intentionally focused on:

- pooling
- lifecycle control
- reliability
- performance
- reset and replacement policy
- operational diagnostics

It is intentionally not a general wrapper around the entire PuppeteerSharp API.

## Minimal end-to-end sample

```csharp
using Microsoft.Extensions.DependencyInjection;
using PuppeteerPagePool;
using PuppeteerPagePool.Configuration;
using PuppeteerPagePool.DependencyInjection;

var services = new ServiceCollection();

services.AddPagePool(
    options =>
    {
        options.PoolSize = 4;
        options.AcquireTimeout = TimeSpan.FromSeconds(20);
        options.ResetTargetUrl = "about:blank";
        options.JavaScriptEnabled = true;
        options.Browser = PagePoolBrowser.Chrome;
    },
    advanced =>
    {
        advanced.MaxPageUses = 250;
        advanced.OperationFailureHandling = PageOperationFailureHandling.ResetPage;
        advanced.ResetStrategy = PageResetStrategy.Navigate;
        advanced.ClearCookiesOnReturn = true;
    });

await using var provider = services.BuildServiceProvider();
var pool = provider.GetRequiredService<IPagePool>();

var title = await pool.ExecuteAsync(
    async (page, cancellationToken) =>
    {
        await page.GoToAsync("https://example.com", cancellationToken: cancellationToken);
        return await page.GetTitleAsync(cancellationToken);
    });

Console.WriteLine(title);
```
