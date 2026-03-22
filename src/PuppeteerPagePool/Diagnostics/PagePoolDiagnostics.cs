using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PuppeteerPagePool.Diagnostics;

internal static class PagePoolDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("PuppeteerPagePool");
    public static readonly Meter Meter = new("PuppeteerPagePool");
}
