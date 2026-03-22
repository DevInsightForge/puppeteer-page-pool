using PuppeteerPagePool.PageModels;

namespace PuppeteerPagePool;

public interface ILeasedPage
{
    bool IsClosed { get; }

    string Url { get; }

    ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default);

    ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default);

    ValueTask GoToAsync(string url, PageNavigationOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask WaitForNavigationAsync(PageNavigationOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask SetContentAsync(string html, PageContentOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask WaitForSelectorAsync(string selector, PageWaitForSelectorOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask WaitForFunctionAsync(string script, object?[]? arguments = null, PageWaitForFunctionOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask WaitForNetworkIdleAsync(PageWaitForNetworkIdleOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask FocusAsync(string selector, CancellationToken cancellationToken = default);

    ValueTask ClickAsync(string selector, PageClickOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask TypeAsync(string selector, string text, PageTypeOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask EvaluateExpressionAsync(string script, CancellationToken cancellationToken = default);

    ValueTask<TResult> EvaluateExpressionAsync<TResult>(string script, CancellationToken cancellationToken = default);

    ValueTask EvaluateFunctionAsync(string script, object?[]? arguments = null, CancellationToken cancellationToken = default);

    ValueTask<TResult> EvaluateFunctionAsync<TResult>(string script, object?[]? arguments = null, CancellationToken cancellationToken = default);

    ValueTask<byte[]> GetScreenshotAsync(PageScreenshotOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask<byte[]> GetPdfAsync(PagePdfOptions? options = null, CancellationToken cancellationToken = default);
}
