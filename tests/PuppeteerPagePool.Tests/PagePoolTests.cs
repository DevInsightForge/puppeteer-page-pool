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
    public void Options_validation_rejects_invalid_configuration(Action<PuppeteerPagePoolOptions> mutate, Type exceptionType)
    {
        var options = new PuppeteerPagePoolOptions();
        mutate(options);

        var exception = Assert.Throws(exceptionType, options.Validate);

        Assert.NotNull(exception);
    }

    [Fact]
    public void Options_validation_accepts_valid_configuration()
    {
        var options = new PuppeteerPagePoolOptions();

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

        await using (var lease = await pool.AcquireAsync())
        {
            var during = await pool.GetSnapshotAsync();

            Assert.Equal(1, during.AvailablePages);
            Assert.Equal(1, during.LeasedPages);
        }

        var final = await pool.GetSnapshotAsync();

        Assert.Equal(2, final.AvailablePages);
        Assert.Equal(0, final.LeasedPages);
        Assert.Equal(1, harness.Session.TotalResetCount);
    }

    [Fact]
    public async Task PageLease_dispose_is_idempotent()
    {
        var page = Substitute.For<IPage>();
        var calls = 0;
        var lease = new PageLease(page, _ =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Acquire_timeout_throws_when_pool_is_exhausted()
    {
        var harness = new PoolHarness(poolSize: 1, acquireTimeout: TimeSpan.FromMilliseconds(50));
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        var lease = await pool.AcquireAsync();

        await Assert.ThrowsAsync<PagePoolAcquireTimeoutException>(() => pool.AcquireAsync().AsTask());

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Unhealthy_lease_is_replaced()
    {
        var harness = new PoolHarness(poolSize: 2);
        await using var pool = harness.CreatePool();

        await pool.StartAsync(CancellationToken.None);

        await using var lease = await pool.AcquireAsync();
        lease.MarkUnhealthy();
        await lease.DisposeAsync();

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
            await using var lease = await pool.AcquireAsync();

            if (Interlocked.Increment(ref acquiredCount) == 2)
            {
                firstWaveReady.TrySetResult();
            }

            await releaseGate.Task.ConfigureAwait(false);
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

        var lease = await pool.AcquireAsync();
        var stopTask = pool.StopAsync(CancellationToken.None);

        await Task.Delay(25);

        Assert.False(stopTask.IsCompleted);
        await Assert.ThrowsAsync<PagePoolDisposedException>(() => pool.AcquireAsync().AsTask());

        await lease.DisposeAsync();
        await stopTask;
    }

    public static IEnumerable<object[]> InvalidOptionScenarios()
    {
        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options => options.PoolSize = 0),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options => options.AcquireTimeout = TimeSpan.Zero),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options => options.ShutdownTimeout = TimeSpan.Zero),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options => options.ResetTargetUrl = "not-a-uri"),
            typeof(ArgumentException)
        ];

        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options => options.MaxPageUses = 0),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options => options.MaxConsecutiveFailures = 0),
            typeof(ArgumentOutOfRangeException)
        ];

        yield return
        [
            (Action<PuppeteerPagePoolOptions>)(options =>
            {
                options.LaunchOptions = new LaunchOptions();
                options.ConnectOptions = new ConnectOptions();
            }),
            typeof(ArgumentException)
        ];
    }

    private sealed class PoolHarness(int poolSize, TimeSpan? acquireTimeout = null)
    {
        private readonly TestBrowserSessionFactory _factory = new();

        public TestBrowserSession Session => _factory.Session;

        public PagePool CreatePool()
        {
            var options = new PuppeteerPagePoolOptions
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

        public ValueTask<IBrowserSession> CreateAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IBrowserSession>(Session);
        }
    }

    private sealed class TestBrowserSession : IBrowserSession
    {
        private readonly List<TestPageSession> _pages = [];

        public bool IsConnected { get; private set; } = true;

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

        public int InitializeCount { get; private set; }

        public int PrepareCount { get; private set; }

        public int ResetCount { get; private set; }

        public int DisposedCount { get; private set; }

        public ValueTask InitializeAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
        {
            InitializeCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask PrepareForLeaseAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
        {
            PrepareCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
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
