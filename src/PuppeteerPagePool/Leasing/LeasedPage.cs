using PuppeteerPagePool.Abstractions;

namespace PuppeteerPagePool.Leasing;

/// <summary>
/// Wraps a live <see cref="IPage"/> and blocks further access once the lease ends.
/// </summary>
internal sealed class LeasedPage : ILeasedPage
{
    private readonly IPage _page;
    private int _isActive = 1;

    /// <summary>
    /// Initializes a new leased page wrapper around the supplied raw page.
    /// </summary>
    internal LeasedPage(IPage page)
    {
        _page = page;
    }

    public int DefaultNavigationTimeout
    {
        get => Page.DefaultNavigationTimeout;
        set => Page.DefaultNavigationTimeout = value;
    }

    public int DefaultTimeout
    {
        get => Page.DefaultTimeout;
        set => Page.DefaultTimeout = value;
    }

    public string Url => Page.Url;

    public bool IsJavaScriptEnabled => Page.IsJavaScriptEnabled;

    public bool IsClosed => Page.IsClosed;

    public IFrame MainFrame => Page.MainFrame;

    public IFrame[] Frames => Page.Frames;

    public IBrowserContext BrowserContext => Page.BrowserContext;

#pragma warning disable CS0618 // Type or member is obsolete
    public ITarget Target => Page.Target;
#pragma warning restore CS0618 // Type or member is obsolete

    public IBrowser Browser => Page.Browser;

    public IPage UnderlyingPage => Page;

    public Task<string> GetContentAsync(GetContentOptions? options = null) =>
        Page.GetContentAsync(options);

    public Task SetContentAsync(string html, NavigationOptions? options = null) =>
        Page.SetContentAsync(html, options);

    public Task<IResponse> GoToAsync(string url, NavigationOptions options) =>
        Page.GoToAsync(url, options);

    public Task<IResponse> GoToAsync(string url, int? timeout = null, WaitUntilNavigation[]? waitUntil = null) =>
        Page.GoToAsync(url, timeout, waitUntil);

    public Task<IResponse> GoToAsync(string url, WaitUntilNavigation waitUntil) =>
        Page.GoToAsync(url, waitUntil);

    public Task<IResponse> ReloadAsync(ReloadOptions options) =>
        Page.ReloadAsync(options);

    public Task<IResponse> ReloadAsync(int? timeout = null, WaitUntilNavigation[]? waitUntil = null) =>
        Page.ReloadAsync(timeout, waitUntil);

    public Task WaitForNavigationAsync(NavigationOptions? options = null) =>
        Page.WaitForNavigationAsync(options);

    public Task WaitForNetworkIdleAsync(WaitForNetworkIdleOptions? options = null) =>
        Page.WaitForNetworkIdleAsync(options);

    public Task WaitForSelectorAsync(string selector, WaitForSelectorOptions? options = null) =>
        Page.WaitForSelectorAsync(selector, options);

    public Task WaitForFunctionAsync(string script, WaitForFunctionOptions? options = null, params object[] args) =>
        Page.WaitForFunctionAsync(script, options, args);

    public Task WaitForFunctionAsync(string script, params object[] args) =>
        Page.WaitForFunctionAsync(script, args);

    public Task EvaluateExpressionAsync(string script) =>
        Page.EvaluateExpressionAsync(script);

    public Task<TResult> EvaluateExpressionAsync<TResult>(string script) =>
        Page.EvaluateExpressionAsync<TResult>(script);

    public Task EvaluateFunctionAsync(string pageFunction, params object[] args) =>
        Page.EvaluateFunctionAsync(pageFunction, args);

    public Task<TResult> EvaluateFunctionAsync<TResult>(string pageFunction, params object[] args) =>
        Page.EvaluateFunctionAsync<TResult>(pageFunction, args);

    public Task<string> GetTitleAsync() =>
        Page.GetTitleAsync();

    public Task SetViewportAsync(ViewPortOptions viewport) =>
        Page.SetViewportAsync(viewport);

    public Task SetUserAgentAsync(string userAgent) =>
        Page.SetUserAgentAsync(userAgent);

    public Task SetExtraHttpHeadersAsync(Dictionary<string, string> headers) =>
        Page.SetExtraHttpHeadersAsync(headers);

    public Task SetJavaScriptEnabledAsync(bool enabled) =>
        Page.SetJavaScriptEnabledAsync(enabled);

    public Task AuthenticateAsync(Credentials credentials) =>
        Page.AuthenticateAsync(credentials);

    public Task SetCacheEnabledAsync(bool enabled) =>
        Page.SetCacheEnabledAsync(enabled);

    public Task SetBypassCSPAsync(bool enabled) =>
        Page.SetBypassCSPAsync(enabled);

    public Task SetOfflineModeAsync(bool offline) =>
        Page.SetOfflineModeAsync(offline);

    public Task PdfAsync(string file) =>
        Page.PdfAsync(file);

    public Task PdfAsync(string file, PdfOptions options) =>
        Page.PdfAsync(file, options);

    public Task<byte[]> PdfDataAsync() =>
        Page.PdfDataAsync();

    public Task<byte[]> PdfDataAsync(PdfOptions options) =>
        Page.PdfDataAsync(options);

    public Task ScreenshotAsync(string file) =>
        Page.ScreenshotAsync(file);

    public Task ScreenshotAsync(string file, ScreenshotOptions options) =>
        Page.ScreenshotAsync(file, options);

    public Task<byte[]> ScreenshotDataAsync() =>
        Page.ScreenshotDataAsync();

    public Task<byte[]> ScreenshotDataAsync(ScreenshotOptions options) =>
        Page.ScreenshotDataAsync(options);

    public Task BringToFrontAsync() =>
        Page.BringToFrontAsync();

    public Task<CookieParam[]> GetCookiesAsync(params string[] urls) =>
        Page.GetCookiesAsync(urls);

    public Task DeleteCookieAsync(params CookieParam[] cookies) =>
        Page.DeleteCookieAsync(cookies);

    public Task SetCookieAsync(params CookieParam[] cookies) =>
        Page.SetCookieAsync(cookies);

    public Task EvaluateExpressionOnNewDocumentAsync(string script) =>
        Page.EvaluateExpressionOnNewDocumentAsync(script);

    public Task EvaluateFunctionOnNewDocumentAsync(string pageFunction, params object[] args) =>
        Page.EvaluateFunctionOnNewDocumentAsync(pageFunction, args);

    /// <summary>
    /// Marks the lease as inactive so future access fails fast.
    /// </summary>
    internal void Invalidate()
    {
        Interlocked.Exchange(ref _isActive, 0);
    }

    /// <summary>
    /// Disposes the leased page by closing the underlying page.
    /// </summary>
    public async ValueTask DisposeAsync()
        => await DisposeAsyncInternal().ConfigureAwait(false);

    private async ValueTask DisposeAsyncInternal()
    {
        if (Page.IsClosed)
        {
            return;
        }

        await Page.CloseAsync().ConfigureAwait(false);
    }

    private IPage Page => Volatile.Read(ref _isActive) == 1
        ? _page
        : throw new ObjectDisposedException(nameof(ILeasedPage), "The leased page is no longer active. Access is only allowed during the ExecuteAsync callback.");
}

