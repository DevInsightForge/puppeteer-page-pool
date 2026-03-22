using Microsoft.Extensions.Hosting;

namespace PuppeteerPagePool;

internal sealed class PagePoolHostedService : IHostedService
{
    private readonly PagePool _pagePool;

    public PagePoolHostedService(PagePool pagePool)
    {
        _pagePool = pagePool;
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
