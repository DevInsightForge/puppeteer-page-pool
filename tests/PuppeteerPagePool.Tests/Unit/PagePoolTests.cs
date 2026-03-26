using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Exceptions;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests;

/// <summary>
/// Legacy unit tests for PagePool - maintained for compatibility.
/// </summary>
public sealed class PagePoolTests
{
    [Theory]
    [InlineData("PoolSize", "PoolSize")]
    [InlineData("AcquireTimeout", "AcquireTimeout")]
    [InlineData("ShutdownTimeout", "ShutdownTimeout")]
    [InlineData("ResetTargetUrl", "ResetTargetUrl")]
    [InlineData("MaxPageUses", "MaxPageUses")]
    [InlineData("LaunchAndConnectTogether", "LaunchOptions")]
    [InlineData("ResetNavigationTimeout", "ResetNavigationTimeout")]
    [InlineData("ResetWaitConditions", "ResetWaitConditions")]
    public void Validate_rejects_invalid_configuration(string scenario, string paramName)
    {
        var options = new PagePoolOptions();
        ApplyInvalidScenario(options, scenario);

        var exception = Record.Exception(() => options.Validate());

        Assert.NotNull(exception);
        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal(paramName, argumentException.ParamName);
    }

    [Fact]
    public void Validate_accepts_valid_configuration()
    {
        var options = new PagePoolOptions();

        options.Validate();
    }

    [Fact]
    public async Task Execute_async_preserves_snapshot_counts()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var initial = await pool.GetSnapshotAsync();

        Assert.Equal(2, initial.AvailablePages);
        Assert.Equal(0, initial.LeasedPages);

        await pool.ExecuteAsync(
            async (page, cancellationToken) =>
            {
                await Task.Yield();

                var during = await pool.GetSnapshotAsync(cancellationToken);

                Assert.Equal(1, during.AvailablePages);
                Assert.Equal(1, during.LeasedPages);
                Assert.NotNull(page);
            });

        var final = await pool.GetSnapshotAsync();

        Assert.Equal(2, final.AvailablePages);
        Assert.Equal(0, final.LeasedPages);
        Assert.Equal(1, harness.Runtime.TotalResetCount);
    }

    [Fact]
    public async Task Execute_async_returns_callback_result()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var result = await pool.ExecuteAsync(
            static async (page, cancellationToken) =>
            {
                await Task.Yield();
                return page is not null;
            },
            CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Execute_async_invalidates_leased_page_after_callback()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        ILeasedPage? captured = null;

        await pool.ExecuteAsync(
            static (page, cancellationToken) =>
            {
                return ValueTask.CompletedTask;
            });

        await pool.ExecuteAsync(
            (page, cancellationToken) =>
            {
                captured = page;
                return ValueTask.CompletedTask;
            });

        Assert.NotNull(captured);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => captured!.GetTitleAsync());
    }

    [Fact]
    public async Task Acquire_timeout_throws_when_pool_is_exhausted()
    {
        var harness = new UnitPoolHarness(1, TimeSpan.FromMilliseconds(50));
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(releaseGate.Task));

        await Assert.ThrowsAsync<PagePoolAcquireTimeoutException>(
            () => pool.ExecuteAsync((page, cancellationToken) => ValueTask.CompletedTask).AsTask());

        releaseGate.TrySetResult();
        await inFlight;
    }

    [Fact]
    public async Task Failed_operation_replaces_page()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.ExecuteAsync(static (page, cancellationToken) => throw new InvalidOperationException("boom")).AsTask());

        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(3, harness.Runtime.TotalPageCount);
        Assert.Equal(1, harness.Runtime.DisposedPageCount);
    }

    [Fact]
    public async Task Invalid_page_is_replaced_before_lease()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);
        harness.Runtime.Pages[0].ReadyState = "loading";

        await pool.ExecuteAsync(static (page, cancellationToken) => ValueTask.CompletedTask);

        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal(1, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(2, harness.Runtime.TotalPageCount);
        Assert.Equal(1, harness.Runtime.DisposedPageCount);
    }

    [Fact]
    public async Task Parallel_contention_keeps_snapshot_consistent()
    {
        var harness = new UnitPoolHarness(2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var acquiredCount = 0;
        var firstWaveReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task WorkerAsync()
        {
            await pool.ExecuteAsync(
                async (page, cancellationToken) =>
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
    public async Task Stop_rejects_new_leases()
    {
        var harness = new UnitPoolHarness(1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.ExecuteAsync((page, cancellationToken) => new ValueTask(releaseGate.Task));
        var stopTask = pool.StopAsync(CancellationToken.None);

        await Task.Delay(25);

        Assert.False(stopTask.IsCompleted);
        await Assert.ThrowsAsync<PagePoolDisposedException>(
            () => pool.ExecuteAsync((page, cancellationToken) => ValueTask.CompletedTask).AsTask());

        releaseGate.TrySetResult();
        await inFlight;
        await stopTask;
    }

    [Fact]
    public async Task Browser_health_check_detects_unresponsive_runtime()
    {
        var harness = new UnitPoolHarness(1);
        harness.Runtime.IsResponsive = false;
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var healthy = await pool.IsHealthyAsync(CancellationToken.None);

        Assert.False(healthy);
    }

    private static void ApplyInvalidScenario(PagePoolOptions options, string scenario)
    {
        switch (scenario)
        {
            case "PoolSize":
                options.PoolSize = 0;
                return;
            case "AcquireTimeout":
                options.AcquireTimeout = TimeSpan.Zero;
                return;
            case "ShutdownTimeout":
                options.ShutdownTimeout = TimeSpan.Zero;
                return;
            case "ResetTargetUrl":
                options.ResetTargetUrl = "not-a-uri";
                return;
            case "MaxPageUses":
                options.MaxPageUses = 0;
                return;
            case "LaunchAndConnectTogether":
                options.LaunchOptions = new LaunchOptions();
                options.ConnectOptions = new ConnectOptions
                {
                    BrowserWSEndpoint = "ws://127.0.0.1:3000"
                };
                return;
            case "ResetNavigationTimeout":
                options.ResetNavigationTimeout = TimeSpan.Zero;
                return;
            case "ResetWaitConditions":
                options.ResetWaitConditions = [];
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown invalid configuration scenario.");
        }
    }

}

