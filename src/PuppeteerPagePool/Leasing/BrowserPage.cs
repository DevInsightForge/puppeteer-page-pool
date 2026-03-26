using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Leasing;

/// <summary>
/// Adapts a live PuppeteerSharp page into the pool page-session contract.
/// </summary>
/// <remarks>
/// Initializes a page-session wrapper around the supplied page.
/// </remarks>
internal sealed class BrowserPage(IPage page, ILogger? logger = null) : IPageSession
{
    private readonly IPage _page = page;
    private readonly ILogger? _logger = logger;


    /// <inheritdoc />
    public IPage Page => _page;

    /// <inheritdoc />
    public bool IsClosed => _page.IsClosed;

    /// <inheritdoc />
    public async ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken)
        => await InitializeAsyncInternal(options, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken)
        => await PrepareForLeaseAsyncInternal(options, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken)
        => await ResetAsyncInternal(options, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
        => await DisposeAsyncInternal().ConfigureAwait(false);

    private async ValueTask InitializeAsyncInternal(PagePoolOptions options, CancellationToken cancellationToken)
    {
        await ResetAsyncInternal(options, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Page initialized successfully.");
    }

    private async ValueTask PrepareForLeaseAsyncInternal(PagePoolOptions options, CancellationToken cancellationToken)
    {
        await EnsureHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ResetAsyncInternal(PagePoolOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await _page.GoToAsync(options.ResetTargetUrl, new NavigationOptions
        {
            WaitUntil = options.ResetWaitConditions,
            Timeout = (int)options.ResetNavigationTimeout.TotalMilliseconds
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (options.ClearCookiesOnReturn)
        {
            var cookies = await _page.GetCookiesAsync().ConfigureAwait(false);
            if (cookies.Length > 0)
            {
                await _page.DeleteCookieAsync(cookies).ConfigureAwait(false);
            }
        }

        if (options.ClearStorageOnReturn)
        {
            await _page.EvaluateExpressionAsync("(() => { try { localStorage.clear(); sessionStorage.clear(); } catch { } })()").ConfigureAwait(false);
        }

        stopwatch.Stop();
        _logger?.LogDebug("Page reset completed in {DurationMs}ms.", stopwatch.ElapsedMilliseconds);
    }

    private async ValueTask DisposeAsyncInternal()
    {
        if (_page.IsClosed)
        {
            return;
        }

        try
        {
            await _page.CloseAsync().ConfigureAwait(false);
            _logger?.LogDebug("Page closed successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to close page gracefully.");
        }
    }

    private async ValueTask EnsureHealthyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_page.IsClosed)
        {
            throw new InvalidOperationException("The pooled page is closed.");
        }

        var readyState = await _page.EvaluateExpressionAsync<string>("document.readyState").ConfigureAwait(false);
        if (readyState is not "complete" and not "interactive")
        {
            throw new InvalidOperationException($"The pooled page is not in a healthy ready state. Current state: {readyState}");
        }
    }
}


