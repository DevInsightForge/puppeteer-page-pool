using PuppeteerPagePool.Abstractions;

namespace PuppeteerPagePool.Core;

/// <summary>
/// Represents a page entry in the pool with tracking information.
/// </summary>
internal sealed class PageEntry
{
    /// <summary>
    /// Gets or sets the generation of the page.
    /// </summary>
    public required int Generation { get; init; }

    /// <summary>
    /// Gets or sets the page session.
    /// </summary>
    public required IPageSession Session { get; init; }

    /// <summary>
    /// Gets or sets the number of times the page has been used.
    /// </summary>
    public int UseCount { get; set; }

    /// <summary>
    /// Gets or sets the time when the page was created.
    /// </summary>
    public DateTime CreatedTime { get; init; }

    /// <summary>
    /// Gets or sets the time when the page was last used.
    /// </summary>
    public DateTime LastUsedTime { get; set; }
}


