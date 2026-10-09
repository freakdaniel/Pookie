using Aprillz.MewUI;
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
        clipboardTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(20));
        clipboardTimer.Tick += () => { nativeClipboard?.Pump(); windowClipboard?.Pump(); };
        searchInput = new(Window, topSearchInput, likedFilter);
    }
}
