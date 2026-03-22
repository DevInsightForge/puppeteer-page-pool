using Microsoft.Extensions.DependencyInjection;
using PuppeteerPagePool.DependencyInjection;

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
            options.LaunchSettings = new PagePoolLaunchSettings
            {
                Headless = true,
                TimeoutMilliseconds = 120_000,
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

        await pool.WithPage(
            async page =>
            {
                await page.GoToAsync(new Uri(server.BaseAddress, "state").ToString());
            });

        var state = await pool.WithPage(
            async page =>
            {
                var storageState = await page.EvaluateExpressionAsync<string>("JSON.stringify({ local: localStorage.getItem('render-state'), session: sessionStorage.getItem('render-session') })");
                var cookieNames = (await page.GetCookiesAsync()).Select(cookie => cookie.Name).ToArray();
                return (storageState, cookieNames);
            });

        var snapshot = await pool.GetSnapshotAsync();

        Assert.Equal("""{"local":null,"session":null}""", state.storageState);
        Assert.DoesNotContain("session", state.cookieNames);
        Assert.True(snapshot.BrowserConnected);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(1, snapshot.AvailablePages);
    }
}
