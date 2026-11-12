using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaylistMatteAsync(SoundCloudTrack track, CollectionFixtureTransport transport)
    {
        var root = (Panel)Window.Content!;
        var fixture = new PopupGlassFixture { IsHitTestVisible = false, Split = double.MaxValue,
            LeftColor = Color.FromRgb(240, 30, 30) };
        root.Insert(root.Children.ToList().IndexOf(playlistDialog), fixture.Row(0));
        Color? before = null;
        var gaps = new List<double>();
        var last = Stopwatch.GetTimestamp();
        var snapshots = playlistDialog.SnapshotAllocations;
        void OpeningFrame()
        {
            var now = Stopwatch.GetTimestamp(); gaps.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds); last = now;
            if (before == null && playlistDialog.IsOpen && playlistDialog.Running && playlistDialog.Pose > .985 && playlistDialog.HasGlassBackdrop)
                before = ReadDialogGlassPixel(playlistDialog, new(playlistDialogContent.BodyBounds.X + 128, playlistDialogContent.BodyBounds.Y + 15));
        }
        try
        {
            await WaitForLoginFrameAsync();
            transport.PickerHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            last = Stopwatch.GetTimestamp();
            Window.FrameRendered += OpeningFrame;
            var pending = OpenPlaylistPickerAsync(track);
            await WaitForLikedLayoutAsync(() => !playlistDialog.Running && !playlistDialogContent.Animating);
            Window.FrameRendered -= OpeningFrame;
            var p95 = Percentile95(gaps);
            Console.WriteLine($"PLAYLIST_OPEN_PERF frames={gaps.Count} p95={p95:F1}ms max={gaps.Max():F1}ms surfaces={playlistDialog.SnapshotAllocations - snapshots}");
            if (p95 > 33.4 || gaps.Max() > 100 || playlistDialog.SnapshotAllocations - snapshots > 1)
                throw new InvalidOperationException("Playlist opening exceeded its frame budget or resized the transition surface.");
            if (before is not { } earlier) throw new InvalidOperationException("Playlist opening had no near-complete glass frame.");
            var after = ReadDialogGlassPixel(playlistDialog, new(playlistDialogContent.BodyBounds.X + 128, playlistDialogContent.BodyBounds.Y + 15));
            var difference = Math.Max(Math.Abs(earlier.R - after.R), Math.Max(Math.Abs(earlier.G - after.G), Math.Abs(earlier.B - after.B)));
            if (difference > 3 || after.R - after.B is < 4 or > 20)
                throw new InvalidOperationException($"Playlist matte glass changed at handoff or lost its tint: before={earlier}, after={after}.");
            Console.WriteLine($"PLAYLIST_GLASS_HANDOFF max_delta={difference}");
            await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
            var body = (GlassCard)VisualTree.Find(playlistDialogContent, item => item is GlassCard and not PaletteGlassCard)!;
            var timingFailure = false;
            await MeasureResize("loading-to-list", async () => { transport.PickerHold!.TrySetResult(); await pending; });
            transport.PickerHold = null;
            foreach (var create in new[] { true, false, true, false })
                await MeasureResize(create ? "create" : "list", () => { ShowPlaylistCreation(create); return Task.CompletedTask; });
            ShowPlaylistCreation(true); await Task.Delay(60, lifetime.Token); ShowPlaylistCreation(false);
            await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing && !playlistDialogPages.Animating);
            if (playlistDialogPages.Creating || !playlistDialog.HasGlassBackdrop)
                throw new InvalidOperationException("Interrupted playlist resize lost its page or glass.");
            CaptureUiPreview("playlist-matte-glass");
            if (timingFailure) throw new InvalidOperationException("Playlist state transition exceeded its frame budget.");
            ClosePlaylistPicker();
            await WaitForLikedLayoutAsync(() => !playlistDialog.IsVisible);
            gaps.Clear(); last = Stopwatch.GetTimestamp(); snapshots = playlistDialog.SnapshotAllocations;
            Window.FrameRendered += OpeningFrame;
            await OpenPlaylistPickerAsync(track);
            await WaitForLikedLayoutAsync(() => !playlistDialog.Running && !playlistDialogContent.Animating &&
                !playlistDialogContent.Resizing && !playlistPickerLoading.Skeleton.IsVisible);
            Window.FrameRendered -= OpeningFrame;
            p95 = Percentile95(gaps);
            Console.WriteLine($"PLAYLIST_FAST_RESPONSE_PERF frames={gaps.Count} p95={p95:F1}ms max={gaps.Max():F1}ms surfaces={playlistDialog.SnapshotAllocations - snapshots}");
            if (p95 > 33.4 || gaps.Max() > 100 || playlistDialog.SnapshotAllocations - snapshots != 1)
                throw new InvalidOperationException("A playlist response during opening reallocated transition surfaces or stalled the animation.");
            Console.WriteLine("PLAYLIST_MATTE_GLASS_OK: continuous matte opening, reused GPU texture, animated loading/list/form heights and interrupted resize");

            async Task MeasureResize(string name, Func<Task> action)
            {
                var intervals = new List<double>(); var sizes = new HashSet<double>();
                var previous = Stopwatch.GetTimestamp();
                var texture = body.OpticalImage; var backdrop = playlistDialog.BackdropVersion;
                var allocations = playlistDialog.OpticalAllocations; var reflections = body.ReflectionRasterizations;
                var lostGlass = false;
                void Frame()
                {
                    var now = Stopwatch.GetTimestamp(); intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
                    sizes.Add(Math.Round(playlistDialogContent.BodyBounds.Height, 2));
                    if (!playlistDialog.HasGlassBackdrop || texture != null &&
                        (!ReferenceEquals(texture, body.OpticalImage) || !body.IsOpticsCurrent)) lostGlass = true;
                }
                Window.FrameRendered += Frame;
                try
                {
                    await action();
                    await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing && !playlistDialogPages.Animating && !playlistPickerLoading.Skeleton.IsVisible);
                    await WaitForLoginFrameAsync();
                }
                finally { Window.FrameRendered -= Frame; }
                var tail = Percentile95(intervals);
                Console.WriteLine($"PLAYLIST_RESIZE_PERF state={name} frames={intervals.Count} sizes={sizes.Count} p95={tail:F1}ms max={intervals.Max():F1}ms backdrops={playlistDialog.BackdropVersion - backdrop} allocations={playlistDialog.OpticalAllocations - allocations} reflections={body.ReflectionRasterizations - reflections}");
                if (tail > 16.7 || intervals.Max() > 75) timingFailure = true;
                if (sizes.Count < 4 || lostGlass || playlistDialog.BackdropVersion != backdrop ||
                    playlistDialog.OpticalAllocations != allocations || body.ReflectionRasterizations != reflections)
                    throw new InvalidOperationException("Playlist resize rebuilt glass or exceeded its frame budget.");
            }
        }
        finally
        {
            Window.FrameRendered -= OpeningFrame; transport.PickerHold?.TrySetResult(); transport.PickerHold = null;
            ClosePlaylistPicker(); playlistDialog.Close(true); root.Remove(fixture); fixture.Dispose();
        }
        static double Percentile95(List<double> values) => values.Order().ElementAt(Math.Min(values.Count - 1, (int)(values.Count * .95)));
    }
}
