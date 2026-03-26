namespace PuppeteerPagePool.Abstractions;

internal interface IBrowserRuntime : IAsyncDisposable
{
    bool IsConnected { get; }

    event EventHandler? Disconnected;

    ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken);

    ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
