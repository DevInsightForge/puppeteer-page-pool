using PuppeteerSharp;

namespace PuppeteerPagePool;

/// <summary>
/// Configures pool behavior, browser lifecycle, and page reset policies.
/// </summary>
public sealed class PuppeteerPagePoolOptions
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
    public int MaxConsecutiveFailures { get; set; } = 3;

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
    /// Browser type used by <see cref="BrowserFetcher"/> when downloading binaries.
    /// </summary>
    public SupportedBrowser Browser { get; set; } = SupportedBrowser.Chrome;

    /// <summary>
    /// Optional browser build id for deterministic browser fetch.
    /// </summary>
    public string? BrowserBuildId { get; set; }

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
    /// Navigation completion signals used during page reset.
    /// </summary>
    public WaitUntilNavigation[] ResetWaitUntil { get; set; } = [WaitUntilNavigation.Load];

    /// <summary>
    /// Browser launch options for local browser processes.
    /// </summary>
    public LaunchOptions? LaunchOptions { get; set; }

    /// <summary>
    /// Browser connection options for remote browser endpoints.
    /// </summary>
    public ConnectOptions? ConnectOptions { get; set; }

    /// <summary>
    /// Optional callback invoked once for each newly created page before first lease.
    /// </summary>
    public Func<IPage, CancellationToken, ValueTask>? ConfigurePageAsync { get; set; }

    /// <summary>
    /// Optional callback invoked before each lease is handed to user code.
    /// </summary>
    public Func<IPage, CancellationToken, ValueTask>? BeforeLeaseAsync { get; set; }

    internal PuppeteerPagePoolOptions Clone()
    {
        return new PuppeteerPagePoolOptions
        {
            PoolSize = PoolSize,
            AcquireTimeout = AcquireTimeout,
            WarmupOnStartup = WarmupOnStartup,
            ShutdownTimeout = ShutdownTimeout,
            ResetTargetUrl = ResetTargetUrl,
            MaxPageUses = MaxPageUses,
            MaxConsecutiveFailures = MaxConsecutiveFailures,
            ClearCookiesOnReturn = ClearCookiesOnReturn,
            ClearStorageOnReturn = ClearStorageOnReturn,
            JavaScriptEnabled = JavaScriptEnabled,
            ValidatePageHealthBeforeLease = ValidatePageHealthBeforeLease,
            EnsureBrowserDownloaded = EnsureBrowserDownloaded,
            Browser = Browser,
            BrowserBuildId = BrowserBuildId,
            BrowserCachePath = BrowserCachePath,
            BrowserHealthCheckTimeout = BrowserHealthCheckTimeout,
            ResetNavigationTimeout = ResetNavigationTimeout,
            ResetWaitUntil = [.. ResetWaitUntil],
            LaunchOptions = LaunchOptions,
            ConnectOptions = ConnectOptions,
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

        if (MaxConsecutiveFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveFailures));
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

        if (ResetWaitUntil.Length == 0)
        {
            throw new ArgumentException("ResetWaitUntil must contain at least one navigation condition.", nameof(ResetWaitUntil));
        }
    }
}
