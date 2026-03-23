using PuppeteerSharp;

namespace PuppeteerPagePool;

/// <summary>
/// Creates browser runtime instances for the page pool.
/// </summary>
internal interface IBrowserRuntimeFactory
{
    /// <summary>
    /// Creates a browser runtime from the supplied page pool options.
    /// </summary>
    ValueTask<IBrowserRuntime> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Creates browser runtimes by either connecting to an existing browser or launching Chromium.
/// </summary>
internal sealed class BrowserRuntimeFactory : IBrowserRuntimeFactory
{
    /// <summary>
    /// Creates a browser runtime from connection or launch settings.
    /// </summary>
    public async ValueTask<IBrowserRuntime> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        if (options.ConnectOptions is not null)
        {
            var browser = await Puppeteer.ConnectAsync(options.ConnectOptions).ConfigureAwait(false);
            return new BrowserRuntime(browser);
        }

        var launchOptions = await BrowserLaunchOptions.ResolveAsync(options).ConfigureAwait(false);
        var browserInstance = await Puppeteer.LaunchAsync(launchOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new BrowserRuntime(browserInstance);
    }
}

/// <summary>
/// Represents a connected browser instance that can create pooled pages and report health.
/// </summary>
internal interface IBrowserRuntime : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the browser is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Raised when the underlying browser disconnects.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Creates a new pooled page session.
    /// </summary>
    ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether the browser responds within the supplied timeout.
    /// </summary>
    ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Adapts a live PuppeteerSharp browser into the pool runtime contract.
/// </summary>
internal sealed class BrowserRuntime : IBrowserRuntime
{
    private readonly IBrowser _browser;

    /// <summary>
    /// Initializes a runtime wrapper around the supplied browser.
    /// </summary>
    public BrowserRuntime(IBrowser browser)
    {
        _browser = browser;
        _browser.Disconnected += OnDisconnected;
    }

    public bool IsConnected => _browser.IsConnected;

    public event EventHandler? Disconnected;

    /// <summary>
    /// Creates a new pooled page backed by the wrapped browser.
    /// </summary>
    public async ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
    {
        var page = await _browser.NewPageAsync().ConfigureAwait(false);
        return new BrowserPage(page);
    }

    /// <summary>
    /// Checks whether the browser responds to a version request.
    /// </summary>
    public async ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!_browser.IsConnected || _browser.IsClosed)
        {
            return false;
        }

        try
        {
            var version = await _browser.GetVersionAsync()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes the wrapped browser.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _browser.Disconnected -= OnDisconnected;

        if (_browser.IsClosed)
        {
            return;
        }

        await _browser.CloseAsync().ConfigureAwait(false);
    }

    private void OnDisconnected(object? sender, EventArgs eventArgs)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Represents a reusable page instance managed by the pool runtime.
/// </summary>
internal interface IPageSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the raw PuppeteerSharp page used by the pool.
    /// </summary>
    IPage Page { get; }

    /// <summary>
    /// Gets a value indicating whether the underlying page is closed.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Runs one-time setup for a newly created page.
    /// </summary>
    ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies that the page is healthy and ready for a lease.
    /// </summary>
    ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Resets the page into the pool baseline state.
    /// </summary>
    ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Adapts a live PuppeteerSharp page into the pool page-session contract.
/// </summary>
/// <remarks>
/// Initializes a page-session wrapper around the supplied page.
/// </remarks>
internal sealed class BrowserPage(IPage page) : IPageSession
{

    public IPage Page => page;

    public bool IsClosed => page.IsClosed;

    /// <summary>
    /// Runs per-page configuration and the initial reset.
    /// </summary>
    public async ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        if (options.ConfigurePageAsync is not null)
        {
            await InvokeHookAsync(options.ConfigurePageAsync, cancellationToken).ConfigureAwait(false);
        }

        await ResetAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that the page is healthy before leasing it out.
    /// </summary>
    public async ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        await EnsureHealthyAsync(cancellationToken).ConfigureAwait(false);

        if (options.BeforeLeaseAsync is not null)
        {
            await InvokeHookAsync(options.BeforeLeaseAsync, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the page to the configured clean baseline.
    /// </summary>
    public async ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        await page.GoToAsync(options.ResetTargetUrl, new NavigationOptions
        {
            WaitUntil = options.ResetWaitConditions,
            Timeout = (int)options.ResetNavigationTimeout.TotalMilliseconds
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (options.ClearCookiesOnReturn)
        {
            var cookies = await page.GetCookiesAsync().ConfigureAwait(false);
            if (cookies.Length > 0)
            {
                await page.DeleteCookieAsync(cookies).ConfigureAwait(false);
            }
        }

        if (options.ClearStorageOnReturn)
        {
            await page.EvaluateExpressionAsync("(() => { try { localStorage.clear(); sessionStorage.clear(); } catch { } })()").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the wrapped page.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (page.IsClosed)
        {
            return;
        }

        await page.CloseAsync().ConfigureAwait(false);
    }

    private async ValueTask EnsureHealthyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (page.IsClosed)
        {
            throw new InvalidOperationException("The pooled page is closed.");
        }

        var readyState = await page.EvaluateExpressionAsync<string>("document.readyState").ConfigureAwait(false);
        if (readyState is not "complete" and not "interactive")
        {
            throw new InvalidOperationException("The pooled page is not in a healthy ready state.");
        }
    }

    private async ValueTask InvokeHookAsync(Func<ILeasedPage, CancellationToken, ValueTask> hook, CancellationToken cancellationToken)
    {
        var lease = new LeasedPage(page);

        try
        {
            await hook(lease, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lease.Invalidate();
        }
    }
}
