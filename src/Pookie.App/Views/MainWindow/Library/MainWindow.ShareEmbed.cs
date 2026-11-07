using System.Net;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private MultiLineTextBox shareEmbedCode = null!;
    private TextBox shareEmbedColor = null!;
    private TextBlock shareEmbedCopyLabel = null!;
    private Button shareEmbedCopy = null!, shareEmbedCompact = null!, shareEmbedVisualButton = null!;
    private bool shareEmbedVisual;

    private FrameworkElement ShareEmbedView()
    {
        shareEmbedCompact = TrackButtons.Action(new TextBlock().Text("Компактный").FontSize(13), () => SetShareEmbedLayout(false), TrackButtons.Tonal, 36);
        shareEmbedVisualButton = TrackButtons.Action(new TextBlock().Text("С обложкой").FontSize(13), () => SetShareEmbedLayout(true), TrackButtons.Text, 36);
        shareEmbedColor = new TextBox().Text("#FF5500").MaxLength(7).FontSize(13).Width(90).Height(36);
        shareEmbedColor.OnTextChanged(_ => RefreshShareEmbed());
        shareEmbedCode = new MultiLineTextBox { IsReadOnly = true }.Wrap(true).Height(100).FontSize(12).Background(Color.Transparent).BorderThickness(0).Padding(0);
        shareEmbedCopyLabel = new TextBlock().Text("Скопировать код").FontSize(13).SemiBold();
        shareEmbedCopy = TrackButtons.Action(shareEmbedCopyLabel, CopyShareEmbed, TrackButtons.Glass, 44);
        ((MaterialTrackButton)shareEmbedCopy).UseLightGlassFinish();
        return new StackPanel().Vertical().Spacing(14).Children(
            new StackPanel().Horizontal().Spacing(12).Children(
                new StackPanel().Horizontal().Spacing(8).Children(shareEmbedCompact, shareEmbedVisualButton),
                new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Цвет").FontSize(12).Foreground(Muted).CenterVertical(), shareEmbedColor)),
            new StackPanel().Vertical().Spacing(8).Children(new TextBlock().Text("Код для сайта").FontSize(12).Foreground(Muted),
                new GlassCard { HoverEnabled = false }.CornerRadius(14).Padding(14).Child(shareEmbedCode)),
            shareEmbedCopy);
    }

    private void ResetShareEmbed()
    { shareEmbedVisual = false; shareEmbedColor.Text = "#FF5500"; RefreshShareEmbed(); }

    private void SetShareEmbedLayout(bool visual)
    { shareEmbedVisual = visual; RefreshShareEmbed(); }

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
        TrackButtons.SetVariant(shareEmbedCompact, shareEmbedVisual ? TrackButtons.Text : TrackButtons.Tonal);
        TrackButtons.SetVariant(shareEmbedVisualButton, shareEmbedVisual ? TrackButtons.Tonal : TrackButtons.Text);
        shareEmbedCode.Text = shareUri == null ? "" : BuildShareEmbed(shareUri, shareEmbedVisual, shareTarget?.Playlist != null, shareEmbedColor.Text);
        shareEmbedCopy.IsEnabled = shareEmbedCode.Text.Length > 0;
        shareEmbedCopyLabel.Text = "Скопировать код";
        if (shareTab.Value == 2) SetShareMessage(shareUri == null ? "У этого трека или плейлиста пока нет ссылки для встраивания." :
            shareEmbedCopy.IsEnabled ? "" : "Укажи цвет в формате #FF5500.");
    }

    private void CopyShareEmbed()
    {
        if (!shareEmbedCopy.IsEnabled) return;
        var clipboard = shareLink.ClipboardService ?? Application.Current.PlatformServices.Clipboard;
        shareEmbedCopyLabel.Text = clipboard?.TrySetText(shareEmbedCode.Text) == true ? "Скопировано" : "Повторить";
    }
}
