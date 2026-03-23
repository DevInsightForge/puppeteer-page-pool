using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PuppeteerPagePool;
using PuppeteerSharp;
using PuppeteerSharp.Media;

var sample = new PdfSmokeSample();
await sample.RunAsync();

internal sealed class PdfSmokeSample
{
    private static readonly Uri[] Urls =
    [
        new("https://www.google.com"),
        new("https://www.bing.com"),
        new("https://example.com"),
        new("https://example.org"),
        new("https://httpbin.org/html")
    ];

    public async Task RunAsync()
    {
        Console.WriteLine("PuppeteerPagePool.Sample");
        Console.WriteLine($"ProcessorCount: {Environment.ProcessorCount}");
        Console.WriteLine("PoolSize: 2");
        Console.WriteLine($"Urls: {Urls.Length}");
        Console.WriteLine();

        using var host = CreateHost();
        await host.StartAsync();

        var pagePool = host.Services.GetRequiredService<IPagePool>();
        var jobs = Urls.Select((url, index) => new RenderJob(url, index + 1)).ToArray();
        var startedAt = Stopwatch.GetTimestamp();
        var results = new ConcurrentBag<JobResult>();

        await Parallel.ForEachAsync(
            jobs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 2
            },
            async (job, cancellationToken) =>
            {
                results.Add(await ExecuteJobAsync(pagePool, job, cancellationToken));
            });

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        await host.StopAsync();

        WriteReport(jobs.Length, elapsed, results.OrderBy(result => result.Sequence).ToArray());
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.AddPuppeteerPagePool(options =>
        {
            options.PoolSize = 2;
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

    private static async Task<JobResult> ExecuteJobAsync(IPagePool pagePool, RenderJob job, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var pdf = await pagePool.ExecuteAsync(
                async (page, token) =>
                {
                    await page.GoToAsync(job.Url.ToString(), new NavigationOptions
                    {
                        Timeout = 90_000,
                        WaitUntil = [WaitUntilNavigation.Networkidle0]
                    });

                    return await page.PdfDataAsync(new PdfOptions
                    {
                        Format = PaperFormat.A4,
                        PrintBackground = true
                    });
                },
                cancellationToken);

            return new JobResult(
                job.Url,
                job.Sequence,
                true,
                pdf.LongLength,
                Stopwatch.GetElapsedTime(startedAt),
                null);
        }
        catch (Exception exception)
        {
            return new JobResult(
                job.Url,
                job.Sequence,
                false,
                0,
                Stopwatch.GetElapsedTime(startedAt),
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void WriteReport(int totalJobs, TimeSpan elapsed, IReadOnlyList<JobResult> results)
    {
        var completed = results.Count(result => result.Succeeded);
        var failed = results.Count - completed;
        var totalBytes = results.Where(result => result.Succeeded).Sum(result => result.FileSizeBytes);

        Console.WriteLine("Report");
        Console.WriteLine($"Jobs: {totalJobs}");
        Console.WriteLine($"Completed: {completed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Elapsed: {elapsed}");
        Console.WriteLine($"PDF/sec: {(completed == 0 ? 0 : completed / elapsed.TotalSeconds):F2}");
        Console.WriteLine($"Avg ms/PDF: {(completed == 0 ? 0 : elapsed.TotalMilliseconds / completed):F2}");
        Console.WriteLine($"Total MB: {totalBytes / 1024d / 1024d:F2}");
        Console.WriteLine();

        foreach (var result in results)
        {
            var status = result.Succeeded ? "OK" : "FAIL";
            Console.WriteLine($"{result.Sequence}. {status} {result.Url} {result.Elapsed.TotalMilliseconds:F0} ms {result.FileSizeBytes} bytes");

            if (!result.Succeeded && result.ErrorMessage is not null)
            {
                Console.WriteLine(result.ErrorMessage);
            }
        }
    }
}

internal sealed record RenderJob(Uri Url, int Sequence);

internal sealed record JobResult(
    Uri Url,
    int Sequence,
    bool Succeeded,
    long FileSizeBytes,
    TimeSpan Elapsed,
    string? ErrorMessage);
