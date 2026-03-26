namespace PuppeteerPagePool.Abstractions;

/// <summary>
/// Represents a lease-scoped page surface that remains valid only during the active pool callback.
/// </summary>
public interface ILeasedPage : IAsyncDisposable
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
    /// Gets a value indicating whether the page is closed.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Gets the main frame of the page.
    /// </summary>
    IFrame MainFrame { get; }

    /// <summary>
    /// Gets all frames attached to the page.
    /// </summary>
    IFrame[] Frames { get; }

    /// <summary>
    /// Gets the browser context this page belongs to.
    /// </summary>
    IBrowserContext BrowserContext { get; }

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
    Task SetUserAgentAsync(string userAgent);

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
    /// Sets the cache disabled state.
    /// </summary>
    Task SetCacheEnabledAsync(bool enabled);

    /// <summary>
    /// Sets the bypass CSP header.
    /// </summary>
    Task SetBypassCSPAsync(bool enabled);

    /// <summary>
    /// Sets the offline mode.
    /// </summary>
    Task SetOfflineModeAsync(bool offline);

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

    /// <summary>
    /// Brings page to front (activates tab).
    /// </summary>
    Task BringToFrontAsync();

    /// <summary>
    /// Gets the cookies for the current page.
    /// </summary>
    Task<CookieParam[]> GetCookiesAsync(params string[] urls);

    /// <summary>
    /// Deletes the specified cookies.
    /// </summary>
    Task DeleteCookieAsync(params CookieParam[] cookies);

    /// <summary>
    /// Sets the specified cookies.
    /// </summary>
    Task SetCookieAsync(params CookieParam[] cookies);

    /// <summary>
    /// Adds a script to be evaluated on every page navigation.
    /// </summary>
    Task EvaluateExpressionOnNewDocumentAsync(string script);

    /// <summary>
    /// Adds a function to be evaluated on every page navigation.
    /// </summary>
    Task EvaluateFunctionOnNewDocumentAsync(string pageFunction, params object[] args);

    /// <summary>
    /// Gets the target this page is associated with.
    /// </summary>
    ITarget Target { get; }

    /// <summary>
    /// Gets the browser this page belongs to.
    /// </summary>
    IBrowser Browser { get; }

    /// <summary>
    /// Gets the underlying <see cref="IPage"/> instance. Use with caution as it bypasses lease protection.
    /// </summary>
    IPage UnderlyingPage { get; }
}

