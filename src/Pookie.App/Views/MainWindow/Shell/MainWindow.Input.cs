using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private GtkClipboardService? nativeClipboard;
    private LinuxWindowClipboard? windowClipboard;
    private DispatcherTimer? clipboardTimer;
    private LinuxSearchInput? searchInput;

    private void InitializeSearchInput()
    {
        if (!OperatingSystem.IsLinux()) return;
        nativeClipboard = GtkClipboardService.TryCreate();
        windowClipboard = new(Window, nativeClipboard);
        topSearchInput.ClipboardService = windowClipboard;
        likedFilter.ClipboardService = windowClipboard;
        trackDetailDescription.ClipboardService = windowClipboard;
        userDescription.ClipboardService = windowClipboard;
        userSidebarBiography.ClipboardService = windowClipboard;
        chatInput.ClipboardService = windowClipboard;
        chatSearch.ClipboardService = windowClipboard;
        chatGifSearch.ClipboardService = windowClipboard;
        foreach (var field in new TextBox[] { topSearchInput, likedFilter, chatSearch, chatGifSearch, chatMessageSearch, chatMusicSearch })
        {
            field.ClipboardService = windowClipboard;
            field.Commands.Unregister(StandardCommands.Paste);
            field.Commands.Register(StandardCommands.Paste, () => Run(() => PasteTextAsync(field)), () => field.IsEffectivelyEnabled && !field.IsReadOnly);
        }
        clipboardTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(20));
        clipboardTimer.Tick += () => { nativeClipboard?.Pump(); windowClipboard?.Pump(); };
        searchInput = new(Window, topSearchInput, likedFilter, chatInput, chatSearch, chatGifSearch);
    }
    private async Task PasteTextAsync(TextBox field)
    {
        if (windowClipboard == null || !field.IsFocused || field.IsReadOnly) return;
        var text = field.Text; var start = field.SelectionStart; var length = field.SelectionLength;
        var value = await windowClipboard.ReadTextAsync(lifetime.Token);
        if (disposed || !field.IsFocused || field.IsReadOnly || !field.IsEffectivelyEnabled || field.Text != text || field.SelectionStart != start || field.SelectionLength != length || value == null) return;
        field.ReplaceSelection(value);
    }
}
