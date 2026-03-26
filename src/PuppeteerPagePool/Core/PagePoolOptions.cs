namespace PuppeteerPagePool.Core;

/// <summary>
/// Configures pooled page lifecycle, browser startup, and reset behavior.
/// </summary>
public sealed class PagePoolOptions
{
    /// <summary>
    /// Gets or sets the name of the pool. Used for logging, metrics, and tracing.
    /// </summary>
    public string PoolName { get; set; } = "default";

    /// <summary>
    /// Gets or sets the maximum number of pages kept in the pool.
    /// </summary>
    public int PoolSize { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 10);

    /// <summary>
    /// Gets or sets the maximum time to wait for an available leased page.
    /// </summary>
    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether the pool should warm up during host startup.
    /// </summary>
    public bool WarmupOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum time to wait for active leases during shutdown.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether to drain all pages during shutdown before closing.
    /// </summary>
    public bool DrainOnShutdown { get; set; } = true;

    /// <summary>
    /// Gets or sets the absolute URL used to reset a page before it returns to the pool.
    /// </summary>
    public string ResetTargetUrl { get; set; } = "about:blank";

    /// <summary>
    /// Gets or sets a value indicating whether local and session storage are cleared on return.
    /// </summary>
    public bool ClearStorageOnReturn { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of successful uses before a page is replaced.
    /// </summary>
    public int MaxPageUses { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets a value indicating whether cookies are cleared on return.
    /// </summary>
    public bool ClearCookiesOnReturn { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout used when resetting a page through navigation.
    /// </summary>
    public TimeSpan ResetNavigationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the navigation readiness conditions used during reset navigation.
    /// </summary>
    public WaitUntilNavigation[] ResetWaitConditions { get; set; } = [WaitUntilNavigation.Load];

    /// <summary>
    /// Gets or sets PuppeteerSharp launch options for starting a local Chromium instance.
    /// </summary>
    public LaunchOptions? LaunchOptions { get; set; }

    /// <summary>
    /// Gets or sets PuppeteerSharp connect options for attaching to an existing browser.
    /// </summary>
    public ConnectOptions? ConnectOptions { get; set; }

    /// <summary>
    /// Validates the configured options and throws when they are invalid.
    /// </summary>
    internal void Validate()
    {
        if (PoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PoolSize), "PoolSize must be greater than 0.");
        }

        if (AcquireTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AcquireTimeout), "AcquireTimeout must be greater than 0.");
        }

        if (ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout), "ShutdownTimeout must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(ResetTargetUrl))
        {
            throw new ArgumentException("ResetTargetUrl is required.", nameof(ResetTargetUrl));
        }

        if (!Uri.TryCreate(ResetTargetUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("ResetTargetUrl must be an absolute URI.", nameof(ResetTargetUrl));
        }

        if (LaunchOptions is not null && ConnectOptions is not null)
        {
            throw new ArgumentException("LaunchOptions and ConnectOptions cannot both be set.", nameof(LaunchOptions));
        }

        if (LaunchOptions is not null && LaunchOptions.Timeout < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LaunchOptions.Timeout), "LaunchOptions.Timeout cannot be negative.");
        }

        if (ConnectOptions is not null &&
            string.IsNullOrWhiteSpace(ConnectOptions.BrowserWSEndpoint) &&
            string.IsNullOrWhiteSpace(ConnectOptions.BrowserURL))
        {
            throw new ArgumentException("ConnectOptions requires BrowserWebSocketEndpoint or BrowserUrl.", nameof(ConnectOptions));
        }

        if (ConnectOptions is not null && ConnectOptions.SlowMo < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectOptions.SlowMo), "ConnectOptions.SlowMo cannot be negative.");
        }

        if (MaxPageUses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPageUses), "MaxPageUses must be greater than 0.");
        }

        if (ResetNavigationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ResetNavigationTimeout), "ResetNavigationTimeout must be greater than 0.");
        }

        if (ResetWaitConditions.Length == 0)
        {
            throw new ArgumentException("ResetWaitConditions must contain at least one navigation condition.", nameof(ResetWaitConditions));
        }
    }
}
