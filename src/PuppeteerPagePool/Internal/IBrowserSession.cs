using PuppeteerSharp;

namespace PuppeteerPagePool.Internal;

internal interface IBrowserSession : IAsyncDisposable
{
    bool IsConnected { get; }

    event EventHandler? Disconnected;

    ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken);

    ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

internal interface IPageSession : IAsyncDisposable
{
    IPage Page { get; }

    bool IsClosed { get; }

    ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken);

    ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken);

    ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken);
}

internal interface IBrowserSessionFactory
{
    ValueTask<IBrowserSession> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken);
}
