using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerPagePool.Diagnostics;
using PuppeteerPagePool.Internal;

namespace PuppeteerPagePool;

public sealed class PagePool : IPagePool
{
    private readonly ILogger<PagePool> _logger;
    private readonly IBrowserSessionFactory _browserSessionFactory;
    private readonly PuppeteerPagePoolOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateLock = new();
    private Channel<PageSlot> _availableSlots;
    private IBrowserSession? _browserSession;
    private Task? _rebuildTask;
    private TaskCompletionSource _drainedTcs = CreateDrainedTcs();
    private int _generation;
    private int _availablePages;
    private int _leasedPages;
    private int _waitingRequests;
    private int _replacementCount;
    private int _browserRestartCount;
    private bool _initialized;
    private bool _acceptingLeases = true;
    private bool _disposed;

    internal PagePool(
        IOptions<PuppeteerPagePoolOptions> options,
        ILogger<PagePool> logger,
        IBrowserSessionFactory browserSessionFactory)
    {
        _options = options.Value.Clone();
        _options.Validate();
        _logger = logger;
        _browserSessionFactory = browserSessionFactory;
        _availableSlots = CreateChannel(_options.PoolSize);
        RegisterMetrics();
    }

    public async ValueTask<PageLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!_acceptingLeases)
        {
            throw new PagePoolDisposedException();
        }

        var started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _waitingRequests);

        using var activity = PagePoolDiagnostics.ActivitySource.StartActivity("pagepool.acquire");
        using var timeoutCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationSource.CancelAfter(_options.AcquireTimeout);

        try
        {
            while (true)
            {
                ThrowIfDisposed();

                var reader = _availableSlots.Reader;
                PageSlot pageSlot;

                try
                {
                    pageSlot = await reader.ReadAsync(timeoutCancellationSource.Token).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    await AwaitRebuildAsync(timeoutCancellationSource.Token).ConfigureAwait(false);
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new PagePoolAcquireTimeoutException(_options.AcquireTimeout);
                }

                Interlocked.Decrement(ref _availablePages);
                pageSlot.State = PageSlotState.Leased;
                Interlocked.Increment(ref _leasedPages);
                ResetDrainedSignal();

                try
                {
                    await pageSlot.PageSession.PrepareForLeaseAsync(_options, timeoutCancellationSource.Token).ConfigureAwait(false);
                    pageSlot.ConsecutiveFailures = 0;
                    return new PageLease(pageSlot.PageSession.Page, unhealthy => ReleaseAsync(pageSlot, unhealthy));
                }
                catch (Exception exception)
                {
                    pageSlot.ConsecutiveFailures++;
                    await ReleaseAsync(pageSlot, true).ConfigureAwait(false);
                    _logger.LogWarning(exception, "Page preparation failed.");
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waitingRequests);
            var elapsed = Stopwatch.GetElapsedTime(started);
            activity?.SetTag("pagepool.wait_ms", elapsed.TotalMilliseconds);
        }
    }

    public ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new PagePoolHealthSnapshot(
            _options.PoolSize,
            Volatile.Read(ref _availablePages),
            Volatile.Read(ref _leasedPages),
            Volatile.Read(ref _waitingRequests),
            _browserSession?.IsConnected ?? false,
            _acceptingLeases && !_disposed,
            Volatile.Read(ref _replacementCount),
            Volatile.Read(ref _browserRestartCount)));
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.WarmupOnStartup)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        _acceptingLeases = false;

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellationTokenSource.CancelAfter(_options.ShutdownTimeout);

        if (Volatile.Read(ref _leasedPages) > 0)
        {
            await _drainedTcs.Task.WaitAsync(linkedCancellationTokenSource.Token).ConfigureAwait(false);
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _acceptingLeases = false;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeCurrentStateAsync().ConfigureAwait(false);
            _availableSlots.Writer.TryComplete();
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private static Channel<PageSlot> CreateChannel(int capacity)
    {
        return Channel.CreateBounded<PageSlot>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    private static TaskCompletionSource CreateDrainedTcs()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await BuildBrowserStateAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask ReleaseAsync(PageSlot pageSlot, bool unhealthy)
    {
        var activity = PagePoolDiagnostics.ActivitySource.StartActivity("pagepool.release");
        pageSlot.State = unhealthy ? PageSlotState.Unhealthy : PageSlotState.Resetting;
        var currentGeneration = Volatile.Read(ref _generation);
        var recyclePage = unhealthy || pageSlot.PageSession.IsClosed || pageSlot.Generation != currentGeneration || pageSlot.UseCount + 1 >= _options.MaxPageUses;

        try
        {
            if (!recyclePage)
            {
                await pageSlot.PageSession.ResetAsync(_options, CancellationToken.None).ConfigureAwait(false);
                pageSlot.UseCount++;
                pageSlot.State = PageSlotState.Warm;
                await EnqueueAsync(pageSlot).ConfigureAwait(false);
                return;
            }

            await ReplaceAsync(pageSlot).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            pageSlot.ConsecutiveFailures++;
            _logger.LogWarning(exception, "Page recycle failed.");
            await ReplaceAsync(pageSlot).ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Decrement(ref _leasedPages) == 0)
            {
                _drainedTcs.TrySetResult();
            }

            activity?.Dispose();
        }
    }

    private async Task EnqueueAsync(PageSlot pageSlot)
    {
        ThrowIfDisposed();
        await _availableSlots.Writer.WriteAsync(pageSlot).ConfigureAwait(false);
        Interlocked.Increment(ref _availablePages);
    }

    private async Task ReplaceAsync(PageSlot retiredSlot)
    {
        retiredSlot.State = PageSlotState.Disposed;

        try
        {
            await retiredSlot.PageSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Page disposal failed during replacement.");
        }

        if (_disposed || !_acceptingLeases)
        {
            return;
        }

        if (_browserSession is null || !_browserSession.IsConnected)
        {
            await AwaitRebuildAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            var replacement = await CreatePageSlotAsync(Volatile.Read(ref _generation), CancellationToken.None).ConfigureAwait(false);
            await EnqueueAsync(replacement).ConfigureAwait(false);
            Interlocked.Increment(ref _replacementCount);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Page replacement failed.");
            await StartRebuildAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<PageSlot> CreatePageSlotAsync(int generation, CancellationToken cancellationToken)
    {
        if (_browserSession is null)
        {
            throw new PagePoolUnavailableException("Browser session is not available.");
        }

        var pageSession = await _browserSession.CreatePageAsync(cancellationToken).ConfigureAwait(false);
        await pageSession.InitializeAsync(_options, cancellationToken).ConfigureAwait(false);

        return new PageSlot
        {
            Generation = generation,
            PageSession = pageSession,
            State = PageSlotState.Warm
        };
    }

    private async Task BuildBrowserStateAsync(CancellationToken cancellationToken)
    {
        var channel = CreateChannel(_options.PoolSize);
        var generation = Interlocked.Increment(ref _generation);
        var browserSession = await _browserSessionFactory.CreateAsync(_options, cancellationToken).ConfigureAwait(false);
        browserSession.Disconnected += OnBrowserDisconnected;

        try
        {
            for (var index = 0; index < _options.PoolSize; index++)
            {
                var slot = await CreatePageSlotAsync(browserSession, generation, cancellationToken).ConfigureAwait(false);
                await channel.Writer.WriteAsync(slot, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _availablePages);
            }
        }
        catch
        {
            browserSession.Disconnected -= OnBrowserDisconnected;
            await browserSession.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var previousChannel = _availableSlots;
        var previousBrowser = _browserSession;
        _availableSlots = channel;
        _browserSession = browserSession;

        previousChannel.Writer.TryComplete();
        await DisposeQueuedSlotsAsync(previousChannel).ConfigureAwait(false);

        if (previousBrowser is not null)
        {
            previousBrowser.Disconnected -= OnBrowserDisconnected;
            await previousBrowser.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<PageSlot> CreatePageSlotAsync(IBrowserSession browserSession, int generation, CancellationToken cancellationToken)
    {
        var pageSession = await browserSession.CreatePageAsync(cancellationToken).ConfigureAwait(false);
        await pageSession.InitializeAsync(_options, cancellationToken).ConfigureAwait(false);

        return new PageSlot
        {
            Generation = generation,
            PageSession = pageSession,
            State = PageSlotState.Warm
        };
    }

    private async Task DisposeCurrentStateAsync()
    {
        var currentChannel = _availableSlots;
        var currentBrowser = _browserSession;
        _browserSession = null;
        currentChannel.Writer.TryComplete();

        await DisposeQueuedSlotsAsync(currentChannel).ConfigureAwait(false);

        if (currentBrowser is not null)
        {
            currentBrowser.Disconnected -= OnBrowserDisconnected;
            await currentBrowser.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeQueuedSlotsAsync(Channel<PageSlot> channel)
    {
        while (channel.Reader.TryRead(out var slot))
        {
            try
            {
                await slot.PageSession.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Page disposal failed.");
            }

            Interlocked.Decrement(ref _availablePages);
        }
    }

    private async Task StartRebuildAsync(CancellationToken cancellationToken)
    {
        Task rebuildTask;

        lock (_stateLock)
        {
            _rebuildTask ??= RebuildCoreAsync(cancellationToken);
            rebuildTask = _rebuildTask;
        }

        await rebuildTask.ConfigureAwait(false);
    }

    private async Task AwaitRebuildAsync(CancellationToken cancellationToken)
    {
        if (_rebuildTask is null)
        {
            await StartRebuildAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _rebuildTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RebuildCoreAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _logger.LogWarning("Rebuilding browser session.");
            await BuildBrowserStateAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _browserRestartCount);
        }
        finally
        {
            lock (_stateLock)
            {
                _rebuildTask = null;
            }

            _lifecycleGate.Release();
        }
    }

    private void OnBrowserDisconnected(object? sender, EventArgs eventArgs)
    {
        _ = StartRebuildAsync(CancellationToken.None);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new PagePoolDisposedException();
        }
    }

    private void RegisterMetrics()
    {
        PagePoolDiagnostics.Meter.CreateObservableGauge("puppeteer_page_pool.available_pages", () => Volatile.Read(ref _availablePages));
        PagePoolDiagnostics.Meter.CreateObservableGauge("puppeteer_page_pool.leased_pages", () => Volatile.Read(ref _leasedPages));
        PagePoolDiagnostics.Meter.CreateObservableGauge("puppeteer_page_pool.waiting_requests", () => Volatile.Read(ref _waitingRequests));
        PagePoolDiagnostics.Meter.CreateObservableCounter("puppeteer_page_pool.replacements", () => Volatile.Read(ref _replacementCount));
        PagePoolDiagnostics.Meter.CreateObservableCounter("puppeteer_page_pool.browser_restarts", () => Volatile.Read(ref _browserRestartCount));
    }

    private void ResetDrainedSignal()
    {
        if (!_drainedTcs.Task.IsCompleted)
        {
            return;
        }

        Interlocked.CompareExchange(ref _drainedTcs, CreateDrainedTcs(), _drainedTcs);
    }
}
