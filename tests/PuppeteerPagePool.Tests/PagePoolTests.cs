using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PuppeteerPagePool.Internal;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests;

public sealed class PagePoolTests
{
    [Theory]
    [MemberData(nameof(InvalidOptionScenarios))]
    public void Options_validation_rejects_invalid_configuration(Action<PagePoolOptions> mutate, Type exceptionType)
    {
        var options = new PagePoolOptions();
        mutate(options);

        var exception = Assert.Throws(exceptionType, options.Validate);

        Assert.NotNull(exception);
    }

    [Fact]
    public void Options_validation_accepts_valid_configuration()
    {
        var options = new PagePoolOptions();

        options.Validate();
    }

    [Fact]
    public async Task Acquire_and_return_preserves_counts()
    {
        var harness = new PoolHarness(poolSize: 2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var initial = await pool.GetSnapshotAsync();

        Assert.Equal(2, initial.AvailablePages);
        Assert.Equal(0, initial.LeasedPages);

        await pool.WithPage(
            async page =>
            {
                await Task.Yield();

                var during = await pool.GetSnapshotAsync();

                Assert.Equal(1, during.AvailablePages);
                Assert.Equal(1, during.LeasedPages);
                Assert.NotNull(page);
            })
            ;

        var final = await pool.GetSnapshotAsync();

        Assert.Equal(2, final.AvailablePages);
        Assert.Equal(0, final.LeasedPages);
        Assert.Equal(1, harness.Session.TotalResetCount);
    }

    [Fact]
    public async Task Execute_async_returns_callback_result()
    {
        var harness = new PoolHarness(poolSize: 1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var result = await pool.WithPage(page => page);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Acquire_timeout_throws_when_pool_is_exhausted()
    {
        var harness = new PoolHarness(poolSize: 1, acquireTimeout: TimeSpan.FromMilliseconds(50));
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.WithPage(_ => new ValueTask(releaseGate.Task));

        await Assert.ThrowsAsync<PagePoolAcquireTimeoutException>(
            () => pool.WithPage(_ => ValueTask.CompletedTask).AsTask());

        releaseGate.TrySetResult();
        await inFlight;
    }

    [Fact]
    public async Task Unhealthy_lease_is_replaced()
    {
        var harness = new PoolHarness(poolSize: 2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.WithPage(_ => throw new InvalidOperationException("Simulated operation failure.")).AsTask());

        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(1, snapshot.ReplacementCount);
        Assert.Equal(3, harness.Session.TotalPageCount);
        Assert.Equal(1, harness.Session.DisposedPageCount);
    }

    [Fact]
    public async Task Parallel_contention_keeps_pool_counts_consistent()
    {
        var harness = new PoolHarness(poolSize: 2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var acquiredCount = 0;
        var firstWaveReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task WorkerAsync()
        {
            await pool.WithPage(
                async _ =>
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
    public async Task Shutdown_rejects_new_leases()
    {
        var harness = new PoolHarness(poolSize: 1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = pool.WithPage(_ => new ValueTask(releaseGate.Task));
        var stopTask = pool.StopAsync(CancellationToken.None);

        await Task.Delay(25);

        Assert.False(stopTask.IsCompleted);
        await Assert.ThrowsAsync<PagePoolDisposedException>(
            () => pool.WithPage(_ => ValueTask.CompletedTask).AsTask());

        releaseGate.TrySetResult();
        await inFlight;
        await stopTask;
    }

    [Fact]
    public async Task Browser_health_check_detects_unresponsive_browser()
    {
        var harness = new PoolHarness(poolSize: 1);
        harness.Session.IsResponsive = false;
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var healthy = await pool.IsBrowserHealthyAsync(CancellationToken.None);

        Assert.False(healthy);
    }

    [Fact]
    public async Task Invalid_page_health_triggers_replacement_before_lease()
    {
        var harness = new PoolHarness(poolSize: 1);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);
        harness.Session.Pages[0].ReadyState = "loading";

        await pool.WithPage(page => page);

        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal(1, snapshot.ReplacementCount);
        Assert.Equal(2, harness.Session.TotalPageCount);
        Assert.Equal(1, harness.Session.DisposedPageCount);
        Assert.Equal(0, snapshot.LeasedPages);
    }

    public static IEnumerable<object[]> InvalidOptionScenarios()
    {
        yield return
        [
            (Action<PagePoolOptions>)(options => options.PoolSize = 0),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.AcquireTimeout = TimeSpan.Zero),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.ShutdownTimeout = TimeSpan.Zero),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.ResetTargetUrl = "not-a-uri"),
            typeof(ArgumentException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.MaxPageUses = 0),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.MaxConsecutiveLeaseFailures = 0),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options =>
            {
                options.LaunchOptions = new PagePoolLaunchOptions();
                options.ConnectOptions = new PagePoolConnectOptions
                {
                    BrowserWebSocketEndpoint = "ws://127.0.0.1:3000"
                };
            }),
            typeof(ArgumentException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.BrowserHealthCheckTimeout = TimeSpan.Zero),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.ResetNavigationTimeout = TimeSpan.Zero),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PagePoolOptions>)(options => options.ResetWaitConditions = []),
            typeof(ArgumentException)
        ];
    }

    private sealed class PoolHarness(int poolSize, TimeSpan? acquireTimeout = null)
    {
        private readonly TestBrowserSessionFactory _factory = new();

        public TestBrowserSession Session => _factory.Session;

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

    private sealed class TestBrowserSessionFactory : IBrowserSessionFactory
    {
        public TestBrowserSession Session { get; } = new();

        public ValueTask<IBrowserSession> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IBrowserSession>(Session);
        }
    }

    private sealed class TestBrowserSession : IBrowserSession
    {
        private readonly List<TestPageSession> _pages = [];

        public bool IsConnected { get; private set; } = true;

        public bool IsResponsive { get; set; } = true;

        public event EventHandler? Disconnected;

        public IReadOnlyList<TestPageSession> Pages => _pages;

        public int TotalPageCount => _pages.Count;

        public int TotalResetCount => _pages.Sum(page => page.ResetCount);

        public int DisposedPageCount => _pages.Count(page => page.DisposedCount > 0);

        public ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
        {
            var page = new TestPageSession();
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

    private sealed class TestPageSession : IPageSession
    {
        public IPage Page { get; } = Substitute.For<IPage>();

        public bool IsClosed { get; private set; }

        public string ReadyState { get; set; } = "complete";

        public int InitializeCount { get; private set; }

        public int PrepareCount { get; private set; }

        public int ResetCount { get; private set; }

        public int DisposedCount { get; private set; }

        public ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken)
        {
            InitializeCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken)
        {
            PrepareCount++;

            if (options.ValidatePageHealthBeforeLease && ReadyState is not ("complete" or "interactive"))
            {
                throw new InvalidOperationException("Invalid ready state.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken)
        {
            ResetCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposedCount++;
            IsClosed = true;
            return ValueTask.CompletedTask;
        }
    }
}
