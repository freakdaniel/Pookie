using System.Diagnostics;
using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Orbit projects a tilted ring while the covers stay facing the viewer.
    private sealed class MixCarousel : Panel
    {
        private const double BaseSide = 200;
        private const double PlayButtonSize = 64;
        private const double BaseHeight = 258;
        private const double Drift = .244; // 14 degrees per second
        private const double Perspective = 1500;
        private readonly MainWindow owner;
        private readonly DispatcherTimer timer = new(TimeSpan.FromMilliseconds(16));
        private readonly DispatcherTimer returnTimer = new(TimeSpan.FromSeconds(3));
        private LibraryItem[] items = [];
        private Card[] cards = [];
        private Projection[] projections = [];
        private double angle, velocity, intro = 1, fit = 1, radius, pointerLean, pointerAim;
        private double? target;
        private int front = -1, direction = -1, playingIndex = -1, hovered = -1;
        private bool canAnimate, pressed, dragging;
        private double sceneScale = 1;
        private double Side => BaseSide * sceneScale;
        private Point pressPoint, lastPointer;
        private Point? hoverPoint;
        private long lastTick, lastMove, holdUntil, returnAt;
        private bool returnAfterTurn, projectionDirty = true;
        private string? playingKey;

        private sealed record Card(LibraryItem Item, Image Artwork, Border Cover, TrackArtworkOverlay Overlay)
        {
            internal double Emphasis;
        }
        private readonly record struct Projection(int Index, double Depth, double Shade, Matrix3x2 Transform) : IComparable<Projection>
        {
            public int CompareTo(Projection other) => Depth.CompareTo(other.Depth);
        }

        internal int FrontIndex => front;
        internal bool MotionActive => timer.IsEnabled;
        internal double FrontCoverEmphasis => front >= 0 ? cards[front].Emphasis : 0;
        internal double PlayingEmphasis => playingIndex >= 0 ? cards[playingIndex].Emphasis : 0;
        private double Step => Math.Tau / Math.Max(1, cards.Length);

        internal MixCarousel(MainWindow owner)
        {
            this.owner = owner;
            Height = BaseHeight;
            Focusable = true;
            Cursor = CursorType.Hand;
            ClipToBounds = true;
            timer.Tick += Tick;
            returnTimer.Tick += ReturnToPlaying;
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
            CancelReturn();
            holdUntil = 0;
            UpdatePlayingIndex();
            if (playingIndex >= 0) angle = -playingIndex * Step;
            if (front < 0 && cards.Length > 0) intro = 0;
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
            var overlay = new TrackArtworkOverlay(buttonSize: PlayButtonSize);
            var cover = new Border().CornerRadius(12).ClipToBounds().BorderThickness(0).Background(TrackButtons.TonalSurface)
                .Child(new Grid().Columns("*").Rows("*").Children(owner.ArtworkLayer(artwork), overlay)).IsHitTestVisible(false);
            Add(cover);
            return new(item, artwork, cover, overlay);
        }

        private void LoadArtwork(Card card)
        {
            owner.SetCollectionArtwork(card.Artwork, card.Item);
            owner.Run(async () =>
            {
                var source = await owner.GetCollectionArtworkAsync(card.Item);
                if (!owner.disposed && cards.Any(candidate => ReferenceEquals(candidate, card)))
                    owner.SetCollectionArtwork(card.Artwork, card.Item, source, finished: true);
            });
        }

        internal void UpdatePlayback(string? key, bool playing)
        {
            var next = playing ? key : null;
            if (playingKey == next) return;
            playingKey = next;
            CancelReturn();
            UpdatePlayingIndex();
            if (playingIndex >= 0) FocusIndex(playingIndex);
            UpdateOverlays();
            Wake();
        }

        internal void SetMotionActive(bool active)
        {
            if (canAnimate == active) return;
            canAnimate = active;
            if (active)
            {
                if (returnAt != 0) ScheduleReturn();
                Wake();
            }
            else
            {
                timer.Stop();
                returnTimer.Stop();
                if (pressed && FindVisualRoot() is Window window && IsMouseCaptured) window.ReleaseMouseCapture();
                pressed = dragging = false;
                pointerAim = pointerLean = 0;
                hoverPoint = null;
                UpdateHover();
                velocity = 0;
            }
        }

        private void Wake()
        {
            if (!canAnimate || cards.Length == 0 || timer.IsEnabled) return;
            lastTick = Stopwatch.GetTimestamp();
            timer.Start();
        }

        private void Tick()
        {
            var now = Stopwatch.GetTimestamp();
            var dt = Math.Min(.05, Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds);
            lastTick = now;
            var oldAngle = angle;
            var oldIntro = intro;
            var oldLean = pointerLean;
            intro = Math.Min(1, intro + dt / .68);
            if (pressed && !IsMouseCaptured)
            {
                pressed = dragging = false;
                FinishTurn();
            }
            pointerLean = Approach(pointerLean, pointerAim, dt);
            var paused = hovered >= 0 || playingIndex >= 0;

            if (!pressed && target is { } destination)
            {
                angle = Approach(angle, destination, dt);
                if (angle == destination) { target = null; FinishTurn(); }
            }
            else if (!pressed && cards.Length > 1 && intro >= 1)
            {
                var desired = paused || now < holdUntil ? 0 : direction * Drift;
                velocity += (desired - velocity) * (1 - Math.Exp(-dt * 7));
                if (desired == 0 && Math.Abs(velocity) < .04)
                {
                    velocity = 0;
                    if (!paused || playingIndex < 0)
                    {
                        var nearest = Math.Round(angle / Step) * Step;
                        if (Math.Abs(nearest - angle) > .002) target = nearest;
                    }
                }
                else angle += velocity * dt;
            }

            if (target == null && !pressed) angle = Math.IEEERemainder(angle, Math.Tau);
            var changed = oldAngle != angle || oldIntro != intro || oldLean != pointerLean;
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
            if (intro >= 1 && target == null && pointerLean == pointerAim && !emphasisMoving &&
                (pressed || cards.Length == 1 || (paused && velocity == 0)))
                timer.Stop();
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

        private void CancelReturn()
        {
            returnTimer.Stop();
            returnAt = 0;
            returnAfterTurn = false;
        }

        private void FinishTurn()
        {
            if (!returnAfterTurn) return;
            returnAfterTurn = false;
            if (playingIndex < 0) return;
            returnAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
            ScheduleReturn();
        }

        private void ScheduleReturn()
        {
            if (!canAnimate) return;
            returnTimer.Stop();
            returnTimer.Interval = TimeSpan.FromSeconds(Math.Max(.001,
                (returnAt - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency));
            returnTimer.Start();
        }

        private void ReturnToPlaying()
        {
            CancelReturn();
            if (playingIndex >= 0 && !pressed) FocusIndex(playingIndex);
        }

        private void UpdateFront()
        {
            var next = cards.Length == 0 ? -1 : (int)Math.Round(-angle / Step);
            if (cards.Length > 0) next = (next % cards.Length + cards.Length) % cards.Length;
            if (front == next) return;
            front = next;
            owner.SetHomeMixCaption(front >= 0 ? items[front] : null);
        }

        internal void StepBy(int offset)
        {
            if (cards.Length < 2) return;
            TurnTo(Math.Round((target ?? angle) / Step) * Step - offset * Step);
        }

        private void TurnTo(double destination, bool manual = true)
        {
            CancelReturn();
            returnAfterTurn = manual && playingIndex >= 0;
            target = destination;
            velocity = 0;
            holdUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            Wake();
        }

        internal void SetViewportWidth(double width)
        {
            var scale = 1 + Math.Clamp((width - 1000) / 800, 0, 1) * .38;
            if (Math.Abs(scale - sceneScale) < .001) return;
            sceneScale = scale;
            Height = BaseHeight * scale;
        }

        protected override Size MeasureContent(Size availableSize)
        {
            foreach (var card in cards) card.Cover.Measure(new Size(Side, Side));
            return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Side, Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            radius = (BaseSide + 25) * sceneScale * 1.45 / (2 * Math.Sin(Math.PI / Math.Max(3, cards.Length)));
            fit = Math.Min(1, Math.Min(finalSize.Width / (radius * 2 + Side), finalSize.Height / (Side * 1.09 + radius * .48)));
            projectionDirty = true;
            var center = Bounds.Center;
            foreach (var card in cards) card.Cover.Arrange(new Rect(center.X - Side / 2, center.Y - Side / 2, Side, Side));
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

        private bool IsFrontCard(int index)
        {
            var distance = Math.Abs(index - front);
            return Math.Min(distance, cards.Length - distance) <= 1;
        }

        private Projection Project(int index)
        {
            var azimuth = cards.Length == 1 ? 0 : index * Step + angle;
            var facing = Math.Cos(azimuth);
            var lateral = Math.Sin(azimuth);
            var depth = radius * (facing - 1);
            var scale = Perspective / (Perspective - depth) * fit;
            var rise = Math.Pow(1 - intro, 3) * (100 + 56 * (1 - facing));
            var center = Bounds.Center;
            var pivot = new Vector2((float)center.X, (float)center.Y);
            var size = scale * (1 + cards[index].Emphasis * .085);
            var transform = Matrix3x2.CreateScale((float)size, pivot) *
                Matrix3x2.CreateTranslation((float)(scale * (radius * lateral + pointerLean * (1 - facing) * 9)),
                    (float)(scale * (depth * .276 + radius * .24 + rise)));
            return new(index, facing, (1 - facing) * .22, transform);
        }

        private void EnsureProjections()
        {
            if (!projectionDirty) return;
            for (var i = 0; i < cards.Length; i++) projections[i] = Project(i);
            Array.Sort(projections);
            projectionDirty = false;
        }

        protected override void RenderSubtree(IGraphicsContext context)
        {
            if (context.GetClipBoundsLocal() is { } clip && !Bounds.IntersectsWith(clip)) return;
            var original = context.GetTransform();
            EnsureProjections();
            context.Save();
            context.SetClip(Bounds);
            try
            {
                foreach (var pose in projections)
                {
                    var card = cards[pose.Index];
                    context.SetTransform(pose.Transform * original);
                    card.Cover.Render(context);
                    var frame = card.Cover.Bounds;
                    if (pose.Shade >= .01)
                        context.FillRoundedRectangle(frame, 12, 12, Color.FromArgb((byte)(pose.Shade * 255), 18, 18, 20));
                }
            }
            finally { context.Restore(); }
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
                if (!Matrix3x2.Invert(pose.Transform, out var inverse)) continue;
                var mapped = Vector2.Transform(p, inverse);
                var frame = cards[pose.Index].Cover.Bounds;
                if (!frame.Contains(new Point(mapped.X, mapped.Y))) continue;
                var dx = mapped.X - frame.Center.X;
                var dy = mapped.Y - frame.Center.Y;
                var radius = PlayButtonSize / 2;
                playHit = dx * dx + dy * dy <= radius * radius;
                return pose.Index;
            }
            return -1;
        }

        private void FocusIndex(int index, bool manual = false)
        {
            var heading = -index * Step;
            TurnTo(angle + Math.IEEERemainder(heading - angle, Math.Tau), manual);
        }

        private void UpdateHover()
        {
            var next = !dragging && hoverPoint is { } point ? CardAt(point, out _) : -1;
            if (next == hovered) return;
            if (hovered >= 0) cards[hovered].Overlay.SetHovered(false);
            hovered = next;
            if (hovered >= 0) cards[hovered].Overlay.SetHovered(true);
            Wake();
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

        protected override void OnMouseEnter() { base.OnMouseEnter(); Wake(); }
        protected override void OnMouseLeave()
        {
            base.OnMouseLeave();
            hoverPoint = null;
            UpdateHover();
            pointerAim = 0;
            Wake();
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
            if (pressed)
            {
                CancelReturn();
                target = null;
            }
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
            var aim = Math.Clamp((position.X - Bounds.Center.X) / Math.Max(1, Bounds.Width / 2), -1, 1);
            if (Math.Abs(aim - pointerAim) > .002) { pointerAim = aim; Wake(); }
            if (!pressed || !IsMouseCaptured || cards.Length < 2 || (!dragging && Math.Abs(position.X - pressPoint.X) < 5))
            { UpdateHover(); return; }
            dragging = true;
            UpdateHover();
            target = null;
            var now = Stopwatch.GetTimestamp();
            var delta = (position.X - lastPointer.X) / Math.Max(44, radius * fit);
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
                if (Math.Abs(flick) > .10) direction = Math.Sign(flick);
                TurnTo(Math.Round((angle + flick) / Step) * Step);
            }
            else
            {
                hoverPoint = Pointer(e);
                UpdateHover();
                var index = CardAt(hoverPoint.Value, out var playHit);
                if (index >= 0 && playHit) PlayMix(index);
                else if (index >= 0 && IsFrontCard(index)) OpenMix(index);
                else if (index >= 0)
                {
                    FocusIndex(index, manual: true);
                }
                else
                {
                    returnAfterTurn = playingIndex >= 0;
                    FinishTurn();
                }
            }
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
            if (newRoot == null) SetMotionActive(false);
        }

        protected override void OnDispose() { timer.Dispose(); returnTimer.Dispose(); base.OnDispose(); }
    }
}
