using System.Net;
using System.Text;

namespace PuppeteerPagePool.IntegrationTests;

internal sealed class TestServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _worker;

    public TestServer()
    {
        var port = GetAvailablePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseAddress.ToString());
        _listener.Start();
        _worker = Task.Run(HandleRequestsAsync);
    }

    public Uri BaseAddress { get; }

    public ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _listener.Close();
        return ValueTask.CompletedTask;
    }

    private async Task HandleRequestsAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            await WriteResponseAsync(context).ConfigureAwait(false);
        }
    }

    private static async Task WriteResponseAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (path == "/reset")
        {
            context.Response.Headers.Add("Set-Cookie", "session=reset; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/");
            await WriteHtmlAsync(context.Response, "<html><body>reset</body></html>").ConfigureAwait(false);
            return;
        }

        if (path == "/state")
        {
            const string html = """
<html>
<body>
<script>
document.cookie = 'session=active; path=/';
localStorage.setItem('render-state', 'dirty');
sessionStorage.setItem('render-session', 'dirty');
</script>
state
</body>
</html>
""";
            await WriteHtmlAsync(context.Response, html).ConfigureAwait(false);
            return;
        }

        await WriteHtmlAsync(context.Response, "<html><body>ok</body></html>").ConfigureAwait(false);
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string content)
    {
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
