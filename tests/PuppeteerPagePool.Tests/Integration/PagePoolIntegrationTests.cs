using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Tests.Integration.Fixtures;
using PuppeteerPagePool.Tests.Integration.Support;
using PuppeteerSharp;

namespace PuppeteerPagePool.Tests.Integration;

public sealed class PagePoolIntegrationTests(PagePoolIntegrationFixture fixture, ITestOutputHelper output)
    : IClassFixture<PagePoolIntegrationFixture>
{
    private readonly PagePoolIntegrationFixture _fixture = fixture;
    private readonly IPagePool _pool = fixture.Pool;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task Pool_Startup_CreatesWarmupPages()
    {
        Log("Pool_Startup_CreatesWarmupPages");
        var snapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.AvailablePages);
        Assert.True(snapshot.BrowserConnected);
        Assert.True(snapshot.AcceptingLeases);
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CanAccessPage()
    {
        Log("Pool_ExecuteAsync_CanAccessPage");
        var executed = false;

        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.SetContentAsync("<html><body><h1>Test</h1></body></html>");
            await page.GetTitleAsync();
            executed = true;
        });

        Assert.True(executed);
        var snapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
    }

    [Fact]
    public async Task Pool_ExecuteAsync_ReturnsResult()
    {
        Log("Pool_ExecuteAsync_ReturnsResult");
        var result = await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.SetContentAsync("<html><body>Content</body></html>");
            return await page.GetContentAsync();
        });

        Assert.Contains("Content", result);
    }

    [Fact]
    public async Task Pool_ConcurrentLeases_AllCompleteSuccessfully()
    {
        Log("Pool_ConcurrentLeases_AllCompleteSuccessfully");
        var completedCount = 0;
        var tasks = Enumerable.Range(0, 8).Select(i =>
            _pool.ExecuteAsync(async (page, token) =>
            {
                await page.SetContentAsync($"<html><body>Page {i}</body></html>");
                await Task.Delay(25, token);
                Interlocked.Increment(ref completedCount);
            }).AsTask());

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(8, completedCount);

        var snapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
    }

    [Fact]
    public async Task Pool_ConcurrentLeases_SnapshotConsistent()
    {
        Log("Pool_ConcurrentLeases_SnapshotConsistent");
        var startSnapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, startSnapshot.AvailablePages);

        var startedLeases = 0;
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 5).Select(i => _pool.ExecuteAsync(async (page, token) =>
        {
            Interlocked.Increment(ref startedLeases);
            await releaseGate.Task.WaitAsync(token);
            await page.SetContentAsync($"<html><body>Page {i}</body></html>");
            await Task.Delay(20, token);
        }).AsTask()).ToArray();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref startedLeases) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(Volatile.Read(ref startedLeases) >= 2);

        var duringSnapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, duringSnapshot.LeasedPages);
        Assert.True(duringSnapshot.WaitingRequests >= 1);

        releaseGate.TrySetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));

        var endSnapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, endSnapshot.AvailablePages);
        Assert.Equal(0, endSnapshot.LeasedPages);
        Assert.Equal(0, endSnapshot.WaitingRequests);
    }

    [Fact]
    public async Task Pool_PageReset_ClearsLocalStorage()
    {
        Log("Pool_PageReset_ClearsLocalStorage");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            await page.EvaluateFunctionAsync("() => localStorage.setItem('testKey', 'testValue')");
            var value = await page.EvaluateFunctionAsync<string>("() => localStorage.getItem('testKey')");
            Assert.Equal("testValue", value);
        });

        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            var value = await page.EvaluateFunctionAsync<string>("() => localStorage.getItem('testKey')");
            Assert.Null(value);
        });
    }

    [Fact]
    public async Task Pool_PageReset_ClearsSessionStorage()
    {
        Log("Pool_PageReset_ClearsSessionStorage");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            await page.EvaluateFunctionAsync("() => sessionStorage.setItem('sessionKey', 'sessionValue')");
            var value = await page.EvaluateFunctionAsync<string>("() => sessionStorage.getItem('sessionKey')");
            Assert.Equal("sessionValue", value);
        });

        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            var value = await page.EvaluateFunctionAsync<string>("() => sessionStorage.getItem('sessionKey')");
            Assert.Null(value);
        });
    }

    [Fact]
    public async Task Pool_PageReset_ClearsCookies()
    {
        Log("Pool_PageReset_ClearsCookies");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            await page.EvaluateFunctionAsync("() => document.cookie = 'testCookie=testValue; path=/'");
            var cookies = await page.GetCookiesAsync(_fixture.BaseUrl);
            Assert.NotEmpty(cookies);
        });

        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            var cookies = await page.GetCookiesAsync(_fixture.BaseUrl);
            Assert.Empty(cookies);
        });
    }

    [Fact]
    public async Task Pool_ExecuteAsync_ThrowsException_Recovers()
    {
        Log("Pool_ExecuteAsync_ThrowsException_Recovers");
        var initialSnapshot = await _pool.GetSnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _pool.ExecuteAsync((_, _) => throw new InvalidOperationException("Test exception"));
        });

        var finalSnapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(initialSnapshot.AvailablePages, finalSnapshot.AvailablePages);
        Assert.Equal(0, finalSnapshot.LeasedPages);
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CancellationTokenCancelled_Throws()
    {
        Log("Pool_ExecuteAsync_CancellationTokenCancelled_Throws");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _pool.ExecuteAsync(async (_, token) => await Task.Delay(100, token), cts.Token);
        });
    }

    [Fact]
    public async Task Pool_ExecuteAsync_NullOperation_ThrowsArgumentNullException()
    {
        Log("Pool_ExecuteAsync_NullOperation_ThrowsArgumentNullException");
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _pool.ExecuteAsync(null!));
    }

    [Fact]
    public async Task Pool_GetSnapshotAsync_ReturnsValidSnapshot()
    {
        Log("Pool_GetSnapshotAsync_ReturnsValidSnapshot");
        var snapshot = await _pool.GetSnapshotAsync();
        Assert.Equal(2, snapshot.PoolSize);
        Assert.Equal(2, snapshot.AvailablePages);
        Assert.Equal(0, snapshot.LeasedPages);
        Assert.Equal(0, snapshot.WaitingRequests);
        Assert.True(snapshot.BrowserConnected);
        Assert.True(snapshot.AcceptingLeases);
        Assert.True(snapshot.Uptime > TimeSpan.Zero);
    }

    [Fact]
    public async Task Pool_MaxPageUses_PageReplaced()
    {
        Log("Pool_MaxPageUses_PageReplaced");
        var options = new PagePoolOptions
        {
            PoolSize = 1,
            MaxPageUses = 2,
            WarmupOnStartup = true,
            LaunchOptions = new LaunchOptions { Headless = true }
        };

        await using var testPool = await IntegrationPoolFactory.CreateAndStartPoolAsync(options, CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            await testPool.ExecuteAsync(async (page, _) =>
            {
                await page.SetContentAsync($"<html><body>Test {i}</body></html>");
            });
        }

        var snapshot = await testPool.GetSnapshotAsync();
        Assert.Equal(1, snapshot.AvailablePages);
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CanGeneratePdf()
    {
        Log("Pool_ExecuteAsync_CanGeneratePdf");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.SetContentAsync("<html><body><h1>PDF Test</h1></body></html>");
            var pdfBytes = await page.PdfDataAsync(new PdfOptions { Width = "200px", Height = "200px" });
            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);
        });
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CanTakeScreenshot()
    {
        Log("Pool_ExecuteAsync_CanTakeScreenshot");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.SetContentAsync("<html><body><h1>Screenshot Test</h1></body></html>");
            var screenshotBytes = await page.ScreenshotDataAsync(new ScreenshotOptions { Type = ScreenshotType.Png });
            Assert.NotNull(screenshotBytes);
            Assert.True(screenshotBytes.Length > 0);
        });
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CanEvaluateJavaScript()
    {
        Log("Pool_ExecuteAsync_CanEvaluateJavaScript");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            var result = await page.EvaluateFunctionAsync<int>("() => 2 + 2");
            Assert.Equal(4, result);
        });
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CanWaitForSelector()
    {
        Log("Pool_ExecuteAsync_CanWaitForSelector");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.SetContentAsync("<html><body><div id='test'>Content</div></body></html>");
            await page.WaitForSelectorAsync("#test");
        });
    }

    [Fact]
    public async Task Pool_ExecuteAsync_CanWaitForFunction()
    {
        Log("Pool_ExecuteAsync_CanWaitForFunction");
        await _pool.ExecuteAsync(async (page, _) =>
        {
            await page.GoToAsync(_fixture.BaseUrl);
            await page.EvaluateFunctionAsync("() => { window.__poolReady = true; }");
            await page.WaitForFunctionAsync(
                "() => window.__poolReady === true",
                new WaitForFunctionOptions { Timeout = 3000 });
        });
    }

    private void Log(string message)
    {
        _output.WriteLine($"{DateTime.UtcNow:O} {message}");
    }
}

