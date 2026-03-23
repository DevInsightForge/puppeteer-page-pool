using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PuppeteerPagePool;

/// <summary>
/// Provides callback-based access to pooled leased pages.
/// </summary>
public interface IPagePool : IAsyncDisposable
{
    /// <summary>
    /// Leases a page, executes the callback, and returns the page to the pool.
    /// </summary>
    ValueTask ExecuteAsync(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases a page, executes the callback, returns the page to the pool, and returns a result.
    /// </summary>
    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a health snapshot for the pool.
    /// </summary>
    ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates pooled page leasing, reset, replacement, browser rebuild, and shutdown behavior.
/// </summary>
internal sealed class PagePool : IPagePool
{
    private static readonly TimeSpan BrowserHealthTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger<PagePool> _logger;
    private readonly IBrowserRuntimeFactory _browserRuntimeFactory;
    private readonly PagePoolOptions _options;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Channel<PageEntry> _availablePages;
    private IBrowserRuntime? _browserRuntime;
    private TaskCompletionSource _drained = CreateDrainedSignal();
    private int _generation;
    private int _availableCount;
    private int _leasedCount;
    private int _waitingCount;
    private bool _isAcceptingLeases = true;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new page pool instance.
    /// </summary>
    internal PagePool(
        IOptions<PagePoolOptions> options,
        ILogger<PagePool> logger,
        IBrowserRuntimeFactory browserRuntimeFactory)
    {
        _options = options.Value;
        _options.Validate();
        _logger = logger;
        _browserRuntimeFactory = browserRuntimeFactory;
        _availablePages = CreateChannel(_options.PoolSize);
    }

    /// <summary>
    /// Leases a page, executes the callback, and returns the page to the pool.
    /// </summary>
    public async ValueTask ExecuteAsync(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var page = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var lease = new LeasedPage(page.Session.Page);
        var replacePage = false;

        try
        {
            await operation(lease, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            replacePage = true;
            throw;
        }
        finally
        {
            lease.Invalidate();
            await ReleaseAsync(page, replacePage).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Leases a page, executes the callback, and returns the callback result.
    /// </summary>
    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var page = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var lease = new LeasedPage(page.Session.Page);
        var replacePage = false;

        try
        {
            return await operation(lease, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            replacePage = true;
            throw;
        }
        finally
        {
            lease.Invalidate();
            await ReleaseAsync(page, replacePage).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the current health snapshot for the pool.
    /// </summary>
    public ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new PagePoolHealthSnapshot(
            _options.PoolSize,
            Volatile.Read(ref _availableCount),
            Volatile.Read(ref _leasedCount),
            Volatile.Read(ref _waitingCount),
            _browserRuntime?.IsConnected ?? false,
            _isAcceptingLeases && !_isDisposed));
    }

    /// <summary>
    /// Checks whether the current browser runtime is connected and responsive.
    /// </summary>
    internal async ValueTask<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        if (_browserRuntime is null || !_browserRuntime.IsConnected)
        {
            return false;
        }

        return await _browserRuntime
            .IsResponsiveAsync(BrowserHealthTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the pool and optionally performs warmup.
    /// </summary>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.WarmupOnStartup)
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the pool, waits for active leases, and disposes browser state.
    /// </summary>
    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return;
        }

        _isAcceptingLeases = false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownTimeout);

        if (Volatile.Read(ref _leasedCount) > 0)
        {
            await _drained.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the pool and all remaining browser state.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isAcceptingLeases = false;

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeCurrentRuntimeAsync().ConfigureAwait(false);
            _availablePages.Writer.TryComplete();
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private async ValueTask<PageEntry> AcquireAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        if (!_isAcceptingLeases)
        {
            throw new PagePoolDisposedException();
        }

        Interlocked.Increment(ref _waitingCount);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AcquireTimeout);

        try
        {
            while (true)
            {
                ThrowIfDisposed();

                try
                {
                    var page = await _availablePages.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    Interlocked.Decrement(ref _availableCount);
                    Interlocked.Increment(ref _leasedCount);
                    ResetDrainedSignal();

                    if (await TryPrepareAsync(page, timeout.Token).ConfigureAwait(false))
                    {
                        return page;
                    }
                }
                catch (ChannelClosedException)
                {
                    await EnsureReadyAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new PagePoolAcquireTimeoutException(_options.AcquireTimeout);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waitingCount);
        }
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (HasReadyRuntime())
        {
            return;
        }

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasReadyRuntime())
            {
                return;
            }

            if (_browserRuntime is not null)
            {
                _logger.LogWarning("Rebuilding browser session.");
            }

            await RebuildRuntimeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async ValueTask ReleaseAsync(PageEntry page, bool replacePage)
    {
        replacePage = ShouldReplace(page, replacePage);

        try
        {
            if (!replacePage)
            {
                await page.Session.ResetAsync(_options, CancellationToken.None).ConfigureAwait(false);
                page.UseCount++;
                await ReturnAsync(page).ConfigureAwait(false);
                return;
            }

            await ReplaceAsync(page).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Page recycle failed.");
            await ReplaceAsync(page).ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Decrement(ref _leasedCount) == 0)
            {
                _drained.TrySetResult();
            }
        }
    }

    private async Task ReturnAsync(PageEntry page)
    {
        ThrowIfDisposed();
        await _availablePages.Writer.WriteAsync(page).ConfigureAwait(false);
        Interlocked.Increment(ref _availableCount);
    }

    private async Task ReplaceAsync(PageEntry page)
    {
        try
        {
            await page.Session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Page disposal failed during replacement.");
        }

        if (_isDisposed || !_isAcceptingLeases)
        {
            return;
        }

        if (_browserRuntime is null || !_browserRuntime.IsConnected)
        {
            await EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            var replacement = await CreatePageAsync(_browserRuntime, Volatile.Read(ref _generation), CancellationToken.None).ConfigureAwait(false);
            await ReturnAsync(replacement).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Page replacement failed.");
            await EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RebuildRuntimeAsync(CancellationToken cancellationToken)
    {
        var pages = CreateChannel(_options.PoolSize);
        var generation = Interlocked.Increment(ref _generation);
        var runtime = await _browserRuntimeFactory.CreateAsync(_options, cancellationToken).ConfigureAwait(false);
        runtime.Disconnected += OnBrowserDisconnected;

        try
        {
            for (var index = 0; index < _options.PoolSize; index++)
            {
                var page = await CreatePageAsync(runtime, generation, cancellationToken).ConfigureAwait(false);
                await pages.Writer.WriteAsync(page, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _availableCount);
            }
        }
        catch
        {
            runtime.Disconnected -= OnBrowserDisconnected;
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var oldPages = _availablePages;
        var oldRuntime = _browserRuntime;
        _availablePages = pages;
        _browserRuntime = runtime;

        oldPages.Writer.TryComplete();
        await DisposeQueuedPagesAsync(oldPages).ConfigureAwait(false);

        if (oldRuntime is not null)
        {
            oldRuntime.Disconnected -= OnBrowserDisconnected;
            await oldRuntime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<PageEntry> CreatePageAsync(IBrowserRuntime runtime, int generation, CancellationToken cancellationToken)
    {
        var session = await runtime.CreatePageAsync(cancellationToken).ConfigureAwait(false);
        await session.InitializeAsync(_options, cancellationToken).ConfigureAwait(false);

        return new PageEntry
        {
            Generation = generation,
            Session = session
        };
    }

    private async Task DisposeCurrentRuntimeAsync()
    {
        var pages = _availablePages;
        var runtime = _browserRuntime;
        _browserRuntime = null;
        pages.Writer.TryComplete();

        await DisposeQueuedPagesAsync(pages).ConfigureAwait(false);

        if (runtime is not null)
        {
            runtime.Disconnected -= OnBrowserDisconnected;
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeQueuedPagesAsync(Channel<PageEntry> pages)
    {
        while (pages.Reader.TryRead(out var page))
        {
            try
            {
                await page.Session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Page disposal failed.");
            }

            Interlocked.Decrement(ref _availableCount);
        }
    }

    private bool HasReadyRuntime()
    {
        return _browserRuntime is not null &&
               _browserRuntime.IsConnected &&
               !_availablePages.Reader.Completion.IsCompleted;
    }

    private void OnBrowserDisconnected(object? sender, EventArgs eventArgs)
    {
        _availablePages.Writer.TryComplete();
        _ = RebuildAfterDisconnectAsync();
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new PagePoolDisposedException();
        }
    }

    private void ResetDrainedSignal()
    {
        if (!_drained.Task.IsCompleted)
        {
            return;
        }

        Interlocked.CompareExchange(ref _drained, CreateDrainedSignal(), _drained);
    }

    private async Task RebuildAfterDisconnectAsync()
    {
        try
        {
            await EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Browser rebuild failed after disconnection.");
        }
    }

    private async ValueTask<bool> TryPrepareAsync(PageEntry page, CancellationToken cancellationToken)
    {
        try
        {
            await page.Session.PrepareForLeaseAsync(_options, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            await ReleaseAsync(page, true).ConfigureAwait(false);
            _logger.LogWarning(exception, "Page preparation failed.");
            return false;
        }
    }

    private bool ShouldReplace(PageEntry page, bool replacePage)
    {
        if (replacePage || page.Session.IsClosed)
        {
            return true;
        }

        if (page.Generation != Volatile.Read(ref _generation))
        {
            return true;
        }

        return page.UseCount + 1 >= _options.MaxPageUses;
    }

    private static Channel<PageEntry> CreateChannel(int capacity)
    {
        return Channel.CreateBounded<PageEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    private static TaskCompletionSource CreateDrainedSignal()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PageEntry
    {
        public required int Generation { get; init; }

        public required IPageSession Session { get; init; }

        public int UseCount { get; set; }
    }
}
