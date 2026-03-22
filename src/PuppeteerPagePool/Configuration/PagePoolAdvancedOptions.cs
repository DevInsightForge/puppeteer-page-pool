namespace PuppeteerPagePool.Configuration;

public sealed class PagePoolAdvancedOptions
{
    public int MaxPageUses { get; set; } = 1_000;

    public int MaxConsecutiveLeaseFailures { get; set; } = 3;

    public PageOperationFailureHandling OperationFailureHandling { get; set; } = PageOperationFailureHandling.ResetPage;

    public PageResetStrategy ResetStrategy { get; set; } = PageResetStrategy.Navigate;

    public string ResetContent { get; set; } = string.Empty;

    public bool ClearCookiesOnReturn { get; set; } = true;

    public bool ValidatePageHealthBeforeLease { get; set; } = true;

    public bool EnsureBrowserDownloaded { get; set; } = true;

    public string? BrowserBuildId { get; set; }

    public string? BrowserCachePath { get; set; }

    public TimeSpan BrowserHealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ResetNavigationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public PagePoolNavigationWaitUntil[] ResetWaitConditions { get; set; } = [PagePoolNavigationWaitUntil.Load];

    public Func<ILeasedPage, CancellationToken, ValueTask>? ConfigurePageAsync { get; set; }

    public Func<ILeasedPage, CancellationToken, ValueTask>? BeforeLeaseAsync { get; set; }

    internal PagePoolAdvancedOptions Clone()
    {
        return new PagePoolAdvancedOptions
        {
            MaxPageUses = MaxPageUses,
            MaxConsecutiveLeaseFailures = MaxConsecutiveLeaseFailures,
            OperationFailureHandling = OperationFailureHandling,
            ResetStrategy = ResetStrategy,
            ResetContent = ResetContent,
            ClearCookiesOnReturn = ClearCookiesOnReturn,
            ValidatePageHealthBeforeLease = ValidatePageHealthBeforeLease,
            EnsureBrowserDownloaded = EnsureBrowserDownloaded,
            BrowserBuildId = BrowserBuildId,
            BrowserCachePath = BrowserCachePath,
            BrowserHealthCheckTimeout = BrowserHealthCheckTimeout,
            ResetNavigationTimeout = ResetNavigationTimeout,
            ResetWaitConditions = [.. ResetWaitConditions],
            ConfigurePageAsync = ConfigurePageAsync,
            BeforeLeaseAsync = BeforeLeaseAsync
        };
    }

    internal void Validate()
    {
        if (MaxPageUses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPageUses));
        }

        if (MaxConsecutiveLeaseFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveLeaseFailures));
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

        if (ResetStrategy == PageResetStrategy.SetContent && ResetContent is null)
        {
            throw new ArgumentException("ResetContent is required when ResetStrategy is SetContent.", nameof(ResetContent));
        }
    }
}
