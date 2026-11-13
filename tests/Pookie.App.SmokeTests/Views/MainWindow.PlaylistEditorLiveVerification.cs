using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaylistEditorLiveAsync()
    {
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native editor pixel checks currently require X11/Linux.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var vault = SessionVault.Open(new AppDataPaths());
            var account = vault.Load() ?? throw new InvalidOperationException("No saved account for the live editor check.");
            var connected = new NativeBrowserSession(account, profilePath: Path.Combine(dataPaths.Browser, "EditorLiveProbe"));
            AttachBrowser(connected);
            me = await RestoreSoundCloudProfileAsync(connected, timeout.Token);
            api.Session = connected.Account; signedIn.Value = true;
            await ShowWorkspaceAsync();
            await NavigateHomeAsync().WaitAsync(timeout.Token);
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            var playlists = await api.GetOwnedPlaylistsAsync(me.Id, timeout.Token, includeTracks: true);
            var item = playlists.FirstOrDefault(item => item.Playlist?.ArtworkUrl != null) ?? playlists.FirstOrDefault()
                ?? throw new InvalidOperationException("No owned playlist for the live editor check.");
            await OpenLibraryItemAsync(item).WaitAsync(timeout.Token);
            OpenPlaylistEditor();
            await WaitForLikedLayoutAsync(() => !playlistEditor.Running && playlistEditArtwork.Source is ImageSource, timeoutMs: 15000);
            Window.Activate(); MoveActionPointer(new(1, 1));
            await WaitForLoginFrameAsync();
            var sample = new Point(playlistEditCover.Bounds.X + 20, playlistEditCover.Bounds.Y + 20);
            var before = ReadPlaylistEditorPixel(sample);
            MoveActionPointer(SettingsCenter(playlistEditCover));
            await WaitForLikedLayoutAsync(() => playlistEditArtworkOverlay.Opacity >= .99);
            await WaitForLoginFrameAsync();
            var after = ReadPlaylistEditorPixel(sample);
            CapturePlaylistEditorNative("live-playlist-editor-hover");
            Console.WriteLine($"LIVE_EDITOR_HOVER opacity={playlistEditArtworkOverlay.Opacity:F2} pixels_changed={before != after} dpi={Window.DpiScale}");
            MoveActionPointer(new(1, 1));
            await WaitForLikedLayoutAsync(() => playlistEditArtworkOverlay.Opacity <= .01);
            SelectPlaylistEditTab(1);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            CapturePlaylistEditorNative("live-playlist-editor-tracks");
            SelectPlaylistEditTab(0);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            // Measure presentation cadence without synchronous X11 pixel reads.
            await VerifyPlaylistEditorMotionAsync(presented: true);
            var display = OperatingSystem.IsLinux() ? XOpenDisplay(0) : 0;
            try { await VerifyPlaylistEditorMotionAsync(presented: true, nativeDisplay: display); }
            finally { if (display != 0) XCloseDisplay(display); }
            SelectPlaylistEditTab(3);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            var original = playlistEditPermalink.Text;
            playlistEditPermalink.Focus();
            await WaitForLoginFrameAsync();
            var fieldPoint = new Point(playlistEditPermalinkGlass.Bounds.Right - 24, playlistEditPermalinkGlass.Bounds.Center.Y);
            var normal = ReadPlaylistEditorPixel(fieldPoint);
            var colours = new List<nuint>();
            var colourDisplay = OperatingSystem.IsLinux() ? XOpenDisplay(0) : 0;
            void ColourFrame()
            {
                if (colourDisplay != 0) colours.Add(ReadNativeChatVideoPixel(colourDisplay, fieldPoint));
            }
            WindowPresentation.FramePresented(Window) += ColourFrame;
            playlistEditPermalink.Text = "неверный адрес";
            try { await WaitForLikedLayoutAsync(() => !playlistEditPermalinkGlass.Animating); }
            finally
            {
                WindowPresentation.FramePresented(Window) -= ColourFrame;
                if (colourDisplay != 0) XCloseDisplay(colourDisplay);
            }
            var red = ReadPlaylistEditorPixel(fieldPoint);
            var saveInk = ReadPlaylistEditorPixel(new(playlistEditSave.Bounds.X + 10, playlistEditSave.Bounds.Center.Y));
            if (((saveInk & 255) <= ((saveInk >> 16) & 255) + 20))
                throw new InvalidOperationException("The dirty draft's confirmation circle is absent from the native window.");
            var overshoot = 0;
            foreach (var colour in colours)
                for (var shift = 0; shift <= 16; shift += 8)
                {
                    var channel = (int)((colour >> shift) & 255);
                    var low = (int)Math.Min((normal >> shift) & 255, (red >> shift) & 255);
                    var high = (int)Math.Max((normal >> shift) & 255, (red >> shift) & 255);
                    overshoot = Math.Max(overshoot, Math.Max(low - channel, channel - high));
                }
            if (overshoot > 3 || playlistEditPermalink.Foreground != playlistEditPermalinkGlass.Label.Foreground ||
                playlistEditPermalink.Foreground.R <= playlistEditPermalink.Foreground.G)
                throw new InvalidOperationException($"Native address field flashed or lost red text: overshoot={overshoot}.");
            Console.WriteLine($"LIVE_EDITOR_ADDRESS frames={colours.Count} colour_overshoot={overshoot} red_label_and_input=true");
            CapturePlaylistEditorNative("live-playlist-editor-address-error");
            playlistEditPermalink.Text = original;
            await Task.Delay(250, timeout.Token);
            ClosePlaylistEditor();
            if (before == after) throw new InvalidOperationException("Native cover pixels did not darken on hover despite the active overlay.");
            Console.WriteLine("LIVE_PLAYLIST_EDITOR_OK: saved session, real playlist/artwork, native presentation timing, local draft only; no writes");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine("LIVE_PLAYLIST_EDITOR_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private nuint ReadPlaylistEditorPixel(Point point)
    {
        if (!OperatingSystem.IsLinux()) return 0;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for the live editor check.");
        try { return ReadNativeChatVideoPixel(display, point); }
        finally { XCloseDisplay(display); }
    }

    private double? ReadPlaylistEditorEdgeError(nint display)
    {
        var scale = Window.DpiScale;
        var expected = (int)Math.Round(playlistEditFrame.Bounds.Bottom * scale);
        var radius = (int)Math.Ceiling(6 * scale);
        var image = ChatXGetImage(display, Window.Handle, (int)(playlistEditFrame.Bounds.Center.X * scale),
            expected - radius, 1, (uint)(radius * 2 + 1), nuint.MaxValue, 2);
        if (image == 0) return null;
        try
        {
            var inside = ChatXGetPixel(image, 0, 0); var outside = ChatXGetPixel(image, 0, radius * 2);
            double Distance(nuint a, nuint b) => Enumerable.Range(0, 3).Sum(i =>
                Math.Pow((double)((a >> (i * 8)) & 255) - (double)((b >> (i * 8)) & 255), 2));
            if (Distance(inside, outside) < 100) return null;
            var edge = 0;
            for (var y = 0; y <= radius * 2; y++)
                if (Distance(ChatXGetPixel(image, 0, y), inside) < Distance(ChatXGetPixel(image, 0, y), outside)) edge = y;
            return Math.Abs(edge + 1 - radius);
        }
        finally { ChatXDestroyImage(image); }
    }

    private int ReadPlaylistEditorTextPeak(nint display, Rect bounds)
    {
        var scale = Window.DpiScale;
        var x = Math.Max(0, (int)(bounds.X * scale)); var y = Math.Max(0, (int)(bounds.Y * scale));
        var width = (uint)Math.Max(1, Math.Min(Window.ClientSize.Width * scale - x, bounds.Width * scale));
        var height = (uint)Math.Max(1, Math.Min(Window.ClientSize.Height * scale - y, bounds.Height * scale));
        var image = ChatXGetImage(display, Window.Handle, x, y, width, height, nuint.MaxValue, 2);
        if (image == 0) throw new InvalidOperationException("Could not sample native text during the page fade.");
        try
        {
            var peak = 0;
            for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                {
                    var pixel = ChatXGetPixel(image, column, row);
                    peak = Math.Max(peak, (int)Math.Max((pixel >> 16) & 255, Math.Max((pixel >> 8) & 255, pixel & 255)));
                }
            return peak;
        }
        finally { ChatXDestroyImage(image); }
    }

    private void CapturePlaylistEditorNative(string name)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("POOKIE_UI_PREVIEW") is not { Length: > 0 } directory) return;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for native capture.");
        var width = (uint)Math.Ceiling(Window.ClientSize.Width * Window.DpiScale);
        var height = (uint)Math.Ceiling(Window.ClientSize.Height * Window.DpiScale);
        var image = ChatXGetImage(display, Window.Handle, 0, 0, width, height, nuint.MaxValue, 2);
        try
        {
            if (image == 0) throw new InvalidOperationException("Could not capture the native editor window.");
            Directory.CreateDirectory(directory);
            using var file = File.Create(Path.Combine(directory, name + ".ppm"));
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
        }
        finally { if (image != 0) ChatXDestroyImage(image); XCloseDisplay(display); }
    }
}
