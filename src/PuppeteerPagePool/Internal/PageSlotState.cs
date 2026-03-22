namespace PuppeteerPagePool.Internal;

internal enum PageSlotState
{
    Warm,
    Leased,
    Resetting,
    Unhealthy,
    Disposed
}
