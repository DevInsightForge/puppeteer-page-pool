using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests;

public sealed class PagePoolTests
{
    [Theory]
    [MemberData(nameof(InvalidOptionScenarios))]
    public void Validate_rejects_invalid_configuration(Action<PagePoolOptions> mutate, Type exceptionType)
    {
        var options = new PagePoolOptions();
        mutate(options);

        var exception = Assert.Throws(exceptionType, options.Validate);

        Assert.NotNull(exception);
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
        var harness = new PoolHarness(2);
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
        var harness = new PoolHarness(1);
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
        var harness = new PoolHarness(1);
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
        var harness = new PoolHarness(1, TimeSpan.FromMilliseconds(50));
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
        var harness = new PoolHarness(2);
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
        var harness = new PoolHarness(1);
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
        var harness = new PoolHarness(2);
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

        await firstWaveReady.Task.WaitAsync(TimeSpan.FromSeconds(1));

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
        var harness = new PoolHarness(1);
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
        var harness = new PoolHarness(1);
        harness.Runtime.IsResponsive = false;
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var healthy = await pool.IsHealthyAsync(CancellationToken.None);

        Assert.False(healthy);
    }

    public static IEnumerable<object[]> InvalidOptionScenarios()
    {
        yield return [(Action<PagePoolOptions>)(options => options.PoolSize = 0), typeof(ArgumentOutOfRangeException)];
        yield return [(Action<PagePoolOptions>)(options => options.AcquireTimeout = TimeSpan.Zero), typeof(ArgumentOutOfRangeException)];
        yield return [(Action<PagePoolOptions>)(options => options.ShutdownTimeout = TimeSpan.Zero), typeof(ArgumentOutOfRangeException)];
        yield return [(Action<PagePoolOptions>)(options => options.ResetTargetUrl = "not-a-uri"), typeof(ArgumentException)];
        yield return [(Action<PagePoolOptions>)(options => options.MaxPageUses = 0), typeof(ArgumentOutOfRangeException)];
        yield return
        [
            (Action<PagePoolOptions>)(options =>
            {
                options.LaunchOptions = new LaunchOptions();
                options.ConnectOptions = new ConnectOptions
                {
                    BrowserWSEndpoint = "ws://127.0.0.1:3000"
                };
            }),
            typeof(ArgumentException)
        ];
        yield return [(Action<PagePoolOptions>)(options => options.ResetNavigationTimeout = TimeSpan.Zero), typeof(ArgumentOutOfRangeException)];
        yield return [(Action<PagePoolOptions>)(options => options.ResetWaitConditions = []), typeof(ArgumentException)];
    }

    private sealed class PoolHarness(int poolSize, TimeSpan? acquireTimeout = null)
    {
        private readonly FakeBrowserRuntimeFactory _factory = new();

        public FakeBrowserRuntime Runtime => _factory.Runtime;

        public PagePool CreatePool()
        {
            var options = new PagePoolOptions
            {
                PoolSize = poolSize,
                AcquireTimeout = acquireTimeout ?? TimeSpan.FromSeconds(1),
                ShutdownTimeout = TimeSpan.FromSeconds(1),
                ResetTargetUrl = "about:blank"
            };

            return new PagePool(Options.Create(options), NullLogger<PagePool>.Instance, _factory);
        }
    }
}

internal sealed class FakeBrowserRuntimeFactory : IBrowserRuntimeFactory
{
    public FakeBrowserRuntime Runtime { get; } = new();

    public ValueTask<IBrowserRuntime> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IBrowserRuntime>(Runtime);
    }
}

internal sealed class FakeBrowserRuntime : IBrowserRuntime
{
    private readonly List<FakePageSession> _pages = [];

    public bool IsConnected { get; private set; } = true;

    public bool IsResponsive { get; set; } = true;

    public event EventHandler? Disconnected;

    public IReadOnlyList<FakePageSession> Pages => _pages;

    public int TotalPageCount => _pages.Count;

    public int TotalResetCount => _pages.Sum(page => page.ResetCount);

    public int DisposedPageCount => _pages.Count(page => page.DisposeCount > 0);

    public ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
    {
        var page = new FakePageSession();
        _pages.Add(page);
        return ValueTask.FromResult<IPageSession>(page);
    }

    public ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IsConnected && IsResponsive);
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public void TriggerDisconnected()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakePageSession : IPageSession
{
    public IPage Page { get; } = InterfaceProxy.Create<IPage>();

    public bool IsClosed { get; private set; }

    public string ReadyState { get; set; } = "complete";

    public int InitializeCount { get; private set; }

    public int PrepareCount { get; private set; }

    public int ResetCount { get; private set; }

    public int DisposeCount { get; private set; }

    public ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        InitializeCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        PrepareCount++;

        if (ReadyState is not ("complete" or "interactive"))
        {
            throw new InvalidOperationException("Invalid ready state.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        ResetCount++;
        ReadyState = "complete";
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsClosed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class InterfaceProxy
{
    public static T Create<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultDispatchProxy>();
    }

    private class DefaultDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            return GetDefaultValue(targetMethod.ReturnType);
        }

        private static object? GetDefaultValue(Type type)
        {
            if (type == typeof(void))
            {
                return null;
            }

            if (type == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (type == typeof(ValueTask))
            {
                return ValueTask.CompletedTask;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = type.GetGenericArguments()[0];
                var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [result]);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var resultType = type.GetGenericArguments()[0];
                var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return Activator.CreateInstance(type, result);
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
