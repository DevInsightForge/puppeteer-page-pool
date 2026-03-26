using PuppeteerPagePool.Health;

namespace PuppeteerPagePool.Abstractions;

public interface IPagePool : IAsyncDisposable
{
    internal Task StartAsync(CancellationToken cancellationToken = default);

    internal Task StopAsync(CancellationToken cancellationToken = default);

    ValueTask ExecuteAsync(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
