namespace PuppeteerPagePool;

/// <summary>
/// Thrown when a page cannot be acquired before the configured timeout.
/// </summary>
public sealed class PagePoolAcquireTimeoutException : TimeoutException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagePoolAcquireTimeoutException"/> class.
    /// </summary>
    /// <param name="timeout">The configured lease acquisition timeout.</param>
    public PagePoolAcquireTimeoutException(TimeSpan timeout)
        : base($"Timed out acquiring a page lease after {timeout}.")
    {
    }
}

/// <summary>
/// Thrown when a pool operation is requested after disposal or lease acceptance is stopped.
/// </summary>
public sealed class PagePoolDisposedException : ObjectDisposedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagePoolDisposedException"/> class.
    /// </summary>
    public PagePoolDisposedException()
        : base(nameof(PagePool))
    {
    }
}

/// <summary>
/// Represents pool unavailability caused by browser/session initialization failures.
/// </summary>
public sealed class PagePoolUnavailableException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagePoolUnavailableException"/> class.
    /// </summary>
    /// <param name="message">Failure message.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public PagePoolUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
