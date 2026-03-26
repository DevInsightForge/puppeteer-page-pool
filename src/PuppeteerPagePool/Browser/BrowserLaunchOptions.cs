using PuppeteerPagePool.Core;
using PuppeteerPagePool.Exceptions;

namespace PuppeteerPagePool.Browser;

/// <summary>
/// Resolves the effective Chromium launch options used by the pool.
/// </summary>
internal static class BrowserLaunchOptions
{
    private const string ForcedHeadlessArgument = "--headless=new";

    private static readonly string[] DefaultArguments =
    [
        ForcedHeadlessArgument,
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
        "--disable-features=TranslateUI,ImprovedCookieControls,AudioServiceOutOfProcess,SitePerProcess",
        "--disable-background-networking",
        "--disable-default-apps",
        "--disable-hang-monitor",
        "--disable-prompt-on-repost",
        "--disable-client-side-phishing-detection",
        "--safebrowsing-disable-auto-update",
        "--metrics-recording-only",
        "--password-store=basic",
        "--use-mock-keychain"
    ];

    /// <summary>
    /// Builds launch options from configuration, environment, and automatic Chromium download fallback.
    /// </summary>
    internal static async ValueTask<LaunchOptions> ResolveAsync(PagePoolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var launchOptions = Clone(options.LaunchOptions);
        EnforceHeadlessMode(launchOptions);

        if (TryGetValidatedExecutablePath(launchOptions.ExecutablePath, out var configuredExecutablePath))
        {
            launchOptions.ExecutablePath = configuredExecutablePath;
            return launchOptions;
        }

        if (TryGetValidatedExecutablePath(
            Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH"),
            out var environmentExecutablePath))
        {
            launchOptions.ExecutablePath = environmentExecutablePath;
            return launchOptions;
        }

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Browser = SupportedBrowser.Chromium
        });

        var installedBrowser = await browserFetcher.DownloadAsync().ConfigureAwait(false);
        launchOptions.ExecutablePath = installedBrowser.GetExecutablePath();

        ValidateExecutable(launchOptions.ExecutablePath);
        return launchOptions;
    }

    /// <summary>
    /// Creates a defensive copy of launch options so the caller's configuration is never mutated.
    /// </summary>
    private static LaunchOptions Clone(LaunchOptions? source)
    {
        var clone = new LaunchOptions();

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

            if (property.Name == nameof(LaunchOptions.Env))
            {
                continue;
            }

            var value = property.GetValue(source);
            property.SetValue(clone, CloneValue(value));
        }

        foreach (var pair in source.Env)
        {
            clone.Env[pair.Key] = pair.Value;
        }

        return clone;
    }

    /// <summary>
    /// Produces a safe clone for mutable values used by launch options.
    /// </summary>
    private static object? CloneValue(object? value)
    {
        return value switch
        {
            null => null,
            string[] items => items.ToArray(),
            Dictionary<string, object> items => new Dictionary<string, object>(items, StringComparer.Ordinal),
            _ => value
        };
    }

    /// <summary>
    /// Applies the required hardening rules so the browser can never launch in non-headless mode.
    /// </summary>
    private static void EnforceHeadlessMode(LaunchOptions launchOptions)
    {
        launchOptions.Headless = true;
        launchOptions.HeadlessMode = HeadlessMode.True;
        launchOptions.Devtools = false;
        launchOptions.Args = MergeArguments(launchOptions.Args);
    }

    /// <summary>
    /// Merges default and user-provided Chromium arguments while preventing any headless override.
    /// </summary>
    private static string[] MergeArguments(string[]? userArguments)
    {
        var merged = new List<string>(DefaultArguments.Length + (userArguments?.Length ?? 0));
        var argumentIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in DefaultArguments)
        {
            AddOrReplaceArgument(merged, argumentIndexes, argument);
        }

        if (userArguments is not null)
        {
            foreach (var argument in userArguments)
            {
                if (ShouldSkipUserArgument(argument))
                {
                    continue;
                }

                AddOrReplaceArgument(merged, argumentIndexes, argument);
            }
        }

        AddOrReplaceArgument(merged, argumentIndexes, ForcedHeadlessArgument);

        return [.. merged];
    }

    /// <summary>
    /// Determines whether a user-supplied argument must be rejected to preserve headless-only execution.
    /// </summary>
    private static bool ShouldSkipUserArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return true;
        }

        var key = GetArgumentKey(argument);

        if (!key.Equals("--headless", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Adds a Chromium argument or replaces a previous argument with the same key.
    /// </summary>
    private static void AddOrReplaceArgument(
        List<string> arguments,
        Dictionary<string, int> argumentIndexes,
        string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return;
        }

        var normalizedArgument = argument.Trim();
        var key = GetArgumentKey(normalizedArgument);

        if (argumentIndexes.TryGetValue(key, out var existingIndex))
        {
            arguments[existingIndex] = normalizedArgument;
            return;
        }

        argumentIndexes[key] = arguments.Count;
        arguments.Add(normalizedArgument);
    }

    /// <summary>
    /// Extracts the canonical argument key so options like <c>--flag=value</c> can replace prior values.
    /// </summary>
    private static string GetArgumentKey(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return argument;
        }

        var separatorIndex = argument.IndexOf('=');
        return separatorIndex < 0 ? argument : argument[..separatorIndex];
    }

    /// <summary>
    /// Validates an executable path when one is present and returns the normalized path if valid.
    /// </summary>
    private static bool TryGetValidatedExecutablePath(string? executablePath, out string validatedPath)
    {
        validatedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        ValidateExecutable(executablePath);
        validatedPath = executablePath;
        return true;
    }

    /// <summary>
    /// Validates that the configured executable exists.
    /// </summary>
    private static void ValidateExecutable(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new PagePoolUnavailableException(
                $"Browser executable path does not exist: {executablePath}",
                FailureType.ExecutableNotFound);
        }
    }
}
