using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The native TextBox keeps selection, IME and clipboard support.
internal sealed class HeaderSearchField : ContentControl
{
    private readonly TextBox input;
    private readonly LinkPreviewControl linkPreview;
    internal bool ShowsLink => linkPreview.IsVisible;
    internal string? LinkLabel => ShowsLink ? linkPreview.Label : null;
    internal bool LinkTransitioning => linkPreview.Transitioning;
    private readonly BoxShadowRaster? shadow;
    private readonly Button? shellTrigger;
    private readonly Image? triggerGlyph;
    private readonly TransformDecorator? shellGlyph, shellEditor;
    internal double Reveal { get; private set; }
    private double expansion;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private double emphasis, from, target;

    internal HeaderSearchField(TextBox input, bool compact = false, bool liveGlass = false, Button? shellTrigger = null)
    {
        this.input = input;
        Height = compact ? 40 : ShellHeader.NavigationButtonHeight;
        Padding = new Thickness(0); BorderThickness = 0; Background = Color.Transparent;
        input.Background(Color.Transparent).BorderThickness(0).Padding(0).Height(28).FontSize(compact ? 13 : 14).Foreground(TrackButtons.OnSurface);
        linkPreview = new LinkPreviewControl().Left().CenterVertical().IsVisible(false);
        linkPreview.FallbackCompleted = () => { linkPreview.IsVisible = false; input.Opacity = 1; };
        var glyph = Icons.View("magnifying-glass", compact ? 20 : 22, TrackButtons.OnSurface).Center();
        var editor = new Grid().Columns("*").Rows("*").Children(input.CenterVertical(), linkPreview);
        var content = new Grid().Columns(compact ? "20,*" : "24,*").Rows("*").Spacing(compact ? 10 : 12);
        if (shellTrigger != null)
        {
            this.shellTrigger = shellTrigger;
            triggerGlyph = (Image)shellTrigger.FindVisualChild<Image>()!;
            shellGlyph = new TransformDecorator { Child = glyph, IsHitTestVisible = false, Opacity = 0 };
            shellEditor = new TransformDecorator { Child = editor, Opacity = 0 };
            content.Children(shellGlyph.Column(0), shellEditor.Column(1));
        }
        else content.Children(glyph.Column(0), editor.Column(1));
        if (liveGlass)
        {
            var surface = new GlassToolGroup(content, new Thickness(16, 0));
            Content = surface;
            input.GotFocus += () => surface.SetEmphasized(true);
            input.LostFocus += () => surface.SetEmphasized(false);
        }
        else if (compact)
        {
            var surface = new GlassSurface().Height(40).CornerRadius(20).Padding(16, 0).Content(content);
            Content = surface;
            shadow = new(6, 2, Color.FromArgb(90, 0, 0, 0));
            motion.TickCallback = value =>
            {
                emphasis = from + (target - from) * value; surface.Emphasis = emphasis;
                surface.InvalidateVisual(); InvalidateVisual();
            };
            MouseEnter += Refresh; MouseLeave += Refresh;
            input.GotFocus += Refresh; input.LostFocus += Refresh;
        }
        else
        {
            Content = new Border().CornerRadius(Height / 2).Background(Color.Transparent).BorderThickness(0).Padding(16, 0).ClipToBounds().Child(content);
        }
        MouseDown += args => { if (args.Button == MouseButton.Left) { input.Focus(); if (ShowsLink) input.SelectAll(); } };
        input.KeyDown += PrepareLinkKey;
        input.TextInput += PrepareLinkText;
    }

    internal void SetReveal(double reveal, bool active)
    {
        if (shellGlyph == null || shellEditor == null || triggerGlyph == null) return;
        Reveal = reveal;
        expansion = SearchPhase(reveal, .07, .89);
        var editor = SearchPhase(reveal, .46, 1);
        triggerGlyph.Opacity = active ? 0 : 1;
        shellGlyph.Opacity = active ? 1 : 0;
        var dx = (triggerGlyph.Bounds.Center.X - shellGlyph.Bounds.Center.X) * (1 - expansion);
        var scale = (float)(1 - .18 * (1 - expansion));
        shellGlyph.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((float)dx, 0);
        shellEditor.Opacity = editor;
        shellEditor.Transform = Matrix3x2.CreateTranslation((float)(dx + 12 * (1 - editor)), 0);
        InvalidateVisual();
    }

    internal static double SearchPhase(double progress, double start, double end)
    {
        var t = Math.Clamp((progress - start) / (end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal void ShowLink(LinkPreviewData? data)
    {
        if (data != null) linkPreview.Update(data);
        else linkPreview.Clear();
        linkPreview.IsVisible = data != null;
        input.Opacity = data == null ? 1 : 0;
        linkPreview.Hint(data?.Url);
    }

    internal void ShowMissingLink(string url) => linkPreview.Update(LinkPreviewData.Missing(url));

    private void PrepareLinkKey(KeyEventArgs args)
    {
        if (!ShowsLink || args.Handled || input.IsReadOnly) return;
        if (args.Key is Key.Backspace or Key.Delete || args.Modifiers.HasFlag(ModifierKeys.Control) && args.Key is Key.C or Key.X or Key.V)
            input.SelectAll();
    }

    private void PrepareLinkText(TextInputEventArgs args)
    { if (ShowsLink && !linkPreview.Loading && !args.Handled && !input.IsReadOnly) input.SelectAll(); }

    private void Refresh()
    {
        var next = input.IsFocused ? 1 : IsMouseOver ? .45 : 0;
        if (target == next) return;
        motion.Stop(); from = emphasis; target = next;
        if (FindVisualRoot() is Window) motion.Start();
        else if (Content is GlassSurface surface) { emphasis = next; surface.Emphasis = next; surface.InvalidateVisual(); }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Content is GlassSurface { Bounds.Width: > 0 } surface)
        {
            shadow?.Draw(context, surface.Bounds, 20, (byte)(65 + emphasis * 25) / 90d);
        }
        else if (shellTrigger != null && Reveal > 0 && Bounds.Width > 0)
        {
            var full = ShellHeader.StateLayerBounds(this, context.DpiScale);
            var start = Math.Clamp(shellTrigger.Bounds.X, full.X, full.Right - full.Height);
            var left = start + (full.X - start) * expansion;
            var fill = TrackButtons.StateColor(Color.Transparent, TrackButtons.OnSurface, HeaderButton.HoverOpacity * SearchPhase(Reveal, .20, .56));
            // Paint the expanding pill once at its current bounds, outside the
            // opacity groups used for the moving navigation and editor.
            if (fill.A > 0) context.FillRoundedRectangle(new Rect(left, full.Y, full.Right - left, full.Height),
                full.Height / 2, full.Height / 2, fill);
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) shadow?.Dispose(); }

    protected override void OnDispose()
    {
        input.KeyDown -= PrepareLinkKey; input.TextInput -= PrepareLinkText;
        motion.Stop(); shadow?.Dispose(); base.OnDispose();
    }

    private sealed class GlassSurface : ContentControl
    {
        private readonly GlassSheenRaster sheen = new();
        internal double Emphasis { get; set; }

        protected override ChildClip? GetChildClip(UIElement child) => ChildClip.Rounded(Bounds, 20);

        protected override void OnRender(IGraphicsContext context)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            context.FillRoundedRectangle(Bounds, 20, 20, Color.FromArgb((byte)(218 + Emphasis * 18), 49, 49, 55));
            sheen.Draw(context, Bounds, new CornerRadius(20), Emphasis, GetDpi() / 96d);
        }

        protected override void OnDispose() { sheen.Dispose(); base.OnDispose(); }
    }
}
