using System.Net;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Browser;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBlock shareEmbedCode = null!;
    private TextBox shareEmbedColor = null!;
    private CopyFeedbackGlyph shareEmbedCopyGlyph = null!;
    private EmbeddedWidgetPreview shareWidgetPreview = null!;
    private Button shareEmbedCopy = null!, shareEmbedCompact = null!, shareEmbedVisualButton = null!;
    private SegmentedSelector shareEmbedLayout = null!;
    private Grid shareEmbedColorRow = null!;
    private Button shareWidgetColorPicker = null!;
    private WidgetHexField shareWidgetHexField = null!;
    private WidgetColorPopup? shareWidgetColorPopup;
    private bool openingShareWidgetColor;
    private readonly List<(Button Button, WidgetColorSwatch Swatch)> shareWidgetSwatches = [];
    private Color[] shareWidgetColors = ArtworkSwatches.Fallback.ToArray();
    private bool settingShareWidgetColor;
    private bool shareEmbedVisual;

    private FrameworkElement ShareEmbedView()
    {
        shareEmbedLayout = new SegmentedSelector("list-bullets", "Компактный", "image", "С обложкой", SetShareEmbedLayout, liquidGlass: true);
        shareEmbedCompact = shareEmbedLayout.First; shareEmbedVisualButton = shareEmbedLayout.Second;
        shareEmbedColor = new TextBox().Text("#FF5500").MaxLength(7).FontSize(13).Height(40).Padding(10, 0)
            .CornerRadius(0).BorderThickness(0).Background(Color.Transparent);
        shareEmbedColor.OnTextChanged(_ => RefreshShareEmbed());
        shareWidgetHexField = new WidgetHexField(shareEmbedColor);
        shareWidgetColorPicker = TrackButtons.Icon(Icons.View("palette", 20, Color.White), () => Run(ToggleShareWidgetColorAsync), TrackButtons.Glass, 40).Hint("Выбрать цвет");
        ((MaterialTrackButton)shareWidgetColorPicker).UseLightGlassFinish();
        ((MaterialTrackButton)shareWidgetColorPicker).Connect(false);
        var swatches = new Grid().Columns("*,*,*,*,*").Rows("40");
        for (var index = 0; index < 5; index++)
        {
            var slot = index;
            var swatch = new WidgetColorSwatch();
            var button = TrackButtons.Icon(swatch, () => shareEmbedColor.Text = WidgetHex(shareWidgetColors[slot]), TrackButtons.Text, 40).CenterHorizontal();
            shareWidgetSwatches.Add((button, swatch)); swatches.Add(button.Column(index));
        }
        shareEmbedColorRow = new Grid().Columns("Auto,*,130").Rows("40").Spacing(10).Children(
            new TextBlock().Text("Цвет").FontSize(13).Foreground(Color.White).CenterVertical(), swatches.Column(1),
            new Grid().Columns("*,40").Spacing(2).Children(shareWidgetHexField, shareWidgetColorPicker.Column(1)).Column(2));
        shareEmbedCode = new TextBlock().TextTrimming(TextTrimming.CharacterEllipsis).Height(26).FontSize(12).VerticalTextAlignment(TextAlignment.Center).IsHitTestVisible(false);
        shareEmbedCopyGlyph = new CopyFeedbackGlyph { Reverted = () => shareEmbedCopy.Hint("Скопировать код") };
        shareEmbedCopy = TrackButtons.Icon(shareEmbedCopyGlyph, CopyShareEmbed, TrackButtons.Glass, 48).Hint("Скопировать код");
        ((MaterialTrackButton)shareEmbedCopy).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)shareEmbedCopy).UseGlassAccentStateLayer();
        shareWidgetPreview = new EmbeddedWidgetPreview(() => shareDialog?.IsOpen == true && (!shareClosing || sharePreparingClose) && shareTab.Value == 2 &&
            !shareDialog.Running && !shareContent.Animating && !shareContent.Resizing && !sharePages.Animating && shareWidgetColorPopup?.IsVisible != true)
            { ViewportChanged = FitShareContent };
        return new StackPanel().Vertical().Spacing(14).Children(
            shareEmbedLayout, shareEmbedColorRow,
            new Border().CornerRadius(12).BorderThickness(0).ClipToBounds().Child(shareWidgetPreview),
            new Grid().Columns("*,48").Rows("48").Spacing(12).Children(
                new GlassCard { HoverEnabled = false, ShadowEnabled = false }.CornerRadius(24)
                    .Background(GlassSheen.LightControlTint(TrackButtons.GlassSurface)).Padding(16, 0).Child(shareEmbedCode.CenterVertical()),
                shareEmbedCopy.Column(1)));
    }

    private void ResetShareEmbed()
    { shareEmbedVisual = false; shareEmbedLayout.Select(false, animate: false); SetShareWidgetSwatches(ArtworkSwatches.Fallback); shareEmbedColor.Text = "#FF5500"; RefreshShareEmbed(); }

    private void SetShareEmbedLayout(bool visual)
    { shareEmbedVisual = visual; RefreshShareEmbed(); }

    private async Task ToggleShareWidgetColorAsync()
    {
        if (shareWidgetColorPopup?.IsOpen == true) { shareWidgetColorPopup.Close(); return; }
        if (openingShareWidgetColor || !shareDialog.IsOpen || shareClosing) return;
        openingShareWidgetColor = true; var generation = shareGeneration;
        try
        {
            await shareWidgetPreview.FreezeForPopupAsync();
            if (disposed || generation != shareGeneration || !shareDialog.IsOpen || shareClosing || shareTab.Value != 2) return;
            shareWidgetColorPopup ??= new WidgetColorPopup(() => shareWidgetColorPicker.Bounds)
            {
                ColorChanged = color => { if (!settingShareWidgetColor) shareEmbedColor.Text = WidgetHex(color); },
                Hidden = () => { shareWidgetPreview.ResumeFromPopup(); }
            };
            if (TryWidgetColor(shareEmbedColor.Text, out var color)) shareWidgetColorPopup.SetColor(color);
            shareWidgetColorPopup.Show(shareWidgetColorPicker);
        }
        finally
        {
            openingShareWidgetColor = false;
            if (shareWidgetColorPopup?.IsVisible != true) shareWidgetPreview.ResumeFromPopup();
        }
    }

    private static string BuildShareEmbed(Uri url, bool visual, bool playlist, string color)
    {
        var hex = color.Trim();
        if (hex.Length != 7 || hex[0] != '#' || !hex.AsSpan(1).ToString().All(char.IsAsciiHexDigit)) return "";
        var canonical = new UriBuilder(url) { Fragment = "" }.Uri.AbsoluteUri;
        var source = "https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString(canonical) +
            "&color=" + Uri.EscapeDataString(hex.ToLowerInvariant()) + "&auto_play=false&visual=" + (visual ? "true" : "false");
        var height = visual || playlist ? 450 : 166;
        return $"<iframe width=\"100%\" height=\"{height}\" scrolling=\"no\" frameborder=\"no\" allow=\"autoplay\" title=\"SoundCloud player\" src=\"{WebUtility.HtmlEncode(source)}\"></iframe>";
    }

    private void RefreshShareEmbed()
    {
        if (shareEmbedCode == null || shareEmbedCopy == null) return;
        shareEmbedLayout.Select(shareEmbedVisual);
        RefreshShareWidgetColors();
        shareEmbedCode.Text = shareUri == null ? "" : BuildShareEmbed(shareUri, shareEmbedVisual, shareTarget?.Playlist != null, shareEmbedColor.Text);
        shareEmbedCopy.IsEnabled = shareEmbedCode.Text.Length > 0;
        shareEmbedCopyGlyph.Reset(); shareEmbedCopy.Hint("Скопировать код");
        if (shareEmbedCode.Text.Length > 0)
        {
            var iframe = System.Xml.Linq.XElement.Parse(shareEmbedCode.Text);
            shareWidgetPreview.Configure(iframe.Attribute("src")!.Value, (shareEmbedVisual || shareTarget?.Playlist != null) ? 450 : 166);
        }
        else shareWidgetPreview.Configure(null, shareEmbedVisual ? 450 : 166);
        if (shareTab.Value == 2 && shareDialog?.IsOpen == true) shareWidgetPreview.Activate();
        if (shareTab.Value == 2) SetShareMessage(shareUri == null ? "У этого трека или плейлиста пока нет ссылки для встраивания." :
            shareEmbedCopy.IsEnabled ? "" : "Укажи цвет в формате #FF5500.");
    }

    private void CopyShareEmbed()
    {
        if (!shareEmbedCopy.IsEnabled) return;
        var clipboard = shareClipboard ?? Application.Current.PlatformServices.Clipboard;
        var copied = clipboard?.TrySetText(shareEmbedCode.Text) == true;
        if (copied) shareEmbedCopyGlyph.ShowCopied(); else shareEmbedCopyGlyph.Reset();
        shareEmbedCopy.Hint(copied ? "Скопировано" : "Не удалось скопировать. Повторить");
    }

    private void FitShareWidgetPreview()
    {
        // Reserve tabs, options, code row, gaps and the track plaque. Keep the
        // full player at 450px on roomy screens; scale its page on small ones.
        var logical = shareEmbedVisual || shareTarget?.Playlist != null ? 450 : 166;
        shareWidgetPreview.Height = Math.Min(logical, Math.Max(96, Window.ClientSize.Height - 414));
    }

    private static string WidgetHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static bool TryWidgetColor(string value, out Color color)
    {
        color = default;
        if (value.Length != 7 || value[0] != '#' || !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
    }
    private void SetShareWidgetSwatches(Color[] colors)
    { shareWidgetColors = colors.ToArray(); RefreshShareWidgetColors(); }
    private void RefreshShareWidgetColors()
    {
        if (shareWidgetColorPicker == null) return;
        var valid = TryWidgetColor(shareEmbedColor.Text, out var selected);
        if (valid)
        {
            settingShareWidgetColor = true;
            try { shareWidgetColorPopup?.SetColor(selected); }
            finally { settingShareWidgetColor = false; }
        }
        for (var index = 0; index < shareWidgetSwatches.Count; index++)
        {
            shareWidgetSwatches[index].Swatch.Set(shareWidgetColors[index], valid && selected == shareWidgetColors[index]);
            shareWidgetSwatches[index].Button.Hint(WidgetHex(shareWidgetColors[index]));
        }
    }
}
