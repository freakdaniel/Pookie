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
                var material = (MaterialTrackButton)button;
                double InnerCorner() => repost ? material.ConnectedCorners.TopLeft : material.ConnectedCorners.TopRight;
                var glyph = repost ? trackDetailRepostIcon : trackDetailHeart;
                var countLabel = repost ? trackDetailRepostCount : trackDetailLikeCount;
                var active = repost ? TrackButtons.RepostedSurface : TrackButtons.SelectedSurface;
                var foreground = repost ? TrackButtons.OnReposted : TrackButtons.OnSelected;
                void Set(bool selected)
                {
                    if (repost) TrackButtons.SetReposted(button, selected, TrackButtons.Glass);
                    else TrackButtons.SetLiked(button, selected, TrackButtons.Glass);
                    TrackButtons.SetGlyph(glyph, repost ? "repeat" : selected ? "heart-filled" : "heart",
                        selected ? foreground : TrackButtons.OnSurface);
                    TrackButtons.SetTextColor(countLabel, selected ? foreground : TrackButtons.OnSurface);
                }
                TrackButtons.SetAvailability(button, true, false); Set(false);
                await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                if (!material.IsConnected || InnerCorner() != 8 || Math.Abs(trackDetailRepost.Bounds.X - trackDetailLike.Bounds.Right - 2) > 1)
                    throw new InvalidOperationException("Track actions lost their connected geometry.");
                var colors = new List<Color>(); var iconFrames = new HashSet<object>();
                var shapeFrames = new HashSet<double>();
                var labelFrames = new HashSet<Color>();
                void Sample()
                {
                    colors.Add(button.Background);
                    shapeFrames.Add(InnerCorner());
                    if (glyph.Source != null) iconFrames.Add(glyph.Source);
                    labelFrames.Add(countLabel.Foreground);
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
                    await WaitForLikedLayoutAsync(() => button.Background != before && button.Background != active);
                    var interrupted = button.Background;
                    TrackButtons.SetAvailability(button, true, false);
                    if (button.Background != interrupted) throw new InvalidOperationException("Completing a request snapped the colour.");
                    await WaitForLikedLayoutAsync(() => button.Background == active && InnerCorner() == 22);
                    if (InnerCorner() != 22 || shapeFrames.Count(corner => corner > 8 && corner < 22) < 3)
                        throw new InvalidOperationException("Connected selection shape snapped instead of animating.");
                    CaptureUiPreview(repost ? "track-repost-selected" : "track-like-selected");
                    Validate(colors, before, active);
                    if (iconFrames.Count < 3 || labelFrames.Count < 3)
                        throw new InvalidOperationException("Icon/count colour snapped instead of interpolating.");
                    colors.Clear(); Set(false);
                    await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                    Validate(colors, active, TrackButtons.GlassSurface);
                    // Reverse an unfinished transition from its visible colour.
                    Set(true);
                    await WaitForLikedLayoutAsync(() => button.Background != TrackButtons.GlassSurface && button.Background != active);
                    var mid = button.Background; Set(false);
                    if (button.Background != mid) throw new InvalidOperationException("Retargeting restarted from a preset colour.");
                    await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                    MoveActionPointer(new Point(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2));
                    var glassHot = TrackButtons.HoverColor(TrackButtons.GlassSurface, TrackButtons.OnSurface);
                    var selectedHot = TrackButtons.HoverColor(active, foreground);
                    await WaitForLikedLayoutAsync(() => button.IsMouseOver && button.Background == glassHot);
                    colors.Clear(); TrackButtons.SetAvailability(button, false, true); Set(true);
                    await WaitForLikedLayoutAsync(() => button.Background != glassHot && button.Background != selectedHot);
                    TrackButtons.SetAvailability(button, true, false);
                    await WaitForLikedLayoutAsync(() => button.Background == selectedHot);
                    Validate(colors, glassHot, selectedHot);
                    colors.Clear(); Set(false);
                    await WaitForLikedLayoutAsync(() => button.Background == glassHot);
                    Validate(colors, selectedHot, glassHot);
                    MoveActionPointer(new Point(1, 1));
                    await WaitForLikedLayoutAsync(() => button.Background == TrackButtons.GlassSurface);
                }
                finally { Window.FrameRendered -= Sample; }
            }
            Console.WriteLine("ACTION_COLORS_OK: direct composited colour frames over dark/coloured/light artwork, hover, reverse, icon/count tint, continuous interruption and no disabled pending flash");
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
            {
                if (!Between(color.A, from.A, to.A) || !Between(color.R, from.R, to.R) ||
                    !Between(color.G, from.G, to.G) || !Between(color.B, from.B, to.B))
                    throw new InvalidOperationException("Colour passed through an unrelated intermediate state.");
                // Straight RGBA can stay between its channel endpoints while
                // producing a bright flash when composited over the artwork.
                foreach (var backdrop in new[] { Color.FromRgb(24, 24, 24), Color.FromRgb(110, 80, 140), Color.FromRgb(180, 180, 180) })
                {
                    var start = Composite(from, backdrop); var end = Composite(to, backdrop);
                    var visible = Composite(color, backdrop);
                    if (!Between(visible.R, start.R, end.R) || !Between(visible.G, start.G, end.G) || !Between(visible.B, start.B, end.B))
                        throw new InvalidOperationException("Translucent action flashed brighter than its visible colour endpoints.");
                }
            }
            static bool Between(byte value, byte a, byte b) => value >= Math.Min(a, b) - 1 && value <= Math.Max(a, b) + 1;
            static Color Composite(Color color, Color backdrop)
            {
                var alpha = color.A / 255d;
                return Color.FromRgb((byte)Math.Round(color.R * alpha + backdrop.R * (1 - alpha)),
                    (byte)Math.Round(color.G * alpha + backdrop.G * (1 - alpha)),
                    (byte)Math.Round(color.B * alpha + backdrop.B * (1 - alpha)));
            }
        }
    }
}
