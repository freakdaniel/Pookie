using System.Net;
using System.Net.Sockets;
using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Browser;
using StbImageSharp;

namespace Pookie.App.Diagnostics;

internal static class AotBrowserCheck
{
    internal static async Task RunAsync(Window window)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
        var serving = ServeAsync(server, stop.Token);
        var profile = Directory.CreateTempSubdirectory("pookie-aot-browser-").FullName;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = EmbeddedBrowserFactory.Create(new((_, _) => true,
            (raw, _) => { if (raw == "pookie-aot-ready") ready.TrySetResult(); },
            _ => { }, () => Console.WriteLine("AOT_BROWSER_LOADED"), error => ready.TrySetException(error)));
        try
        {
            const string script = """
                (() => {
                 const notify = () => {
                  requestAnimationFrame(() => requestAnimationFrame(() => {
                    if (window.chrome?.webview) window.chrome.webview.postMessage('pookie-aot-ready');
                    else window.webkit.messageHandlers.pookie.postMessage('pookie-aot-ready');
                  }));
                 };
                 if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', notify, {once:true});
                 else notify();
                })();
                """;
            await browser.InitializeAsync(window.Handle, profile, script, isolated: true);
            browser.SetBounds(new(20, 20, 400, 240), window.DpiScale); browser.SetVisible(true);
            browser.Navigate("http://127.0.0.1:" + ((IPEndPoint)server.LocalEndpoint).Port + "/");
            await ready.Task.WaitAsync(stop.Token);
            await browser.ExecuteAsync("document.title='AOT fixture';");
            var png = await browser.CaptureAsync();
            var pixels = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
            var middle = ((pixels.Height / 2) * pixels.Width + pixels.Width / 2) * 4;
            if (pixels.Data[middle] != 11 || pixels.Data[middle + 1] != 31 || pixels.Data[middle + 2] != 61)
                throw new InvalidOperationException("Native browser did not capture its fixture pixels.");
            Console.WriteLine("AOT_BROWSER_OK: native initialization, page load, paired script callback, execution and PNG capture");
        }
        catch (Exception error) { Console.Error.WriteLine(error); throw; }
        finally
        {
            stop.Cancel(); server.Stop();
            try { await serving; }
            catch (Exception error) when (stop.IsCancellationRequested && error is OperationCanceledException or SocketException or ObjectDisposedException) { }
            await browser.ShutdownAsync();
            try { Directory.Delete(profile, true); } catch (IOException) { }
        }
    }
    private static async Task ServeAsync(TcpListener listener, CancellationToken token)
    {
        var body = "<!doctype html><html style='background:rgb(11,31,61)'><head><meta charset='utf-8'></head><body></body></html>"u8.ToArray();
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        while (!token.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(token);
            _ = RespondAsync(client, header, body, token);
        }
    }
    private static async Task RespondAsync(TcpClient client, byte[] header, byte[] body, CancellationToken token)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await using var stream = client.GetStream();
                if (await stream.ReadAsync(new byte[8192], deadline.Token) == 0) return;
                await stream.WriteAsync(header, deadline.Token); await stream.WriteAsync(body, deadline.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }
}
