using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FloatingMenuLayer chatAttachMenu = null!;
    private Border chatAttachMenuPanel = null!;
    private bool chatFilePickerOpen;
    private FloatingMenuHover chatAttachInteraction = null!;
    private FrameworkElement ChatAttachMenuView()
    {
        InitializeChatAttachMenu(); return chatAttachMenu;
    }
    private void InitializeChatAttachMenu()
    {
        Button Choice(string icon, string text, Action action)
        {
            return MenuChoice(icon, text, () => { CloseChatAttachMenu(true); action(); });
        }
        chatAttachMenuPanel = FloatingMenuLayer.CreateCard(new StackPanel().Vertical().Children(
                Choice("music-notes", "Трек или плейлист", () => Run(OpenChatMusicPickerAsync)),
                Choice("file", "Документ", () => Run(() => PickChatFilesAsync(documents: true))),
                Choice("image", "Фото или видео", () => Run(() => PickChatFilesAsync(mediaOnly: true)))), 238).Background(ChatGlassSurface);
        chatAttachMenu = new FloatingMenuLayer(chatComposer, chatAttachMenuPanel, gap: 11).Initialize();
        chatAttachInteraction = new FloatingMenuHover(chatAttach, chatAttachMenuPanel, ShowChatAttachMenu, () => CloseChatAttachMenu());
        chatAttach.OnKeyDown(e => { if (e.Key is Key.Up or Key.Down) { e.Handled = true; ShowChatAttachMenu(); } });
    }
    private void ShowChatAttachMenu()
    { if (CanAttachChat() && !chatFilePickerOpen && Window.IsActive) { CloseChatGifs(); CloseChatMessageMenu(); CloseShellMenus(); chatAttachMenu.Show(); } }
    private void CloseChatAttachMenu(bool immediate = false)
    { chatAttachInteraction?.Stop(); chatAttachMenu?.Close(immediate); }
}
