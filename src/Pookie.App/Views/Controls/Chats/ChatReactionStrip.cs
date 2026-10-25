using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed record ChatReactionChip(string Emoji, long[] Authors, bool Own);

// A personal conversation has at most two participants. Reuse its two chips;
// ordinary polling must neither recreate buttons nor replay their entrance.
internal sealed class ChatReactionStrip : Panel
{
    private sealed record Slot(Button Button, Image Emoji, Image First, Image Second, Grid Avatars)
    { internal string Value = ""; }
    private readonly List<Slot> slots = [];
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.4, 0, .2, 1));
    private string? messageId;
    private string signature = "";
    private double amount;
    private bool showing;
    internal bool HasContent => showing || amount > .001;
    internal double Reveal => amount;
    internal bool Animating => motion.IsRunning;
    internal Action? Changing;
    internal IReadOnlyList<Button> Buttons => slots.Select(s => s.Button).ToArray();
    internal ChatReactionStrip(Action<string> activate)
    {
        for (var i = 0; i < 2; i++)
        {
            var emoji = new Image().Width(22).Height(22).StretchMode(Stretch.Uniform);
            var first = new Image().Width(18).Height(18).StretchMode(Stretch.UniformToFill);
            var second = new Image().Width(18).Height(18).StretchMode(Stretch.UniformToFill);
            var avatars = new Grid().Columns("14,18").Rows("18").Children(
                new Border().Width(18).Height(18).CornerRadius(9).ClipToBounds().Child(first).Column(0),
                new Border().Width(18).Height(18).CornerRadius(9).ClipToBounds().Child(second).Column(1));
            Slot? slot = null;
            var button = TrackButtons.Action(new StackPanel().Horizontal().Spacing(5).Children(emoji.CenterVertical(), avatars.CenterVertical()),
                () => { if (slot?.Value.Length > 0) activate(slot.Value); }, TrackButtons.Text, 30).Padding(7, 0).CornerRadius(15).IsVisible(false);
            slot = new(button, emoji, first, second, avatars); slots.Add(slot); Add(button);
        }
    }
    internal void Set(string id, IReadOnlyList<ChatReactionChip> values, Func<long, IImageSource?> avatar, bool animate)
    {
        var key = string.Join('|', values.Select(v => v.Emoji + ":" + string.Join(',', v.Authors) + ":" + v.Own));
        var sameMessage = messageId == id; var changed = signature != key; messageId = id; signature = key;
        if (sameMessage && !changed)
        {
            for (var i = 0; i < values.Count && i < slots.Count; i++)
            { slots[i].First.Source = avatar(values[i].Authors[0]) ?? Icons.Source("user"); if (values[i].Authors.Length > 1) slots[i].Second.Source = avatar(values[i].Authors[1]) ?? Icons.Source("user"); }
            return;
        }
        if (sameMessage && animate && FindVisualRoot() is Window) Changing?.Invoke();
        if (sameMessage && values.Count == 0 && amount > 0 && animate && FindVisualRoot() is Window)
        {
            motion.Stop(); showing = false; IsHitTestVisible = false; var closingFrom = amount;
            motion.TickCallback = t => { amount = closingFrom * (1 - t); InvalidateMeasure(); };
            motion.CompletedCallback = () => { amount = 0; ClearSlots(); InvalidateMeasure(); };
            motion.Start(); return;
        }
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i]; var value = values.ElementAtOrDefault(i);
            slot.Button.IsVisible = value != null;
            if (value == null) { slot.Value = ""; slot.Emoji.Source = slot.First.Source = slot.Second.Source = null; continue; }
            slot.Value = value.Emoji; slot.Emoji.Source = ChatEmojiArtwork.Source(value.Emoji);
            slot.First.Source = avatar(value.Authors[0]) ?? Icons.Source("user");
            slot.Second.Source = value.Authors.Length > 1 ? avatar(value.Authors[1]) ?? Icons.Source("user") : null;
            slot.Avatars.Width = value.Authors.Length > 1 ? 32 : 18;
            ((UIElement)slot.Avatars.Children[1]).IsVisible = value.Authors.Length > 1;
            ((MaterialTrackButton)slot.Button).Palette(value.Own ? Color.FromRgb(58, 111, 151) : Color.FromArgb(42, 145, 169, 189), TrackButtons.OnSurface);
            slot.Button.ToolTip = new TextBlock().Text(value.Own ? "Твоя реакция · нажми, чтобы убрать" : "Нажми, чтобы отреагировать");
        }
        if (!changed && sameMessage) return;
        motion.Stop(); showing = values.Count > 0; IsHitTestVisible = showing;
        var target = showing ? 1d : 0d;
        if (!sameMessage || !animate || FindVisualRoot() is not Window) { amount = target; InvalidateMeasure(); return; }
        var from = amount;
        if (Math.Abs(from - target) < .001) { amount = target; InvalidateMeasure(); return; }
        motion.TickCallback = t => { amount = from + (target - from) * t; InvalidateMeasure(); };
        motion.CompletedCallback = () => { amount = target; InvalidateMeasure(); };
        motion.Start(); InvalidateMeasure();
    }
    internal void Reset()
    { motion.Stop(); messageId = null; signature = ""; amount = 0; showing = false; ClearSlots(); InvalidateMeasure(); }
    private void ClearSlots()
    { foreach (var slot in slots) { slot.Value = ""; slot.Button.IsVisible = false; slot.Emoji.Source = slot.First.Source = slot.Second.Source = null; } }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = 0d;
        foreach (var slot in slots.Where(s => s.Button.IsVisible)) { slot.Button.Measure(new(double.PositiveInfinity, 30)); width += slot.Button.DesiredSize.Width + 4; }
        return new(Math.Min(availableSize.Width, Math.Max(0, width - 4) * amount), 34 * amount);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = Bounds.X;
        foreach (var slot in slots.Where(s => s.Button.IsVisible))
        { slot.Button.Opacity = amount; slot.Button.Arrange(new(x, Bounds.Y + 4, slot.Button.DesiredSize.Width, 30)); x += slot.Button.DesiredSize.Width + 4; }
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    { context.Save(); try { context.SetClip(Bounds); base.RenderSubtree(context); } finally { context.Restore(); } }
    protected override void OnDispose() { motion.Stop(); Changing = null; base.OnDispose(); }
}
