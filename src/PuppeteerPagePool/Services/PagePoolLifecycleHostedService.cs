using Microsoft.Extensions.Hosting;
using PuppeteerPagePool.Abstractions;

namespace PuppeteerPagePool.Services;

internal sealed class PagePoolLifecycleHostedService(IPagePool pagePool, ILogger<PagePoolLifecycleHostedService>? logger = null) : IHostedService
{
    private readonly IPagePool _pagePool = pagePool;
    private readonly ILogger<PagePoolLifecycleHostedService>? _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
        => StartAsyncInternal(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => StopAsyncInternal(cancellationToken);

    private async Task StartAsyncInternal(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting page pool hosted service.");
        await _pagePool.StartAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Page pool hosted service started.");
    }

    private async Task StopAsyncInternal(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Stopping page pool hosted service.");
        await _pagePool.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Page pool hosted service stopped.");
    }
}


