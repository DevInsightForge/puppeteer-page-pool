using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Tests.Integration.Support;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests.Integration.Fixtures;

public sealed class PagePoolStressFixture : IAsyncLifetime
{
    public IPagePool Pool { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var options = new PagePoolOptions
        {
            PoolName = "stress-test",
            PoolSize = 4,
            AcquireTimeout = TimeSpan.FromSeconds(15),
            ShutdownTimeout = TimeSpan.FromSeconds(15),
            WarmupOnStartup = true,
            MaxPageUses = 50,
            LaunchOptions = new LaunchOptions
            {
                Headless = true,
                Timeout = 20000,
                Args = ["--no-sandbox", "--disable-dev-shm-usage"]
            }
        };

        Pool = await IntegrationPoolFactory.CreateAndStartPoolAsync(options, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Pool.DisposeAsync();
    }
}

