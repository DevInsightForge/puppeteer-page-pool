using PuppeteerPagePool.Core;

namespace PuppeteerPagePool.Abstractions;

internal interface IBrowserRuntimeFactory
{
    ValueTask<IBrowserRuntime> CreateAsync(PagePoolOptions options, CancellationToken cancellationToken);
}
