using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Pookie.App.Diagnostics;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatAttachmentInputAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "pookie входящее " + Guid.NewGuid().ToString("N") + ".png");
        using (var input = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-small.png")!)
        using (var output = File.Create(path)) input.CopyTo(output);
        var oldRead = chatClipboardRead; var oldClipboard = chatInput.ClipboardService;
        try
        {
            chatInput.Text = "Подпись остаётся";
            if (chatInput.Padding.Left < 10) throw new InvalidOperationException("Composer text and placeholder need an inset.");
            await AddChatFilesAsync([path]);
            await AddChatFilesAsync([path]);
            if (chatSelectedFiles.Length != 1 || chatInput.Text != "Подпись остаётся") throw new InvalidOperationException("Attachment imports duplicate paths or overwrite text.");
            await AddChatFilesAsync([Path.GetTempPath()]);
            if (chatSelectedFiles.Length != 1) throw new InvalidOperationException("A directory replaced valid attachments.");
            ClearChatAttachments();
            await Task.Delay(120);

            var point = new Point(chatComposer.Bounds.X + chatComposer.ActualWidth / 2, chatComposer.Bounds.Y + 20);
            var data = new DataObject(); data.SetStorageItems([path]);
            var args = new DragEventArgs(data, point, point);
            var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowDragDropRouter")!;
            router.GetMethod("OnExternalDragEnter")!.Invoke(null, [Window, args]);
            await WaitForLikedLayoutAsync(() => chatComposerSurface.Dragging);
            await Task.Delay(220); CaptureUiPreview("chats-composer-drag");
            point = new Point(chatComposer.Bounds.X + chatComposer.ActualWidth / 2, chatComposer.Bounds.Y + chatComposer.ActualHeight / 2);
            var drop = new DragEventArgs(data, point, point);
            router.GetMethod("OnExternalDrop")!.Invoke(null, [Window, drop]);
            await WaitForLikedLayoutAsync(() => chatSelectedFiles.Length == 1 && !chatComposerSurface.Dragging && !chatImporting);
            if (!drop.Accepted || !File.Exists(path)) throw new InvalidOperationException("Drop failed or moved the user's original file.");
            ClearChatAttachments();

            chatClipboardRead = _ => Task.FromResult<ChatClipboardData?>(new([], File.ReadAllBytes(path)));
            await PasteChatAsync(); var temporary = chatSelectedFiles.Single();
            await WaitForLikedLayoutAsync(() => chatAttachmentPreview.Source != null);
            await Task.Delay(220);
            CaptureUiPreview("chats-composer-pasted-image");
            if (chatComposer.Background.R > 100 || chatComposer.Background.G > 100 || chatComposer.Background.B > 100)
                throw new InvalidOperationException("Composer feedback must retain the neutral dark surface.");
            if (temporary == path || !File.Exists(temporary)) throw new InvalidOperationException("Clipboard image was not staged.");
            ClearChatAttachments();
            if (File.Exists(temporary) || !File.Exists(path)) throw new InvalidOperationException("Clipboard staging cleanup touched the wrong file.");
            var delayed = new TaskCompletionSource<ChatClipboardData?>(TaskCreationOptions.RunContinuationsAsynchronously);
            chatClipboardRead = _ => delayed.Task;
            var paste = PasteChatAsync(); ClearChatAttachments();
            delayed.SetResult(new([], File.ReadAllBytes(path))); await paste;
            if (chatSelectedFiles.Length != 0) throw new InvalidOperationException("Cancelled paste attached stale clipboard data.");

            if (OperatingSystem.IsLinux())
            {
                var selection = "POOKIE_CHAT_CLIPBOARD_" + Guid.NewGuid().ToString("N");
                var reader = GtkClipboardService.TryCreate(selection)!;
                using var clipboard = windowClipboard!.ForSelection(reader, selection);
                chatInput.ClipboardService = clipboard;
                chatClipboardRead = token => ChatClipboard.ReadAsync(token, selection);
                var list = path + ".uris";
                await File.WriteAllTextAsync(list, "copy\n" + new Uri(path).AbsoluteUri + "\n");
                try
                {
                    await WithChatClipboardFixtureAsync(selection, "image/png", path, async () =>
                    {
                        chatInput.Focus();
                        var display = XOpenDisplay(0);
                        try { SendCharacter(display, 'v', 4); }
                        finally { XCloseDisplay(display); }
                        await WaitForLikedLayoutAsync(() => chatSelectedFiles.Length == 1 && !chatImporting && chatAttachmentPreview.Source != null);
                        ClearChatAttachments();
                    });
                    await WithChatClipboardFixtureAsync(selection, "x-special/gnome-copied-files", list, async () =>
                    { await PasteChatAsync(); if (chatSelectedFiles.Single() != path) throw new InvalidOperationException("Native file clipboard did not preserve the Unicode URI."); ClearChatAttachments(); });
                    await WithChatClipboardFixtureAsync(selection, "UTF8_STRING", list, async () =>
                    {
                        chatInput.Text = "До вставки"; chatInput.SelectAll(); await PasteChatAsync();
                        if (!chatInput.Text.StartsWith("copy\n")) throw new InvalidOperationException("Plain-text paste failed.");
                        chatInput.Undo(); if (chatInput.Text != "До вставки") throw new InvalidOperationException("Text paste lost undo.");
                    });
                }
                finally { File.Delete(list); }
            }
        }
        finally { chatClipboardRead = oldRead; chatInput.ClipboardService = oldClipboard; ClearChatAttachments(); chatInput.Text = ""; File.Delete(path); }
    }
    private async Task WithChatClipboardFixtureAsync(string selection, string format, string path, Func<Task> check)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(MainWindow).Assembly.Location);
        foreach (var value in new[] { "--chat-clipboard-fixture", selection, format, path }) start.ArgumentList.Add(value);
        using var owner = Process.Start(start)!;
        try
        {
            if (await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) != "CLIPBOARD_READY")
                throw new InvalidOperationException("Rich clipboard fixture failed to start.");
            await check();
        }
        finally { await owner.StandardInput.WriteLineAsync(); await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
