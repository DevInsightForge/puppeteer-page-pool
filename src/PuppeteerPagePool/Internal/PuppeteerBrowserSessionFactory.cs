using PuppeteerSharp;
using PuppeteerSharp.BrowserData;

namespace PuppeteerPagePool.Internal;

internal sealed class PuppeteerBrowserSessionFactory : IBrowserSessionFactory
{
    public async ValueTask<IBrowserSession> CreateAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
    {
        if (options.ConnectOptions is not null)
        {
            var connectedBrowser = await Puppeteer.ConnectAsync(options.ConnectOptions).ConfigureAwait(false);
            return new PuppeteerBrowserSession(connectedBrowser);
        }

        var launchOptions = options.LaunchOptions ?? new LaunchOptions
        {
            Headless = true
        };

        if (options.EnsureBrowserDownloaded)
        {
            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Browser = options.Browser,
                Path = options.BrowserCachePath
            });

            var installedBrowser = string.IsNullOrWhiteSpace(options.BrowserBuildId)
                ? await browserFetcher.DownloadAsync().ConfigureAwait(false)
                : await browserFetcher.DownloadAsync(options.BrowserBuildId).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(launchOptions.ExecutablePath))
            {
                launchOptions.ExecutablePath = installedBrowser.GetExecutablePath();
            }
        }

        var launchedBrowser = await Puppeteer.LaunchAsync(launchOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new PuppeteerBrowserSession(launchedBrowser);
    }

    private sealed class PuppeteerBrowserSession : IBrowserSession
    {
        private readonly IBrowser _browser;

        public PuppeteerBrowserSession(IBrowser browser)
        {
            _browser = browser;
            _browser.Disconnected += OnDisconnected;
        }

        public bool IsConnected => _browser.IsConnected;

        public event EventHandler? Disconnected;

        public async ValueTask<IPageSession> CreatePageAsync(CancellationToken cancellationToken)
        {
            var page = await _browser.NewPageAsync().ConfigureAwait(false);
            return new PuppeteerPageSession(page);
        }

        public async ValueTask DisposeAsync()
        {
            _browser.Disconnected -= OnDisconnected;

            if (_browser.IsClosed)
            {
                return;
            }

            await _browser.CloseAsync().ConfigureAwait(false);
        }

        private void OnDisconnected(object? sender, EventArgs eventArgs)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class PuppeteerPageSession : IPageSession
    {
        private readonly IPage _page;

        public PuppeteerPageSession(IPage page)
        {
            _page = page;
        }

        public IPage Page => _page;

        public bool IsClosed => _page.IsClosed;

        public async ValueTask InitializeAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
        {
            if (options.ConfigurePageAsync is not null)
            {
                await options.ConfigurePageAsync(_page, cancellationToken).ConfigureAwait(false);
            }

            await ResetAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask PrepareForLeaseAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
        {
            if (options.BeforeLeaseAsync is not null)
            {
                await options.BeforeLeaseAsync(_page, cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask ResetAsync(PuppeteerPagePoolOptions options, CancellationToken cancellationToken)
        {
            await _page.GoToAsync(options.ResetTargetUrl, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle0]
            }).ConfigureAwait(false);

            if (options.ClearCookiesOnReturn)
            {
                var cookies = await _page.GetCookiesAsync().ConfigureAwait(false);
                if (cookies.Length > 0)
                {
                    await _page.DeleteCookieAsync(cookies).ConfigureAwait(false);
                }
            }

            if (options.ClearStorageOnReturn)
            {
                await _page.EvaluateExpressionAsync("(() => { try { localStorage.clear(); sessionStorage.clear(); } catch { } })()").ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_page.IsClosed)
            {
                return;
            }

            await _page.CloseAsync().ConfigureAwait(false);
        }
    }
}
