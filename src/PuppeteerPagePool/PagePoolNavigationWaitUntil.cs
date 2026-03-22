namespace PuppeteerPagePool;

/// <summary>
/// Navigation completion conditions for reset navigation.
/// </summary>
public enum PagePoolNavigationWaitUntil
{
    /// <summary>
    /// Wait until load event.
    /// </summary>
    Load,

    /// <summary>
    /// Wait until DOMContentLoaded event.
    /// </summary>
    DOMContentLoaded,

    /// <summary>
    /// Wait until no more than zero network connections for at least 500 ms.
    /// </summary>
    Networkidle0,

    /// <summary>
    /// Wait until no more than two network connections for at least 500 ms.
    /// </summary>
    Networkidle2
}
