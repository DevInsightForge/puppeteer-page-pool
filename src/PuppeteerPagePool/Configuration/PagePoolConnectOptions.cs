namespace PuppeteerPagePool.Configuration;

/// <summary>
/// Stable connection configuration used by the pool for remote browser attachment.
/// </summary>
public sealed class PagePoolConnectOptions
{
    /// <summary>
    /// Browser WebSocket endpoint.
    /// </summary>
    public string? BrowserWebSocketEndpoint { get; set; }

    /// <summary>
    /// Browser HTTP endpoint.
    /// </summary>
    public string? BrowserUrl { get; set; }

    /// <summary>
    /// Reserved connection compatibility flag.
    /// </summary>
    public bool IgnoreHttpsErrors { get; set; }

    /// <summary>
    /// Delay added to each operation in milliseconds.
    /// </summary>
    public int SlowMoMilliseconds { get; set; }
}
