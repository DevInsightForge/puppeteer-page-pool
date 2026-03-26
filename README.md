# PuppeteerPagePool

`PuppeteerPagePool` is a callback-based Chromium page pool for PuppeteerSharp workloads like HTML-to-PDF, screenshots, and repeated server-side rendering.

## Install

```bash
dotnet add package PuppeteerPagePool
```

## Target Frameworks

- `net8.0`
- `net10.0`

## Core Concepts

- You run rendering work via `IPagePool.ExecuteAsync(...)`.
- The callback receives an `ILeasedPage`.
- `ILeasedPage` is lease-scoped and invalid after callback completion.
- The pool owns page lifecycle (reset, reuse, replacement, shutdown).

## Public API

### `IPagePool`

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

### `ILeasedPage`

`ILeasedPage` exposes rendering-focused operations (navigation, content loading, waiting, JS evaluation, PDF, screenshot, headers, cookies, auth, viewport) over a lease-scoped page.

### `PagePoolHealthSnapshot`

Runtime snapshot includes:

- `PoolSize`
- `AvailablePages`
- `LeasedPages`
- `WaitingRequests`
- `BrowserConnected`
- `AcceptingLeases`
- `Uptime`
- `LastHealthCheck`

## Registration (DI)

```csharp
using Microsoft.Extensions.DependencyInjection;
using PuppeteerPagePool;
using PuppeteerPagePool.Abstractions;
using PuppeteerSharp;

var services = new ServiceCollection();

services.AddPuppeteerPagePool(options =>
{
    options.PoolSize = 4;
    options.AcquireTimeout = TimeSpan.FromSeconds(20);
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
    options.ResetTargetUrl = "about:blank";
    options.LaunchOptions = new LaunchOptions
    {
        Headless = true,
        Timeout = 120_000,
        Args =
        [
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--no-sandbox"
        ]
    };
});

await using var provider = services.BuildServiceProvider();
var pool = provider.GetRequiredService<IPagePool>();
```

## Non-DI Usage

```csharp
using PuppeteerPagePool;
using PuppeteerPagePool.Abstractions;
using PuppeteerSharp;

await using var pool = await PagePoolFactory.CreateAsync(options =>
{
    options.PoolSize = 4;
    options.ResetTargetUrl = "about:blank";
    options.LaunchOptions = new LaunchOptions
    {
        Headless = true
    };
});
```

## Quick Usage

### PDF

```csharp
var pdf = await pool.ExecuteAsync(async (page, cancellationToken) =>
{
    await page.SetContentAsync("<html><body><h1>Report</h1></body></html>");
    await page.WaitForNetworkIdleAsync();

    return await page.PdfDataAsync(new PdfOptions
    {
        PrintBackground = true
    });
});
```

### Screenshot

```csharp
var image = await pool.ExecuteAsync(async (page, cancellationToken) =>
{
    await page.GoToAsync("https://example.com");
    await page.WaitForNetworkIdleAsync();

    return await page.ScreenshotDataAsync(new ScreenshotOptions
    {
        FullPage = true
    });
});
```

## Configuration (`PagePoolOptions`)

- `PoolName`
- `PoolSize`
- `AcquireTimeout`
- `WarmupOnStartup`
- `ShutdownTimeout`
- `DrainOnShutdown`
- `ResetTargetUrl`
- `ResetNavigationTimeout`
- `ResetWaitConditions`
- `ClearStorageOnReturn`
- `ClearCookiesOnReturn`
- `MaxPageUses`
- `LaunchOptions`
- `ConnectOptions`

Validation rules include:

- `LaunchOptions` and `ConnectOptions` cannot both be set.
- `ResetTargetUrl` must be an absolute URI.
- Timeouts and limits must be positive.

## Browser Boot Behavior

When launching locally:

- Headless mode is always enforced.
- If `LaunchOptions.ExecutablePath` is not set, `PUPPETEER_EXECUTABLE_PATH` is checked.
- If still not found, Chromium is downloaded via `BrowserFetcher`.

When connecting remotely:

- `ConnectOptions` must contain `BrowserWSEndpoint` or `BrowserURL`.

## Pool Behavior

After successful callback completion, the pool resets the page and returns it to the channel.

A page is replaced when:

- callback throws
- page is closed
- page exceeds `MaxPageUses`
- page preparation/reset fails

If browser disconnects, the pool rebuilds runtime and repopulates pooled pages.

## Exceptions

- `PagePoolAcquireTimeoutException`
- `PagePoolDisposedException`
- `PagePoolUnavailableException`
- `PageOperationException`
- `PagePoolCircuitOpenException`

## Operational Guidance

- Keep callbacks short.
- Return bytes/DTOs from callbacks, not page references.
- Never cache `ILeasedPage` beyond callback scope.
- Prefer a fast reset URL (`about:blank` or internal lightweight route).
- Size `PoolSize` according to workload and host capacity.
