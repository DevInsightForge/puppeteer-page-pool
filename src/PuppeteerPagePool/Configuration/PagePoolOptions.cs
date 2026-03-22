namespace PuppeteerPagePool.Configuration;

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
    /// Clears local and session storage when a lease completes.
    /// </summary>
    public bool ClearStorageOnReturn { get; set; } = true;

    /// <summary>
    /// Sets JavaScript availability for pooled pages.
    /// </summary>
    public bool JavaScriptEnabled { get; set; } = false;

    /// <summary>
    /// Browser family to launch or validate.
    /// </summary>
    public PagePoolBrowser Browser { get; set; } = PagePoolBrowser.Chrome;

    /// <summary>
    /// Browser executable path that must be used when provided.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Launch configuration for local browser processes.
    /// </summary>
    public PagePoolLaunchOptions? LaunchOptions { get; set; }

    /// <summary>
    /// Connection configuration for remote browser endpoints.
    /// </summary>
    public PagePoolConnectOptions? ConnectOptions { get; set; }

    internal PagePoolAdvancedOptions Advanced { get; private set; } = new();

    internal PagePoolOptions Clone()
    {
        return new PagePoolOptions
        {
            PoolSize = PoolSize,
            AcquireTimeout = AcquireTimeout,
            WarmupOnStartup = WarmupOnStartup,
            ShutdownTimeout = ShutdownTimeout,
            ResetTargetUrl = ResetTargetUrl,
            ClearStorageOnReturn = ClearStorageOnReturn,
            JavaScriptEnabled = JavaScriptEnabled,
            Browser = Browser,
            ExecutablePath = ExecutablePath,
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
            Advanced = Advanced.Clone()
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

        if (_optionsRequiresResetTargetUrl() && string.IsNullOrWhiteSpace(ResetTargetUrl))
        {
            throw new ArgumentException("ResetTargetUrl is required.", nameof(ResetTargetUrl));
        }

        if (_optionsRequiresResetTargetUrl() && !Uri.TryCreate(ResetTargetUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("ResetTargetUrl must be an absolute URI.", nameof(ResetTargetUrl));
        }

        if (LaunchOptions is not null && ConnectOptions is not null)
        {
            throw new ArgumentException("LaunchOptions and ConnectOptions cannot both be set.");
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

            if (!string.IsNullOrWhiteSpace(Advanced.BrowserBuildId))
            {
                throw new ArgumentException("BrowserBuildId cannot be used with ExecutablePath.", nameof(Advanced.BrowserBuildId));
            }
        }

        Advanced.Validate();
    }

    internal void ConfigureAdvanced(Action<PagePoolAdvancedOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Advanced);
    }

    private bool _optionsRequiresResetTargetUrl()
    {
        return Advanced.ResetStrategy == PageResetStrategy.Navigate;
    }
}
