using System.Diagnostics;
using Aprillz.MewUI;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaylistLiveUiAsync(string url)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The live wheel probe requires Windows.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "soundcloud.com" ||
                uri.UserInfo.Length != 0 || !uri.AbsolutePath.Contains("/sets/", StringComparison.Ordinal))
                throw new ArgumentException("Expected an HTTPS SoundCloud playlist URL.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var savedVault = SessionVault.Open(new AppDataPaths());
            var account = savedVault.Load() ?? throw new InvalidOperationException("No saved account for the live playlist check.");
            AttachBrowser(new NativeBrowserSession(account, profilePath: Path.Combine(dataPaths.Browser, "PlaylistLiveProbe")));
            me = await browser!.GetMeAsync(timeout.Token);
            api.Session = browser.Account;
            signedIn.Value = true;
            await ShowWorkspaceAsync();
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);

            var resolve = Stopwatch.StartNew();
            var item = await api.ResolveLinkAsync(uri.AbsoluteUri, timeout.Token);
            if (item.Playlist == null) throw new InvalidOperationException("The URL did not resolve to a playlist.");
            Console.WriteLine($"LIVE_PLAYLIST_RESOLVE ms={resolve.Elapsed.TotalMilliseconds:F0} advertised={item.Playlist.TrackCount}");
            var opening = Stopwatch.StartNew();
            await OpenLibraryItemAsync(item).WaitAsync(timeout.Token);
            await WaitForTrackPageLiveAsync(() => shellPageScroll.IsReady && playlistCollectionVisible.Value &&
                activeCollection is { Items.Length: > 0 } && compactTrackRows.Any(row => row.CollectionRow && row.Track != null));
            var count = activeCollection!.Items.Length;
            if (count < 100 || current != null) throw new InvalidOperationException("Expected a large playlist with playback stopped.");
            Console.WriteLine($"LIVE_PLAYLIST_OPEN ms={opening.Elapsed.TotalMilliseconds:F0} tracks={count} frame={Window.LastFramePerformanceStats.FrameIndex}");

            // Send wheel input independently of frame completion. The frames sampled
            // here include the cold covers that arrive as new rows enter the viewport.
            if (Environment.GetEnvironmentVariable("POOKIE_PLAYLIST_COVER_PROFILE") == "1")
            {
                var saved = default(LikedActionPoint);
                var restore = GetLikedActionCursor(out saved);
                try
                {
                    var seen = new HashSet<ImageSource>();
                    await ProbeLivePlaylistWheelAsync(timeout.Token, "cold", seen);
                    var side = CompactArtworkSide(CompactCoverSize);
                    await Task.WhenAll(activeCollection.Items.Take(90).Select(item =>
                        CompactArtworkAsync(item.Track?.ArtworkUrl ?? item.Track?.User?.AvatarUrl, side))).WaitAsync(timeout.Token);
                    shellScroll.SetScrollOffsets(0, 0);
                    await WaitForTrackPageLiveAsync(() => shellPageScroll.IsReady && !playlistHoverSuspended);
                    await ProbeLivePlaylistWheelAsync(timeout.Token, "warm", seen);
                    await WaitForLikedLayoutAsync(() => !playlistHoverSuspended && compactTrackRows.Any(row =>
                        row.CollectionRow && row.Hovered && row.Root.FindVisualRoot() == Window &&
                        row.Trailing.Reveal > .99 && row.Overlay.Opacity > .99));
                    Console.WriteLine("LIVE_PLAYLIST_HOVER_OK: controls returned under the stationary cursor");
                }
                finally { if (restore) SetLikedActionCursor(saved.X, saved.Y); }
            }
            else await ProbeLivePlaylistWheelAsync(timeout.Token);
            await WaitForTrackPageLiveAsync(() => compactTrackRows.Any(row => row.CollectionRow && row.Track != null && row.Cover.Source is ImageSource));
            await ProbeContinuousScrollAsync($"live-playlist-{count}");
            Console.WriteLine("LIVE_PLAYLIST_UI_OK: authenticated playlist, native wheel, artwork loading and retained scrolling");
        }
        catch (Exception error)
        {
            VerificationFailure = error;
            Console.Error.WriteLine("LIVE_PLAYLIST_UI_FAILED: " + error);
        }
        finally { Window.Close(); }
    }

    private async Task ProbeLivePlaylistWheelAsync(CancellationToken token, string? phase = null, HashSet<ImageSource>? seen = null)
    {
        var localPoint = new Point(shellScroll.Bounds.Center.X, shellScroll.Bounds.Center.Y);
        if (phase != null)
        {
            MoveLikedActionCursor(localPoint);
            SendLikedActionMouse(0x200, 0, localPoint);
            await WaitForLoginFrameAsync();
        }
        var point = Window.ClientToScreen(localPoint);
        var coordinates = (nint)(((int)Math.Round(point.Y) << 16) | ((int)Math.Round(point.X) & 0xffff));
        var window = Window.Handle;
        var intervals = new List<double>();
        var coverFrames = new List<double>();
        var otherFrames = new List<double>();
        var covers = 0;
        var suppressedHover = false;
        if (seen != null)
            foreach (var row in compactTrackRows)
                if (row.CollectionRow && row.Cover.Source is ImageSource bitmap) seen.Add(bitmap);
        var lastFrame = Stopwatch.GetTimestamp();
        var start = lastFrame;
        var furthest = shellScroll.VerticalOffset;
        void Frame()
        {
            var now = Stopwatch.GetTimestamp();
            var interval = Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds;
            intervals.Add(interval);
            lastFrame = now;
            furthest = Math.Max(furthest, shellScroll.VerticalOffset);
            if (seen != null)
            {
                var arrived = 0;
                foreach (var row in compactTrackRows)
                {
                    if (!row.CollectionRow) continue;
                    if (row.Cover.Source is ImageSource bitmap && seen.Add(bitmap)) arrived++;
                    suppressedHover |= playlistHoverSuspended && row.Hovered && row.Track != null &&
                        row.Trailing.Reveal == 0 && row.Overlay.Opacity == 0;
                }
                covers += arrived;
                (arrived > 0 ? coverFrames : otherFrames).Add(interval);
            }
        }
        Console.WriteLine($"LIVE_PLAYLIST_WHEEL_START phase={phase ?? "normal"} frame={Window.LastFramePerformanceStats.FrameIndex}");
        Window.FrameRendered += Frame;
        int messages;
        try
        {
            messages = await Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(12));
                var sent = 0;
                while (await timer.WaitForNextTickAsync(token) &&
                    (phase != null ? sent < 390 : Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(6)))
                {
                    var forward = phase != null ? sent < 260 : Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(4);
                    var delta = forward ? -30 : 30;
                    if (!PostLikedActionMessage(window, 0x20a, unchecked((nuint)(uint)(delta << 16)), coordinates))
                        throw new InvalidOperationException("Could not post live playlist wheel input.");
                    sent++;
                }
                return sent;
            }, token);
        }
        finally { Window.FrameRendered -= Frame; }
        if (intervals.Count < 30 || furthest < 1000) throw new InvalidOperationException("The native playlist wheel did not scroll.");
        intervals.Sort();
        Console.WriteLine($"LIVE_PLAYLIST_WHEEL phase={phase ?? "normal"} messages={messages} frames={intervals.Count} furthest={furthest:F0} p95={intervals[(int)(intervals.Count * .95)]:F1}ms max={intervals[^1]:F1}ms over33={intervals.Count(value => value >= 33)} frame={Window.LastFramePerformanceStats.FrameIndex}");
        if (seen != null)
        {
            Console.WriteLine($"LIVE_PLAYLIST_COVERS phase={phase} new={covers} with_cover={Summary(coverFrames)} other={Summary(otherFrames)}");
            if (!suppressedHover) throw new InvalidOperationException("The hovered playlist row did not hide its controls while scrolling.");
        }

        static string Summary(List<double> frames)
        {
            if (frames.Count == 0) return "0";
            frames.Sort();
            return $"{frames.Count}/p95:{frames[(int)(frames.Count * .95)]:F1}/max:{frames[^1]:F1}";
        }
    }
}
