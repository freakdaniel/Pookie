namespace Pookie.App;

internal sealed partial class MainWindow
{
    internal void StartEmbeddedLoginLiveProbe(string profile) => Run(async () =>
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var login = AuthorizeEmbeddedAsync(stop.Token, profilePath: profile);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stop.Token);
            if (embeddedLogin != null) await embeddedLogin.SavePreparationProbeAsync("/tmp/pookie-embedded-live-page.png", stop.Token);
            if (embeddedLoginSurface is not { Loading: false, NativeVisible: true })
                throw new InvalidOperationException("The real sign-in page stayed behind the loading surface.");
            VerifyEmbeddedNativeGeometry();
            CaptureEmbeddedNativeWindow("/tmp/pookie-embedded-live-window.ppm");
            Console.WriteLine($"EMBEDDED_LIVE_UI loading={embeddedLoginSurface?.Loading} visible={embeddedLoginSurface?.NativeVisible}; isolated signed-out profile; no credentials entered");
        }
        catch (Exception error) { VerificationFailure = error; }
        finally
        {
            stop.Cancel();
            try { await login; }
            catch (OperationCanceledException) { }
            catch (Exception error) { VerificationFailure ??= error; }
            finally { Window.Close(); }
        }
    });

    private void CaptureEmbeddedNativeWindow(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for native login capture.");
        nint image = 0;
        try
        {
            var scale = Window.DpiScale;
            var width = (int)Math.Round(Window.ClientSize.Width * scale);
            var height = (int)Math.Round(Window.ClientSize.Height * scale);
            image = ChatXGetImage(display, Window.Handle, 0, 0, (uint)width, (uint)height, nuint.MaxValue, 2);
            if (image == 0) throw new InvalidOperationException("Could not capture the actual login window.");
            var bounds = embeddedLogin!.TranslateRect(new(0, 0, embeddedLogin.ActualWidth, embeddedLogin.ActualHeight), Window);
            var colors = new HashSet<nuint>();
            for (var y = 1; y < 24; y++)
            for (var x = 1; x < 24; x++)
                colors.Add(ChatXGetPixel(image, (int)((bounds.X + bounds.Width * x / 24) * scale),
                    (int)((bounds.Y + bounds.Height * y / 24) * scale)) & 0xffffff);
            if (colors.Count < 8) throw new InvalidOperationException($"The displayed login viewport is blank: only {colors.Count} sampled colors.");
            using var file = File.Create(path);
            file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
            var row = new byte[width * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = ChatXGetPixel(image, x, y);
                    row[x * 3] = (byte)(pixel >> 16); row[x * 3 + 1] = (byte)(pixel >> 8); row[x * 3 + 2] = (byte)pixel;
                }
                file.Write(row);
            }
            Console.WriteLine($"EMBEDDED_LIVE_NATIVE_WINDOW_OK sampled_colors={colors.Count}; actual X11 window captured before closing");
        }
        finally { if (image != 0) ChatXDestroyImage(image); XCloseDisplay(display); }
    }
}
