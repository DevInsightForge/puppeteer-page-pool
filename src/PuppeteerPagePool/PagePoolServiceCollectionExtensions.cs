using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PuppeteerPagePool;

/// <summary>
/// Registers the page pool and its supporting runtime services with dependency injection.
/// </summary>
public static class PagePoolServiceCollectionExtensions
{
    /// <summary>
    /// Adds the page pool, hosted lifecycle integration, and supporting browser runtime services.
    /// </summary>
    public static IServiceCollection AddPuppeteerPagePool(
        this IServiceCollection services,
        Action<PagePoolOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PagePoolOptions>()
            .Configure(configure);

        services.TryAddSingleton<IBrowserRuntimeFactory, BrowserRuntimeFactory>();
        services.TryAddSingleton<PagePool>(serviceProvider => new PagePool(
            serviceProvider.GetRequiredService<IOptions<PagePoolOptions>>(),
            serviceProvider.GetRequiredService<ILogger<PagePool>>(),
            serviceProvider.GetRequiredService<IBrowserRuntimeFactory>()));
        services.TryAddSingleton<IPagePool>(serviceProvider => serviceProvider.GetRequiredService<PagePool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PagePoolHostedService>());

        return services;
    }
}

/// <summary>
/// Registers health check support for the page pool package.
/// </summary>
public static class PagePoolHealthChecksBuilderExtensions
{
    private const string DefaultHealthCheckName = "puppeteer_page_pool";

    /// <summary>
    /// Adds the built-in page pool health check to the health checks pipeline.
    /// </summary>
    public static IHealthChecksBuilder AddPuppeteerPagePoolHealthCheck(
        this IHealthChecksBuilder builder,
        string name = DefaultHealthCheckName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Health check name is required.", nameof(name));
        }

        builder.AddCheck<PagePoolHealthCheck>(name);
        return builder;
    }
}

/// <summary>
/// Starts and stops the page pool together with the host lifetime.
/// </summary>
/// <remarks>
/// Initializes a hosted service wrapper around the supplied page pool.
/// </remarks>
internal sealed class PagePoolHostedService(PagePool pagePool) : IHostedService
{


    /// <summary>
    /// Starts the page pool.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return pagePool.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the page pool.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return pagePool.StopAsync(cancellationToken);
    }
}
