using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PuppeteerPagePool;
using PuppeteerPagePool.Abstractions;
using PuppeteerSharp;

await DiSample.RunAsync();

internal static class DiSample
{
    public static async Task RunAsync()
    {
        using var host = CreateHost();
        await host.StartAsync();

        var pool = host.Services.GetRequiredService<IPagePool>();
        var title = await pool.ExecuteAsync(async (page, token) =>
        {
            await page.GoToAsync("https://example.com", new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Load],
                Timeout = 60_000
            });
            return await page.GetTitleAsync();
        });

        var snapshot = await pool.GetSnapshotAsync();
        Console.WriteLine($"Title: {title}");
        Console.WriteLine($"Available: {snapshot.AvailablePages}, Leased: {snapshot.LeasedPages}");

        await host.StopAsync();
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddPuppeteerPagePool(options =>
        {
            options.PoolName = "sample";
            options.PoolSize = 2;
            options.WarmupOnStartup = true;
            options.AcquireTimeout = TimeSpan.FromSeconds(30);
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            options.ResetTargetUrl = "about:blank";
            options.LaunchOptions = new LaunchOptions
            {
                Headless = true,
                Timeout = 120_000,
                Args =
                [
                    "--no-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu"
                ]
            };
        });

        return builder.Build();
    }
}

