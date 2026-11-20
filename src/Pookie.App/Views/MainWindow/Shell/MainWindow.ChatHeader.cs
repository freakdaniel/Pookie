using Pookie.Localization;
using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private HeaderButton chatHeaderButton = null!;
    private Border chatHeaderBadge = null!;
    private TextBlock chatHeaderCount = null!;

    private FrameworkElement ChatHeaderButton()
    {
        var glyph = Icons.TintableView("chat-circle", 24, out var ink);
        chatHeaderCount = new TextBlock().FontSize(10).Bold().Foreground(Color.White).Center();
        chatHeaderBadge = new Border().Background(Color.FromRgb(224, 50, 65)).CornerRadius(9)
            .MinWidth(18).Height(18).Padding(4, 0).Top().Right().Margin(0, -3, -3, 0)
            .IsHitTestVisible(false).Child(chatHeaderCount);
        chatHeaderButton = new HeaderButton(new CornerRadius(ShellHeader.AccountButtonSize / 2));
        var animatedGlyph = chatHeaderButton.WithActivationMotion(glyph, progress =>
        {
            var bounce = MathF.Sin(2 * MathF.PI * progress) * (1 - progress);
            return Matrix3x2.CreateScale(1 + .14f * bounce, 1 - .14f * bounce) * Matrix3x2.CreateTranslation(0, 2.6f * bounce);
        }, durationMs: 440, easing: Easing.Linear);
        chatHeaderButton.Width(ShellHeader.AccountButtonSize).Height(ShellHeader.AccountButtonSize).CenterVertical()
            .Content(new Grid().Columns("*").Rows("*").Children(animatedGlyph.Center(), chatHeaderBadge))
            .OnClick(() => { if (page.Value != Page.Chats) Run(() => NavigateAsync(Page.Chats)); });
        chatHeaderButton.UseAnimatedInk(ink, NavInk, NavHover, Color.White);
        void Refresh() => chatHeaderButton.SetSelected(page.Value == Page.Chats);
        page.Changed += Refresh;
        signedIn.Changed += RefreshChatHeaderBadge;
        Refresh(); RefreshChatHeaderBadge();
        return chatHeaderButton;
    }

    private void RefreshChatHeaderBadge()
    {
        if (chatHeaderBadge == null) return;
        var count = signedIn.Value ? chatUnread.TotalBadge : "";
        if (chatHeaderCount.Text == count && chatHeaderBadge.IsVisible == (count.Length > 0)) return;
        chatHeaderCount.Text = count;
        chatHeaderBadge.IsVisible = count.Length > 0;
        chatHeaderButton.Hint(count.Length > 0 ? UiText.Format("chats.badge.unread", count): UiText.Get("header.chats"));
    }
}
