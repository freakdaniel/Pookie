using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatAttachLayer chatAttachMenu = null!;
    private Border chatAttachMenuPanel = null!;
    private bool chatFilePickerOpen;
    private readonly DispatcherTimer chatAttachHover = new(TimeSpan.FromMilliseconds(220));
    private readonly DispatcherTimer chatAttachLeave = new(TimeSpan.FromMilliseconds(280));
    private FrameworkElement ChatAttachMenuView()
    {
        InitializeChatAttachMenu(); return chatAttachMenu;
    }
    private void InitializeChatAttachMenu()
    {
        Button Choice(string icon, string text, Action action)
        {
            var button = TrackButtons.Action(new StackPanel().Horizontal().Spacing(12).Children(
                Icons.View(icon, 21).CenterVertical(), new TextBlock().Text(text).FontSize(14).CenterVertical()),
                () => { CloseChatAttachMenu(true); action(); }, TrackButtons.Text, 42).Padding(12, 0);
            ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
            return button;
        }
        chatAttachMenuPanel = new Border().Background(chatComposer.Background).BorderThickness(0).CornerRadius(14).Padding(6).Width(238)
            .Child(new StackPanel().Vertical().Children(
                Choice("music-notes", "Трек или плейлист", () => Run(OpenChatMusicPickerAsync)),
                Choice("file", "Документ", () => Run(() => PickChatFilesAsync(documents: true))),
                Choice("image", "Фото или видео", () => Run(() => PickChatFilesAsync(mediaOnly: true)))));
        var shadow = new ShadowDecorator().BlurRadius(12).OffsetY(3).ShadowColor(Color.FromArgb(85, 0, 0, 0)).CornerRadius(14).Child(chatAttachMenuPanel);
        chatAttachMenu = new ChatAttachLayer(chatComposer, shadow).Initialize();
        chatAttachHover.Tick += () => { chatAttachHover.Stop(); ShowChatAttachMenu(); };
        chatAttachLeave.Tick += () =>
        { chatAttachLeave.Stop(); if (!chatAttach.IsMouseOver && !chatAttachMenuPanel.IsMouseOver) CloseChatAttachMenu(); };
        chatAttach.MouseEnter += () => { chatAttachLeave.Stop(); if (CanAttachChat() && !chatFilePickerOpen) chatAttachHover.Start(); };
        chatAttach.MouseLeave += () => { chatAttachHover.Stop(); chatAttachLeave.Start(); };
        chatAttachMenuPanel.MouseEnter += () => chatAttachLeave.Stop();
        chatAttachMenuPanel.MouseLeave += () => chatAttachLeave.Start();
        chatAttach.OnKeyDown(e => { if (e.Key is Key.Up or Key.Down) { e.Handled = true; ShowChatAttachMenu(); } });
    }
    private void ShowChatAttachMenu()
    { if (CanAttachChat() && !chatFilePickerOpen && Window.IsActive) chatAttachMenu.Show(); }
    private void CloseChatAttachMenu(bool immediate = false)
    { chatAttachHover.Stop(); chatAttachLeave.Stop(); chatAttachMenu?.Close(immediate); }
}
