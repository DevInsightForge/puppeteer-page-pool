using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Abstractions;

/// <summary>
/// Represents a reusable page instance managed by the pool runtime.
/// </summary>
internal interface IPageSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the raw PuppeteerSharp page used by the pool.
    /// </summary>
    IPage Page { get; }

    /// <summary>
    /// Gets a value indicating whether the underlying page is closed.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Runs one-time setup for a newly created page.
    /// </summary>
    ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies that the page is healthy and ready for a lease.
    /// </summary>
    ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Resets the page into the pool baseline state.
    /// </summary>
    ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken);
}

