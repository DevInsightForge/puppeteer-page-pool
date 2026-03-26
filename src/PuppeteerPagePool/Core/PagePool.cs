using System.Threading.Channels;
using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Browser;
using PuppeteerPagePool.Exceptions;
using PuppeteerPagePool.Health;
using PuppeteerPagePool.Leasing;

namespace PuppeteerPagePool.Core;

internal sealed class PagePool : IPagePool
{
    private readonly string _poolName;
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
    private int _isAcceptingLeases = 1;
    private int _isDisposed;
    private DateTime _startTime;

    internal PagePool(
        PagePoolOptions options,
        IBrowserRuntimeFactory browserRuntimeFactory)
    {
        _options = options;
        _options.Validate();
        _poolName = _options.PoolName;
        _browserRuntimeFactory = browserRuntimeFactory;
        _availablePages = CreateChannel(_options.PoolSize);
        _startTime = DateTime.UtcNow;
    }

    public ValueTask ExecuteAsync(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
        => ExecuteAsyncInternal(operation, cancellationToken);

    public ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
        => ExecuteAsyncInternal(operation, cancellationToken);

    public ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => GetSnapshotAsyncInternal(cancellationToken);

    public ValueTask DisposeAsync()
        => DisposeAsyncInternal();

    public Task StartAsync(CancellationToken cancellationToken = default)
        => StartAsyncInternal(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => StopAsyncInternal(cancellationToken);

    private async Task StartAsyncInternal(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_options.WarmupOnStartup)
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StopAsyncInternal(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return;
        }

        StopAcceptingLeases();

        if (_options.DrainOnShutdown && Volatile.Read(ref _leasedCount) > 0)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ShutdownTimeout);

            try
            {
                await _drained.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        await DisposeAsyncInternal().ConfigureAwait(false);
    }

    internal async ValueTask<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        var runtime = _browserRuntime;
        if (runtime is null || !runtime.IsConnected)
        {
            return false;
        }

        return await runtime.IsResponsiveAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ExecuteAsyncInternal(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
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

    private async ValueTask<TResult> ExecuteAsyncInternal<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
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

    private ValueTask<PagePoolHealthSnapshot> GetSnapshotAsyncInternal(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new PagePoolHealthSnapshot(
            _options.PoolSize,
            Volatile.Read(ref _availableCount),
            Volatile.Read(ref _leasedCount),
            Volatile.Read(ref _waitingCount),
            _browserRuntime?.IsConnected ?? false,
            IsAcceptingLeases && !IsDisposed,
            DateTime.UtcNow - _startTime,
            DateTime.UtcNow);

        return ValueTask.FromResult(snapshot);
    }

    private async ValueTask<PageEntry> AcquireAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (!IsAcceptingLeases)
        {
            throw new PagePoolDisposedException(_poolName);
        }

        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        if (!IsAcceptingLeases)
        {
            throw new PagePoolDisposedException(_poolName);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AcquireTimeout);

        Interlocked.Increment(ref _waitingCount);
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
                    var snapshot = await GetSnapshotAsyncInternal(cancellationToken).ConfigureAwait(false);
                    throw new PagePoolAcquireTimeoutException(
                        _options.AcquireTimeout,
                        snapshot.AvailablePages,
                        snapshot.LeasedPages,
                        _poolName);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waitingCount);
        }
    }

    private async ValueTask<bool> TryPrepareAsync(PageEntry page, CancellationToken cancellationToken)
    {
        try
        {
            await page.Session.PrepareForLeaseAsync(_options, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await ReleaseAsync(page, true).ConfigureAwait(false);
            return false;
        }
    }

    private async ValueTask ReleaseAsync(PageEntry page, bool replacePage)
    {
        try
        {
            if (ShouldReplace(page, replacePage))
            {
                await ReplaceAsync(page).ConfigureAwait(false);
                return;
            }

            await ResetPageAsync(page).ConfigureAwait(false);
            page.UseCount++;
            page.LastUsedTime = DateTime.UtcNow;
            await ReturnAsync(page).ConfigureAwait(false);
        }
        catch
        {
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

    private bool ShouldReplace(PageEntry page, bool replacePage)
    {
        if (replacePage || page.Session.IsClosed)
        {
            return true;
        }

        if (page.UseCount + 1 >= _options.MaxPageUses)
        {
            return true;
        }

        return false;
    }

    private async Task ResetPageAsync(PageEntry page)
    {
        await page.Session.ResetAsync(_options, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReplaceAsync(PageEntry page)
    {
        try
        {
            await page.Session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        if (!IsAcceptingLeases || IsDisposed)
        {
            return;
        }

        var runtime = _browserRuntime;
        if (runtime is null || !runtime.IsConnected)
        {
            await EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            var replacement = await CreatePageAsync(runtime, Volatile.Read(ref _generation), CancellationToken.None).ConfigureAwait(false);
            await ReturnAsync(replacement).ConfigureAwait(false);
        }
        catch
        {
            await EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ReturnAsync(PageEntry page)
    {
        ThrowIfDisposed();
        await _availablePages.Writer.WriteAsync(page).ConfigureAwait(false);
        Interlocked.Increment(ref _availableCount);
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

            await RebuildRuntimeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private bool HasReadyRuntime()
        => _browserRuntime is not null && _browserRuntime.IsConnected && !_availablePages.Reader.Completion.IsCompleted;

    private async Task RebuildRuntimeAsync(CancellationToken cancellationToken)
    {
        var newPages = CreateChannel(_options.PoolSize);
        var generation = Interlocked.Increment(ref _generation);
        var newRuntime = await CreateRuntimeAndPagesAsync(newPages, generation, cancellationToken).ConfigureAwait(false);

        var oldPages = _availablePages;
        var oldRuntime = _browserRuntime;

        _availablePages = newPages;
        _browserRuntime = newRuntime;

        oldPages.Writer.TryComplete();
        await DisposeQueuedPagesAsync(oldPages).ConfigureAwait(false);

        if (oldRuntime is not null)
        {
            oldRuntime.Disconnected -= OnBrowserDisconnected;
            await oldRuntime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<IBrowserRuntime> CreateRuntimeAndPagesAsync(Channel<PageEntry> pages, int generation, CancellationToken cancellationToken)
    {
        var runtime = await _browserRuntimeFactory.CreateAsync(_options, cancellationToken).ConfigureAwait(false);
        runtime.Disconnected += OnBrowserDisconnected;

        try
        {
            for (var i = 0; i < _options.PoolSize; i++)
            {
                var page = await CreatePageAsync(runtime, generation, cancellationToken).ConfigureAwait(false);
                await pages.Writer.WriteAsync(page, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _availableCount);
            }

            return runtime;
        }
        catch
        {
            runtime.Disconnected -= OnBrowserDisconnected;
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<PageEntry> CreatePageAsync(IBrowserRuntime runtime, int generation, CancellationToken cancellationToken)
    {
        var session = await runtime.CreatePageAsync(cancellationToken).ConfigureAwait(false);
        await session.InitializeAsync(_options, cancellationToken).ConfigureAwait(false);

        return new PageEntry
        {
            Generation = generation,
            Session = session,
            CreatedTime = DateTime.UtcNow,
            LastUsedTime = DateTime.UtcNow
        };
    }

    private void OnBrowserDisconnected(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed)
        {
            return;
        }

        _availablePages.Writer.TryComplete();
        _ = RebuildAfterDisconnectAsync();
    }

    private async Task RebuildAfterDisconnectAsync()
    {
        try
        {
            await EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
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
            catch
            {
            }

            Interlocked.Decrement(ref _availableCount);
        }
    }

    private async ValueTask DisposeAsyncInternal()
    {
        if (!TryMarkDisposed())
        {
            return;
        }

        StopAcceptingLeases();

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
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
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static Channel<PageEntry> CreateChannel(int capacity)
        => Channel.CreateBounded<PageEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new PagePoolDisposedException(_poolName);
        }
    }

    private void ResetDrainedSignal()
    {
        if (!_drained.Task.IsCompleted)
        {
            return;
        }

        var current = _drained;
        Interlocked.CompareExchange(ref _drained, CreateDrainedSignal(), current);
    }

    private static TaskCompletionSource CreateDrainedSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool IsDisposed => Volatile.Read(ref _isDisposed) == 1;

    private bool IsAcceptingLeases => Volatile.Read(ref _isAcceptingLeases) == 1;

    private bool TryMarkDisposed() => Interlocked.Exchange(ref _isDisposed, 1) == 0;

    private void StopAcceptingLeases() => Volatile.Write(ref _isAcceptingLeases, 0);
}




