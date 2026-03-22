using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PuppeteerPagePool.Internal;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests;

internal static class TestPagePoolFactory
{
    public static PagePool Create(
        FakeBrowserSessionFactory? browserSessionFactory = null,
        PuppeteerPagePoolOptions? options = null)
    {
        return new PagePool(
            Options.Create(options ?? new PuppeteerPagePoolOptions
            {
                PoolSize = 1,
                AcquireTimeout = TimeSpan.FromMilliseconds(100),
                ShutdownTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLogger<PagePool>.Instance,
            browserSessionFactory ?? new FakeBrowserSessionFactory());
    }
}

internal sealed class FakeBrowserSessionFactory : IBrowserSessionFactory
{
    private readonly Queue<FakeBrowserSession> _preparedSessions = new();

    public int CreateCount { get; private set; }

    public List<FakeBrowserSession> CreatedSessions { get; } = [];

    public void Enqueue(FakeBrowserSession session)
    {
        _preparedSessions.Enqueue(session);
    }

    public ValueTask<IBrowserSession> CreateAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
    {
        CreateCount++;
        var session = _preparedSessions.Count > 0 ? _preparedSessions.Dequeue() : new FakeBrowserSession(options.PoolSize * 4);
        CreatedSessions.Add(session);
        return ValueTask.FromResult<IBrowserSession>(session);
    }
}

internal sealed class FakeBrowserSession : IBrowserSession
{
    private readonly int _pageCapacity;
    private int _createdPages;

    public FakeBrowserSession(int pageCapacity = 8)
    {
        _pageCapacity = pageCapacity;
    }

    public bool IsConnected { get; private set; } = true;

    public event EventHandler? Disconnected;

    public List<FakePageSession> Pages { get; } = [];

    public ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Browser session is disconnected.");
        }

        if (_createdPages >= _pageCapacity)
        {
            throw new InvalidOperationException("No more fake pages are available.");
        }

        _createdPages++;
        var page = new FakePageSession();
        Pages.Add(page);
        return ValueTask.FromResult<IPageSession>(page);
    }

    public void TriggerDisconnected()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakePageSession : IPageSession
{
    private readonly IPage _page;

    public FakePageSession()
    {
        _page = InterfaceProxy.Create<IPage>();
    }

    public IPage Page => _page;

    public bool IsClosed { get; private set; }

    public int InitializeCount { get; private set; }

    public int PrepareCount { get; private set; }

    public int ResetCount { get; private set; }

    public int DisposeCount { get; private set; }

    public Func<PuppeteerPagePoolOptions, CancellationToken, ValueTask>? OnInitializeAsync { get; set; }

    public Func<PuppeteerPagePoolOptions, CancellationToken, ValueTask>? OnPrepareAsync { get; set; }

    public Func<PuppeteerPagePoolOptions, CancellationToken, ValueTask>? OnResetAsync { get; set; }

    public ValueTask InitializeAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
    {
        InitializeCount++;
        return OnInitializeAsync is null ? ValueTask.CompletedTask : OnInitializeAsync(options, cancellationToken);
    }

    public ValueTask PrepareForLeaseAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
    {
        PrepareCount++;
        return OnPrepareAsync is null ? ValueTask.CompletedTask : OnPrepareAsync(options, cancellationToken);
    }

    public ValueTask ResetAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
    {
        ResetCount++;
        return OnResetAsync is null ? ValueTask.CompletedTask : OnResetAsync(options, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsClosed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class InterfaceProxy
{
    public static T Create<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultDispatchProxy>();
    }

    private sealed class DefaultDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            return GetDefaultValue(targetMethod.ReturnType);
        }

        private static object? GetDefaultValue(Type type)
        {
            if (type == typeof(void))
            {
                return null;
            }

            if (type == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (type == typeof(ValueTask))
            {
                return ValueTask.CompletedTask;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = type.GetGenericArguments()[0];
                var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [result]);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var resultType = type.GetGenericArguments()[0];
                var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return Activator.CreateInstance(type, result);
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new TimeoutException("Condition was not satisfied.");
    }
}
