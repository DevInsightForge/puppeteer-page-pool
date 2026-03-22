using System.Threading;
using PuppeteerSharp;

namespace PuppeteerPagePool;

public sealed class PageLease : IAsyncDisposable
{
    private readonly Func<bool, ValueTask> _releaseAsync;
    private int _disposed;

    internal PageLease(IPage page, Func<bool, ValueTask> releaseAsync)
    {
        Page = page;
        _releaseAsync = releaseAsync;
    }

    public IPage Page { get; }

    public void MarkUnhealthy()
    {
        Unhealthy = true;
    }

    public bool Unhealthy { get; private set; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return _releaseAsync(Unhealthy);
    }
}
