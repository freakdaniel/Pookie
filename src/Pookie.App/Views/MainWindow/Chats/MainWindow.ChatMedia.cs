using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly SemaphoreSlim chatMediaGate = new(2, 2);
    private readonly Dictionary<Image, ChatGifAnimation> chatAnimations = [];
    private readonly Dictionary<string, ChatMediaPackage> chatMediaCache = [];
    private readonly LinkedList<string> chatMediaOrder = [];
    private long chatMediaBytes;

    private sealed record ChatMediaPhoto(Size Size, ImageSource Source, ImageSource Blurred);
    private sealed record ChatMediaSummary(string Label, IImageSource? Thumbnail, Size? ImageSize, string Caption, ChatMediaPhoto?[]? Photos = null, MediaFile[]? Files = null);
    private readonly Dictionary<string, ChatMediaSummary> chatMediaSummaries = [];
    private readonly Queue<string> chatMediaSummaryOrder = [];
    private readonly HashSet<string> chatMediaAutoRequested = [];
    private readonly Queue<string> chatMediaAutoOrder = [];
    private readonly Dictionary<string, Task<bool>> chatMediaLoads = [];
    private static string ChatMediaCacheId(Pookie.SoundCloud.ChatMessage message, long peer) => message.Id.StartsWith("local:", StringComparison.Ordinal) && ChatMediaReference.Parse(message.Body) == null
        ? $"{message.SenderId}:{peer}:{message.Id}" : $"{message.SenderId}:{peer}:{message.Body}";
    private ChatMediaPackage? CachedChatMedia(Pookie.SoundCloud.ChatMessage message, long? peer = null)
    {
        var key = ChatMediaCacheId(message, peer ?? chatRecipient?.Id ?? 0);
        if (!chatMediaCache.TryGetValue(key, out var package)) return null;
        chatMediaOrder.Remove(key); chatMediaOrder.AddLast(key); return package;
    }
    private ChatMediaSummary? ChatMediaSummaryFor(Pookie.SoundCloud.ChatMessage message, long peer) =>
        chatMediaSummaries.GetValueOrDefault(ChatMediaCacheId(message, peer));

    private void QueueChatMediaPreview(Pookie.SoundCloud.ChatMessage message, long peer)
    {
        if (page.Value != Page.Chats || HasDecodedChatMedia(message, peer) || ChatMediaReference.Parse(message.Body) == null || me == null ||
            !chatMediaAutoRequested.Add(ChatMediaCacheId(message, peer))) return;
        chatMediaAutoOrder.Enqueue(ChatMediaCacheId(message, peer));
        while (chatMediaAutoOrder.Count > 256) chatMediaAutoRequested.Remove(chatMediaAutoOrder.Dequeue());
        Run(async () => { if (await EnsureChatMediaAsync(message, peer)) chatMediaAutoRequested.Remove(ChatMediaCacheId(message, peer)); });
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
        var account = me.Id; var token = chatAccountLifetime?.Token ?? lifetime.Token;
        var cacheId = ChatMediaCacheId(message, peer);
        ChatMediaPackage? downloaded = null; var cached = false; var entered = false;
        try
        {
            await chatMediaGate.WaitAsync(token);
            entered = true;
            ChatMediaPackage package;
            package = await chatMedia.DownloadAsync(reference, message.SenderId, message.SenderId == account ? peer : account, token, async early =>
            {
                if (early.Files.Length == 0 && early.GifUrl != null) return;
                var photos = await Task.Run(() => early.Images.Select(image =>
                {
                    if (image == null) return null;
                    var source = ImageSource.FromBgraPixels(image.ThumbWidth, image.ThumbHeight, image.Pixels.AsSpan());
                    return new ChatMediaPhoto(new(image.Width, image.Height), source, ArtworkBackdrop.Create(source, 1) ?? source);
                }).ToArray(), token);
                if (disposed || token.IsCancellationRequested || me?.Id != account) return;
                var first = photos.FirstOrDefault(p => p != null);
                var description = new ChatMediaPackage(early.SenderId, early.RecipientId, early.Caption,
                    early.Files.Select(f => new ChatAttachment(f.Name, f.Type, [], f.AsFile, f.Video)).ToArray(), early.GifUrl);
                AddChatMediaSummary(cacheId, new(ChatPresentation.MediaLabel(description), first?.Source, first?.Size, early.Caption, photos, early.Files));
                foreach (var row in ChatRows.Where(r => r.Bound && r.Message != null && ChatMediaCacheId(r.Message, chatRecipient?.Id ?? 0) == cacheId).ToArray())
                { row.FollowMediaResize = ChatIsAtBottom(); row.Content.AnimateMediaChange(); StopChatAnimations(row.Attachments); row.Attachments.Clear(); BindChatMediaPreview(row); }
                foreach (var binding in chatInboxBindings) RefreshChatInboxPreview(binding.Key, binding.Value);
            });
            downloaded = package;
            if (disposed || token.IsCancellationRequested || me?.Id != account) { ClearMediaPackage(package); return false; }
            var summary = await PrepareChatMediaSummaryAsync(package, token);
            if (package.Attachments.Length == 0 && package.GifUrl is { } gif)
            {
                try
                {
                    var bytes = await klipy.DownloadAsync(gif, token);
                    var decoded = await Task.Run(() => ((IImageSource?)PrepareChatImage(bytes, 72, 72), ChatImagePreview.ReadSize(bytes)), token);
                    summary = summary with { Thumbnail = decoded.Item1, ImageSize = decoded.Item2 };
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            if (disposed || token.IsCancellationRequested || me?.Id != account) return false;
            var key = ChatMediaCacheId(message, peer);
            AddChatMediaSummary(key, summary);
            AddChatMediaCache(key, package);
            cached = true;
            foreach (var row in ChatRows.Where(r => r.Bound && r.Message != null && ChatMediaCacheId(r.Message, chatRecipient?.Id ?? 0) == key).ToArray()) RenderChatMedia(row, package);
            foreach (var binding in chatInboxBindings) RefreshChatInboxPreview(binding.Key, binding.Value);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch
        {
            foreach (var row in ChatRows.Where(r => r.Bound && r.Message != null && ChatMediaCacheId(r.Message, chatRecipient?.Id ?? 0) == ChatMediaCacheId(message, peer)))
            {
                RenderChatMediaRetry(row);
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
    private static async Task<ChatMediaSummary> PrepareChatMediaSummaryAsync(ChatMediaPackage package, CancellationToken token)
    {
        var videoPosters = new Dictionary<int, ImageSource>();
        for (var index = 0; index < package.Attachments.Length; index++)
        {
            var attachment = package.Attachments[index];
            if (!attachment.AsFile && attachment.MediaType.StartsWith("video/", StringComparison.Ordinal))
            {
                var slot = index;
                var info = await ChatVideoProbe.ReadAsync(attachment.Data, token, source => videoPosters[slot] = source);
                package.Attachments[index] = attachment with { Video = info ?? attachment.Video };
            }
        }
        return await Task.Run(() =>
    {
        var photos = package.Attachments.Select((attachment, index) =>
        {
            if (!attachment.AsFile && attachment.Video is { Valid: true } video)
            {
                var poster = videoPosters.GetValueOrDefault(index) ?? ImageSource.FromBgraPixels(video.ThumbWidth, video.ThumbHeight, video.Pixels);
                return new ChatMediaPhoto(new(video.Width, video.Height), poster, ArtworkBackdrop.Create(poster, 1) ?? poster);
            }
            if (attachment.AsFile || !attachment.MediaType.StartsWith("image/", StringComparison.Ordinal) || ChatImagePreview.ReadSize(attachment.Data) is not { } size ||
                PrepareChatImage(attachment.Data, 72, 72) is not { } source) return null;
            return new ChatMediaPhoto(size, source, ArtworkBackdrop.Create(source, 1) ?? source);
        }).ToArray();
        var first = photos.FirstOrDefault(photo => photo != null);
        return new ChatMediaSummary(ChatPresentation.MediaLabel(package), first?.Source, first?.Size, package.Caption, photos,
            package.Attachments.Select(a => new MediaFile(a.Name, a.MediaType, a.Data.Length, a.AsFile, a.Video)).ToArray());
    }, token);
    }

    private bool HasDecodedChatMedia(Pookie.SoundCloud.ChatMessage message, long peer)
    {
        var summary = ChatMediaSummaryFor(message, peer);
        if (summary?.Files is { Length: > 0 } files && files.All(f => !f.AsFile && f.Video is { Valid: true })) return true;
        return summary?.Photos is { Length: > 0 } photos && photos.All(p => p != null) &&
            Enumerable.Range(0, photos.Length).All(index => ChatDecoded(ChatMediaCacheId(message, peer), index) != null);
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
    private void RenderChatMediaRetry(ChatBubbleRow row)
    {
        if (row.Message is not { } message) return;
        var files = ChatMediaSummaryFor(message, chatRecipient?.Id ?? 0)?.Files;
        row.FollowMediaResize = ChatIsAtBottom(); row.Content.AnimateMediaChange();
        StopChatAnimations(row.Attachments); row.Attachments.Clear(); row.Attachments.IsVisible = true;
        row.ResetMediaLayout(); row.Open.IsVisible = false; row.MediaResolved = false;
        var descriptions = files is { Length: > 0 } ? files : [new MediaFile("Вложение", "application/octet-stream", 0)];
        foreach (var file in descriptions)
            row.Attachments.Add(new ChatFileCard(file.Name, "Не удалось загрузить · повторить", message.SenderId == me?.Id, false,
                () => { if (row.Message?.Id == message.Id) Run(() => OpenChatMediaAsync(row)); }));
        row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
    }
    private void BindChatMediaPreview(ChatBubbleRow row)
    {
        if (row.Message is not { } message) return;
        if (CachedChatMedia(message) is { } package) { RenderChatMedia(row, package); return; }
        var local = ChatOutgoing(message);
        var summary = ChatMediaSummaryFor(message, chatRecipient?.Id ?? 0);
        row.MediaKey = ChatMediaCacheId(message, chatRecipient?.Id ?? 0); row.MediaResolved = false;
        row.Attachments.IsVisible = true;
        var size = summary?.ImageSize ?? local?.ImageSize ?? new Size(240, 180);
        var source = summary?.Thumbnail ?? local?.Thumbnail;
        row.MediaLoading = summary == null && source == null;
        void OpenFrame(ChatPhotoFrame frame)
        {
            frame.MouseDown += args =>
            {
                if (args.Button == MouseButton.Left && local == null && chatRecipient != null) Run(() => OpenChatMediaAsync(row));
            };
        }
        var allPhotos = summary?.Photos is { Length: > 0 } photos && photos.All(p => p != null);
        var descriptions = summary?.Files ?? (local?.Files.Any(path => local.Documents.Contains(path) || ChatMediaType(path).StartsWith("video/", StringComparison.Ordinal)) == true
            ? local.Files.Select(path => new MediaFile(Path.GetFileName(path), ChatMediaType(path), 0, local.Documents.Contains(path))).ToArray() : null);
        if (descriptions is { Length: > 0 } files)
        {
            for (var index = 0; index < files.Length; index++)
            {
                var file = files[index];
                if (!file.AsFile && file.Type.StartsWith("video/", StringComparison.Ordinal))
                {
                    row.Attachments.Add(CreateChatVideoFrame(row, file.Video, file.Length, index));
                }
                else if (!file.AsFile && summary?.Photos is { } imagePreviews && index < imagePreviews.Length && imagePreviews[index] is { } photo)
                {
                    var photoFrame = CreateChatPhoto(photo.Size);
                    if (!allPhotos) photoFrame.Width = ChatImagePreview.Fit(photo.Size).Width;
                    if (ChatDecoded(row.MediaKey!, index) is { } decoded) photoFrame.ShowImmediate(decoded.Source, decoded.Backdrop);
                    else photoFrame.ShowPreview(photo.Blurred, ArtworkBackdrop.Create(photo.Source, .6));
                    row.Attachments.Add(photoFrame); OpenFrame(photoFrame);
                }
                else
                {
                    var slot = index;
                    var pendingFile = new ChatFileCard(file.Name, summary == null && local != null ? "Подготовка…" : FormatChatFileSize(file.Length), message.SenderId == me?.Id,
                        !file.AsFile && file.Type.StartsWith("video/", StringComparison.Ordinal), () => Run(() => ActivateChatAttachmentAsync(message.Id, slot)));
                    row.Attachments.Add(pendingFile);
                }
            }
        }
        else
        {
            var frame = CreateChatPhoto(size);
            if (source is ImageSource pixels) frame.ShowPreview(ArtworkBackdrop.Create(pixels, 1) ?? pixels, ArtworkBackdrop.Create(pixels, .6));
            row.Attachments.Add(frame); OpenFrame(frame);
        }
        row.Body.Text = local?.Draft ?? summary?.Caption ?? "";
        if (ChatMediaReference.Parse(row.Body.Text) != null) row.Body.Text = "";
        row.Body.IsVisible = !string.IsNullOrWhiteSpace(row.Body.Text);
        if (descriptions is not { Length: > 0 } || allPhotos || descriptions.All(f => !f.AsFile && f.Type.StartsWith("video/", StringComparison.Ordinal))) row.SetPhotoLayout(size, descriptions?.Any(f => f.Type.StartsWith("video/", StringComparison.Ordinal)) == true);
        else row.ResetMediaLayout();
        row.Open.IsVisible = false;
        if (local == null && chatRecipient != null) QueueChatMediaPreview(message, chatRecipient.Id);
    }

    private ChatVideoFrame CreateChatVideoFrame(ChatBubbleRow row, ChatVideoInfo? info, int bytes, int index)
    {
        var message = row.Message!; var peer = chatRecipient?.Id; var generation = row.MediaGeneration;
        var frame = new ChatVideoFrame(info, bytes,
            () => !disposed && Window.IsActive && page.Value == Page.Chats && !chatVideoOpen.Value && row.Bound && row.MediaGeneration == generation &&
                chatRecipient?.Id == peer && ((FrameworkElement)chatColumns.Children[1]).IsVisible &&
                row.Bubble.Bounds.Bottom > chatViewport.Bounds.Y && row.Bubble.Bounds.Y < chatViewport.Bounds.Bottom,
            () => row.Message is { } current ? CachedChatMedia(current)?.Attachments.ElementAtOrDefault(index)?.Data : null,
            () => { if (row.Message is { } current) Run(() => ActivateChatAttachmentAsync(current.Id, index)); });
        if (ChatMediaSummaryFor(message, peer ?? 0)?.Photos?.ElementAtOrDefault(index) is { } photo)
            frame.ShowPoster(photo.Source, photo.Blurred);
        return frame;
    }
    private ChatPhotoFrame CreateChatPhoto(Size size) => new(size,
        () => !disposed && Window.IsActive && page.Value == Page.Chats);

    private void RenderChatMedia(ChatBubbleRow row, ChatMediaPackage package)
    {
        var mediaKey = ChatMediaCacheId(row.Message!, chatRecipient?.Id ?? 0);
        if (row.MediaKey == mediaKey && row.MediaResolved) { row.RefreshDelivery(); return; }
        var follow = ChatIsAtBottom();
        row.FollowMediaResize = follow; if (row.MediaKey != null) row.Content.AnimateMediaChange();
        row.MediaKey = mediaKey; row.MediaResolved = true; ++row.MediaGeneration;
        row.Load?.Cancel(); row.Load?.Dispose(); row.Load = null;
        StopChatAnimations(row.Attachments); row.Attachments.Clear(); row.Attachments.IsVisible = true;
        row.Body.Text = package.Caption;
        row.Body.IsVisible = !string.IsNullOrWhiteSpace(package.Caption);
        row.ResetMediaLayout();
        row.Open.IsVisible = false;
        var messageId = row.Message!.Id;
        var allPhotos = package.Attachments.Length > 0 && package.GifUrl == null &&
            package.Attachments.All(a => !a.AsFile && (a.MediaType.StartsWith("image/", StringComparison.Ordinal) && ChatImagePreview.ReadSize(a.Data) != null ||
                a.MediaType.StartsWith("video/", StringComparison.Ordinal)));
        Size? MediaSize(ChatAttachment a) => a.Video is { } video ? new Size(video.Width, video.Height) : ChatImagePreview.ReadSize(a.Data);
        if (allPhotos) row.SetPhotoLayout(MediaSize(package.Attachments[0]) ?? new Size(240, 180), package.Attachments[0].MediaType.StartsWith("video/", StringComparison.Ordinal));
        else if (package.Attachments.Length == 0 && package.GifUrl != null) row.SetPhotoLayout(ChatMediaSummaryFor(row.Message!, chatRecipient?.Id ?? 0)?.ImageSize ?? new Size(260, 190));
        for (var index = 0; index < package.Attachments.Length; index++)
        {
            var attachment = package.Attachments[index];
            var slot = index;
            var body = new StackPanel().Vertical().Spacing(8);
            ChatFileCard FileCard() => new(attachment.Name, FormatChatFileSize(attachment.Data.Length), row.Message!.SenderId == me?.Id,
                !attachment.AsFile && attachment.MediaType.StartsWith("video/", StringComparison.Ordinal), () => Run(() => ActivateChatAttachmentAsync(messageId, slot)));
            if (!attachment.AsFile && attachment.MediaType.StartsWith("video/", StringComparison.Ordinal))
                body.Add(CreateChatVideoFrame(row, attachment.Video, attachment.Data.Length, index));
            else if (!attachment.AsFile && attachment.MediaType.StartsWith("image/", StringComparison.Ordinal) && ChatImagePreview.ReadSize(attachment.Data) is { } original)
            {
                var frame = CreateChatPhoto(original);
                if (ChatMediaSummaryFor(row.Message!, chatRecipient?.Id ?? 0)?.Photos is { } previews && index < previews.Length && previews[index] is { } earlyPhoto)
                    frame.ShowPreview(earlyPhoto.Blurred, ArtworkBackdrop.Create(earlyPhoto.Source, .6));
                else if (index == 0 && ChatMediaSummaryFor(row.Message!, chatRecipient?.Id ?? 0)?.Thumbnail is ImageSource thumbnail)
                    frame.ShowPreview(ArtworkBackdrop.Create(thumbnail, 1) ?? thumbnail, ArtworkBackdrop.Create(thumbnail, .6));
                if (!allPhotos) frame.Width = ChatImagePreview.Fit(original).Width;
                body.Add(frame);
                LoadChatImage(row, frame, attachment.Data, index, attachment.MediaType == "image/gif", () =>
                {
                    row.FollowMediaResize = ChatIsAtBottom(); row.Content.AnimateMediaChange();
                    frame.Stop(); body.Clear(); body.Add(FileCard()); row.ResetMediaLayout(); row.Root.InvalidateMeasure();
                });
            }
            else body.Add(FileCard());
            row.Attachments.Add(body);
        }
        if (package.GifUrl is { } gif)
        {
            var summary = ChatMediaSummaryFor(row.Message!, chatRecipient?.Id ?? 0);
            var frame = CreateChatPhoto(summary?.ImageSize ?? new(260, 190));
            if (summary?.Thumbnail is ImageSource thumbnail) frame.ShowPreview(ArtworkBackdrop.Create(thumbnail, 1) ?? thumbnail, ArtworkBackdrop.Create(thumbnail, .6));
            row.Attachments.Add(frame);
            LoadKlipyImage(row, frame, gif);
        }
        row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
        if (follow) RequestChatBottom();
    }

    private async void LoadChatImage(ChatBubbleRow row, ChatPhotoFrame frame, byte[] bytes, int index, bool animated, Action unavailable)
    {
        var message = row.Message; var revision = chatRevision; var generation = row.MediaGeneration;
        row.Load ??= CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = row.Load.Token;
        var image = frame.Picture;
        var mediaKey = ChatMediaCacheId(message!, chatRecipient?.Id ?? 0);
        if (ChatDecoded(mediaKey, index) is { } cached)
        {
            frame.ShowImmediate(cached.Source, cached.Backdrop);
            row.MediaLoading = false; row.RefreshDelivery();
            if (animated) StartChatAnimation(image, bytes);
            return;
        }
        var encoded = bytes.ToArray();
        try
        {
            await chatMediaGate.WaitAsync(token);
            ImageSource? source; ImageSource? backdrop;
            try
            {
                (source, backdrop) = await Task.Run(() =>
                {
                    var decoded = PrepareChatImage(encoded);
                    return (decoded, decoded == null ? null : ArtworkBackdrop.Create(decoded, .6));
                }, token);
            }
            finally { chatMediaGate.Release(); }
            if (!disposed && !token.IsCancellationRequested && row.MediaGeneration == generation && chatRevision == revision && row.Attachments.IsAncestorOf(image))
            {
                if (source != null)
                {
                    CacheChatDecoded(row.MediaKey ?? mediaKey, index, new(source, backdrop));
                    frame.Show(source, backdrop);
                    row.MediaLoading = false; row.RefreshDelivery();
                    var follow = ChatIsAtBottom();
                    row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
                    if (follow) RequestChatBottom();
                }
                else unavailable();
                if (animated && source != null) StartChatAnimation(image, encoded);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!disposed && !token.IsCancellationRequested && row.MediaGeneration == generation && chatRevision == revision && row.Attachments.IsAncestorOf(image)) unavailable();
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded); }
    }
    private async void LoadKlipyImage(ChatBubbleRow row, ChatPhotoFrame frame, string url)
    {
        var message = row.Message; var revision = chatRevision; var generation = row.MediaGeneration;
        row.Load ??= CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = row.Load.Token;
        var mediaKey = row.MediaKey ?? ChatMediaCacheId(message!, chatRecipient?.Id ?? 0);
        if (ChatDecoded(mediaKey, 0) is { } cached)
        {
            frame.ShowImmediate(cached.Source, cached.Backdrop);
            if (cached.Gif != null) StartChatAnimation(frame.Picture, cached.Gif);
            return;
        }
        try
        {
            var bytes = await klipy.DownloadAsync(url, token);
            var (source, backdrop, size) = await Task.Run(() =>
            {
                var decoded = PrepareChatImage(bytes);
                return (decoded, decoded == null ? null : ArtworkBackdrop.Create(decoded, .6), ChatImagePreview.ReadSize(bytes));
            }, token);
            if (!disposed && !token.IsCancellationRequested && row.MediaGeneration == generation && chatRevision == revision && row.Attachments.IsAncestorOf(frame))
            {
                if (source == null) throw new InvalidDataException("GIF could not be decoded.");
                frame.IntrinsicSize = size ?? new(source.PixelWidth, source.PixelHeight);
                if (row.Content.PhotoMode) row.SetPhotoLayout(frame.IntrinsicSize);
                CacheChatDecoded(row.MediaKey ?? mediaKey, 0, new(source, backdrop, ChatGifAnimation.CanAnimate(bytes) ? bytes.ToArray() : null));
                frame.Show(source, backdrop); StartChatAnimation(frame.Picture, bytes);
                row.Root.InvalidateMeasure(); chatMessagesView.InvalidateMeasure();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!disposed && row.MediaGeneration == generation && row.Attachments.IsAncestorOf(frame))
            { frame.Stop(); RenderChatMediaRetry(row); }
        }
    }
    private static ImageSource? PrepareChatImage(byte[] bytes, int maxWidth = 520, int maxHeight = 380) =>
        ChatImagePreview.Decode(bytes, maxWidth, maxHeight);
    private async Task SaveChatAttachmentAsync(string messageId, int index)
    {
        var message = chatMessageItems.FirstOrDefault(m => m.Id == messageId)?.Message;
        var peer = chatRecipient?.Id;
        if (message != null && peer != null && CachedChatMedia(message) == null)
        {
            await EnsureChatMediaAsync(message, peer.Value);
            if (chatRecipient?.Id != peer || !chatMessageItems.Any(m => m.Id == messageId)) return;
        }
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
        chatMediaOrder.Remove(id);
        if (chatMediaCache.Remove(id, out var replaced))
        { chatMediaBytes -= replaced.Attachments.Sum(a => (long)a.Data.Length); if (!ReferenceEquals(replaced, package)) ClearMediaPackage(replaced); }
        while (chatMediaOrder.Count > 0 && (chatMediaCache.Count >= 16 || chatMediaBytes + size > ChatMediaCrypto.MaxPayloadBytes))
        {
            var oldest = chatMediaOrder.First!.Value; chatMediaOrder.RemoveFirst();
            if (!chatMediaCache.Remove(oldest, out var removed)) continue;
            chatMediaBytes -= removed.Attachments.Sum(a => (long)a.Data.Length);
            ClearMediaPackage(removed);
        }
        chatMediaCache[id] = package; chatMediaOrder.AddLast(id); chatMediaBytes += size;
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
    private static string FormatChatFileSize(long size) => size >= 1024 * 1024 ? $"{size / (1024d * 1024):0.#} МБ" : size >= 1024 ? $"{size / 1024d:0.#} КБ" : $"{size} Б";
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
        VisualTree.Visit(element, child =>
        {
            if (child is ChatPhotoFrame frame) frame.Stop();
            if (child is ChatVideoFrame video) video.Stop();
            if (child is Grid grid && chatMusicViews.TryGetValue(grid, out var music))
            { music.Row.Reset(); music.Item = null; chatMusicViews.Remove(grid); }
            if (child is Image image)
            { StopCardArtwork(image); if (chatAnimations.Remove(image, out var animation)) animation.Dispose(); image.Source = null; }
        });
    }
}
