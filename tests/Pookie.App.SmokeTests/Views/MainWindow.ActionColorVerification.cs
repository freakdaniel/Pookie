using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyActionColorTransitionsAsync()
    {
        timer.Stop(); MoveActionPointer(new Point(1, 1)); Window.FocusManager.ClearFocus();
        var likeEnabled = trackDetailLike.IsEnabled; var repostEnabled = trackDetailRepost.IsEnabled;
        try
        {
            foreach (var repost in new[] { false, true })
            {
                var button = repost ? trackDetailRepost : trackDetailLike;
                var glyph = repost ? trackDetailRepostIcon : trackDetailHeart;
                var active = repost ? TrackButtons.RepostedSurface : TrackButtons.SelectedSurface;
                var foreground = repost ? TrackButtons.OnReposted : TrackButtons.OnSelected;
                void Set(bool selected)
                {
                    if (repost) TrackButtons.SetReposted(button, selected, TrackButtons.Glass);
                    else TrackButtons.SetLiked(button, selected, TrackButtons.Glass);
                    TrackButtons.SetGlyph(glyph, repost ? "repeat" : selected ? "heart-filled" : "heart",
                        selected ? foreground : TrackButtons.OnSurface);
                    if (!repost) TrackButtons.SetTextColor(trackDetailLikeCount, selected ? foreground : TrackButtons.OnSurface);
                }
                TrackButtons.SetAvailability(button, true, false); Set(false);
                await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                var colors = new List<Color>(); var iconFrames = new HashSet<object>();
                var labelFrames = new HashSet<Color>();
                void Sample()
                {
                    colors.Add(button.Background);
                    if (glyph.Source != null) iconFrames.Add(glyph.Source);
                    if (!repost) labelFrames.Add(trackDetailLikeCount.Foreground);
                }
                Window.FrameRendered += Sample;
                try
                {
                    var before = button.Background;
                    TrackButtons.SetAvailability(button, false, true);
                    if (button.Opacity != 1 || button.IsEnabled || button.Background != before)
                        throw new InvalidOperationException("Pending action flashed a disabled state.");
                    Set(true);
                    if (button.Background != before) throw new InvalidOperationException("Selection colour snapped.");
                    await Task.Delay(80, lifetime.Token);
                    var interrupted = button.Background;
                    TrackButtons.SetAvailability(button, true, false);
                    if (button.Background != interrupted) throw new InvalidOperationException("Completing a request snapped the colour.");
                    await WaitForLikedLayoutAsync(() => button.Background == active);
                    Validate(colors, before, active);
                    if (iconFrames.Count < 3 || !repost && labelFrames.Count < 3)
                        throw new InvalidOperationException("Icon/count colour snapped instead of interpolating.");
                    colors.Clear(); Set(false);
                    await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                    Validate(colors, active, TrackButtons.GlassSurface);
                    // Reverse an unfinished transition from its visible colour.
                    Set(true); await Task.Delay(70, lifetime.Token);
                    var mid = button.Background; Set(false);
                    if (button.Background != mid) throw new InvalidOperationException("Retargeting restarted from a preset colour.");
                    await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                }
                finally { Window.FrameRendered -= Sample; }
            }
            Console.WriteLine("ACTION_COLORS_OK: direct forward/reverse colour frames, icon/count tint, continuous interruption and no disabled pending flash");
        }
        finally
        {
            TrackButtons.SetAvailability(trackDetailLike, likeEnabled, false);
            TrackButtons.SetAvailability(trackDetailRepost, repostEnabled, false);
            RefreshTrackDetailPlayback(); timer.Start();
        }

        static void Validate(List<Color> frames, Color from, Color to)
        {
            if (frames.Distinct().Count(color => color != from && color != to) < 3)
                throw new InvalidOperationException("Too few intermediate colour frames.");
            foreach (var color in frames)
                if (!Between(color.A, from.A, to.A) || !Between(color.R, from.R, to.R) ||
                    !Between(color.G, from.G, to.G) || !Between(color.B, from.B, to.B))
                    throw new InvalidOperationException("Colour passed through an unrelated intermediate state.");
            static bool Between(byte value, byte a, byte b) => value >= Math.Min(a, b) && value <= Math.Max(a, b);
        }
    }
}
