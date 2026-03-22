namespace PuppeteerPagePool;

public interface IPagePool : IAsyncDisposable
{
    ValueTask<PageLease> AcquireAsync(CancellationToken cancellationToken = default);

    ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
