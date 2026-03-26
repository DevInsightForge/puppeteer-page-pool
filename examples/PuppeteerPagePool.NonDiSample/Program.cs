using PuppeteerPagePool;
using PuppeteerSharp;

await NonDiSample.RunAsync();

internal static class NonDiSample
{
    public static async Task RunAsync()
    {
        await using var pool = await PagePoolFactory.CreateAsync(options =>
        {
            options.PoolName = "non-di-sample";
            options.PoolSize = 2;
            options.WarmupOnStartup = true;
            options.AcquireTimeout = TimeSpan.FromSeconds(30);
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            options.ResetTargetUrl = "about:blank";
            options.MaxPageUses = 100;
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
    }
}
