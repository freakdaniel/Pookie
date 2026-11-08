using System.Diagnostics;
using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Project five positions along an arc with the outer covers turning inward.
    private sealed class MixCarousel : Panel, IVisualTreeHost
    {
        private const double BaseSide = 220;
        private const double PlayButtonSize = 64;
        private const double BaseHeight = 310;
        private readonly MainWindow owner;
        private readonly AnimationClock motion;
        private readonly BoxShadowRaster shadow = new(28, 5, Color.FromArgb(118, 0, 0, 0));
        private LibraryItem[] items = [];
        private Card[] cards = [];
        private Projection[] projections = [];
        private double angle, velocity, fit = 1;
        private double? target;
        private int front = -1, playingIndex = -1, hovered = -1;
        private bool canAnimate, pressed, dragging;
        private double sceneScale = 1;
        private double Side => BaseSide * sceneScale;
        private Point pressPoint, lastPointer;
        private Point? hoverPoint;
        private Vector2 parallax, parallaxTarget;
        private long lastTick, lastMove;
        private bool projectionDirty = true;
        private string? playingKey;

        private sealed record Card(LibraryItem Item, Image Artwork, OrbitCover Cover, TrackArtworkOverlay Overlay, CardSlot Slot)
        {
            internal double Emphasis;
        }
        private readonly record struct Projection(int Index, double Depth, double Shade, double Opacity, Matrix3x2 Transform) : IComparable<Projection>
        {
            public int CompareTo(Projection other) => Depth.CompareTo(other.Depth);
        }

        internal int FrontIndex => front;
        internal bool MotionActive => motion.IsRunning;
        internal string MotionState => $"hover={hovered}, point={hoverPoint}, velocity={velocity}, target={target}, angle={angle}";
        internal double FrontCoverEmphasis => front >= 0 ? cards[front].Emphasis : 0;
        internal double PlayingEmphasis => playingIndex >= 0 ? cards[playingIndex].Emphasis : 0;
        internal bool SelectedMixIsPlaying => front >= 0 && front == playingIndex;
        internal bool SelectedGlassReady => front >= 0 && cards[front].Cover.Glass.IsCurrent;
        internal double SelectedOverlayOpacity => front >= 0 ? cards[front].Overlay.Opacity : 0;
        internal Vector2 ParallaxOffset => parallax;
        internal int VisibleCoverCount
        {
            get { EnsureProjections(); return projections.Count(pose => pose.Opacity > 0); }
        }
        private double Step => Math.Tau / Math.Max(1, cards.Length);

        internal MixCarousel(MainWindow owner)
        {
            this.owner = owner;
            Height = BaseHeight;
            Focusable = true;
            Cursor = CursorType.Hand;
            ClipToBounds = true;
            motion = new AnimationClock(TimeSpan.FromSeconds(1), Easing.Linear) { RepeatCount = -1, TickCallback = _ => Tick() };
        }

        internal bool SetItems(LibraryItem[] next)
        {
            if (items.SequenceEqual(next)) return false;
            var previous = front >= 0 ? items[front].Key : null;
            foreach (var card in cards) owner.StopCardArtwork(card.Artwork);
            Clear();
            items = next;
            cards = next.Select(CreateCard).ToArray();
            projections = new Projection[cards.Length];
            angle = -Math.Max(0, Array.FindIndex(items, item => item.Key == previous)) * Step;
            velocity = 0;
            target = null;
            UpdatePlayingIndex();
            if (playingIndex >= 0) angle = -playingIndex * Step;
            front = -2;
            hovered = -1;
            UpdateOverlays(animate: false);
            UpdateFront();
            foreach (var card in cards) LoadArtwork(card);
            InvalidateMeasure();
            InvalidatePose();
            Wake();
            return true;
        }

        private Card CreateCard(LibraryItem item)
        {
            var artwork = new Image().StretchMode(Stretch.UniformToFill);
            artwork.ImageScaleQuality = ImageScaleQuality.HighQuality;
            var cover = new OrbitCover(artwork, owner.ArtworkLayer(artwork), PlayButtonSize);
            var overlay = new TrackArtworkOverlay(cover.Glass, buttonSize: PlayButtonSize, scalingGlyphs: true);
            var slot = new CardSlot(new Grid().Columns("*").Rows("*").Children(cover, overlay), shadow);
            Add(slot);
            return new(item, artwork, cover, overlay, slot);
        }

        private void LoadArtwork(Card card)
        {
            owner.SetCollectionArtwork(card.Artwork, card.Item);
            card.Cover.RefreshArtwork();
            owner.Run(async () =>
            {
                var source = await owner.GetCollectionArtworkAsync(card.Item);
                if (!owner.disposed && cards.Any(candidate => ReferenceEquals(candidate, card)))
                {
                    owner.SetCollectionArtwork(card.Artwork, card.Item, source, finished: true);
                    card.Cover.RefreshArtwork();
                }
            });
        }

        internal void PrepareGlass()
        {
            if (!canAnimate || front < 0) return;
            // Warm one cover per presented frame, starting with the selected
            // cover and its closest neighbors in either direction.
            for (var offset = 0; offset < cards.Length; offset++)
            {
                var distance = (offset + 1) / 2;
                var index = (front + (offset % 2 == 0 ? -distance : distance) + cards.Length) % cards.Length;
                var glass = cards[index].Cover.Glass;
                if (glass.HasBitmapArtwork && glass.Prepare()) break;
            }
        }

        internal void UpdatePlayback(string? key, bool playing)
        {
            var next = playing ? key : null;
            if (playingKey == next) return;
            playingKey = next;
            UpdatePlayingIndex();
            if (playingIndex >= 0) FocusIndex(playingIndex);
            UpdateOverlays();
            Wake();
        }

        internal void SetMotionActive(bool active)
        {
            if (canAnimate == active) return;
            canAnimate = active;
            if (active) Wake();
            else
            {
                motion.Stop();
                if (pressed && FindVisualRoot() is Window window && IsMouseCaptured) window.ReleaseMouseCapture();
                if (dragging) target = Math.Round(angle / Step) * Step;
                pressed = dragging = false;
                hoverPoint = null;
                parallaxTarget = Vector2.Zero;
                if (parallax != Vector2.Zero) { parallax = Vector2.Zero; InvalidatePose(); }
                UpdateHover();
                velocity = 0;
            }
        }

        private void Wake()
        {
            if (!canAnimate || cards.Length == 0 || motion.IsRunning) return;
            lastTick = Stopwatch.GetTimestamp();
            motion.Start();
        }

        private void Tick()
        {
            var now = Stopwatch.GetTimestamp();
            var dt = Math.Min(.05, Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds);
            lastTick = now;
            var oldAngle = angle;
            if (pressed && !IsMouseCaptured)
            {
                if (dragging) target = Math.Round(angle / Step) * Step;
                pressed = dragging = false;
            }

            if (!pressed && target is { } destination)
            {
                angle = Approach(angle, destination, dt);
                if (angle == destination) target = null;
            }

            if (target == null && !pressed) angle = Math.IEEERemainder(angle, Math.Tau);
            var nextParallax = new Vector2((float)Approach(parallax.X, parallaxTarget.X, dt), (float)Approach(parallax.Y, parallaxTarget.Y, dt));
            var changed = oldAngle != angle || parallax != nextParallax;
            parallax = nextParallax;
            var emphasisMoving = false;
            for (var i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                var goal = i == playingIndex ? 1d : FrontEmphasis(i);
                var next = Approach(card.Emphasis, goal, dt);
                changed |= next != card.Emphasis;
                emphasisMoving |= next != goal;
                card.Emphasis = next;
            }
            UpdateFront();
            if (changed)
            {
                InvalidatePose();
                UpdateHover();
            }
            if (target == null && !emphasisMoving && parallax == parallaxTarget) motion.Stop();
        }

        private static double Approach(double value, double goal, double dt)
        {
            var next = goal + (value - goal) * Math.Exp(-dt * 11);
            return Math.Abs(next - goal) < .002 ? goal : next;
        }

        private void InvalidatePose()
        {
            projectionDirty = true;
            InvalidateVisual();
        }

        private void UpdatePlayingIndex() =>
            playingIndex = playingKey == null ? -1 : Array.FindIndex(cards, card => card.Item.Key == playingKey);

        private void UpdateFront()
        {
            var next = cards.Length == 0 ? -1 : (int)Math.Round(-angle / Step);
            if (cards.Length > 0) next = (next % cards.Length + cards.Length) % cards.Length;
            if (front == next) return;
            front = next;
            owner.SetHomeMixCaption(front >= 0 ? items[front] : null);
        }

        private void AimParallax(Point? position)
        {
            var next = Vector2.Zero;
            if (position is { } point && Bounds.Contains(point))
            {
                var center = Bounds.Center;
                next = new Vector2((float)((point.X - center.X) / (Bounds.Width / 2)),
                    (float)((point.Y - center.Y) / (Bounds.Height / 2)));
            }
            if (next == parallaxTarget) return;
            parallaxTarget = next;
            Wake();
        }

        internal void StepBy(int offset)
        {
            if (cards.Length < 2) return;
            TurnTo(Math.Round((target ?? angle) / Step) * Step - offset * Step);
        }

        private void TurnTo(double destination)
        {
            target = destination;
            velocity = 0;
            Wake();
        }

        internal void SetViewportWidth(double width)
        {
            var scale = 1 + Math.Clamp((width - 1000) / 800, 0, 1) * .14;
            if (Math.Abs(scale - sceneScale) < .001) return;
            sceneScale = scale;
            Height = BaseHeight * scale;
        }

        protected override Size MeasureContent(Size availableSize)
        {
            foreach (var card in cards) card.Slot.Measure(new Size(Side, Side));
            return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Side, Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            fit = Math.Min(1, finalSize.Width / (4.1 * Side));
            projectionDirty = true;
            var center = Bounds.Center;
            foreach (var card in cards) card.Slot.Arrange(new Rect(center.X - Side / 2, center.Y - Side / 2, Side, Side));
            return finalSize;
        }

        private double FrontDistance(int index) =>
            Math.Abs(Math.IEEERemainder(index * Step + angle, Math.Tau)) / Step;

        // Give the centered cover the full lift, with a smaller lift for its neighbors.
        private double FrontEmphasis(int index)
        {
            var distance = FrontDistance(index);
            return Math.Max(0, 1 - distance / 1.6);
        }

        private Projection Project(int index)
        {
            var offset = Math.IEEERemainder(index * Step + angle, Math.Tau) / Step;
            var distance = Math.Abs(offset);
            var opacity = Math.Clamp((2.5 - distance) * 2, 0, 1);
            var center = Bounds.Center;
            var pivot = new Vector2((float)center.X, (float)center.Y);
            var size = fit * (1 + cards[index].Emphasis * .025) / (1 + .34 * distance + .025 * distance * distance);
            var span = Side * fit;
            var depth = 1 / (1 + .7 * distance);
            var x = span * (offset * (.92 - .13 * distance) + .057 * parallax.X * depth);
            var rise = distance * (.16 + .006 * distance) + .085 * Math.Min(1, distance * distance / 4);
            var y = Bounds.Height / 2 - span / 2 - 28 + span * (.036 * parallax.Y * depth - rise);
            var turn = (float)-Math.Clamp(offset * .078, -.17, .17);
            var transform = Matrix3x2.CreateScale((float)size, pivot) *
                Matrix3x2.CreateRotation(turn, pivot) *
                Matrix3x2.CreateTranslation((float)x, (float)y);
            return new(index, -distance, Math.Min(.20, distance * .08), opacity, transform);
        }

        private void EnsureProjections()
        {
            if (!projectionDirty) return;
            for (var i = 0; i < cards.Length; i++) projections[i] = Project(i);
            Array.Sort(projections);
            projectionDirty = false;
        }

        bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        {
            EnsureProjections();
            foreach (var pose in projections)
            {
                var slot = cards[pose.Index].Slot;
                slot.Opacity = pose.Opacity;
                slot.SetPose(pose.Transform, pose.Shade);
                if (!visitor(slot)) return false;
            }
            return true;
        }

        private sealed class CardSlot : Panel
        {
            private readonly CardInk ink;
            private Matrix3x2 transform = Matrix3x2.Identity;
            internal CardSlot(FrameworkElement cover, BoxShadowRaster shadow)
            { IsHitTestVisible = false; ink = new CardInk(cover, shadow); Add(ink); }
            internal void SetPose(Matrix3x2 next, double shade)
            {
                if (transform != next) { transform = next; InvalidateVisual(); }
                ink.SetShade(shade);
            }
            protected override Size MeasureContent(Size availableSize) { ink.Measure(availableSize); return ink.DesiredSize; }
            protected override Size ArrangeOverride(Size finalSize) { ink.Arrange(Bounds); return finalSize; }
            protected override bool TryGetChildTransform(out Matrix3x2 value) { value = transform; return !value.IsIdentity; }
        }

        private sealed class CardInk : ContentControl
        {
            private readonly BoxShadowRaster shadow;
            private double shade;
            internal CardInk(FrameworkElement cover, BoxShadowRaster shadow)
            {
                this.shadow = shadow;
                Content = cover; Padding = new Thickness(0); BorderThickness = 0; Background = Color.Transparent;
            }
            internal void SetShade(double value) { if (shade == value) return; shade = value; InvalidateVisual(); }
            protected override void OnRender(IGraphicsContext context) => shadow.Draw(context, Bounds, 12);
            protected override void OnRenderOverlay(IGraphicsContext context)
            {
                if (shade >= .01) context.FillRoundedRectangle(Bounds, 12, 12, Color.FromArgb((byte)(shade * 255), 18, 18, 20));
            }
        }

        internal int CardAt(Point point, out bool playHit)
        {
            playHit = false;
            if (!Bounds.Contains(point)) return -1;
            EnsureProjections();
            var p = new Vector2((float)point.X, (float)point.Y);
            for (var i = projections.Length - 1; i >= 0; i--)
            {
                var pose = projections[i];
                if (pose.Opacity < .15) continue;
                if (!Matrix3x2.Invert(pose.Transform, out var inverse)) continue;
                var mapped = Vector2.Transform(p, inverse);
                var frame = cards[pose.Index].Cover.Bounds;
                if (!frame.Contains(new Point(mapped.X, mapped.Y))) continue;
                var dx = mapped.X - frame.Center.X;
                var dy = mapped.Y - frame.Center.Y;
                var radius = PlayButtonSize / 2;
                playHit = pose.Index == front && dx * dx + dy * dy <= radius * radius;
                return pose.Index;
            }
            return -1;
        }

        private void FocusIndex(int index)
        {
            var heading = -index * Step;
            TurnTo(angle + Math.IEEERemainder(heading - angle, Math.Tau));
        }

        private void UpdateHover()
        {
            var next = !dragging && hoverPoint is { } point && CardAt(point, out _) == front ? front : -1;
            if (next == hovered) return;
            if (hovered >= 0) cards[hovered].Overlay.SetHovered(false);
            hovered = next;
            if (hovered >= 0) cards[hovered].Overlay.SetHovered(true);
        }

        private void UpdateOverlays(bool animate = true)
        {
            for (var i = 0; i < cards.Length; i++)
                cards[i].Overlay.SetPlaying(i == playingIndex, i == playingIndex, animate);
        }

        private void PlayMix(int index)
        {
            var item = cards[index].Item;
            FocusIndex(index);
            owner.Run(() => owner.PlayLibraryCollectionAsync(item));
        }

        private void OpenMix(int index)
        {
            var item = cards[index].Item;
            owner.Run(() => owner.OpenLibraryItemAsync(item));
        }

        protected override void OnMouseLeave()
        {
            base.OnMouseLeave();
            hoverPoint = null;
            AimParallax(null);
            UpdateHover();
        }

        private Point Pointer(MouseEventArgs e)
        {
            var local = e.GetPosition(this);
            return new Point(Bounds.X + local.X, Bounds.Y + local.Y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left || cards.Length == 0 || FindVisualRoot() is not Window window) return;
            Focus();
            window.CaptureMouse(this);
            pressed = IsMouseCaptured;
            if (pressed) target = null;
            dragging = false;
            pressPoint = lastPointer = Pointer(e);
            hoverPoint = pressPoint;
            UpdateHover();
            lastMove = Stopwatch.GetTimestamp();
            e.Handled = pressed;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var position = Pointer(e);
            hoverPoint = position;
            if (!pressed || !IsMouseCaptured || cards.Length < 2 || (!dragging && Math.Abs(position.X - pressPoint.X) < 5))
            {
                if (!pressed) AimParallax(position);
                UpdateHover();
                return;
            }
            dragging = true;
            AimParallax(null);
            UpdateHover();
            target = null;
            var now = Stopwatch.GetTimestamp();
            var delta = (position.X - lastPointer.X) / Math.Max(44, Side * fit * .83) * Step;
            velocity = Math.Clamp(delta / Math.Max(.008, Stopwatch.GetElapsedTime(lastMove, now).TotalSeconds), -7, 7);
            lastMove = now;
            lastPointer = position;
            angle += delta;
            UpdateFront();
            InvalidatePose();
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButton.Left || !pressed) return;
            pressed = false;
            if (IsMouseCaptured && FindVisualRoot() is Window window) window.ReleaseMouseCapture();
            if (dragging)
            {
                dragging = false;
                var flick = Stopwatch.GetElapsedTime(lastMove).TotalSeconds < .13 ? velocity * .18 : 0;
                TurnTo(Math.Round((angle + flick) / Step) * Step);
            }
            else
            {
                hoverPoint = Pointer(e);
                UpdateHover();
                var index = CardAt(hoverPoint.Value, out var playHit);
                if (index >= 0 && playHit) PlayMix(index);
                else if (index >= 0 && index == front) OpenMix(index);
                else if (index >= 0) FocusIndex(index);
                else TurnTo(Math.Round(angle / Step) * Step);
            }
            AimParallax(Pointer(e));
            UpdateHover();
            Wake();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Math.Abs(e.Delta.X) <= Math.Abs(e.Delta.Y) || cards.Length < 2) return;
            StepBy(Math.Sign(e.Delta.X));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.Key)
            {
                case Key.Left: StepBy(-1); break;
                case Key.Right: StepBy(1); break;
                case Key.Home: TurnTo(angle + Math.IEEERemainder(-angle, Math.Tau)); break;
                case Key.End: TurnTo(angle + Math.IEEERemainder(Step - angle, Math.Tau)); break;
                case Key.Enter when front >= 0: OpenMix(front); break;
                case Key.Space when front >= 0: PlayMix(front); break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
        {
            base.OnVisualRootChanged(oldRoot, newRoot);
            if (newRoot == null) { SetMotionActive(false); shadow.Dispose(); }
        }

        protected override void OnDispose() { motion.Stop(); shadow.Dispose(); base.OnDispose(); }
    }
}
