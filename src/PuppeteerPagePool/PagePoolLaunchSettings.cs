namespace PuppeteerPagePool;

/// <summary>
/// Stable launch configuration used by the pool for local browser startup.
/// </summary>
public sealed class PagePoolLaunchSettings
{
    /// <summary>
    /// Runs browser without visible UI when <see langword="true"/>.
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Browser launch timeout in milliseconds.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 30_000;

    /// <summary>
    /// Additional process arguments passed to browser startup.
    /// </summary>
    public string[] Args { get; set; } = [];
}
