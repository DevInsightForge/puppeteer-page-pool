using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Tests.Integration.Support;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests.Integration.Fixtures;

public sealed class PagePoolIntegrationFixture : IAsyncLifetime
{
    private LocalTestServer? _server;

    public IPagePool Pool { get; private set; } = default!;
    public string BaseUrl => _server?.BaseUrl ?? throw new InvalidOperationException("Local test server is not initialized.");

    public async Task InitializeAsync()
    {
        _server = new LocalTestServer();

        var options = new PagePoolOptions
        {
            PoolName = "integration-test",
            PoolSize = 2,
            AcquireTimeout = TimeSpan.FromSeconds(20),
            ShutdownTimeout = TimeSpan.FromSeconds(20),
            ResetTargetUrl = BaseUrl,
            WarmupOnStartup = true,
            MaxPageUses = 100,
            ClearCookiesOnReturn = true,
            ClearStorageOnReturn = true,
            LaunchOptions = new LaunchOptions
            {
                Headless = true,
                Timeout = 20000,
                Args =
                [
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-background-networking"
                ]
            }
        };

        Pool = await IntegrationPoolFactory.CreateAndStartPoolAsync(options, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Pool.DisposeAsync();
        _server?.Dispose();
        _server = null;
    }
}

