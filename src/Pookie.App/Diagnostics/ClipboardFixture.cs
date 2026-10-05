using Pookie.App.Platform;

namespace Pookie.App.Diagnostics;

internal static class ClipboardFixture
{
    // A private selection never reads or overwrites the user's real clipboard.
    internal const string Text = "Музыка / Ελληνικά / العربية / 日本語 / café 🎵";
    public static void Run(string selection)
    {
        var clipboard = GtkClipboardService.TryCreate(selection)
            ?? throw new InvalidOperationException("GTK clipboard is unavailable");
        clipboard.TrySetText(Text);
        Console.WriteLine("CLIPBOARD_READY");
        var stop = Task.Run(() => Console.ReadLine());
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!stop.IsCompleted && DateTime.UtcNow < deadline)
        {
            clipboard.Pump();
            Thread.Sleep(2);
        }
    }
}
