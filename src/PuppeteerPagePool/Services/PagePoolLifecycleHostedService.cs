using Microsoft.Extensions.Hosting;
using PuppeteerPagePool.Abstractions;

namespace PuppeteerPagePool.Services;

internal sealed class PagePoolLifecycleHostedService(IPagePool pagePool, ILogger<PagePoolLifecycleHostedService>? logger = null) : IHostedService
{
    private readonly IPagePool _pagePool = pagePool;
    private readonly ILogger<PagePoolLifecycleHostedService>? _logger = logger;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private Task? _startupTask;

    public Task StartAsync(CancellationToken cancellationToken)
        => StartAsyncInternal();

    public Task StopAsync(CancellationToken cancellationToken)
        => StopAsyncInternal(cancellationToken);

    private Task StartAsyncInternal()
    {
        _logger?.LogInformation("Starting page pool hosted service in background.");
        _startupTask = Task.Run(
            () => RunStartupAsync(_startupCancellationTokenSource.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _pagePool.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Page pool hosted service started.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Page pool hosted service startup canceled.");
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Page pool hosted service failed to start.");
        }
    }

    private async Task StopAsyncInternal(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Stopping page pool hosted service.");

        _startupCancellationTokenSource.Cancel();

        if (_startupTask is not null)
        {
            try
            {
                await _startupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        await _pagePool.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Page pool hosted service stopped.");
    }
}

