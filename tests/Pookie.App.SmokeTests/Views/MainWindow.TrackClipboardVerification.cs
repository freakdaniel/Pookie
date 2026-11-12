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
        var previousShareClipboard = shareClipboard;
        var selection = "POOKIE_COPY_TEST_" + Guid.NewGuid().ToString("N");
        nativeClipboard = null;
        windowClipboard = previousWriter!.ForSelection(null, selection);
        try
        {
            using var oldOwner = previousWriter.ForSelection(null, selection);
            oldOwner.TrySetText("Previous clipboard content");
            shareClipboard = windowClipboard;
            RouteWaveformClick(new(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running && !shareContent.Resizing &&
                sharePreview.Opacity == 1 && shareCopy.IsEnabled && shareUri != null && shareCopy.ActualWidth > 0);
            RouteWaveformClick(SettingsCenter(shareCopy));
            if (TooltipHints.TextFor(shareCopy) != "Скопировано") throw new InvalidOperationException("Share copy action was not invoked.");
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
        finally { CloseShare(true); shareClipboard = previousShareClipboard; windowClipboard.Dispose(); windowClipboard = previousWriter; nativeClipboard = previous; }
        Console.WriteLine("TRACK_COPY_OK: Share exports a SoundCloud URL to an external Linux process");
    }
}
