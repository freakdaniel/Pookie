using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class TrackArtworkOverlay : ContentControl
    {
        private const double DefaultButtonSize = 46;
        private readonly Image play, pause;
        private readonly CoverGlassBackdrop glass;
        private readonly bool ownsGlass;
        private Window? prepareWindow;
        private bool hovered, active;
        internal Button? PlaybackButton { get; }
        internal bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public TrackArtworkOverlay(CoverGlassBackdrop glass, Action? onPlay = null, double buttonSize = DefaultButtonSize,
            bool scalingGlyphs = false, bool ownsGlass = false, int? glyphSize = null, Color? scrim = null)
        {
            this.glass = glass;
            this.ownsGlass = ownsGlass;
            var scale = buttonSize / DefaultButtonSize;
            Image Glyph(string name, int size) => (scalingGlyphs ? Icons.ScalingView(name, size) :
                Icons.View(name, size)).CenterHorizontal().CenterVertical();
            play = Glyph("play-solid", glyphSize ?? (int)Math.Round(23 * scale));
            pause = Glyph("pause-solid", glyphSize ?? (int)Math.Round(22 * scale)).IsVisible(false);
            Background = scrim ?? Color.Transparent;
            Padding = new Thickness(0); BorderThickness = 0; CornerRadius = 0;
            var glyphs = new Grid().Columns("*").Rows("*").Children(play, pause);
            var surface = new ExpandedGlassButton(glass).Initialize(glyphs);
            if (onPlay == null) { surface.Focusable = false; surface.IsHitTestVisible = false; }
            else
            {
                PlaybackButton = surface.IsHitTestVisible(false).OnClick(onPlay).Hint("Воспроизвести");
                surface.GotFocus += Refresh;
                surface.LostFocus += Refresh;
            }
            Content = surface.Width(buttonSize).Height(buttonSize).Center();
            glass.Changed += RefreshGlass;
            if (ownsGlass) SizeChanged += _ => QueueGlass();
            Opacity = 0;
            Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(.2, 0, 0, 1))];
        }

        public void SetHovered(bool value) { hovered = value; Refresh(); }
        public void SetPlaying(bool selected, bool playing, bool animate = true)
        {
            active = selected;
            play.IsVisible = !selected || !playing;
            pause.IsVisible = selected && playing;
            PlaybackButton?.Hint(ShowsPause ? "Приостановить" : "Воспроизвести");
            if (animate) Refresh();
            else { SetImmediately(TargetOpacity); QueueGlass(); }
        }

        public void Reset()
        {
            hovered = active = false;
            SetImmediately(0);
            play.IsVisible = true; pause.IsVisible = false;
            QueueGlass();
            if (ownsGlass) glass.Reset();
        }

        internal void ArtworkChanged()
        {
            // A row clears its sample when it changes tracks. Keep the lens
            // visible while a new image for the same track is being prepared.
            glass.RefreshArtwork(keepCurrent: glass.HasArtwork && glass.HasBlurredArtwork);
            QueueGlass();
        }

        private bool WaitingForGlass => (ownsGlass || glass.HasBitmapArtwork) && !glass.HasBlurredArtwork;
        private bool Revealed => hovered || active || PlaybackButton is { } button && KeyboardFocusFeedback.IsVisible(button);
        private double TargetOpacity => Revealed && !WaitingForGlass ? 1 : 0;
        private void Refresh() { Opacity = TargetOpacity; QueueGlass(); }

        private void RefreshGlass()
        {
            if (WaitingForGlass) SetImmediately(0);
            else Opacity = TargetOpacity;
        InvalidateVisual();
            QueueGlass();
        }

        private void QueueGlass()
        {
            if (!ownsGlass) return;
            var window = Revealed && glass is { HasArtwork: true, IsCurrent: false }
                ? FindVisualRoot() as Window : null;
            if (prepareWindow == window) return;
            StopPreparing();
            if (window == null) return;
            prepareWindow = window;
            window.FrameRendered += PrepareGlass;
            InvalidateVisual();
        }

        private void PrepareGlass()
        {
            if (prepareWindow is not { } window ||
                !TranslateRect(new Rect(Point.Zero, Bounds.Size), window).IntersectsWith(new Rect(Point.Zero, window.ClientSize))) return;
            for (Element? element = this; element is UIElement visual; element = element.Parent)
                if (!visual.IsVisible) return;
            glass.Prepare();
        }

        private void StopPreparing()
        {
            if (prepareWindow != null) prepareWindow.FrameRendered -= PrepareGlass;
            prepareWindow = null;
        }
        private void SetImmediately(double opacity)
        {
            var transitions = Transitions;
            Transitions = null;
            Opacity = opacity;
            Transitions = transitions;
        }

        protected override UIElement? OnHitTest(Point point) => Opacity > 0 ? base.OnHitTest(point) : null;

        protected override void OnMewPropertyChanged(MewProperty property)
        {
            base.OnMewPropertyChanged(property);
            if (property == OpacityProperty && PlaybackButton is { } button)
                button.IsHitTestVisible = Opacity > 0 && !WaitingForGlass;
        }

        protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
        {
            base.OnVisualRootChanged(oldRoot, newRoot);
            if (!ownsGlass) return;
            if (newRoot == null) { StopPreparing(); glass.Reset(); }
            else QueueGlass();
        }

        protected override void OnDispose()
        {
            StopPreparing();
            glass.Changed -= RefreshGlass;
            if (ownsGlass) glass.Dispose();
            base.OnDispose();
        }
    }
}
