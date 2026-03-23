using System.Diagnostics;
using PuppeteerSharp;

namespace PuppeteerPagePool;

/// <summary>
/// Resolves the effective Chromium launch options used by the pool.
/// </summary>
internal static class BrowserLaunchOptions
{
    private static readonly string[] DefaultArguments =
    [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--disable-extensions",
        "--no-zygote",
        "--no-first-run",
        "--disable-sync",
        "--disable-accelerated-2d-canvas",
        "--force-color-profile=srgb",
        "--renderer-process-limit=1",
        "--js-flags=--max-old-space-size=128",
        "--disk-cache-size=1",
        "--media-cache-size=1",
        "--disable-background-timer-throttling",
        "--disable-features=TranslateUI,ImprovedCookieControls,AudioServiceOutOfProcess,SitePerProcess"
    ];

    /// <summary>
    /// Builds launch options from configuration, environment, and automatic Chromium download fallback.
    /// </summary>
    internal static async ValueTask<LaunchOptions> ResolveAsync(PagePoolOptions options)
    {
        var launchOptions = Clone(options.LaunchOptions);
        launchOptions.Headless = true;
        launchOptions.HeadlessMode = HeadlessMode.True;
        launchOptions.Devtools = false;

        if (launchOptions.Args is null || launchOptions.Args.Length == 0)
        {
            launchOptions.Args = [.. DefaultArguments];
        }

        if (!string.IsNullOrWhiteSpace(launchOptions.ExecutablePath))
        {
            ValidateExecutable(launchOptions.ExecutablePath);
            return launchOptions;
        }

        var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            ValidateExecutable(executablePath);
            launchOptions.ExecutablePath = executablePath;
            return launchOptions;
        }

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Browser = SupportedBrowser.Chromium
        });

        var browser = await browserFetcher.DownloadAsync().ConfigureAwait(false);
        launchOptions.ExecutablePath = browser.GetExecutablePath();
        return launchOptions;
    }

    private static LaunchOptions Clone(LaunchOptions? source)
    {
        var clone = new LaunchOptions
        {
            Headless = true,
            HeadlessMode = HeadlessMode.True,
            Devtools = false
        };

        if (source is null)
        {
            return clone;
        }

        foreach (var property in typeof(LaunchOptions).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(clone, CloneValue(property.GetValue(source)));
        }

        foreach (var pair in source.Env)
        {
            clone.Env[pair.Key] = pair.Value;
        }

        return clone;
    }

    private static object? CloneValue(object? value)
    {
        return value switch
        {
            null => null,
            string[] items => items.ToArray(),
            Dictionary<string, object> items => new Dictionary<string, object>(items),
            _ => value
        };
    }

    private static void ValidateExecutable(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new PagePoolUnavailableException($"Browser executable path does not exist: {executablePath}");
        }

        var version = GetVersionOutput(executablePath).ToLowerInvariant();
        if (!version.Contains("chromium") && !version.Contains("chrome"))
        {
            throw new PagePoolUnavailableException(
                $"Browser executable is not a Chromium-compatible browser. Version output: '{version}'.");
        }
    }

    private static string GetVersionOutput(string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new PagePoolUnavailableException($"Could not start browser executable: {executablePath}");
            }

            if (!process.WaitForExit(5_000))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                throw new PagePoolUnavailableException($"Browser executable did not respond to --version: {executablePath}");
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();

            if (process.ExitCode != 0)
            {
                throw new PagePoolUnavailableException(
                    $"Browser executable failed validation. ExitCode={process.ExitCode}, Error='{error}'.");
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new PagePoolUnavailableException($"Browser executable produced empty version output: {executablePath}");
            }

            return output;
        }
        catch (PagePoolUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PagePoolUnavailableException($"Failed to validate browser executable: {executablePath}", exception);
        }
    }
}
