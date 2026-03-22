using Microsoft.Extensions.Hosting;
using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Hosting;

internal sealed class PagePoolHostedService : IHostedService
{
    private readonly PagePool _pagePool;

    public PagePoolHostedService(IPagePool pagePool)
    {
        _pagePool = pagePool as PagePool
            ?? throw new InvalidOperationException($"Registered {nameof(IPagePool)} implementation must be {nameof(PagePool)}.");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _pagePool.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _pagePool.StopAsync(cancellationToken);
    }
}
