using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Leasing;

namespace PuppeteerPagePool.Browser;

/// <summary>
/// Creates browser runtimes by either connecting to an existing browser or launching Chromium.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BrowserRuntimeFactory"/> class.
/// </remarks>
internal sealed class BrowserRuntimeFactory(ILogger<BrowserRuntimeFactory>? logger = null) : IBrowserRuntimeFactory
{
    private readonly ILogger<BrowserRuntimeFactory>? _logger = logger;


    /// <summary>
    /// Creates a browser runtime from connection or launch settings.
    /// </summary>
    public async ValueTask<IBrowserRuntime> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken)
        => await CreateAsyncInternal(options, cancellationToken).ConfigureAwait(false);

    private async ValueTask<IBrowserRuntime> CreateAsyncInternal(PagePoolOptions options, CancellationToken cancellationToken)
    {
        if (options.ConnectOptions is not null)
        {
            _logger?.LogInformation("Connecting to existing browser at {Endpoint}.",
                options.ConnectOptions.BrowserWSEndpoint ?? options.ConnectOptions.BrowserURL);

            var browser = await Puppeteer.ConnectAsync(options.ConnectOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new BrowserRuntime(browser, _logger);
        }

        var launchOptions = await BrowserLaunchOptions.ResolveAsync(options).ConfigureAwait(false);
        _logger?.LogInformation("Launching Chromium with executable path: {ExecutablePath}", launchOptions.ExecutablePath);

        var browserInstance = await Puppeteer.LaunchAsync(launchOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new BrowserRuntime(browserInstance, _logger);
    }
}

/// <summary>
/// Adapts a live PuppeteerSharp browser into the pool runtime contract.
/// </summary>
internal sealed class BrowserRuntime : IBrowserRuntime
{
    private readonly IBrowser _browser;
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a runtime wrapper around the supplied browser.
    /// </summary>
    public BrowserRuntime(IBrowser browser, ILogger? logger = null)
    {
        _browser = browser;
        _logger = logger;
        _browser.Disconnected += OnDisconnected;
    }

    /// <inheritdoc />
    public bool IsConnected => _browser.IsConnected;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public async ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
        => await CreatePageAsyncInternal(cancellationToken).ConfigureAwait(false);

    private async ValueTask<IPageSession> CreatePageAsyncInternal(CancellationToken cancellationToken)
    {
        var page = await _browser.NewPageAsync().ConfigureAwait(false);
        return new BrowserPage(page, _logger);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => await IsResponsiveAsyncInternal(timeout, cancellationToken).ConfigureAwait(false);

    private async ValueTask<bool> IsResponsiveAsyncInternal(TimeSpan timeout, CancellationToken cancellationToken)
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
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Browser health check failed.");
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
        => await DisposeAsyncInternal().ConfigureAwait(false);

    private async ValueTask DisposeAsyncInternal()
    {
        _browser.Disconnected -= OnDisconnected;

        if (_browser.IsClosed)
        {
            return;
        }

        try
        {
            await _browser.CloseAsync().ConfigureAwait(false);
            _logger?.LogDebug("Browser closed successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to close browser gracefully.");
        }
    }

    private void OnDisconnected(object? sender, EventArgs eventArgs)
    {
        _logger?.LogWarning("Browser disconnected event received.");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}
