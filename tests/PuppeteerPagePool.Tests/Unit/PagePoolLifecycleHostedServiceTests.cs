using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Health;
using PuppeteerPagePool.Services;

namespace PuppeteerPagePool.Tests;

public sealed class PagePoolLifecycleHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WhenPoolStartupIsSlow_CompletesImmediately()
    {
        var pagePool = new BlockingStartPagePool();
        var hostedService = new PagePoolLifecycleHostedService(pagePool);

        var startTask = hostedService.StartAsync(CancellationToken.None);
        await pagePool.WaitForStartAsync(TimeSpan.FromSeconds(2));

        Assert.True(startTask.IsCompletedSuccessfully);
        Assert.Equal(1, pagePool.StartCallCount);

        pagePool.CompleteStartup();
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhenPoolStartupIsInFlight_CancelsStartupThenStopsPool()
    {
        var pagePool = new BlockingStartPagePool();
        var hostedService = new PagePoolLifecycleHostedService(pagePool);

        await hostedService.StartAsync(CancellationToken.None);
        await pagePool.WaitForStartAsync(TimeSpan.FromSeconds(2));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(stopTimeout.Token);

        Assert.True(pagePool.StartWasCanceled);
        Assert.Equal(1, pagePool.StopCallCount);
    }

    [Fact]
    public async Task HostStartup_CompletesWhilePagePoolStartupIsStillRunning()
    {
        var pagePool = new BlockingStartPagePool();
        var appStartupSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPagePool>(pagePool);
                services.AddSingleton(appStartupSignal);
                services.AddHostedService<PagePoolLifecycleHostedService>();
                services.AddHostedService<AppStartupSignalHostedService>();
            })
            .Build();

        using var startTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await host.StartAsync(startTimeout.Token);
        await pagePool.WaitForStartAsync(TimeSpan.FromSeconds(2));

        Assert.True(appStartupSignal.Task.IsCompletedSuccessfully);
        Assert.Equal(1, pagePool.StartCallCount);
        Assert.False(pagePool.StartCompleted);

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await host.StopAsync(stopTimeout.Token);

        Assert.True(pagePool.StartWasCanceled);
        Assert.Equal(1, pagePool.StopCallCount);
    }

    private sealed class BlockingStartPagePool : IPagePool
    {
        private readonly TaskCompletionSource _startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _startEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCallCount;
        private int _stopCallCount;

        public int StartCallCount => Volatile.Read(ref _startCallCount);
        public int StopCallCount => Volatile.Read(ref _stopCallCount);
        public bool StartCompleted { get; private set; }
        public bool StartWasCanceled { get; private set; }

        Task IPagePool.StartAsync(CancellationToken cancellationToken)
            => StartAsyncInternal(cancellationToken);

        Task IPagePool.StopAsync(CancellationToken cancellationToken)
            => StopAsyncInternal(cancellationToken);

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;

        public ValueTask ExecuteAsync(
            Func<ILeasedPage, CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void CompleteStartup()
        {
            _startGate.TrySetResult();
        }

        public Task WaitForStartAsync(TimeSpan timeout)
            => _startEntered.Task.WaitAsync(timeout);

        private async Task StartAsyncInternal(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCallCount);
            _startEntered.TrySetResult();

            try
            {
                await _startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                StartCompleted = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StartWasCanceled = true;
                throw;
            }
        }

        private Task StopAsyncInternal(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _stopCallCount);
            return Task.CompletedTask;
        }
    }

    private sealed class AppStartupSignalHostedService(TaskCompletionSource appStartupSignal) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            appStartupSignal.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
