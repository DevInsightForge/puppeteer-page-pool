using PuppeteerSharp;

namespace PuppeteerPagePool;

public sealed class PuppeteerPagePoolOptions
{
    public int PoolSize { get; set; } = 4;

    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool WarmupOnStartup { get; set; } = true;

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public string ResetTargetUrl { get; set; } = "about:blank";

    public int MaxPageUses { get; set; } = 1_000;

    public int MaxConsecutiveFailures { get; set; } = 3;

    public bool ClearCookiesOnReturn { get; set; } = true;

    public bool ClearStorageOnReturn { get; set; } = true;

    public bool EnsureBrowserDownloaded { get; set; } = true;

    public SupportedBrowser Browser { get; set; } = SupportedBrowser.Chrome;

    public string? BrowserBuildId { get; set; }

    public string? BrowserCachePath { get; set; }

    public LaunchOptions? LaunchOptions { get; set; }

    public ConnectOptions? ConnectOptions { get; set; }

    public Func<IPage, CancellationToken, ValueTask>? ConfigurePageAsync { get; set; }

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
            EnsureBrowserDownloaded = EnsureBrowserDownloaded,
            Browser = Browser,
            BrowserBuildId = BrowserBuildId,
            BrowserCachePath = BrowserCachePath,
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
    }
}
