using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Tests;

internal sealed class UnitPoolHarness
{
    private readonly FakeBrowserRuntimeFactory _factory = new();

    public UnitPoolHarness(int poolSize, TimeSpan? acquireTimeout = null)
    {
        PoolSize = poolSize;
        AcquireTimeout = acquireTimeout ?? TimeSpan.FromSeconds(1);
    }

    public int PoolSize { get; }
    public TimeSpan AcquireTimeout { get; }
    public FakeBrowserRuntime Runtime => _factory.Runtime;

    public PagePool CreatePool(
        bool warmupOnStartup = true,
        int maxPageUses = 1000,
        TimeSpan? shutdownTimeout = null,
        string resetTargetUrl = "about:blank")
    {
        var options = new PagePoolOptions
        {
            PoolSize = PoolSize,
            AcquireTimeout = AcquireTimeout,
            ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(1),
            ResetTargetUrl = resetTargetUrl,
            WarmupOnStartup = warmupOnStartup,
            MaxPageUses = maxPageUses
        };

        return new PagePool(options, _factory);
    }
}
