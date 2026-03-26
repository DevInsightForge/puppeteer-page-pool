using System.Reflection;
using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests;

internal sealed class FakeBrowserRuntimeFactory : IBrowserRuntimeFactory
{
    public FakeBrowserRuntime Runtime { get; } = new();

    public ValueTask<IBrowserRuntime> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IBrowserRuntime>(Runtime);
    }
}

internal sealed class FakeBrowserRuntime : IBrowserRuntime
{
    private readonly List<FakePageSession> _pages = [];

    public bool IsConnected { get; set; } = true;
    public bool IsResponsive { get; set; } = true;

    public event EventHandler? Disconnected;

    public IReadOnlyList<FakePageSession> Pages => _pages;
    public int TotalPageCount => _pages.Count;
    public int TotalResetCount => _pages.Sum(page => page.ResetCount);
    public int DisposedPageCount => _pages.Count(page => page.DisposeCount > 0);

    public ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
    {
        var page = new FakePageSession();
        _pages.Add(page);
        return ValueTask.FromResult<IPageSession>(page);
    }

    public ValueTask<bool> IsResponsiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IsConnected && IsResponsive);
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public void TriggerDisconnected()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakePageSession : IPageSession
{
    public IPage Page { get; } = InterfaceProxy.Create<IPage>();
    public bool IsClosed { get; set; }
    public string ReadyState { get; set; } = "complete";
    public int InitializeCount { get; private set; }
    public int PrepareCount { get; private set; }
    public int ResetCount { get; private set; }
    public int DisposeCount { get; private set; }

    public ValueTask InitializeAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        InitializeCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask PrepareForLeaseAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        PrepareCount++;
        if (ReadyState is not ("complete" or "interactive"))
        {
            throw new InvalidOperationException("Invalid ready state.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        ResetCount++;
        ReadyState = "complete";
        return ValueTask.CompletedTask;
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

    private class DefaultDispatchProxy : DispatchProxy
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

