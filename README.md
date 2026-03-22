# PuppeteerPagePool

`PuppeteerPagePool` is a .NET library for running PuppeteerSharp page work through a bounded page pool. It provides deterministic page reset, controlled concurrency, host lifecycle integration, and optional health checks.

## Install

```bash
dotnet add package PuppeteerPagePool
```

## Target frameworks

- `net8.0`
- `net10.0`

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using PuppeteerPagePool;
using PuppeteerPagePool.DependencyInjection;

var services = new ServiceCollection();

services.AddPuppeteerPagePool(options =>
{
    options.PoolSize = 4;
    options.ResetTargetUrl = "about:blank";
});

await using var provider = services.BuildServiceProvider();
var pool = provider.GetRequiredService<IPagePool>();

await pool.WithPage(async page =>
{
    await page.SetContentAsync("<html><body>Hello</body></html>");
});
```

## Dependency injection

Register the pool once during startup:

```csharp
services.AddPuppeteerPagePool(options =>
{
    options.PoolSize = 4;
    options.AcquireTimeout = TimeSpan.FromSeconds(30);
    options.ResetTargetUrl = "about:blank";
});
```

Optional health check registration:

```csharp
services.AddHealthChecks().AddPagePoolHealthCheck();
```

Use a preinstalled browser executable:

```csharp
services.AddPuppeteerPagePool(options =>
{
    options.Browser = PagePoolBrowser.Chrome;
    options.BrowserExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    options.EnsureBrowserDownloaded = false;
});
```

When `BrowserExecutablePath` is provided, the pool validates that path and browser family before launch and fails fast on mismatch.

## Core API

`IPagePool` exposes `WithPage` overloads for sync and async callbacks:

```csharp
ValueTask WithPage(Action<IPage> operation, CancellationToken cancellationToken = default);
ValueTask WithPage(Func<IPage, ValueTask> operation, CancellationToken cancellationToken = default);
ValueTask<TResult> WithPage<TResult>(Func<IPage, TResult> operation, CancellationToken cancellationToken = default);
ValueTask<TResult> WithPage<TResult>(Func<IPage, ValueTask<TResult>> operation, CancellationToken cancellationToken = default);
ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
```

## Options reference

`PuppeteerPagePoolOptions` controls pool size, lifecycle behavior, and reset policy:

- `PoolSize`: Maximum concurrent leased pages.
- `AcquireTimeout`: Maximum wait time for a free page.
- `WarmupOnStartup`: Builds browser/pages during host startup.
- `ShutdownTimeout`: Maximum wait for in-flight leases during shutdown.
- `ResetTargetUrl`: Absolute URL used to reset pages.
- `MaxPageUses`: Recycles a page after this many successful uses.
- `ClearCookiesOnReturn`: Clears cookies after each lease.
- `ClearStorageOnReturn`: Clears local/session storage after each lease.
- `JavaScriptEnabled`: Sets JavaScript enabled state on pooled pages.
- `ValidatePageHealthBeforeLease`: Validates readiness before handing over a page.
- `EnsureBrowserDownloaded`: Downloads browser binary when needed.
- `Browser`: Browser type used by pool config (`PagePoolBrowser`).
- `BrowserBuildId`: Optional pinned browser build id.
- `BrowserExecutablePath`: Forces use of a specific browser executable path.
- `BrowserCachePath`: Optional location for downloaded browser binaries.
- `BrowserHealthCheckTimeout`: Timeout for browser responsiveness checks.
- `ResetNavigationTimeout`: Navigation timeout during reset.
- `ResetWaitUntil`: Navigation completion criteria (`PagePoolNavigationWaitUntil[]`).
- `LaunchSettings`: Local launch configuration (`PagePoolLaunchSettings`).
- `ConnectSettings`: Remote browser configuration (`PagePoolConnectSettings`).
- `ConfigurePageAsync`: Per-page initialization callback.
- `BeforeLeaseAsync`: Callback invoked before each lease.

## Health snapshot

`GetSnapshotAsync` returns `PagePoolHealthSnapshot`:

- `PoolSize`
- `AvailablePages`
- `LeasedPages`
- `WaitingRequests`
- `BrowserConnected`
- `AcceptingLeases`
- `ReplacementCount`
- `BrowserRestartCount`

## Exceptions

- `PagePoolAcquireTimeoutException`: No page was available before `AcquireTimeout`.
- `PagePoolDisposedException`: Lease request was made after disposal/stop.
- `PagePoolUnavailableException`: Browser/session is unavailable during initialization or rebuild.

## Operational guidance

- Keep callbacks short and focused to reduce queue wait time.
- Reuse one `IPagePool` singleton per app process.
- Prefer `ConnectSettings` for remote browser fleets and `LaunchSettings` for local managed browsers.
- When `BrowserExecutablePath` is set, the pool uses that executable only and fails fast if the file is missing, not loadable, or browser type does not match `Browser`.
- Use `ResetTargetUrl` that is fast and deterministic for your workload.
