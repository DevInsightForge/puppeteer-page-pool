using PuppeteerSharp;
using PuppeteerPagePool.Internal;
using PuppeteerPagePool.PageModels;
using PuppeteerPagePool.Exceptions;

namespace PuppeteerPagePool.Core;

internal sealed class LeasedPage : ILeasedPage
{
    private readonly IPage _page;
    private int _active = 1;

    public LeasedPage(IPage page)
    {
        _page = page;
    }

    public bool IsClosed
    {
        get
        {
            EnsureActive();
            return _page.IsClosed;
        }
    }

    public string Url
    {
        get
        {
            EnsureActive();
            return _page.Url;
        }
    }

    public async ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return await _page.GetTitleAsync().ConfigureAwait(false);
    }

    public async ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return await _page.GetContentAsync().ConfigureAwait(false);
    }

    public async ValueTask GoToAsync(string url, PageNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.GoToAsync(url, PuppeteerOptionMapper.ToNavigationOptions(options)).ConfigureAwait(false);
    }

    public async ValueTask WaitForNavigationAsync(PageNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.WaitForNavigationAsync(PuppeteerOptionMapper.ToNavigationOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetContentAsync(string html, PageContentOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.SetContentAsync(html, PuppeteerOptionMapper.ToNavigationOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WaitForSelectorAsync(string selector, PageWaitForSelectorOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        var handle = await _page.WaitForSelectorAsync(selector, PuppeteerOptionMapper.ToWaitForSelectorOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (handle is not null)
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask WaitForFunctionAsync(string script, object?[]? arguments = null, PageWaitForFunctionOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        var handle = await _page.WaitForFunctionAsync(script, PuppeteerOptionMapper.ToWaitForFunctionOptions(options), arguments ?? []).WaitAsync(cancellationToken).ConfigureAwait(false);
        await handle.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask WaitForNetworkIdleAsync(PageWaitForNetworkIdleOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.WaitForNetworkIdleAsync(PuppeteerOptionMapper.ToWaitForNetworkIdleOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask FocusAsync(string selector, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.FocusAsync(selector).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ClickAsync(string selector, PageClickOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.ClickAsync(selector, PuppeteerOptionMapper.ToClickOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask TypeAsync(string selector, string text, PageTypeOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.TypeAsync(selector, text, PuppeteerOptionMapper.ToTypeOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask EvaluateExpressionAsync(string script, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.EvaluateExpressionAsync(script).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TResult> EvaluateExpressionAsync<TResult>(string script, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return await _page.EvaluateExpressionAsync<TResult>(script).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask EvaluateFunctionAsync(string script, object?[]? arguments = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await _page.EvaluateFunctionAsync(script, arguments ?? []).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TResult> EvaluateFunctionAsync<TResult>(string script, object?[]? arguments = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return await _page.EvaluateFunctionAsync<TResult>(script, arguments ?? []).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> GetScreenshotAsync(PageScreenshotOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return await _page.ScreenshotDataAsync(PuppeteerOptionMapper.ToScreenshotOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> GetPdfAsync(PagePdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return await _page.PdfDataAsync(PuppeteerOptionMapper.ToPdfOptions(options)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Expire()
    {
        Interlocked.Exchange(ref _active, 0);
    }

    private void EnsureActive()
    {
        if (Volatile.Read(ref _active) == 0)
        {
            throw new PageLeaseExpiredException();
        }
    }
}
