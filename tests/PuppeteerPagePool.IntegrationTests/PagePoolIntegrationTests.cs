using Microsoft.Extensions.DependencyInjection;
using PuppeteerPagePool.DependencyInjection;
using PuppeteerSharp;

namespace PuppeteerPagePool.IntegrationTests;

public sealed class PagePoolIntegrationTests
{
    [Fact]
    public async Task Pool_Resets_Page_State_Between_Leases()
    {
        await using var server = new TestServer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPuppeteerPagePool(options =>
        {
            options.PoolSize = 1;
            options.AcquireTimeout = TimeSpan.FromSeconds(10);
            options.ShutdownTimeout = TimeSpan.FromSeconds(10);
            options.ResetTargetUrl = new Uri(server.BaseAddress, "reset").ToString();
            options.BrowserCachePath = Path.Combine(Path.GetTempPath(), "puppeteer-page-pool-tests", Guid.NewGuid().ToString("N"));
            options.LaunchOptions = new LaunchOptions
            {
                Headless = true,
                Timeout = 120_000,
                Args =
                [
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--no-sandbox"
                ]
            };
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var hostedService = serviceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostedService>();
        await hostedService.StartAsync(CancellationToken.None);

        var pool = serviceProvider.GetRequiredService<IPagePool>();

        await using (var firstLease = await pool.AcquireAsync())
        {
            await firstLease.Page.GoToAsync(new Uri(server.BaseAddress, "state").ToString());
        }

        await using var secondLease = await pool.AcquireAsync();
        var storageState = await secondLease.Page.EvaluateExpressionAsync<string>("JSON.stringify({ local: localStorage.getItem('render-state'), session: sessionStorage.getItem('render-session') })");
        var cookieNames = (await secondLease.Page.GetCookiesAsync()).Select(cookie => cookie.Name).ToArray();
        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal("""{"local":null,"session":null}""", storageState);
        Assert.DoesNotContain("session", cookieNames);
        Assert.True(snapshot.BrowserConnected);
        Assert.Equal(1, snapshot.LeasedPages);
        Assert.Equal(0, snapshot.AvailablePages);
    }
}
