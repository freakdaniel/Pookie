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
    private readonly GlassGeometry stateGeometry = new();
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private double emphasis, from, target;

    internal HeaderSearchField(TextBox input, bool compact = false, bool liveGlass = false)
    {
        this.input = input;
        Height = compact ? 40 : ShellHeader.NavigationButtonHeight;
        Padding = new Thickness(0); BorderThickness = 0; Background = Color.Transparent;
        input.Background(Color.Transparent).BorderThickness(0).Padding(0).Height(28).FontSize(compact ? 13 : 14).Foreground(TrackButtons.OnSurface);
        linkPreview = new LinkPreviewControl().Left().CenterVertical().IsVisible(false);
        linkPreview.FallbackCompleted = () => { linkPreview.IsVisible = false; input.Opacity = 1; };
        var content = new Grid().Columns(compact ? "20,*" : "24,*").Rows("*").Spacing(compact ? 10 : 12).Children(
            Icons.View("magnifying-glass", compact ? 20 : 22, TrackButtons.OnSurface).Center().Column(0),
            new Grid().Columns("*").Rows("*").Column(1).Children(input.CenterVertical(), linkPreview));
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
        else if (Content is Border && Bounds.Width > 0)
        {
            var bounds = ShellHeader.StateLayerBounds(this, context.DpiScale);
            var fill = TrackButtons.StateColor(Color.Transparent, TrackButtons.OnSurface, HeaderButton.HoverOpacity);
            context.FillPath(stateGeometry.Get(bounds, new CornerRadius(bounds.Height / 2)), fill);
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
