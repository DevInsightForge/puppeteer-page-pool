using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PuppeteerPagePool.Abstractions;
using PuppeteerPagePool.Browser;
using PuppeteerPagePool.Core;
using PuppeteerPagePool.Services;

namespace PuppeteerPagePool;

public static class PagePoolFactory
{
    public static IServiceCollection AddPuppeteerPagePool(
        this IServiceCollection services,
        Action<PagePoolOptions> configure)
        => AddPuppeteerPagePoolInternal(services, configure);

    public static ValueTask<IPagePool> CreateAsync(
        Action<PagePoolOptions> configure,
        CancellationToken cancellationToken = default)
        => CreateAsyncInternal(configure, cancellationToken);

    private static IServiceCollection AddPuppeteerPagePoolInternal(
        IServiceCollection services,
        Action<PagePoolOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PagePoolOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton<IBrowserRuntimeFactory, BrowserRuntimeFactory>();
        services.TryAddSingleton<IPagePool>(serviceProvider => new PagePool(
            serviceProvider.GetRequiredService<PagePoolOptions>(),
            serviceProvider.GetRequiredService<IBrowserRuntimeFactory>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PagePoolLifecycleHostedService>());

        return services;
    }

    private static async ValueTask<IPagePool> CreateAsyncInternal(
        Action<PagePoolOptions> configure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PagePoolOptions();
        configure(options);
        options.Validate();

        var pool = new PagePool(options, new BrowserRuntimeFactory());

        try
        {
            await pool.StartAsync(cancellationToken).ConfigureAwait(false);
            return pool;
        }
        catch
        {
            await pool.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}


