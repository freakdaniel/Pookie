using System.Diagnostics;
using System.Text;
using Aprillz.MewUI.Controls;
using Pookie.App.Platform;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackClipboardAsync(Button button, SoundCloudTrack track)
    {
        if (!OperatingSystem.IsLinux()) return;
        var previous = nativeClipboard;
        var previousWriter = windowClipboard;
        var selection = "POOKIE_COPY_TEST_" + Guid.NewGuid().ToString("N");
        nativeClipboard = null;
        windowClipboard = previousWriter!.ForSelection(null, selection);
        try
        {
            using var oldOwner = previousWriter.ForSelection(null, selection);
            oldOwner.TrySetText("Previous clipboard content");
            RouteWaveformClick(new(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2));
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(MainWindow).Assembly.Location);
            start.Environment["GDK_BACKEND"] = "x11";
            start.ArgumentList.Add("--clipboard-read-fixture"); start.ArgumentList.Add(selection);
            using var reader = Process.Start(start)!;
            var output = await reader.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (output != "CLIPBOARD_TEXT:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(track.PermalinkUrl!)))
                throw new InvalidOperationException($"Track copy did not reach another Linux application: {output}; {await reader.StandardError.ReadToEndAsync()}");
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { windowClipboard.Dispose(); windowClipboard = previousWriter; nativeClipboard = previous; }
        Console.WriteLine("TRACK_COPY_OK: real button click exports a SoundCloud URL to an external Linux process");
    }
}
