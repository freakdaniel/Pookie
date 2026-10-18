using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatMusicComposerAsync()
    {
        const string url = "https://soundcloud.com/fixture/another-track";
        var recognizer = chatMusicEditor!;
        var draft = chatInput.Text; var originalTrack = current?.Id; var originalGeneration = playGeneration;
        var text = url + "\nОгненный ваще";
        chatInput.Focus(); chatInput.SelectAll(); chatInput.ReplaceSelection(text);
        await WaitForLikedLayoutAsync(() => recognizer.Count == 1 && recognizer.ChipBounds[0].Width > 50 && !chatComposerSurface.IsResizing);
        if (chatInput.Text != text || chatDrafts.GetValueOrDefault(chatRecipient!.Id) != text || chatComposer.ActualHeight > 120)
            throw new InvalidOperationException("Recognizing a music link altered the draft or inflated the composer.");
        foreach (var position in new[] { url.Length, url.Length / 2, 0 })
        {
            chatInput.Select(position, 0);
            ((ITextInputClient)chatInput).HandleTextInput(new("рядом"));
            var expected = position == 0 ? "рядом " + text : url + " рядом\nОгненный ваще";
            if (chatInput.Text != expected || recognizer.Count != 1)
                throw new InvalidOperationException("Typing beside or inside a recognized link changed its URL.");
            ((ITextInputClient)chatInput).HandleTextInput(new("!"));
            if (recognizer.Count != 1 || !chatInput.Text.Contains(url, StringComparison.Ordinal))
                throw new InvalidOperationException("Continuing to type beside a recognized link corrupted it.");
            chatInput.SelectAll(); chatInput.ReplaceSelection(text);
        }
        chatInput.Select(10, 8);
        ((ITextInputClient)chatInput).HandleTextInput(new("замена"));
        if (chatInput.Text != "замена\nОгненный ваще" || recognizer.Count != 0)
            throw new InvalidOperationException("Replacing part of a recognized link left a broken URL.");
        chatInput.Undo();
        if (chatInput.Text != text || recognizer.Count != 1)
            throw new InvalidOperationException("Undo failed to restore the complete recognized link.");
        var oldRead = chatClipboardRead; var oldClipboard = chatInput.ClipboardService;
        try
        {
            chatClipboardRead = _ => Task.FromResult<Platform.ChatClipboardData?>(null);
            chatInput.ClipboardService = new MusicComposerClipboard("из буфера");
            chatInput.Select(url.Length, 0); await PasteChatAsync();
            if (chatInput.Text != url + " из буфера\nОгненный ваще" || recognizer.Count != 1)
                throw new InvalidOperationException("Pasting beside a recognized link changed its URL.");
            chatInput.Undo();
            if (chatInput.Text != text) throw new InvalidOperationException("Pasting beside a link lost undo.");
            const string pendingUrl = "https://soundcloud.com/fixture/pending-track";
            chatInput.ClipboardService.TrySetText(pendingUrl);
            chatInput.SelectAll(); await PasteChatAsync();
            ((ITextInputClient)chatInput).HandleTextInput(new("сразу"));
            if (chatInput.Text != pendingUrl + " сразу")
                throw new InvalidOperationException("Typing immediately after paste changed a link before metadata arrived.");
            chatInput.SelectAll(); chatInput.ReplaceSelection(text);
        }
        finally { chatClipboardRead = oldRead; chatInput.ClipboardService = oldClipboard; }
        chatInput.Select(url.Length, 0);
        var composition = (ITextCompositionClient)chatInput;
        composition.HandleTextCompositionStart(new());
        composition.HandleTextCompositionUpdate(new("日本語"));
        composition.HandleTextCompositionEnd(new("日本語"));
        if (chatInput.Text != url + " 日本語\nОгненный ваще" || recognizer.Count != 1)
            throw new InvalidOperationException("IME composition changed a recognized URL.");
        chatInput.SelectAll(); chatInput.ReplaceSelection(text);
        chatInput.Select(0, url.Length);
        if (chatInput.SelectedText != url) throw new InvalidOperationException("The music chip changed the editor's source/clipboard selection.");
        chatInput.CaretPosition = url.Length;
        chatInput.Focus();
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        router.GetMethod("KeyDown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [Window, new KeyEventArgs(Key.Backspace, 0)]);
        if (chatInput.Text != "\nОгненный ваще" || recognizer.Count != 0)
            throw new InvalidOperationException($"Backspace did not delete the complete link: text={chatInput.Text}, chips={recognizer.Count}, caret={chatInput.CaretPosition}, selection={chatInput.SelectionLength}, focused={chatInput.IsFocused}.");
        chatInput.Undo();
        await WaitForLikedLayoutAsync(() => chatInput.Text == text && recognizer.Count == 1 && recognizer.ChipBounds[0].Width > 50);
        chatInput.Select(text.Length, 0);
        await WaitForLoginFrameAsync(); CaptureUiPreview("chats-composer-music-chip");
        var chip = recognizer.ChipBounds[0]; var point = new Point(chip.X + 12, chip.Y + chip.Height / 2);
        if (Math.Abs(chatInput.GetCharRectInWindow(url.Length).X - chip.Right) > 3)
            throw new InvalidOperationException("The recognized link left part of its original URL visible after the title.");
        MoveActionPointer(point); PressActionPointer(point, true); PressActionPointer(point, false);
        await WaitForLikedLayoutAsync(() => page.Value == Page.Track && !navigationHistory[navigationIndex].Pending);
        if (current?.Id != originalTrack || playGeneration != originalGeneration || trackDetailState?.Track.Id != 101)
            throw new InvalidOperationException("Clicking the composer chip started playback or opened the wrong page.");
        await MoveNavigationAsync(-1); chatTimer.Stop();
        await WaitForLikedLayoutAsync(() => page.Value == Page.Chats && chatInput.Text == text && recognizer.Count == 1);
        await SendChatAsync();
        if (!chatMessages.Any(message => message.SenderId == me!.Id && message.Body == text) || chatInput.Text != "" ||
            chatPending.ContainsKey(chatRecipient!.Id) || chatOutgoing.GetValueOrDefault(chatRecipient.Id)?.State == ChatDelivery.Failed)
            throw new InvalidOperationException("The recognized link or caption was lost when sending the source message.");
        chatInput.Text = draft;
    }

    private sealed class MusicComposerClipboard(string value) : IClipboardService
    {
        public bool TrySetText(string text) { value = text; return true; }
        public bool TryGetText(out string text) { text = value; return true; }
    }
}
