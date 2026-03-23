namespace PuppeteerPagePool;

/// <summary>
/// Thrown when no page becomes available before the configured acquire timeout expires.
/// </summary>
/// <remarks>
/// Initializes a new exception for the supplied timeout.
/// </remarks>
public sealed class PagePoolAcquireTimeoutException(TimeSpan timeout) : TimeoutException($"Timed out acquiring a page lease after {timeout}.")
{
}

/// <summary>
/// Thrown when work is requested from a disposed pool or from a pool that no longer accepts leases.
/// </summary>
public sealed class PagePoolDisposedException : ObjectDisposedException
{
    /// <summary>
    /// Initializes a new disposed exception for the page pool.
    /// </summary>
    public PagePoolDisposedException()
        : base(nameof(PagePool))
    {
    }
}

/// <summary>
/// Thrown when browser startup, connection, validation, or rebuild fails.
/// </summary>
/// <remarks>
/// Initializes a new unavailable exception with an optional inner exception.
/// </remarks>
public sealed class PagePoolUnavailableException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
{
}
