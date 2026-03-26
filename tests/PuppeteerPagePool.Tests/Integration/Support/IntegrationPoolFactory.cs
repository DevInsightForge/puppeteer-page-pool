using System.Reflection;
using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Tests.Integration.Support;

internal static class IntegrationPoolFactory
{
    public static async ValueTask<IPagePool> CreateAndStartPoolAsync(PagePoolOptions options, CancellationToken cancellationToken)
    {
        var assembly = typeof(IPagePool).Assembly;
        var browserRuntimeFactoryType = assembly.GetType("PuppeteerPagePool.Browser.BrowserRuntimeFactory", throwOnError: true)!;
        var pagePoolType = assembly.GetType("PuppeteerPagePool.Core.PagePool", throwOnError: true)!;

        var browserRuntimeFactory = Activator.CreateInstance(
            browserRuntimeFactoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [null],
            culture: null)
            ?? throw new InvalidOperationException("Failed to create BrowserRuntimeFactory.");

        var pagePool = Activator.CreateInstance(
            pagePoolType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [options, browserRuntimeFactory],
            culture: null) as IPagePool
            ?? throw new InvalidOperationException("Failed to create PagePool.");

        var startAsync = pagePoolType.GetMethod("StartAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Failed to locate PagePool.StartAsync.");

        var startTask = startAsync.Invoke(pagePool, [cancellationToken]) as Task
            ?? throw new InvalidOperationException("Failed to invoke PagePool.StartAsync.");

        await startTask.ConfigureAwait(false);
        return pagePool;
    }
}

