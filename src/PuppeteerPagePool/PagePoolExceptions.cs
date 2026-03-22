namespace PuppeteerPagePool;

public sealed class PagePoolAcquireTimeoutException : TimeoutException
{
    public PagePoolAcquireTimeoutException(TimeSpan timeout)
        : base($"Timed out acquiring a page lease after {timeout}.")
    {
    }
}

public sealed class PagePoolDisposedException : ObjectDisposedException
{
    public PagePoolDisposedException()
        : base(nameof(PagePool))
    {
    }
}

public sealed class PagePoolUnavailableException : InvalidOperationException
{
    public PagePoolUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
