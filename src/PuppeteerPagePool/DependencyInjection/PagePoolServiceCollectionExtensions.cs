using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerPagePool.Internal;

namespace PuppeteerPagePool.DependencyInjection;

/// <summary>
/// Service collection extensions for registering the page pool.
/// </summary>
public static class PagePoolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the page pool, hosted lifecycle integration, and required internal services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Options configuration delegate.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddPagePool(
        this IServiceCollection services,
        Action<PagePoolOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PagePoolOptions>()
            .Configure(configure);

        services.TryAddSingleton<IBrowserSessionFactory, PuppeteerBrowserSessionFactory>();
        services.TryAddSingleton<PagePool>(serviceProvider => new PagePool(
            serviceProvider.GetRequiredService<IOptions<PagePoolOptions>>(),
            serviceProvider.GetRequiredService<ILogger<PagePool>>(),
            serviceProvider.GetRequiredService<IBrowserSessionFactory>()));
        services.TryAddSingleton<IPagePool>(serviceProvider => serviceProvider.GetRequiredService<PagePool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PagePoolHostedService>());

        return services;
    }
}

/// <summary>
/// Health checks builder extensions for the page pool package.
/// </summary>
public static class PagePoolHealthChecksBuilderExtensions
{
    private const string DefaultHealthCheckName = "puppeteer_page_pool";

    /// <summary>
    /// Adds the page pool health check to the health checks pipeline.
    /// </summary>
    /// <param name="builder">Health checks builder.</param>
    /// <param name="name">Registration name used by the health checks system.</param>
    /// <returns>The same health checks builder for chaining.</returns>
    public static IHealthChecksBuilder AddPagePoolHealthCheck(
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
