using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Tests.Integration.Fixtures;

namespace PuppeteerPagePool.Tests.Integration;

public sealed class PagePoolStressTests(PagePoolStressFixture fixture, ITestOutputHelper output)
    : IClassFixture<PagePoolStressFixture>
{
    private readonly IPagePool _pool = fixture.Pool;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task StressTest_HighConcurrency_AllOperationsComplete()
    {
        Log("StressTest_HighConcurrency_AllOperationsComplete");
        var completedCount = 0;
        var failedCount = 0;

        var tasks = Enumerable.Range(0, 40).Select(async i =>
        {
            try
            {
                await _pool.ExecuteAsync(async (page, token) =>
                {
                    await page.SetContentAsync($"<html><body>Request {i}</body></html>");
                    await page.WaitForSelectorAsync("body");
                    await Task.Delay(10, token);
                    Interlocked.Increment(ref completedCount);
                });
            }
            catch
            {
                Interlocked.Increment(ref failedCount);
            }
        });

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(0, failedCount);
        Assert.Equal(40, completedCount);

        var snapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(4, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
    }

    [Fact]
    public async Task StressTest_RapidAcquireRelease_NoDeadlocks()
    {
        Log("StressTest_RapidAcquireRelease_NoDeadlocks");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var tasks = Enumerable.Range(0, 16).Select(async _ =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _pool.ExecuteAsync(async (page, token) =>
                    {
                        await page.SetContentAsync("<html><body>Test</body></html>");
                        await Task.Delay(8, token);
                    }, cts.Token);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    break;
                }
            }
        });

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(true);
    }

    private void Log(string message)
    {
        _output.WriteLine($"{DateTime.UtcNow:O} {message}");
    }
}

