namespace PuppeteerPagePool.Internal;

internal sealed class PageSlot
{
    public required int Generation { get; init; }

    public required IPageSession PageSession { get; init; }

    public int UseCount { get; set; }

    public int ConsecutiveLeaseFailures { get; set; }

    public PageSlotState State { get; set; }
}
