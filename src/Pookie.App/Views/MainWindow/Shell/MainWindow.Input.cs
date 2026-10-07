using Aprillz.MewUI;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private GtkClipboardService? nativeClipboard;
    private DispatcherTimer? clipboardTimer;
    private LinuxSearchInput? searchInput;

    private void InitializeSearchInput()
    {
        if (!OperatingSystem.IsLinux()) return;
        nativeClipboard = GtkClipboardService.TryCreate();
        if (nativeClipboard == null) return;
        topSearchInput.ClipboardService = nativeClipboard;
        likedFilter.ClipboardService = nativeClipboard;
        clipboardTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(20));
        clipboardTimer.Tick += nativeClipboard.Pump;
        searchInput = new(Window, topSearchInput, likedFilter);
    }
}
