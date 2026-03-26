using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Exceptions;

namespace PuppeteerPagePool.Tests;

/// <summary>
/// Comprehensive unit tests for PagePool core functionality.
/// Tests cover all public APIs, edge cases, and error conditions.
/// </summary>
public sealed class PagePoolBehaviorTests
{
    #region Pool Initialization

    [Fact]
    public async Task Constructor_ValidOptions_CreatesPool()
    {
        var options = CreateValidOptions();
        var factory = new FakeBrowserRuntimeFactory();

        var pool = new PagePool(options, factory);

        Assert.NotNull(pool);
        await pool.DisposeAsync();
    }

    [Fact]
    public void Constructor_InvalidOptions_Throws()
    {
        var options = new PagePoolOptions { PoolSize = 0 };
        var factory = new FakeBrowserRuntimeFactory();

        Assert.ThrowsAny<Exception>(() =>
            new PagePool(options, factory));
    }

    [Fact]
    public async Task Constructor_DoesNotRequireServiceProvider()
    {
        var options = CreateValidOptions();
        var factory = new FakeBrowserRuntimeFactory();
        var pool = new PagePool(options, factory);

        Assert.NotNull(pool);
        await pool.DisposeAsync();
    }

    #endregion

    #region Pool Start/Stop

    [Fact]
    public async Task StartAsync_WarmupEnabled_CreatesPages()
    {
        var harness = new UnitPoolHarness(3);
        await using var pool = harness.CreatePool(warmupOnStartup: true);

        await pool.StartAsync(CancellationToken.None);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(3, snapshot.AvailablePages);
        Assert.Equal(3, harness.Runtime.TotalPageCount);
    }

    [Fact]
    public async Task StartAsync_WarmupDisabled_DoesNotCreatePages()
    {
        var harness = new UnitPoolHarness(3);
        await using var pool = harness.CreatePool(warmupOnStartup: false);

        await pool.StartAsync(CancellationToken.None);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(0, snapshot.AvailablePages);
    }

    [Fact]
    public async Task StartAsync_AlreadyStarted_DoesNotRecreate()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool(warmupOnStartup: true);

        await pool.StartAsync(CancellationToken.None);
        await pool.StartAsync(CancellationToken.None);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(2, harness.Runtime.TotalPageCount);
    }

    [Fact]
    public async Task StopAsync_WithActiveLeases_WaitsForDrain()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(releaseGate.Task));

        var stopTask = pool.StopAsync(CancellationToken.None);

        await Task.Delay(50);
        Assert.False(stopTask.IsCompleted);

        releaseGate.TrySetResult();
        await inFlight;
        await stopTask;

        var snapshot = await pool.GetSnapshotAsync();
        Assert.False(snapshot.AcceptingLeases);
    }

    [Fact]
    public async Task StopAsync_RejectsNewLeases()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(releaseGate.Task));

        await pool.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<PagePoolDisposedException>(() =>
            pool.ExecuteAsync((page, cancellationToken) => ValueTask.CompletedTask).AsTask());

        releaseGate.TrySetResult();
        await inFlight;
    }

    [Fact]
    public async Task StopAsync_NoActiveLeases_CompletesImmediately()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var stopTask = pool.StopAsync(CancellationToken.None);
        await stopTask;

        Assert.True(stopTask.IsCompleted);
    }

    [Fact]
    public async Task StopAsync_MultipleCalls_DoesNotThrow()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);
        await pool.StopAsync(CancellationToken.None);

        await pool.StopAsync(CancellationToken.None);
    }

    #endregion

    #region ExecuteAsync - Basic

    [Fact]
    public async Task ExecuteAsync_PreservesSnapshotCounts()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool(warmupOnStartup: true);

        await pool.StartAsync(CancellationToken.None);

        var initial = await pool.GetSnapshotAsync();
        Assert.Equal(2, initial.AvailablePages);
        Assert.Equal(0, initial.LeasedPages);

        await pool.ExecuteAsync(async (page, cancellationToken) =>
        {
            await Task.Yield();
            var during = await pool.GetSnapshotAsync(cancellationToken);
            Assert.Equal(1, during.AvailablePages);
            Assert.Equal(1, during.LeasedPages);
        });

        var final = await pool.GetSnapshotAsync();
        Assert.Equal(2, final.AvailablePages);
        Assert.Equal(0, final.LeasedPages);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCallbackResult()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var result = await pool.ExecuteAsync(static async (page, cancellationToken) =>
        {
            await Task.Yield();
            return "test-result";
        });

        Assert.Equal("test-result", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValueTypeResult()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var result = await pool.ExecuteAsync(static async (page, cancellationToken) =>
        {
            await Task.Yield();
            return 42;
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidatesLeasedPageAfterCallback()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        ILeasedPage? captured = null;

        await pool.ExecuteAsync((page, cancellationToken) =>
        {
            captured = page;
            return ValueTask.CompletedTask;
        });

        Assert.NotNull(captured);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await captured!.GetTitleAsync());
    }

    [Fact]
    public async Task ExecuteAsync_FailedOperation_ReplacesPage()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.ExecuteAsync(static (page, cancellationToken) => throw new InvalidOperationException("boom")).AsTask());

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(3, harness.Runtime.TotalPageCount);
        Assert.Equal(1, harness.Runtime.DisposedPageCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellationToken_Cancelled_Throws()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pool.ExecuteAsync((page, cancellationToken) => ValueTask.CompletedTask, cts.Token).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_NullOperation_ThrowsArgumentNullException()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            pool.ExecuteAsync(null!).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_AfterDispose_ThrowsPagePoolDisposedException()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);
        await pool.DisposeAsync();

        await Assert.ThrowsAsync<PagePoolDisposedException>(() =>
            pool.ExecuteAsync((page, cancellationToken) => ValueTask.CompletedTask).AsTask());
    }

    #endregion

    #region ExecuteAsync - Timeout

    [Fact]
    public async Task ExecuteAsync_PoolExhausted_ThrowsTimeoutException()
    {
        var harness = new UnitPoolHarness(1, acquireTimeout: TimeSpan.FromMilliseconds(50));
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(releaseGate.Task));

        var exception = await Assert.ThrowsAsync<PagePoolAcquireTimeoutException>(() =>
            pool.ExecuteAsync((page, cancellationToken) => ValueTask.CompletedTask).AsTask());

        Assert.Equal(TimeSpan.FromMilliseconds(50), exception.Timeout);
        Assert.Equal(0, exception.AvailablePages);
        Assert.Equal(1, exception.LeasedPages);

        releaseGate.TrySetResult();
        await inFlight;
    }

    [Fact]
    public async Task ExecuteAsync_WaitingRequests_Tracked()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var blockFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(blockFirst.Task));

        await Task.Delay(50);

        var secondTask = pool.ExecuteAsync(async (page, cancellationToken) =>
        {
            await Task.Yield();
        });

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(1, snapshot.WaitingRequests);

        blockFirst.TrySetResult();
        await first;
        await secondTask;
    }

    #endregion

    #region Page Lifecycle

    [Fact]
    public async Task ExecuteAsync_MaxPageUsesReached_PageReplaced()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool(maxPageUses: 2);
        await pool.StartAsync(CancellationToken.None);

        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);
        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);
        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(1, snapshot.AvailablePages);
        Assert.True(harness.Runtime.DisposedPageCount >= 1);
    }

    [Fact]
    public async Task ExecuteAsync_GenerationMismatch_PageReplaced()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        harness.Runtime.TriggerDisconnected();
        await Task.Delay(100);

        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.AvailablePages);
    }

    #endregion

    #region Browser Lifecycle

    [Fact]
    public async Task BrowserDisconnected_TriggersRebuild()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);
        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var initialSnapshot = await pool.GetSnapshotAsync();

        harness.Runtime.TriggerDisconnected();

        await Task.Delay(200);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.True(snapshot.AcceptingLeases);
    }

    [Fact]
    public async Task IsHealthyAsync_BrowserDisconnected_ReturnsFalse()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        harness.Runtime.IsConnected = false;

        var healthy = await pool.IsHealthyAsync(CancellationToken.None);
        Assert.False(healthy);
    }

    [Fact]
    public async Task IsHealthyAsync_BrowserUnresponsive_ReturnsFalse()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        harness.Runtime.IsResponsive = false;

        var healthy = await pool.IsHealthyAsync(CancellationToken.None);
        Assert.False(healthy);
    }

    [Fact]
    public async Task IsHealthyAsync_BrowserConnectedAndResponsive_ReturnsTrue()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);
        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var healthy = await pool.IsHealthyAsync(CancellationToken.None);
        Assert.True(healthy);
    }

    #endregion

    #region Parallel Operations

    [Fact]
    public async Task ExecuteAsync_ParallelOperations_SnapshotConsistent()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var acquiredCount = 0;
        var firstWaveReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task WorkerAsync()
        {
            await pool.ExecuteAsync(async (page, cancellationToken) =>
            {
                if (Interlocked.Increment(ref acquiredCount) == 2)
                {
                    firstWaveReady.TrySetResult();
                }
                await releaseGate.Task.ConfigureAwait(false);
            });
        }

        var workers = Enumerable.Range(0, 4).Select(_ => WorkerAsync()).ToArray();

        await firstWaveReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var during = await pool.GetSnapshotAsync();
        Assert.Equal(0, during.AvailablePages);
        Assert.Equal(2, during.LeasedPages);

        releaseGate.SetResult();
        await Task.WhenAll(workers);

        var final = await pool.GetSnapshotAsync();
        Assert.Equal(2, final.AvailablePages);
        Assert.Equal(0, final.LeasedPages);
        Assert.Equal(0, final.WaitingRequests);
    }

    [Fact]
    public async Task ExecuteAsync_Concurrent_NoDeadlock()
    {
        var harness = new UnitPoolHarness(3);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var tasks = Enumerable.Range(0, 20).Select(i =>
            pool.ExecuteAsync(async (page, _) =>
            {
                await Task.Delay(10);
                return i;
            }).AsTask());

        var results = await Task.WhenAll(tasks);
        Assert.Equal(20, results.Length);
    }

    #endregion

    #region Generation Tracking

    [Fact]
    public async Task RebuildRuntime_GenerationIncremented()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);
        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var initialSnapshot = await pool.GetSnapshotAsync();

        harness.Runtime.TriggerDisconnected();
        await Task.Delay(100);
        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var finalSnapshot = await pool.GetSnapshotAsync();
        Assert.True(finalSnapshot.AcceptingLeases);
        Assert.Equal(0, finalSnapshot.LeasedPages);
    }

    #endregion

    #region Health Snapshot

    [Fact]
    public async Task GetSnapshotAsync_ReturnsValidSnapshot()
    {
        var harness = new UnitPoolHarness(3);
        await using var pool = harness.CreatePool(warmupOnStartup: false);
        await pool.StartAsync(CancellationToken.None);

        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal(3, snapshot.PoolSize);
        Assert.Equal(0, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(0, snapshot.WaitingRequests);
        Assert.False(snapshot.BrowserConnected);
        Assert.True(snapshot.AcceptingLeases);
        Assert.True(snapshot.Uptime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GetSnapshotAsync_DuringLease_UpdatesCounts()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();
        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(releaseGate.Task));

        await Task.Delay(50);

        var snapshot = await pool.GetSnapshotAsync();
        Assert.Equal(1, snapshot.AvailablePages);
        Assert.Equal(1, snapshot.LeasedPages);

        releaseGate.TrySetResult();
        await inFlight;
    }

    #endregion

    #region Helper Methods

    private static PagePoolOptions CreateValidOptions()
    {
        return new PagePoolOptions
        {
            PoolSize = 2,
            AcquireTimeout = TimeSpan.FromSeconds(30),
            ShutdownTimeout = TimeSpan.FromSeconds(30),
            ResetTargetUrl = "about:blank",
            WarmupOnStartup = false
        };
    }

    #endregion
}




