using PuppeteerSharp;

namespace PuppeteerPagePool;

/// <summary>
/// Configures pool behavior, browser lifecycle, and page reset policies.
/// </summary>
public sealed class PagePoolOptions
{
    /// <summary>
    /// Maximum number of pages that can be leased concurrently.
    /// </summary>
    public int PoolSize { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 10);

    /// <summary>
    /// Maximum time to wait for a page lease before timing out.
    /// </summary>
    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes browser and pages during host startup when <see langword="true"/>.
    /// </summary>
    public bool WarmupOnStartup { get; set; } = true;

    /// <summary>
    /// Maximum time to wait for in-flight leases during host shutdown.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Absolute URL used to reset pages before returning them to the pool.
    /// </summary>
    public string ResetTargetUrl { get; set; } = "about:blank";

    /// <summary>
    /// Maximum number of successful uses before a page is recycled.
    /// </summary>
    public int MaxPageUses { get; set; } = 1_000;

    /// <summary>
    /// Reserved for failure policy compatibility and validation.
    /// </summary>
    public int MaxConsecutiveLeaseFailures { get; set; } = 3;

    /// <summary>
    /// Clears page cookies when a lease completes.
    /// </summary>
    public bool ClearCookiesOnReturn { get; set; } = true;

    /// <summary>
    /// Clears local and session storage when a lease completes.
    /// </summary>
    public bool ClearStorageOnReturn { get; set; } = true;

    /// <summary>
    /// Sets JavaScript availability for pooled pages.
    /// </summary>
    public bool JavaScriptEnabled { get; set; } = false;

    /// <summary>
    /// Validates page readiness before each lease.
    /// </summary>
    public bool ValidatePageHealthBeforeLease { get; set; } = true;

    /// <summary>
    /// Downloads browser binaries when needed and no executable path is configured.
    /// </summary>
    public bool EnsureBrowserDownloaded { get; set; } = true;

    /// <summary>
    /// Browser family to launch or validate.
    /// </summary>
    public PagePoolBrowser Browser { get; set; } = PagePoolBrowser.Chrome;

    /// <summary>
    /// Optional browser build id for deterministic browser fetch.
    /// </summary>
    public string? BrowserBuildId { get; set; }

    /// <summary>
    /// Browser executable path that must be used when provided.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Optional cache path for downloaded browser binaries.
    /// </summary>
    public string? BrowserCachePath { get; set; }

    /// <summary>
    /// Timeout used by browser responsiveness health probes.
    /// </summary>
    public TimeSpan BrowserHealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Navigation timeout used during page reset.
    /// </summary>
    public TimeSpan ResetNavigationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Navigation completion conditions required during reset.
    /// </summary>
    public PagePoolNavigationWaitUntil[] ResetWaitConditions { get; set; } = [PagePoolNavigationWaitUntil.Load];

    /// <summary>
    /// Launch configuration for local browser processes.
    /// </summary>
    public PagePoolLaunchOptions? LaunchOptions { get; set; }

    /// <summary>
    /// Connection configuration for remote browser endpoints.
    /// </summary>
    public PagePoolConnectOptions? ConnectOptions { get; set; }

    /// <summary>
    /// Optional callback invoked once for each newly created page before first lease.
    /// </summary>
    public Func<IPage, CancellationToken, ValueTask>? ConfigurePageAsync { get; set; }

    /// <summary>
    /// Optional callback invoked before each lease is handed to user code.
    /// </summary>
    public Func<IPage, CancellationToken, ValueTask>? BeforeLeaseAsync { get; set; }

    internal PagePoolOptions Clone()
    {
        return new PagePoolOptions
        {
            PoolSize = PoolSize,
            AcquireTimeout = AcquireTimeout,
            WarmupOnStartup = WarmupOnStartup,
            ShutdownTimeout = ShutdownTimeout,
            ResetTargetUrl = ResetTargetUrl,
            MaxPageUses = MaxPageUses,
            MaxConsecutiveLeaseFailures = MaxConsecutiveLeaseFailures,
            ClearCookiesOnReturn = ClearCookiesOnReturn,
            ClearStorageOnReturn = ClearStorageOnReturn,
            JavaScriptEnabled = JavaScriptEnabled,
            ValidatePageHealthBeforeLease = ValidatePageHealthBeforeLease,
            EnsureBrowserDownloaded = EnsureBrowserDownloaded,
            Browser = Browser,
            BrowserBuildId = BrowserBuildId,
            ExecutablePath = ExecutablePath,
            BrowserCachePath = BrowserCachePath,
            BrowserHealthCheckTimeout = BrowserHealthCheckTimeout,
            ResetNavigationTimeout = ResetNavigationTimeout,
            ResetWaitConditions = [.. ResetWaitConditions],
            LaunchOptions = LaunchOptions is null
                ? null
                : new PagePoolLaunchOptions
                {
                    Headless = LaunchOptions.Headless,
                    TimeoutMilliseconds = LaunchOptions.TimeoutMilliseconds,
                    Args = [.. LaunchOptions.Args]
                },
            ConnectOptions = ConnectOptions is null
                ? null
                : new PagePoolConnectOptions
                {
                    BrowserWebSocketEndpoint = ConnectOptions.BrowserWebSocketEndpoint,
                    BrowserUrl = ConnectOptions.BrowserUrl,
                    IgnoreHttpsErrors = ConnectOptions.IgnoreHttpsErrors,
                    SlowMoMilliseconds = ConnectOptions.SlowMoMilliseconds
                },
            ConfigurePageAsync = ConfigurePageAsync,
            BeforeLeaseAsync = BeforeLeaseAsync
        };
    }

    internal void Validate()
    {
        if (PoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PoolSize));
        }

        if (AcquireTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AcquireTimeout));
        }

        if (ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));
        }

        if (string.IsNullOrWhiteSpace(ResetTargetUrl))
        {
            throw new ArgumentException("ResetTargetUrl is required.", nameof(ResetTargetUrl));
        }

        if (!Uri.TryCreate(ResetTargetUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("ResetTargetUrl must be an absolute URI.", nameof(ResetTargetUrl));
        }

        if (MaxPageUses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPageUses));
        }

        if (MaxConsecutiveLeaseFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveLeaseFailures));
        }

        if (LaunchOptions is not null && ConnectOptions is not null)
        {
            throw new ArgumentException("LaunchOptions and ConnectOptions cannot both be set.");
        }

        if (BrowserHealthCheckTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BrowserHealthCheckTimeout));
        }

        if (ResetNavigationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ResetNavigationTimeout));
        }

        if (ResetWaitConditions.Length == 0)
        {
            throw new ArgumentException("ResetWaitConditions must contain at least one navigation condition.", nameof(ResetWaitConditions));
        }

        if (LaunchOptions is not null && LaunchOptions.TimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LaunchOptions.TimeoutMilliseconds));
        }

        if (ConnectOptions is not null &&
            string.IsNullOrWhiteSpace(ConnectOptions.BrowserWebSocketEndpoint) &&
            string.IsNullOrWhiteSpace(ConnectOptions.BrowserUrl))
        {
            throw new ArgumentException("ConnectOptions requires BrowserWebSocketEndpoint or BrowserUrl.", nameof(ConnectOptions));
        }

        if (ConnectOptions is not null && ConnectOptions.SlowMoMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectOptions.SlowMoMilliseconds));
        }

        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            if (ConnectOptions is not null)
            {
                throw new ArgumentException("ExecutablePath cannot be used with ConnectOptions.", nameof(ExecutablePath));
            }

            if (!string.IsNullOrWhiteSpace(BrowserBuildId))
            {
                throw new ArgumentException("BrowserBuildId cannot be used with ExecutablePath.", nameof(BrowserBuildId));
            }
        }
    }
}
