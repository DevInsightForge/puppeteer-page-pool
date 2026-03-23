using PuppeteerSharp;

namespace PuppeteerPagePool;

/// <summary>
/// Represents a lease-scoped page surface that remains valid only during the active pool callback.
/// </summary>
public interface ILeasedPage
{
    /// <summary>
    /// Gets or sets the default navigation timeout in milliseconds for the leased page.
    /// </summary>
    int DefaultNavigationTimeout { get; set; }

    /// <summary>
    /// Gets or sets the default timeout in milliseconds for general page operations.
    /// </summary>
    int DefaultTimeout { get; set; }

    /// <summary>
    /// Gets the current page URL.
    /// </summary>
    string Url { get; }

    /// <summary>
    /// Gets a value indicating whether JavaScript execution is enabled for the page.
    /// </summary>
    bool IsJavaScriptEnabled { get; }

    /// <summary>
    /// Gets the current page content.
    /// </summary>
    Task<string> GetContentAsync(GetContentOptions? options = null);

    /// <summary>
    /// Sets the current page content.
    /// </summary>
    Task SetContentAsync(string html, NavigationOptions? options = null);

    /// <summary>
    /// Navigates to the specified URL with explicit navigation options.
    /// </summary>
    Task<IResponse> GoToAsync(string url, NavigationOptions options);

    /// <summary>
    /// Navigates to the specified URL with optional timeout and wait conditions.
    /// </summary>
    Task<IResponse> GoToAsync(string url, int? timeout = null, WaitUntilNavigation[]? waitUntil = null);

    /// <summary>
    /// Navigates to the specified URL using a single wait condition.
    /// </summary>
    Task<IResponse> GoToAsync(string url, WaitUntilNavigation waitUntil);

    /// <summary>
    /// Reloads the current page with explicit reload options.
    /// </summary>
    Task<IResponse> ReloadAsync(ReloadOptions options);

    /// <summary>
    /// Reloads the current page with optional timeout and wait conditions.
    /// </summary>
    Task<IResponse> ReloadAsync(int? timeout = null, WaitUntilNavigation[]? waitUntil = null);

    /// <summary>
    /// Waits for navigation to complete.
    /// </summary>
    Task WaitForNavigationAsync(NavigationOptions? options = null);

    /// <summary>
    /// Waits until network activity becomes idle.
    /// </summary>
    Task WaitForNetworkIdleAsync(WaitForNetworkIdleOptions? options = null);

    /// <summary>
    /// Waits for a selector to appear or satisfy the supplied options.
    /// </summary>
    Task WaitForSelectorAsync(string selector, WaitForSelectorOptions? options = null);

    /// <summary>
    /// Waits for a JavaScript function to satisfy the supplied options.
    /// </summary>
    Task WaitForFunctionAsync(string script, WaitForFunctionOptions? options = null, params object[] args);

    /// <summary>
    /// Waits for a JavaScript function to produce a truthy result.
    /// </summary>
    Task WaitForFunctionAsync(string script, params object[] args);

    /// <summary>
    /// Evaluates a JavaScript expression without returning a value.
    /// </summary>
    Task EvaluateExpressionAsync(string script);

    /// <summary>
    /// Evaluates a JavaScript expression and returns the result.
    /// </summary>
    Task<TResult> EvaluateExpressionAsync<TResult>(string script);

    /// <summary>
    /// Evaluates a JavaScript function without returning a value.
    /// </summary>
    Task EvaluateFunctionAsync(string pageFunction, params object[] args);

    /// <summary>
    /// Evaluates a JavaScript function and returns the result.
    /// </summary>
    Task<TResult> EvaluateFunctionAsync<TResult>(string pageFunction, params object[] args);

    /// <summary>
    /// Gets the current document title.
    /// </summary>
    Task<string> GetTitleAsync();

    /// <summary>
    /// Sets the viewport used for rendering.
    /// </summary>
    Task SetViewportAsync(ViewPortOptions viewport);

    /// <summary>
    /// Sets the user agent used by the page.
    /// </summary>
    Task SetUserAgentAsync(string userAgent, UserAgentMetadata? userAgentData = null);

    /// <summary>
    /// Sets additional HTTP headers for outgoing requests.
    /// </summary>
    Task SetExtraHttpHeadersAsync(Dictionary<string, string> headers);

    /// <summary>
    /// Enables or disables JavaScript execution.
    /// </summary>
    Task SetJavaScriptEnabledAsync(bool enabled);

    /// <summary>
    /// Applies HTTP authentication credentials for subsequent requests.
    /// </summary>
    Task AuthenticateAsync(Credentials credentials);

    /// <summary>
    /// Writes a PDF file to disk.
    /// </summary>
    Task PdfAsync(string file);

    /// <summary>
    /// Writes a PDF file to disk using the supplied options.
    /// </summary>
    Task PdfAsync(string file, PdfOptions options);

    /// <summary>
    /// Generates PDF bytes using default options.
    /// </summary>
    Task<byte[]> PdfDataAsync();

    /// <summary>
    /// Generates PDF bytes using the supplied options.
    /// </summary>
    Task<byte[]> PdfDataAsync(PdfOptions options);

    /// <summary>
    /// Writes a screenshot file to disk.
    /// </summary>
    Task ScreenshotAsync(string file);

    /// <summary>
    /// Writes a screenshot file to disk using the supplied options.
    /// </summary>
    Task ScreenshotAsync(string file, ScreenshotOptions options);

    /// <summary>
    /// Generates screenshot bytes using default options.
    /// </summary>
    Task<byte[]> ScreenshotDataAsync();

    /// <summary>
    /// Generates screenshot bytes using the supplied options.
    /// </summary>
    Task<byte[]> ScreenshotDataAsync(ScreenshotOptions options);
}

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

    public Task<string> GetContentAsync(GetContentOptions? options = null) => Page.GetContentAsync(options);

    public Task SetContentAsync(string html, NavigationOptions? options = null) => Page.SetContentAsync(html, options);

    public Task<IResponse> GoToAsync(string url, NavigationOptions options) => Page.GoToAsync(url, options);

    public Task<IResponse> GoToAsync(string url, int? timeout = null, WaitUntilNavigation[]? waitUntil = null) => Page.GoToAsync(url, timeout, waitUntil);

    public Task<IResponse> GoToAsync(string url, WaitUntilNavigation waitUntil) => Page.GoToAsync(url, waitUntil);

    public Task<IResponse> ReloadAsync(ReloadOptions options) => Page.ReloadAsync(options);

    public Task<IResponse> ReloadAsync(int? timeout = null, WaitUntilNavigation[]? waitUntil = null) => Page.ReloadAsync(timeout, waitUntil);

    public async Task WaitForNavigationAsync(NavigationOptions? options = null)
    {
        await Page.WaitForNavigationAsync(options).ConfigureAwait(false);
    }

    public Task WaitForNetworkIdleAsync(WaitForNetworkIdleOptions? options = null) => Page.WaitForNetworkIdleAsync(options);

    public async Task WaitForSelectorAsync(string selector, WaitForSelectorOptions? options = null)
    {
        await Page.WaitForSelectorAsync(selector, options).ConfigureAwait(false);
    }

    public async Task WaitForFunctionAsync(string script, WaitForFunctionOptions? options = null, params object[] args)
    {
        await Page.WaitForFunctionAsync(script, options, args).ConfigureAwait(false);
    }

    public async Task WaitForFunctionAsync(string script, params object[] args)
    {
        await Page.WaitForFunctionAsync(script, args).ConfigureAwait(false);
    }

    public Task EvaluateExpressionAsync(string script) => Page.EvaluateExpressionAsync(script);

    public Task<TResult> EvaluateExpressionAsync<TResult>(string script) => Page.EvaluateExpressionAsync<TResult>(script);

    public Task EvaluateFunctionAsync(string pageFunction, params object[] args) => Page.EvaluateFunctionAsync(pageFunction, args);

    public Task<TResult> EvaluateFunctionAsync<TResult>(string pageFunction, params object[] args) => Page.EvaluateFunctionAsync<TResult>(pageFunction, args);

    public Task<string> GetTitleAsync() => Page.GetTitleAsync();

    public Task SetViewportAsync(ViewPortOptions viewport) => Page.SetViewportAsync(viewport);

    public Task SetUserAgentAsync(string userAgent, UserAgentMetadata? userAgentData = null) => Page.SetUserAgentAsync(userAgent, userAgentData);

    public Task SetExtraHttpHeadersAsync(Dictionary<string, string> headers) => Page.SetExtraHttpHeadersAsync(headers);

    public Task SetJavaScriptEnabledAsync(bool enabled) => Page.SetJavaScriptEnabledAsync(enabled);

    public Task AuthenticateAsync(Credentials credentials) => Page.AuthenticateAsync(credentials);

    public Task PdfAsync(string file) => Page.PdfAsync(file);

    public Task PdfAsync(string file, PdfOptions options) => Page.PdfAsync(file, options);

    public Task<byte[]> PdfDataAsync() => Page.PdfDataAsync();

    public Task<byte[]> PdfDataAsync(PdfOptions options) => Page.PdfDataAsync(options);

    public Task ScreenshotAsync(string file) => Page.ScreenshotAsync(file);

    public Task ScreenshotAsync(string file, ScreenshotOptions options) => Page.ScreenshotAsync(file, options);

    public Task<byte[]> ScreenshotDataAsync() => Page.ScreenshotDataAsync();

    public Task<byte[]> ScreenshotDataAsync(ScreenshotOptions options) => Page.ScreenshotDataAsync(options);

    /// <summary>
    /// Marks the lease as inactive so future access fails fast.
    /// </summary>
    internal void Invalidate()
    {
        Interlocked.Exchange(ref _isActive, 0);
    }

    private IPage Page => Volatile.Read(ref _isActive) == 1
        ? _page
        : throw new ObjectDisposedException(nameof(ILeasedPage), "The leased page is no longer active.");
}
