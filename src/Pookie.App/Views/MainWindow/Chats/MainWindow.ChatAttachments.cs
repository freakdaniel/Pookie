using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Pookie.App.Messages;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ChatAttachmentStaging chatAttachmentStaging = new();
    private ChatComposerSurface chatComposerSurface = null!;
    private bool chatImporting;
    private Func<CancellationToken, Task<ChatClipboardData?>> chatClipboardRead = token => ChatClipboard.ReadAsync(token);
    private bool CanAttachChat() => !disposed && !chatImporting && chatInput.IsEnabled && !chatSending && chatRecipient != null && !chatPending.ContainsKey(chatRecipient.Id);
    private void InitializeChatComposer()
    {
        chatInput.Commands.Unregister(StandardCommands.Paste);
        chatInput.Commands.Register(StandardCommands.Paste, () => Run(PasteChatAsync), CanAttachChat);
        Window.AllowDrop = true; chatComposer.AllowDrop = true;
        void RejectOutside(DragEventArgs args)
        { args.Accepted = false; args.Handled = true; args.Effect = DragDropEffects.None; SetChatDragging(false); }
        Window.DragEnter += RejectOutside; Window.DragOver += RejectOutside; Window.Drop += RejectOutside;
        chatComposer.DragEnter += UpdateChatDrag;
        chatComposer.DragOver += UpdateChatDrag;
        chatComposer.DragLeave += _ => SetChatDragging(false);
        chatComposer.Drop += args =>
        {
            var files = ChatDropFiles(args.Data);
            var accepted = CanAttachChat() && files.Length > 0;
            args.Accepted = accepted; args.Handled = true; args.Effect = accepted ? DragDropEffects.Copy : DragDropEffects.None;
            SetChatDragging(false);
            if (accepted) Run(() => AddChatFilesAsync(files));
        };
        chatComposer.Transitions = [Transition.Create(Control.BackgroundProperty, 170), Transition.Create(Control.BorderBrushProperty, 170)];
        chatComposer.MouseEnter += RefreshChatComposerSurface; chatComposer.MouseLeave += RefreshChatComposerSurface;
        chatInput.GotFocus += RefreshChatComposerSurface; chatInput.LostFocus += RefreshChatComposerSurface;
    }
    private static string[] ChatDropFiles(IDataObject data) => data.GetData(StandardDataFormats.StorageItems) is IEnumerable<string> paths ? paths.ToArray() :
        data.GetData("text/uri-list") is string uris ? ChatClipboard.FileUris(uris) : [];
    private void UpdateChatDrag(DragEventArgs args)
    {
        var accept = CanAttachChat() && (args.AllowedEffects & DragDropEffects.Copy) != 0 &&
            (args.Data.Contains(StandardDataFormats.StorageItems) || args.Data.Contains("text/uri-list"));
        args.Accepted = accept; args.Effect = accept ? DragDropEffects.Copy : DragDropEffects.None;
        SetChatDragging(accept);
    }
    private void SetChatDragging(bool value) { chatComposerSurface.SetDragging(value); RefreshChatComposerSurface(); }
    private void RefreshChatComposerSurface()
    {
        if (chatComposerSurface == null) return;
        var dragging = chatComposerSurface.Dragging;
        chatComposer.Background = TrackButtons.StateColor(Color.FromRgb(51, 51, 51), dragging ? ChatAccent : TrackButtons.OnSurface,
            dragging ? .18 : chatInput.IsFocused ? .075 : chatComposer.IsMouseOver ? .035 : 0);
        chatComposer.BorderBrush = dragging ? ChatAccent : chatInput.IsFocused ? Color.FromArgb(75, 170, 211, 246) : Color.Transparent;
    }
    private void PruneChatStaging() => chatAttachmentStaging.Keep(chatSelectedFiles.Concat(chatOutgoing.Values
        .Where(o => o.State != ChatDelivery.Sent).SelectMany(o => o.Files)));

    private async Task PasteChatAsync()
    {
        if (!CanAttachChat()) return;
        var account = me?.Id; var recipient = chatRecipient?.Id; var revision = chatRevision; var generation = chatAttachmentGeneration;
        chatImporting = true; RefreshChatComposer();
        try
        {
            var data = await chatClipboardRead(ChatReadToken());
            if (disposed || me?.Id != account || chatRecipient?.Id != recipient || revision != chatRevision || generation != chatAttachmentGeneration) return;
            if (data == null) { chatInput.Paste(); return; }
            var files = data.Files;
            if (data.Image != null) files = [chatAttachmentStaging.AddImage(data.Image, data.Extension)];
            await AddChatFilesCoreAsync(files, revision, generation);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!disposed && chatRecipient?.Id == recipient && revision == chatRevision) chatThreadStatus.Value = error.Message; }
        finally { chatImporting = false; PruneChatStaging(); if (!disposed) RefreshChatComposer(); }
    }
    private async Task AddChatFilesAsync(string[] files)
    {
        if (!CanAttachChat()) return;
        var revision = chatRevision; var generation = chatAttachmentGeneration;
        chatImporting = true; RefreshChatComposer();
        try { await AddChatFilesCoreAsync(files, revision, generation); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!disposed && revision == chatRevision) chatThreadStatus.Value = error.Message; }
        finally { chatImporting = false; PruneChatStaging(); if (!disposed) RefreshChatComposer(); }
    }
    private async Task AddChatFilesCoreAsync(string[] files, long revision, int generation)
    {
        var combined = chatSelectedFiles.Concat(files).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        await Task.Run(() =>
        {
            if (combined.Length > ChatMediaCrypto.MaxAttachments) throw new InvalidOperationException("За раз можно прикрепить до четырёх файлов.");
            long total = 0;
            foreach (var path in combined)
            {
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidOperationException("Можно прикрепить только существующие файлы.");
                var length = new FileInfo(path).Length;
                if (length > ChatMediaCrypto.MaxFileBytes || (total += length) > ChatMediaCrypto.MaxPayloadBytes - 65536)
                    throw new InvalidOperationException("Максимум 16 МБ на файл и 32 МБ на сообщение.");
            }
        });
        if (disposed || revision != chatRevision || generation != chatAttachmentGeneration || chatRecipient == null || !chatInput.IsEnabled) return;
        chatSelectedFiles = combined; chatSelectedGif = null; chatThreadStatus.Value = "";
        UpdateChatAttachments(); chatInput.Focus();
    }
}
