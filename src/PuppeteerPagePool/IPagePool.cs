using PuppeteerSharp;

namespace PuppeteerPagePool;

/// <summary>
/// Provides pooled, callback-based access to <see cref="IPage"/> instances.
/// </summary>
public interface IPagePool : IAsyncDisposable
{
    /// <summary>
    /// Leases a page, executes the callback, and returns the page to the pool.
    /// </summary>
    /// <param name="operation">Synchronous callback that uses the leased page.</param>
    /// <param name="cancellationToken">Token used to cancel lease acquisition.</param>
    /// <returns>A task that completes when the callback has finished and the page has been returned.</returns>
    ValueTask WithPage(
        Action<IPage> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases a page, executes the callback, and returns the page to the pool.
    /// </summary>
    /// <param name="operation">Asynchronous callback that uses the leased page.</param>
    /// <param name="cancellationToken">Token used to cancel lease acquisition.</param>
    /// <returns>A task that completes when the callback has finished and the page has been returned.</returns>
    ValueTask WithPage(
        Func<IPage, ValueTask> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases a page, executes the callback, returns the page to the pool, and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="operation">Synchronous callback that uses the leased page.</param>
    /// <param name="cancellationToken">Token used to cancel lease acquisition.</param>
    /// <returns>The callback result.</returns>
    ValueTask<TResult> WithPage<TResult>(
        Func<IPage, TResult> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases a page, executes the callback, returns the page to the pool, and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="operation">Asynchronous callback that uses the leased page.</param>
    /// <param name="cancellationToken">Token used to cancel lease acquisition.</param>
    /// <returns>The callback result.</returns>
    ValueTask<TResult> WithPage<TResult>(
        Func<IPage, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current pool health snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the snapshot request.</param>
    /// <returns>Current counters and readiness information for the pool.</returns>
    ValueTask<PagePoolHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
