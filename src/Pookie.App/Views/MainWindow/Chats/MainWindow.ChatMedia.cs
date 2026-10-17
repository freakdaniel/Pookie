using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly SemaphoreSlim chatMediaGate = new(2, 2);
    private readonly Dictionary<Image, ChatGifAnimation> chatAnimations = [];
    private readonly Dictionary<string, ChatMediaPackage> chatMediaCache = [];
    private readonly Queue<string> chatMediaOrder = [];
    private long chatMediaBytes;

    private sealed record ChatMediaSummary(string Label, IImageSource? Thumbnail, Size? ImageSize, string Caption);
    private readonly Dictionary<string, ChatMediaSummary> chatMediaSummaries = [];
    private readonly Queue<string> chatMediaSummaryOrder = [];
    private readonly HashSet<string> chatMediaAutoRequested = [];
    private readonly Queue<string> chatMediaAutoOrder = [];
    private readonly Dictionary<string, Task<bool>> chatMediaLoads = [];
    private static string ChatMediaCacheId(Pookie.SoundCloud.ChatMessage message, long peer) => $"{message.SenderId}:{peer}:{message.Body}";
    private ChatMediaPackage? CachedChatMedia(Pookie.SoundCloud.ChatMessage message, long? peer = null) =>
        chatMediaCache.GetValueOrDefault(ChatMediaCacheId(message, peer ?? chatRecipient?.Id ?? 0));
    private ChatMediaSummary? ChatMediaSummaryFor(Pookie.SoundCloud.ChatMessage message, long peer) =>
        chatMediaSummaries.GetValueOrDefault(ChatMediaCacheId(message, peer));

    private void QueueChatMediaPreview(Pookie.SoundCloud.ChatMessage message, long peer)
    {
        if (ChatMediaReference.Parse(message.Body) == null || me == null ||
            !chatMediaAutoRequested.Add(ChatMediaCacheId(message, peer))) return;
        chatMediaAutoOrder.Enqueue(ChatMediaCacheId(message, peer));
        while (chatMediaAutoOrder.Count > 256) chatMediaAutoRequested.Remove(chatMediaAutoOrder.Dequeue());
        Run(async () => { await EnsureChatMediaAsync(message, peer); });
    }
    private async Task<bool> EnsureChatMediaAsync(Pookie.SoundCloud.ChatMessage message, long peer)
    {
        if (CachedChatMedia(message, peer) != null) return true;
        var key = ChatMediaCacheId(message, peer);
        if (chatMediaLoads.TryGetValue(key, out var running)) return await running;
        var task = FetchChatMediaAsync(message, peer);
        chatMediaLoads[key] = task;
        try { return await task; }
        finally { if (chatMediaLoads.GetValueOrDefault(key) == task) chatMediaLoads.Remove(key); }
    }
    private async Task<bool> FetchChatMediaAsync(Pookie.SoundCloud.ChatMessage message, long peer)
    {
        if (me == null || ChatMediaReference.Parse(message.Body) is not { } reference) return false;
        var account = me.Id; var token = ChatReadToken();
        ChatMediaPackage? downloaded = null; var cached = false; var entered = false;
        try
        {
            await chatMediaGate.WaitAsync(token);
            entered = true;
            ChatMediaPackage package;
            package = await chatMedia.DownloadAsync(reference, message.SenderId, message.SenderId == account ? peer : account, token);
            downloaded = package;
            if (disposed || token.IsCancellationRequested || me?.Id != account) { ClearMediaPackage(package); return false; }
            var thumbnail = package.Attachments.FirstOrDefault(a => a.MediaType.StartsWith("image/", StringComparison.Ordinal));
            // Decode while the package is owned here; eviction cannot zero its bytes underneath a decoder.
            IImageSource? preview = null;
            Size? imageSize = null;
            if (thumbnail != null)
            {
                var bytes = thumbnail.Data.ToArray();
                try
                {
                    (preview, imageSize) = await Task.Run(() => ((IImageSource?)PrepareChatImage(bytes, 72, 72), ChatImagePreview.ReadSize(bytes)), token);
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            }
            else if (package.GifUrl is { } gif)
            {
                try
                {
                    var bytes = await klipy.DownloadAsync(gif, token);
                    (preview, imageSize) = await Task.Run(() => ((IImageSource?)PrepareChatImage(bytes, 72, 72), ChatImagePreview.ReadSize(bytes)), token);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            if (disposed || token.IsCancellationRequested || me?.Id != account) { ClearMediaPackage(package); return false; }
            var key = ChatMediaCacheId(message, peer);
            AddChatMediaSummary(key, new(ChatPresentation.MediaLabel(package), preview, imageSize, package.Caption));
            AddChatMediaCache(key, package);
            cached = true;
            foreach (var row in ChatRows.Where(r => r.Message != null && ChatMediaCacheId(r.Message, chatRecipient?.Id ?? 0) == key).ToArray()) RenderChatMedia(row, package);
            foreach (var binding in chatInboxBindings) RefreshChatInboxPreview(binding.Key, binding.Value);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch
        {
            foreach (var row in ChatRows.Where(r => r.Message != null && ChatMediaCacheId(r.Message, chatRecipient?.Id ?? 0) == ChatMediaCacheId(message, peer)))
            {
                row.Open.IsVisible = true; row.Open.IsEnabled = true;
                row.MediaLoading = false; row.RefreshDelivery();
                VisualTree.Visit(row.Attachments, child => { if (child is ChatPhotoFrame frame) frame.Stop(); });
                row.Root.InvalidateMeasure();
            }
            return false;
        }
        finally
        {
            if (!cached && downloaded != null) ClearMediaPackage(downloaded);
            if (entered) chatMediaGate.Release();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(reference.Key);
        }
    }
    private void AddChatMediaSummary(string key, ChatMediaSummary summary)
    {
        if (!chatMediaSummaries.ContainsKey(key)) chatMediaSummaryOrder.Enqueue(key);
        chatMediaSummaries[key] = summary;
        while (chatMediaSummaryOrder.Count > 64) chatMediaSummaries.Remove(chatMediaSummaryOrder.Dequeue());
    }
    private async Task OpenChatMediaAsync(ChatBubbleRow row)
    {
        if (row.Message is not { } message || chatRecipient == null) return;
        row.Open.IsEnabled = false;
        if (await EnsureChatMediaAsync(message, chatRecipient.Id) && row.Message?.Id == message.Id && CachedChatMedia(message) is { } package)
            RenderChatMedia(row, package);
        if (row.Message?.Id == message.Id) row.Open.IsEnabled = true;
    }
    private void BindChatMediaPreview(ChatBubbleRow row)
    {
        if (row.Message is not { } message) return;
        if (CachedChatMedia(message) is { } package) { RenderChatMedia(row, package); return; }
        var local = ChatOutgoing(message);
        var summary = ChatMediaSummaryFor(message, chatRecipient?.Id ?? 0);
        row.Attachments.IsVisible = true;
        var size = summary?.ImageSize ?? local?.ImageSize ?? new Size(180, 120);
        var frame = CreateChatPhoto(size);
        var source = summary?.Thumbnail ?? local?.Thumbnail;
        row.MediaLoading = source == null;
        if (source is ImageSource pixels) frame.Show(pixels, ArtworkBackdrop.Create(pixels, .6));
        row.Attachments.Add(frame);
        row.Body.Text = local?.Draft ?? summary?.Caption ?? "";
        if (ChatMediaReference.Parse(row.Body.Text) != null) row.Body.Text = "";
        row.Body.IsVisible = !string.IsNullOrWhiteSpace(row.Body.Text);
        row.SetPhotoLayout(size);
        row.Open.IsVisible = false;
        frame.MouseDown += args =>
        {
            if (args.Button == MouseButton.Left && local == null && chatRecipient != null)
                Run(() => OpenChatMediaAsync(row));
        };
        if (local == null && chatRecipient != null) QueueChatMediaPreview(message, chatRecipient.Id);
    }

    private ChatPhotoFrame CreateChatPhoto(Size size) => new(size,
        () => !disposed && Window.IsActive && page.Value == Page.Chats);

    private void RenderChatMedia(ChatBubbleRow row, ChatMediaPackage package)
    {
        var follow = ChatIsAtBottom();
        StopChatAnimations(row.Attachments); row.Attachments.Clear(); row.Attachments.IsVisible = true;
        row.Body.Text = package.Caption;
        row.Body.IsVisible = !string.IsNullOrWhiteSpace(package.Caption);
        row.ResetMediaLayout();
        row.Open.IsVisible = false;
        var messageId = row.Message!.Id;
        var allPhotos = package.Attachments.Length > 0 && package.GifUrl == null &&
            package.Attachments.All(a => a.MediaType.StartsWith("image/", StringComparison.Ordinal) && ChatImagePreview.ReadSize(a.Data) != null);
        if (allPhotos) row.SetPhotoLayout(ChatImagePreview.ReadSize(package.Attachments[0].Data));
        else if (package.Attachments.Length == 0 && package.GifUrl != null) row.SetPhotoLayout(new Size(260, 190));
        for (var index = 0; index < package.Attachments.Length; index++)
        {
            var attachment = package.Attachments[index];
            var slot = index;
            var body = new StackPanel().Vertical().Spacing(8);
            Button SaveButton() => TrackButtons.Action(new TextBlock().Text($"{attachment.Name}\n{FormatChatFileSize(attachment.Data.Length)}   ↓").FontSize(12)
                .TextTrimming(TextTrimming.CharacterEllipsis), () => Run(() => SaveChatAttachmentAsync(messageId, slot)), TrackButtons.Tonal, 54).MaxWidth(360).Left();
            if (attachment.MediaType.StartsWith("image/", StringComparison.Ordinal) && ChatImagePreview.ReadSize(attachment.Data) is { } original)
            {
                var frame = CreateChatPhoto(original);
                if (!allPhotos) frame.Width = ChatImagePreview.Fit(original).Width;
                body.Add(frame);
                LoadChatImage(row, frame, attachment.Data, attachment.MediaType == "image/gif", () =>
                {
                    frame.Stop(); body.Clear(); body.Add(SaveButton()); row.ResetMediaLayout();
                });
            }
            else body.Add(SaveButton());
            row.Attachments.Add(body);
        }
        if (package.GifUrl is { } gif)
        {
            var frame = CreateChatPhoto(new(260, 190));
            row.Attachments.Add(frame);
            LoadKlipyImage(row, frame, gif);
        }
        row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
        if (follow) RequestChatBottom();
    }

    private async void LoadChatImage(ChatBubbleRow row, ChatPhotoFrame frame, byte[] bytes, bool animated, Action unavailable)
    {
        var message = row.Message; var revision = chatRevision;
        row.Load ??= CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = row.Load.Token;
        var image = frame.Picture;
        try
        {
            await chatMediaGate.WaitAsync(token);
            ImageSource? source; ImageSource? backdrop;
            try
            {
                var encoded = bytes.ToArray();
                try
                {
                    (source, backdrop) = await Task.Run(() =>
                    {
                        var decoded = PrepareChatImage(encoded);
                        return (decoded, decoded == null ? null : ArtworkBackdrop.Create(decoded, .6));
                    }, token);
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded); }
            }
            finally { chatMediaGate.Release(); }
            if (!disposed && !token.IsCancellationRequested && row.Message == message && chatRevision == revision && row.Attachments.IsAncestorOf(image))
            {
                if (source != null)
                {
                    frame.Show(source, backdrop);
                    row.MediaLoading = false; row.RefreshDelivery();
                    var follow = ChatIsAtBottom();
                    row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
                    if (follow) RequestChatBottom();
                }
                else unavailable();
                if (animated && source != null) StartChatAnimation(image, bytes);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!disposed && !token.IsCancellationRequested && row.Message == message && chatRevision == revision && row.Attachments.IsAncestorOf(image)) unavailable();
        }
    }
    private async void LoadKlipyImage(ChatBubbleRow row, ChatPhotoFrame frame, string url)
    {
        var message = row.Message; var revision = chatRevision;
        row.Load ??= CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = row.Load.Token;
        try
        {
            var bytes = await klipy.DownloadAsync(url, token);
            var (source, backdrop, size) = await Task.Run(() =>
            {
                var decoded = PrepareChatImage(bytes);
                return (decoded, decoded == null ? null : ArtworkBackdrop.Create(decoded, .6), ChatImagePreview.ReadSize(bytes));
            }, token);
            if (!disposed && !token.IsCancellationRequested && row.Message == message && chatRevision == revision && row.Attachments.IsAncestorOf(frame))
            {
                if (source == null) throw new InvalidDataException("GIF could not be decoded.");
                frame.IntrinsicSize = size ?? new(source.PixelWidth, source.PixelHeight);
                if (row.Content.PhotoMode) row.SetPhotoLayout(frame.IntrinsicSize);
                frame.Show(source, backdrop); StartChatAnimation(frame.Picture, bytes);
                row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!disposed && row.Message == message && row.Attachments.IsAncestorOf(frame))
            { frame.Stop(); row.Open.IsVisible = true; }
        }
    }
    private static ImageSource? PrepareChatImage(byte[] bytes, int maxWidth = 520, int maxHeight = 380) =>
        ChatImagePreview.Decode(bytes, maxWidth, maxHeight);
    private async Task SaveChatAttachmentAsync(string messageId, int index)
    {
        var message = chatMessageItems.FirstOrDefault(m => m.Id == messageId);
        if (message == null || CachedChatMedia(message) is not { } package || index < 0 || index >= package.Attachments.Length)
        { chatThreadStatus.Value = "Открой медиа заново перед сохранением."; return; }
        var attachment = package.Attachments[index]; var revision = chatRevision;
        var destination = await FileDialog.SaveFileAsync(new SaveFileDialogOptions { Owner = Window, Title = "Сохранить вложение", FileName = attachment.Name }, lifetime.Token);
        if (destination == null || disposed || revision != chatRevision || CachedChatMedia(message) != package) return;
        await File.WriteAllBytesAsync(destination, attachment.Data, lifetime.Token);
        
    }
    private void AddChatMediaCache(string id, ChatMediaPackage package)
    {
        var size = package.Attachments.Sum(a => (long)a.Data.Length);
        while (chatMediaOrder.Count > 0 && (chatMediaCache.Count >= 4 || chatMediaBytes + size > ChatMediaCrypto.MaxPayloadBytes))
        {
            var oldest = chatMediaOrder.Dequeue();
            if (!chatMediaCache.Remove(oldest, out var removed)) continue;
            chatMediaBytes -= removed.Attachments.Sum(a => (long)a.Data.Length);
            var affected = ChatRows.Where(r => r.Message != null && ChatMediaCacheId(r.Message, chatRecipient?.Id ?? 0) == oldest).Select(r => (Row: r, Message: r.Message!, WasBound: r.Bound)).ToArray();
            foreach (var pair in affected) pair.Row.Clear();
            ClearMediaPackage(removed);
            foreach (var pair in affected.Where(p => p.WasBound)) pair.Row.Bind(pair.Message);
            chatMessagesView.InvalidateMeasure();
        }
        chatMediaCache[id] = package; chatMediaOrder.Enqueue(id); chatMediaBytes += size;
    }
    private void ClearChatMediaCache(bool clearSummaries = false)
    {
        foreach (var row in ChatRows) row.Clear();
        foreach (var package in chatMediaCache.Values) ClearMediaPackage(package);
        chatMediaCache.Clear(); chatMediaOrder.Clear(); chatMediaBytes = 0; chatMediaLoads.Clear(); chatMediaAutoRequested.Clear(); chatMediaAutoOrder.Clear();
        if (clearSummaries) { chatMediaSummaries.Clear(); chatMediaSummaryOrder.Clear(); }
    }
    private static void ClearMediaPackage(ChatMediaPackage package)
    { foreach (var attachment in package.Attachments) System.Security.Cryptography.CryptographicOperations.ZeroMemory(attachment.Data); }
    private static string FormatChatFileSize(long size) => size >= 1024 * 1024 ? $"{size / (1024d * 1024):0.#} МБ" : $"{Math.Max(1, size / 1024)} КБ";
    private void StartChatAnimation(Image image, byte[] bytes)
    {
        if (!ChatGifAnimation.CanAnimate(bytes)) return;
        if (chatAnimations.Remove(image, out var old)) old.Dispose();
        while (chatAnimations.Count >= 6) { var first = chatAnimations.First(); first.Value.Dispose(); chatAnimations.Remove(first.Key); }
        chatAnimations[image] = new(image, bytes, () =>
        {
            if (disposed || !Window.IsActive || page.Value != Page.Chats || !image.IsVisible || image.ActualHeight <= 0) return false;
            var picker = chatGifsOpen.Value && chatGifResults.IsAncestorOf(image);
            if (!picker && !((FrameworkElement)chatColumns.Children[1]).IsVisible) return false;
            var viewport = picker ? chatGifScroll : chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
            return viewport == null || image.Bounds.Bottom > viewport.Bounds.Y && image.Bounds.Y < viewport.Bounds.Bottom;
        });
    }
    private void StopChatAnimations(FrameworkElement element)
    {
        VisualTree.Visit(element, child => { if (child is ChatPhotoFrame frame) frame.Stop(); if (child is Image image) { if (chatAnimations.Remove(image, out var animation)) animation.Dispose(); image.Source = null; } });
    }
}
