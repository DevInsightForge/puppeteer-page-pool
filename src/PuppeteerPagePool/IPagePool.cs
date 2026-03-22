namespace PuppeteerPagePool;

/// <summary>
/// Provides pooled, callback-based access to leased page instances.
/// </summary>
public interface IPagePool : IAsyncDisposable
{
    /// <summary>
    /// Leases a page, executes the callback, and returns the page to the pool.
    /// </summary>
    /// <param name="operation">Asynchronous callback that uses the leased page.</param>
    /// <param name="cancellationToken">Token used to cancel lease acquisition.</param>
    /// <returns>A task that completes when the callback has finished and the page has been returned.</returns>
    ValueTask ExecuteAsync(
        Func<ILeasedPage, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases a page, executes the callback, returns the page to the pool, and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="operation">Asynchronous callback that uses the leased page.</param>
    /// <param name="cancellationToken">Token used to cancel lease acquisition.</param>
    /// <returns>The callback result.</returns>
    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<ILeasedPage, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current pool health snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the snapshot request.</param>
    /// <returns>Current counters and readiness information for the pool.</returns>
    ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
