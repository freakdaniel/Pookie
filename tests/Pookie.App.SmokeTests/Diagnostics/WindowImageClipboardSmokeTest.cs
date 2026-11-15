using System.Diagnostics;
using Aprillz.MewUI;
using Pookie.App.Hosting;
using Pookie.App.Platform;

namespace Pookie.App.Diagnostics;

internal static class WindowImageClipboardSmokeTest
{
    internal static async Task RunAsync(string? external)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (external != null && Environment.GetEnvironmentVariable("POOKIE_ISOLATED_CLIPBOARD") != "1")
            throw new InvalidOperationException("The real-clipboard fixture requires an isolated test compositor.");
        Exception? failure = null;
        await AppHost.RunAsync(new() { Preview = true, RequireSignIn = false, SkipSessionRestore = true, RequireWidevineSetup = false,
            AudioEnabled = false, DiscordPresence = false, SystemMediaSession = false, IsolatedData = true }, host =>
        {
            host.Window.Loaded += async () =>
            {
                try { await VerifyAsync(host.Window, external); }
                catch (Exception error) { failure = error; }
                finally { host.Window.Close(); }
            };
        });
        if (failure != null) throw new InvalidOperationException("Window image clipboard failed.", failure);
    }

    private static async Task VerifyAsync(Window window, string? external)
    {
        var selection = external == null ? "POOKIE_WINDOW_IMAGE_" + Guid.NewGuid().ToString("N") : "CLIPBOARD";
        var directory = external ?? Directory.CreateTempSubdirectory("pookie-window-image-").FullName;
        Directory.CreateDirectory(directory);
        using var clipboard = new LinuxWindowClipboard(window, null, selection);
        using var pump = new DispatcherTimer(TimeSpan.FromMilliseconds(10));
        pump.Tick += clipboard.Pump; pump.Start();
        try
        {
            for (var phase = 0; phase < 3; phase++)
            {
                var side = phase == 0 ? 3 : phase == 1 ? 512 : 1024;
                var bytes = new byte[side * side * 4];
                new Random(17 + phase).NextBytes(bytes);
                for (var index = 3; index < bytes.Length; index += 4) bytes[index] = 255;
                var png = await Task.Run(() => ImageClipboard.EncodePng(bytes, side, side));
                var path = Path.Combine(directory, $"image-{phase}.png");
                await File.WriteAllBytesAsync(path, png);
                window.Activate();
                await Task.Delay(100);
                var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!clipboard.TrySetPng(pending.Task) || clipboard.HasText() || clipboard.TryGetText(out _))
                    throw new InvalidOperationException("Image ownership was not acquired immediately, or PNG was exposed as text.");
                if (external == null)
                {
                    using var reader = StartReader("--image-clipboard-read-fixture", selection, path);
                    await Task.Delay(200); // The consumer requests PNG while encoding is still pending.
                    pending.SetResult(png);
                    var stdout = reader.StandardOutput.ReadToEndAsync(); var stderr = reader.StandardError.ReadToEndAsync();
                    await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    if (reader.ExitCode != 0 || !(await stdout).Contains("PNG_READ_OK"))
                        throw new InvalidOperationException("External GTK image read failed: " + await stderr);
                }
                else
                {
                    // The external Qt process changes focus before encoding completes.
                    await File.WriteAllTextAsync(Path.Combine(directory, "ready"), phase.ToString());
                    await WaitForFileAsync(Path.Combine(directory, $"requesting-{phase}"));
                    await Task.Delay(200);
                    pending.SetResult(png);
                    await WaitForFileAsync(Path.Combine(directory, $"done-{phase}"));
                }
                Console.WriteLine($"WINDOW_PNG_OK phase={phase} pixels={side}x{side} bytes={png.Length}; delayed encoding and external consumer");
            }
            if (external == null)
            {
                clipboard.TrySetText(ClipboardFixture.Text);
                using var reader = StartReader("--clipboard-read-fixture", selection);
                var stdout = reader.StandardOutput.ReadToEndAsync();
                await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if (reader.ExitCode != 0 || !(await stdout).Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ClipboardFixture.Text))))
                    throw new InvalidOperationException("Text copy failed after replacing PNG.");
            }
            Console.WriteLine("WINDOW_IMAGE_CLIPBOARD_OK: immediate ownership, asynchronous PNG, large images, repeated replacement and external paste");
        }
        finally
        {
            pump.Stop();
            if (external == null) Directory.Delete(directory, recursive: true);
        }
    }

    private static Process StartReader(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(WindowImageClipboardSmokeTest).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    private static async Task WaitForFileAsync(string path)
    {
        var start = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (start.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("External consumer did not signal " + Path.GetFileName(path));
            await Task.Delay(10);
        }
    }
}
