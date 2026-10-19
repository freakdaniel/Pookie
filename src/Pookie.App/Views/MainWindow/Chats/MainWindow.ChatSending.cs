using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private CancellationTokenSource? chatAccountLifetime;
    private int chatAttachmentGeneration;
    private Size? chatAttachmentImageSize;
    private CancellationTokenSource? chatAttachmentPreviewLoad;
    private async Task PickChatFilesAsync(bool documents = false, bool mediaOnly = false)
    {
        if (chatSending || chatRecipient == null || chatFilePickerOpen) return;
        var recipient = chatRecipient.Id; var account = me?.Id; var generation = chatAttachmentGeneration;
        CloseChatAttachMenu(true); chatFilePickerOpen = true;
        string[]? files;
        try { files = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions { Owner = Window, Title = mediaOnly ? "Фото или видео" : "Прикрепить файлы", Multiselect = true,
            Filters = mediaOnly ? [new FileFilter("Фото и видео", "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp", "*.mp4", "*.webm", "*.mov", "*.mkv", "*.avi")] : [] }, lifetime.Token); }
        finally { chatFilePickerOpen = false; chatAttachInteraction.Stop(); }
        if (disposed || chatRecipient?.Id != recipient || me?.Id != account || generation != chatAttachmentGeneration || files == null || files.Length == 0) return;
        await AddChatFilesAsync(files, documents);
    }
    private void ClearChatAttachments()
    {
        chatSelectedFiles = []; chatSelectedGif = null; chatDocumentFiles.Clear();
        PruneChatStaging();
        if (chatAttachmentStrip == null) return;
        UpdateChatAttachments();
    }
    private void UpdateChatAttachments()
    {
        chatDocumentFiles.IntersectWith(chatSelectedFiles);
        PruneChatStaging();
        chatAttachmentStrip.IsVisible = chatSelectedFiles.Length > 0 || chatSelectedGif != null;
        chatAttachmentLabel.Text = chatSelectedGif != null ? "GIF" : string.Join(", ", chatSelectedFiles.Select(Path.GetFileName));
        chatAttachmentPreview.Source = null;
        chatAttachmentImageSize = null;
        chatAttachmentPreviewLoad?.Cancel(); chatAttachmentPreviewLoad?.Dispose();
        chatAttachmentPreviewLoad = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = chatAttachmentPreviewLoad.Token;
        var generation = ++chatAttachmentGeneration; var path = chatSelectedFiles.FirstOrDefault(); var gif = chatSelectedGif;
        Run(async () =>
        {
            byte[]? bytes = null;
            try
            {
                var video = path != null && ChatMediaType(path).StartsWith("video/", StringComparison.Ordinal);
                if (path != null && (video || ChatMediaType(path).StartsWith("image/", StringComparison.Ordinal)))
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, true);
                    if (stream.Length > ChatMediaCrypto.MaxFileBytes) return;
                    bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes, token);
                }
                else if (gif != null) bytes = await klipy.DownloadAsync(gif.PreviewUrl, token);
                if (bytes == null) return;
                ImageSource? source; Size? size;
                if (video)
                {
                    ImageSource? poster = null;
                    var info = await ChatVideoProbe.ReadAsync(bytes, token, image => poster = image);
                    source = poster; size = info == null ? null : new Size(info.Width, info.Height);
                }
                else (source, size) = await Task.Run(() => (PrepareChatImage(bytes, 72, 72), ChatImagePreview.ReadSize(bytes)), token);
                if (!disposed && !token.IsCancellationRequested && generation == chatAttachmentGeneration)
                { chatAttachmentPreview.Source = source; chatAttachmentImageSize = size; }
            }
            catch (Exception) { }
            finally { if (bytes != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        });
        RefreshChatComposer();
    }

    private bool ChatWriteCurrent(long account, CancellationToken token) => !disposed && chatAccountId == account && me?.Id == account &&
        api.Session != null && !token.IsCancellationRequested && chatAccountLifetime?.Token == token;

    private async Task SendChatAsync()
    {
        if (!chatSend.IsEnabled || chatSending || me == null || api.Session == null || chatRecipient == null || chatLocalStore == null) return;
        var user = me; var recipient = chatRecipient;
        var store = chatLocalStore;
        var draft = chatInput.Text; var files = chatSelectedFiles; var gif = chatSelectedGif;
        var documents = chatDocumentFiles.ToHashSet(chatDocumentFiles.Comparer);
        var token = chatAccountLifetime?.Token ?? lifetime.Token;
        var outgoing = new ChatOutgoingItem(new("local:" + Guid.NewGuid().ToString("N"), user.Id, draft.Trim(), DateTimeOffset.UtcNow), draft, files, gif)
        {
            Thumbnail = chatAttachmentPreview.Source, ImageSize = chatAttachmentImageSize, Documents = documents,
            BeforeSendIds = chatMessages.Select(m => m.Id).ToHashSet(),
            DisplayOrder = new[] { DateTimeOffset.UtcNow, chatMessages.Select(ChatOrder).DefaultIfEmpty(DateTimeOffset.MinValue).Max().AddTicks(1) }.Max()
        };
        chatOutgoing[recipient.Id] = outgoing; chatMessageEntrances.Add(outgoing.Message.Id);
        if (!chatConversations.Any(c => c.Recipient.Id == recipient.Id))
            chatConversations.Insert(0, new($"{Math.Min(user.Id, recipient.Id)}:{Math.Max(user.Id, recipient.Id)}", recipient, null, true));
        chatSending = true; RefreshChatComposer();
        chatThreadStatus.Value = ""; RefreshChatTimeline(); RenderChatInbox(); RequestChatBottom();
        try
        {
            string body;
            if (files.Length > 0 || gif != null)
            {
                var attachments = new List<ChatAttachment>();
                try
                {
                    foreach (var path in files)
                    {
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, useAsync: true);
                        if (input.Length > ChatMediaCrypto.MaxFileBytes || attachments.Sum(a => a.Data.Length) + input.Length > ChatMediaCrypto.MaxPayloadBytes - 65536)
                            throw new InvalidOperationException("Вложения слишком большие.");
                        var bytes = new byte[(int)input.Length];
                        await input.ReadExactlyAsync(bytes, token);
                        if (input.ReadByte() != -1) throw new InvalidOperationException("Файл изменился во время чтения. Выбери его заново.");
                        var name = Path.GetFileName(path);
                        attachments.Add(new(name, ChatMediaType(name), bytes, documents.Contains(path)));
                    }
                    var package = new ChatMediaPackage(user.Id, recipient.Id, draft, attachments.ToArray(), gif?.Url);
                    await PrepareOutgoingChatMediaAsync(outgoing, package, recipient.Id, token);
                    var reference = await chatMedia.UploadAsync(package, token);
                    body = reference.Code;
                    if (ChatWriteCurrent(user.Id, token)) CommitOutgoingChatMedia(outgoing, package, recipient.Id, body);
                }
                finally { foreach (var item in attachments) System.Security.Cryptography.CryptographicOperations.ZeroMemory(item.Data); }
                if (body.Length > 36) throw new InvalidOperationException("Код вложения превышает 36 символов.");
            }
            else body = draft.Trim();
            if (!ChatWriteCurrent(user.Id, token)) return;
            outgoing.Message = outgoing.Message with { Body = body }; RefreshChatTimeline();
            var label = files.Length > 0 || gif != null ? "Медиа" : draft[..Math.Min(draft.Length, 255)];
            var pending = new ChatPending(recipient.Id, body, label, DateTimeOffset.UtcNow, true, outgoing.BeforeSendIds.TakeLast(64).ToArray());
            chatPending[recipient.Id] = pending;
            // Commit before POST: a process crash must never cause an automatic replay.
            try { store.Save(new(chatDrafts.Where(p => p.Value.Length > 0).TakeLast(50).ToDictionary(), chatPending.Values.ToArray(), chatUnread.States.TakeLast(500).ToDictionary())); }
            catch { chatPending.Remove(recipient.Id); throw; }
            await SendPendingChatAsync(pending, user.Id, recipient, token);
            if (ChatWriteCurrent(user.Id, token) && !chatPending.ContainsKey(recipient.Id))
            {
                if (chatDrafts.GetValueOrDefault(recipient.Id) == draft) chatDrafts.Remove(recipient.Id);
                if (chatRecipient?.Id == recipient.Id)
                {
                    if (chatInput.Text == draft) { chatRenderingDraft = true; chatInput.Text = ""; chatRenderingDraft = false; }
                    ClearChatAttachments();
                }
                SaveChatState();
                if (gif != null) { try { await klipy.SharedAsync(gif.Slug, token); } catch (HttpRequestException) { } }
            }
        }
        catch (OperationCanceledException) when (!ChatWriteCurrent(user.Id, token)) { }
        catch (Exception error)
        {
            if (ChatWriteCurrent(user.Id, token))
            {
                outgoing.State = ChatDelivery.Failed;
                outgoing.Error = chatPending.ContainsKey(recipient.Id) ? "Отправка не подтверждена. Нажми, чтобы проверить." : "Не удалось отправить. Нажми, чтобы повторить.";
                Pookie.Logging.AppLog.Failure("Pookie.Chats", "Не удалось отправить сообщение", error);
                RefreshChatTimeline(); RenderChatInbox();
            }
            if (error is SoundCloudException { StatusCode: 401 }) throw;
        }
        finally
        {
            PruneChatStaging();
            if (ChatWriteCurrent(user.Id, token)) { chatSending = false; RefreshChatComposer(); RefreshChatTimeline(); }
        }
    }

    private async Task SendPendingChatAsync(ChatPending pending, long account, SoundCloudUser recipient, CancellationToken token)
    {
        var conversation = await api.SendMessageAsync(account, recipient.Id, pending.Body, token);
        if (!ChatWriteCurrent(account, token)) return;
        if (conversation.Recipient.Id != recipient.Id || conversation.LastMessage is not { } confirmed || confirmed.SenderId != account || confirmed.Body != pending.Body)
            throw new SoundCloudException("SoundCloud не подтвердил содержимое отправленного сообщения.");
        chatPending.Remove(recipient.Id);
        if (chatOutgoing.TryGetValue(recipient.Id, out var outgoing))
        { outgoing.State = ChatDelivery.Sent; outgoing.Error = null; outgoing.Message = outgoing.Message with { Body = confirmed.Body, SentAt = confirmed.SentAt }; RefreshChatTimeline(); }
        var index = chatConversations.FindIndex(c => c.Recipient.Id == recipient.Id);
        if (index >= 0) chatConversations[index] = conversation; else chatConversations.Insert(0, conversation);
        chatConversations.Sort((a, b) => Nullable.Compare(b.LastMessage?.SentAt, a.LastMessage?.SentAt));
        if (chatRecipient?.Id == recipient.Id)
        {
            // POST last_message and GET messages can have different IDs/serialization.
            // Only the thread endpoint supplies displayed rows; never insert a second representation.
            chatThreadRefreshRequested = true;
            await RefreshChatThreadAsync();
        }
        RenderChatInbox(); SaveChatState();
    }

    private void ReconcilePendingChat()
    {
        if (chatRecipient == null || me == null || !chatPending.TryGetValue(chatRecipient.Id, out var pending)) return;
        if (chatMessages.Any(m => m.SenderId == me.Id && m.Body == pending.Body && m.SentAt >= pending.CreatedAt.AddSeconds(-5) &&
            !(chatOutgoing.GetValueOrDefault(chatRecipient.Id)?.BeforeSendIds.Contains(m.Id) ?? pending.BeforeIds?.Contains(m.Id) ?? false)))
        {
            chatPending.Remove(chatRecipient.Id); chatOutgoing.Remove(chatRecipient.Id); chatDrafts.Remove(chatRecipient.Id);
            chatRenderingDraft = true; chatInput.Text = ""; chatRenderingDraft = false; ClearChatAttachments(); SaveChatState();
        }
        RefreshChatTimeline();
    }
    private async Task CheckPendingChatAsync()
    {
        if (chatSending || chatRecipient == null || me == null || api.Session == null || !chatPending.TryGetValue(chatRecipient.Id, out var pending)) return;
        var recipient = chatRecipient; var session = api.Session; var account = me.Id;
        await RefreshChatThreadAsync();
        if (disposed || api.Session != session || chatRecipient?.Id != recipient.Id || !chatPending.ContainsKey(recipient.Id)) return;
        if (!await MessageBox.AskYesNoAsync("Повторить отправку?", detail: "В загруженной переписке сообщение не найдено. Если SoundCloud уже принял его, повтор может создать дубль.", owner: Window)) return;
        if (api.Session != session || me?.Id != account || chatRecipient?.Id != recipient.Id) return;
        chatSending = true; RefreshChatComposer();
        if (chatOutgoing.TryGetValue(recipient.Id, out var retry)) { retry.State = ChatDelivery.Sending; retry.Error = null; RefreshChatTimeline(); }
        try { await SendPendingChatAsync(pending, account, recipient, chatAccountLifetime?.Token ?? lifetime.Token); }
        catch (OperationCanceledException) when (disposed || me?.Id != account || chatAccountLifetime?.IsCancellationRequested == true) { }
        catch (Exception)
        {
            if (api.Session == session && chatOutgoing.TryGetValue(recipient.Id, out var failed))
            { failed.State = ChatDelivery.Failed; failed.Error = "Отправка не подтверждена. Нажми, чтобы проверить."; RefreshChatTimeline(); }
        }
        finally { if (!disposed && chatAccountId == account) { chatSending = false; RefreshChatComposer(); } }
    }

    private void DiscardPendingChat()
    {
        if (chatSending || chatRecipient == null) return;
        chatPending.Remove(chatRecipient.Id); chatOutgoing.Remove(chatRecipient.Id); SaveChatState(); RefreshChatTimeline();
        PruneChatStaging();
        chatThreadStatus.Value = "";
        RefreshChatComposer();
    }

    private static string ChatMediaType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif",
        ".mp4" => "video/mp4", ".webm" => "video/webm", ".mov" => "video/quicktime", ".mkv" => "video/x-matroska", ".avi" => "video/x-msvideo",
        ".mp3" => "audio/mpeg", ".ogg" => "audio/ogg", _ => "application/octet-stream"
    };
    private static string ChatActionError(Exception error) => error is InvalidDataException ? "Не удалось прочитать данные вложения." :
        error is IOException ? "Не удалось прочитать или сохранить файл." : FriendlyError(error);
}
