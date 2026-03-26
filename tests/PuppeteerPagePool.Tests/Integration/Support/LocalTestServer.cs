using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PuppeteerPagePool.Tests.Integration.Support;

internal sealed class LocalTestServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _serveTask;

    public LocalTestServer()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _serveTask = Task.Run(() => ServeLoopAsync(_shutdown.Token));
    }

    public string BaseUrl { get; }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
        }

        try
        {
            _serveTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private async Task ServeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            try
            {
                var content = "<html><body><div id='test'>Content</div></body></html>";
                var buffer = Encoding.UTF8.GetBytes(content);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, cancellationToken);
            }
            catch
            {
            }
            finally
            {
                try
                {
                    context.Response.OutputStream.Close();
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
