using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class HomeShelf
    {
        internal const double CoverSize = 160;
        private const double Spacing = 24;
        private const double Stride = CoverSize + Spacing;
        private readonly MainWindow owner;
        private readonly StackPanel strip = new StackPanel().Horizontal().Spacing(Spacing);
        private readonly Button previous, next;
        private readonly List<CollectionCardView> cards = [];
        private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(240), Easing.CubicBezier(.2, 0, 0, 1));
        internal readonly ScrollViewer Scroll;
        internal readonly Grid Root;

        internal HomeShelf(MainWindow owner)
        {
            this.owner = owner;
            Scroll = new ScrollViewer().HorizontalScroll(ScrollMode.Auto).VerticalScroll(ScrollMode.Disabled)
                .Background(Color.Transparent).BorderThickness(0).Padding(0).Content(strip);
            ((IVisualTreeHost)Scroll).VisitChildren(element =>
            {
                if (element is ScrollBar { Orientation: Orientation.Horizontal } bar)
                { bar.Opacity = 0; bar.IsHitTestVisible = false; }
                return true;
            });
            previous = Arrow("arrow-left", "Предыдущие карточки", -1).Left();
            next = Arrow("arrow-right", "Следующие карточки", 1).Right();
            Root = new Grid().Columns("*").Rows("Auto").Children(Scroll, previous, next);
            Scroll.ScrollChanged += RefreshArrows;
            RefreshArrows();
        }

        private Button Arrow(string glyph, string label, int direction) =>
            TrackButtons.Icon(Icons.View(glyph, 16), () => Move(direction), TrackButtons.Tonal, 34)
                .Top().Margin(8, (CoverSize - 34) / 2, 8, 0).Hint(label);

        private double Maximum => Math.Max(0, strip.DesiredSize.Width - Scroll.ViewportWidth);

        private void RefreshArrows()
        {
            previous.IsVisible = Scroll.HorizontalOffset > 1;
            next.IsVisible = Scroll.ViewportWidth > 0 && Scroll.HorizontalOffset < Maximum - 1;
        }

        private void Move(int direction)
        {
            var from = Scroll.HorizontalOffset;
            var steps = Math.Max(1, Math.Floor((Scroll.ViewportWidth + Spacing) / Stride));
            var target = Math.Clamp(from + direction * steps * Stride, 0, Maximum);
            motion.Stop();
            motion.TickCallback = t => Scroll.SetScrollOffsets(from + (target - from) * t, 0);
            motion.Start();
        }

        internal void SetItems(IEnumerable<LibraryItem> items)
        {
            motion.Stop();
            foreach (var card in cards)
            {
                card.Item = null;
                owner.StopCardArtwork(card.Cover);
            }
            strip.Clear();
            cards.Clear();
            foreach (var item in items)
            {
                var card = owner.CreateHomeShelfCard(item);
                cards.Add(card);
                strip.Add(card.Root);
            }
            Scroll.SetScrollOffsets(0, 0);
        }
    }

    private CollectionCardView CreateHomeShelfCard(LibraryItem item)
    {
        var card = CreateCollectionCardView();
        card.Item = item;
        card.SetArtist(false);
        card.SetSize(HomeShelf.CoverSize);
        card.Root.Width = HomeShelf.CoverSize;
        card.Title.Text = item.Title;
        card.Author.Text = item.Subtitle;
        card.Root.Click += () => Run(() => OpenLibraryItemAsync(item));
        RefreshCollectionCardPlayback(card, animate: false);
        SetCollectionArtwork(card.Cover, item);
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (!disposed && ReferenceEquals(card.Item, item))
                SetCollectionArtwork(card.Cover, item, source, finished: true);
        });
        return card;
    }
}
