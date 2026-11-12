using Aprillz.MewUI;

namespace Pookie.App.Browser;

// All calls and callbacks belong to the MewUI UI thread. Browser engines own
// their renderer processes; no second application window or message loop is run.
internal interface IEmbeddedBrowser : IDisposable
{
    Task InitializeAsync(nint parent, string profile, string script, bool isolated);
    void Navigate(string url);
    Task ExecuteAsync(string script);
    void SetBounds(Rect bounds, double scale);
    void SetVisible(bool visible);
    Task<byte[]> CaptureAsync();
    // Platforms with native child expose events may atomically replace the
    // child with this already decoded frame while moving it offscreen.
    bool TryParkWithSnapshot(Rect bounds, double scale, byte[] bgra, int width, int height) => false;
    Task ShutdownAsync() { Dispose(); return Task.CompletedTask; }
}

internal sealed record EmbeddedBrowserEvents(
    Func<string, bool, bool> Navigate,
    Action<string, string> Message,
    Action<string> SourceChanged,
    Action Ready,
    Action<Exception> Failed);

internal static class EmbeddedBrowserFactory
{
    internal static IEmbeddedBrowser Create(EmbeddedBrowserEvents events)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new WindowsEmbeddedBrowser(events);
#endif
        if (OperatingSystem.IsLinux()) return new WebKitLoginWindow.EmbeddedBrowser(events);
        if (OperatingSystem.IsMacOS()) return new MacEmbeddedBrowser(events);
        throw new PlatformNotSupportedException("Встроенный вход поддерживает Windows, Linux и macOS.");
    }
}
