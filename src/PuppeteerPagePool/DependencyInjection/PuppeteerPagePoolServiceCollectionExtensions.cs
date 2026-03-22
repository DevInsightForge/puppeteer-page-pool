using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerPagePool.Internal;

namespace PuppeteerPagePool.DependencyInjection;

public static class PuppeteerPagePoolServiceCollectionExtensions
{
    public static IServiceCollection AddPuppeteerPagePool(
        this IServiceCollection services,
        Action<PuppeteerPagePoolOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PuppeteerPagePoolOptions>()
            .Configure(configure);

        services.TryAddSingleton<IBrowserSessionFactory, PuppeteerBrowserSessionFactory>();
        services.TryAddSingleton<PagePool>(serviceProvider => new PagePool(
            serviceProvider.GetRequiredService<IOptions<PuppeteerPagePoolOptions>>(),
            serviceProvider.GetRequiredService<ILogger<PagePool>>(),
            serviceProvider.GetRequiredService<IBrowserSessionFactory>()));
        services.TryAddSingleton<IPagePool>(serviceProvider => serviceProvider.GetRequiredService<PagePool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PagePoolHostedService>());
        services.AddHealthChecks().AddCheck<PagePoolHealthCheck>("puppeteer_page_pool");

        return services;
    }
}
