using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatsUiAsync()
    {
        try
        {
            var transport = new ChatFixtureTransport();
            var mediaTransport = new ChatFixtureMediaHandler();
            chatMedia.Dispose(); chatMedia = new ChatMediaHosting(mediaTransport);
            api.Session = new("chatfixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Ты" }; signedIn.Value = true;
            await ShowWorkspaceAsync();
            transport.DelayList = true;
            var opening = NavigateAsync(Page.Chats);
            await WaitForLikedLayoutAsync(() => transport.ListReady != null && chatInboxLoading.Skeleton.Running && chatInboxLoading.Skeleton.ActualHeight > 100 && startupContent.Opacity > .95);
            CaptureUiPreview("chats-loading");
            transport.ListReady!.SetResult(); await opening;
            // This scenario drives each poll explicitly; timer ticks would race hover/scroll assertions.
            chatTimer.Stop();
            await WaitForLikedLayoutAsync(() => chatInboxBindings.Count >= 2 && chatColumns.ActualWidth > 760);
            foreach (var button in chatInboxBindings.Keys)
            {
                var avatar = (Image)button.FindVisualChild<Image>()!;
                if (avatar.Parent is not Border { ClipToBounds: true, CornerRadius: 23 } frame ||
                    frame.ActualWidth != 46 || frame.ActualHeight != 46)
                    throw new InvalidOperationException("Inbox avatar does not have a square circular clipping frame.");
            }
            if (chatInput.IsEnabled || chatRecipient != null) throw new InvalidOperationException("Unselected chat enables sending.");
            if (IsLibrary(Page.Chats)) throw new InvalidOperationException("Chats accidentally belongs to the library navigation.");
            var nav = topNavigation.Children.OfType<Button>().Select(b => (b.FindVisualChild<TextBlock>() as TextBlock)?.Text).ToArray();
            if (Array.IndexOf(nav, "Чаты") != Array.IndexOf(nav, "Библиотека") + 1) throw new InvalidOperationException("Chats is not immediately after Library.");
            transport.DelayThread = true;
            var selecting = SelectChatAsync(transport.Other);
            await WaitForLikedLayoutAsync(() => transport.ThreadReady != null && chatThreadLoading.Skeleton.Running && chatThreadLoading.Skeleton.ActualHeight > 100);
            CaptureUiPreview("chats-thread-loading");
            transport.ThreadReady!.SetResult(); await selecting;
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message != null && r.Root.ActualHeight > 40) && chatInput.IsEnabled);
            var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
            await WaitForLikedLayoutAsync(() => SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 10);
            if (chatMessages.Count != 30 || !chatOlder.IsVisible) throw new InvalidOperationException("Initial chat page or pagination was lost.");
            var bound = ChatRows.First(r => r.Message != null);
            var reference = bound;
            await RefreshChatsAsync();
            if (!ChatRows.Contains(reference) || ChatRows.All(r => r.Message == null)) throw new InvalidOperationException("Polling discarded visible chat rows.");
            if (chatMusicUiSmoke || chatMediaUiSmoke)
            {
                await VerifyChatRichAttachmentsAsync(transport, mediaTransport, musicOnly: !chatMediaUiSmoke);
                Console.WriteLine(chatMediaUiSmoke ? "UI_CHAT_MEDIA_OK: documents, images, music attachments, inline video, focus-aware autoplay, viewer reopen, controls and music suspension" :
                    "UI_CHAT_MUSIC_OK: inline artwork/title mentions, compact search attachments, ordered message queue, Next/Previous, skipped track selection, navigation, clipboard, undo and recycled bindings");
                return;
            }
            if (OperatingSystem.IsLinux())
            {
                var display = XOpenDisplay(0);
                if (display == 0) throw new InvalidOperationException("No display for chat input verification.");
                try
                {
                    chatInput.Focus(); chatInput.Text = "";
                    var text = "Привет World";
                    if (!text.All(c => TryFindInputKey(display, c, out _, out _))) text = "Hello World";
                    foreach (var c in text) SendCharacter(display, c);
                    await WaitForLikedLayoutAsync(() => chatInput.Text == text);
                }
                finally { XCloseDisplay(display); }
            }
            await VerifyChatGifAnimationAsync();
            await VerifyChatAttachmentInputAsync();
            await VerifyChatComposerLayoutAsync();
            chatInput.Text = "Черновик 🎵\nВторая строка";
            await SelectChatAsync(transport.Third);
            chatInput.Text = "Другой черновик";
            await SelectChatAsync(transport.Other);
            if (chatInput.Text != "Черновик 🎵\nВторая строка") throw new InvalidOperationException("Switching conversations lost drafts.");
            await WaitForLikedLayoutAsync(() => !chatScrollBottom && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 10);
            viewer.SetScrollOffsets(0, 0);
            await WaitForLoginFrameAsync();
            for (var unreadIndex = 0; unreadIndex < 4; unreadIndex++) transport.AddIncoming($"Новое сообщение в фоне {unreadIndex + 1}");
            await RefreshChatsAsync();
            await WaitForLoginFrameAsync();
            if (viewer.VerticalOffset > 10 || !chatNewMessages.IsVisible) throw new InvalidOperationException($"Incoming chat message interrupted reading history: offset={viewer.VerticalOffset}, maximum={SmoothScroll.Maximum(viewer)}, badge={chatNewMessages.IsVisible}, following={chatScrollBottom}");
            if (chatUnread.Badge(90) != "4") throw new InvalidOperationException($"Unread counter did not count four arrivals: {chatUnread.Badge(90)}");
            await Task.Delay(300); CaptureUiPreview("chats-unread-counter");
            await LoadOlderMessagesAsync();
            if (chatMessages.Count != 39 || chatMessageCursor != null) throw new InvalidOperationException("Older history did not merge or exhausted cursor reappeared.");
            RequestChatBottom();
            await WaitForLikedLayoutAsync(() => SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 10);
            chatInput.Text = "Тестовое сообщение";
            transport.NextSentAt = DateTimeOffset.FromUnixTimeSeconds(chatMessages.Last().SentAt.ToUnixTimeSeconds());
            transport.DelaySend = true;
            var sending = SendChatAsync();
            await WaitForLikedLayoutAsync(() => transport.Sends == 1);
            if (chatSend.IsEnabled) throw new InvalidOperationException("A pending send accepts a second click.");
            await SendChatAsync();
            if (transport.Sends != 1) throw new InvalidOperationException("Duplicate chat send.");
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Id == chatOutgoing[90].Message.Id && r.Spinner.IsActive));
            var localRow = ChatRows.First(r => r.Message?.Id == chatOutgoing[90].Message.Id);
            chatOutgoing[90].Message = chatOutgoing[90].Message with { SentAt = chatMessages.Last().SentAt.AddSeconds(-1) };
            RefreshChatTimeline();
            if (chatMessageItems.Last().Id != chatOutgoing[90].Message.Id)
                throw new InvalidOperationException("Client/server clock skew moved the outgoing row above previous messages.");
            CaptureUiPreview("chats-sending");
            transport.SendReady!.SetResult(); await sending;
            if (chatInput.Text.Length != 0 || chatPending.Count != 0 || chatMessages.Last().Body != "Тестовое сообщение") throw new InvalidOperationException("Confirmed send did not commit.");
            await WaitForLikedLayoutAsync(() => localRow.Message?.Body == "Тестовое сообщение" && localRow.Spinner.IsActive == false);
            if (chatMessageItems.Last().Body != "Тестовое сообщение" || ChatRows.First(r => r.Message?.Id == chatMessageItems.Last().Id) != localRow)
                throw new InvalidOperationException("Server acknowledgement moved or rebuilt the optimistic row.");
            await RefreshChatThreadAsync();
            if (chatMessages.Count(m => m.Body == "Тестовое сообщение") != 1) throw new InvalidOperationException("POST and GET representations created duplicate bubbles.");
            // Capture an older GET snapshot, then complete a new send before that read returns.
            transport.DelayThread = true;
            var polling = RefreshChatThreadAsync();
            await WaitForLikedLayoutAsync(() => transport.ThreadReady != null);
            chatInput.Text = "Тестовое сообщение";
            await SendChatAsync();
            transport.ThreadReady!.SetResult(); await polling;
            if (chatMessages.Count(m => m.Body == "Тестовое сообщение") != 2 || chatMessageItems.Count != chatMessages.Count)
                throw new InvalidOperationException("In-flight polling lost a send or merged two intentional identical messages.");
            chatInput.Text = "Потерянный ответ сервера";
            transport.ThrowAfterSend = true;
            await SendChatAsync();
            if (chatPending.Count != 1 || chatInput.Text != "Потерянный ответ сервера" || chatSend.IsEnabled || chatOutgoing.GetValueOrDefault(90)?.State != ChatDelivery.Failed)
                throw new InvalidOperationException("Unknown send lost its outbox/draft or became replayable automatically.");
            if (chatLocalStore!.Load().Pending.Length != 1) throw new InvalidOperationException("Pending send is not durable.");
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Error.IsVisible && r.Message?.Id == chatOutgoing[90].Message.Id));
            CaptureUiPreview("chats-failed");
            transport.ThrowAfterSend = false;
            await RefreshChatThreadAsync();
            if (chatPending.Count != 0 || chatInput.Text.Length != 0 || transport.Sends != 3) throw new InvalidOperationException("Reconciliation replayed or failed to detect accepted send.");
            viewer.SetScrollOffsets(0, 90);
            await WaitForLikedLayoutAsync(() => chatFloatingDate.IsVisible);
            await WaitForLikedLayoutAsync(() => chatFloatingDate.Opacity > .95);
            var topRow = ChatRows.Where(r => r.Bound && r.Message != null && r.Root.Bounds.Bottom > viewer.Bounds.Y + 30 && r.Root.Bounds.Y < viewer.Bounds.Bottom)
                .OrderBy(r => r.Root.Bounds.Y).First();
            if (Math.Abs(chatFloatingDate.Bounds.X + chatFloatingDate.ActualWidth / 2 - topRow.Root.Bounds.X - topRow.Root.ActualWidth / 2) > 1)
                throw new InvalidOperationException("Pinned date is not centered on the original row's axis.");
            CaptureUiPreview("chats-date-pinned");
            await WaitForLikedLayoutAsync(() => !chatFloatingDate.IsVisible);
            RequestChatBottom();
            await WaitForLikedLayoutAsync(() => !chatScrollBottom);
            byte[] photo;
            using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.App.Assets.Stickle.pixel-heart.png")!)
            using (var output = new MemoryStream()) { resource.CopyTo(output); photo = output.ToArray(); }
            var code = mediaTransport.Add(new(90, 42, "Смотри, какая иллюстрация", [new("heart.png", "image/png", photo)]));
            mediaTransport.DelayDownload = true;
            transport.AddIncoming(code);
            await RefreshChatsAsync();
            await WaitForLikedLayoutAsync(() => mediaTransport.DownloadReady != null && ChatRows.Any(r => r.Message?.Body == code &&
                r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { PreviewSource: not null, Picture.Source: null, ActualHeight: > 0 }));
            var loadingRow = ChatRows.First(r => r.Message?.Body == code);
            if (loadingRow.Open.IsVisible || loadingRow.Body.Text != "Смотри, какая иллюстрация")
                throw new InvalidOperationException("Progressive media lost its authenticated caption or exposed a download button.");
            await WaitForLikedLayoutAsync(() => !loadingRow.Content.IsResizing);
            var loadingSize = loadingRow.Root.Bounds.Size;
            await Task.Delay(300);
            CaptureUiPreview("chats-media-loading");
            mediaTransport.DownloadReady!.SetResult();
            await WaitForLikedLayoutAsync(() => chatMediaSummaries.Values.Any(s => s.Thumbnail != null) &&
                ChatRows.Any(r => r.Message?.Body == code && !r.Open.IsVisible && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
            if (loadingRow.Root.Bounds.Size != loadingSize)
                throw new InvalidOperationException("Full photo changed the geometry reserved by its blurred thumbnail.");
            if (!chatInboxBindings.Any(b => b.Value.Recipient.Id == 90 && chatInboxVisuals.TryGetValue(b.Key, out var v) && v.ThumbnailFrame.IsVisible && v.Preview.Text == "Смотри, какая иллюстрация"))
                throw new InvalidOperationException("Inbox did not show a decrypted media thumbnail.");
            if (ChatRows.Where(r => r.Message != null).Any(r => r.Time.Text.Length != 5 || r.Time.Text[2] != ':'))
                throw new InvalidOperationException("Chat message contains a full date instead of time.");
            var attachmentFile = Path.Combine(Path.GetTempPath(), "pookie-chat-attachment-" + Guid.NewGuid().ToString("N") + ".png");
            await File.WriteAllBytesAsync(attachmentFile, photo);
            try
            {
                chatSelectedFiles = [attachmentFile]; UpdateChatAttachments();
                await WaitForLikedLayoutAsync(() => chatAttachmentPreview.Source != null);
                await Task.Delay(350);
                CaptureUiPreview("chats-attachment-preview");
                chatInput.Text = "А вот моя картинка";
                transport.DelaySend = true; transport.SendReady = null;
                var mediaSending = SendChatAsync();
                await WaitForLikedLayoutAsync(() => transport.SendReady != null && ChatRows.Any(r => r.Message?.SenderId == 42 && r.Body.Text == "А вот моя картинка" &&
                    !r.Content.IsResizing && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
                var sendingMediaRow = ChatRows.First(r => r.Message?.SenderId == 42 && r.Body.Text == "А вот моя картинка");
                var sendingMediaFrame = (ChatPhotoFrame)sendingMediaRow.Attachments.FindVisualChild<ChatPhotoFrame>()!;
                var sendingMediaSize = sendingMediaRow.Root.Bounds.Size;
                var mediaDownloadsBeforeAck = mediaTransport.Downloads;
                Window.PerformLayout();
                var stableItem = chatMessageItems.Last();
                var bottomInset = viewer.Bounds.Bottom - sendingMediaRow.Root.Bounds.Bottom;
                var paintedInsets = new List<double>();
                var replacedItems = 0;
                void Changes(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs change)
                { if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace) replacedItems++; }
                void Paint() { if (sendingMediaRow.Bound) paintedInsets.Add(viewer.Bounds.Bottom - sendingMediaRow.Root.Bounds.Bottom); }
                chatMessageItems.CollectionChanged += Changes; chatViewport.BeforePaint += Paint;
                try
                {
                    transport.SendReady!.SetResult(); await mediaSending;
                    await WaitForLikedLayoutAsync(() => !sendingMediaRow.Content.IsResizing && !chatComposerSurface.IsResizing);
                    await WaitForLoginFrameAsync();
                }
                finally { chatMessageItems.CollectionChanged -= Changes; chatViewport.BeforePaint -= Paint; }
                if (!ReferenceEquals(stableItem, chatMessageItems.Last()) || replacedItems != 0)
                    throw new InvalidOperationException("Delivery acknowledgement replaced the stable timeline object.");
                if (paintedInsets.Count == 0 || paintedInsets.Any(inset => Math.Abs(inset - bottomInset) > 1))
                    throw new InvalidOperationException($"Acknowledged media shifted within a painted viewport: expected={bottomInset}, actual={string.Join(',', paintedInsets)}");
                if (!ReferenceEquals(sendingMediaFrame, sendingMediaRow.Attachments.FindVisualChild<ChatPhotoFrame>()) || sendingMediaRow.Root.Bounds.Size != sendingMediaSize || mediaTransport.Downloads != mediaDownloadsBeforeAck)
                    throw new InvalidOperationException("Media acknowledgement replaced its image, changed its geometry or downloaded the sender's own upload.");
                await WaitForLikedLayoutAsync(() => chatSelectedFiles.Length == 0 && ChatRows.Any(r =>
                    r.Message?.SenderId == 42 && r.Body.Text == "А вот моя картинка" && r.Checks.IsVisible &&
                    r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
            }
            finally { ClearChatAttachments(); File.Delete(attachmentFile); }

            await VerifyChatCachingAsync(transport, mediaTransport);

            byte[] FixtureImage(string name)
            {
                using var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media." + name)!;
                using var output = new MemoryStream(); resource.CopyTo(output); return output.ToArray();
            }
            var brokenCode = mediaTransport.Add(new(90, 42, "", [new("broken.png", "image/png", [0x89, 0x50, 0x4E, 0x47])]));
            transport.AddIncoming(brokenCode); await RefreshChatsAsync();
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == brokenCode &&
                r.Attachments.FindVisualChild<ChatFileCard>() is ChatFileCard && r.Attachments.FindVisualChild<ChatPhotoFrame>() == null));
            foreach (var (name, width, height) in new[] { ("chat-small.png", 48d, 32d), ("chat-4k.jpg", 300d, 168d) })
            {
                var bytes = FixtureImage(name);
                var imageCode = mediaTransport.Add(new(90, 42, name, [new(name, ChatMediaType(name), bytes)]));
                transport.AddIncoming(imageCode); await RefreshChatsAsync();
                await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == imageCode && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
                var frame = (ChatPhotoFrame)ChatRows.First(r => r.Message?.Body == imageCode).Attachments.FindVisualChild<ChatPhotoFrame>()!;
                await WaitForLikedLayoutAsync(() => frame.Picture.ActualWidth > 0 && !ChatRows.First(r => r.Message?.Body == imageCode).Content.IsResizing);
                var image = frame.Picture;
                if (image.ActualWidth != width || image.ActualHeight != height) throw new InvalidOperationException("Media preview lost intrinsic sizing or aspect ratio.");
                if (ChatMediaSummaryFor(ChatRows.First(r => r.Message?.Body == imageCode).Message!, 90)?.Thumbnail == null)
                    throw new InvalidOperationException("Photo is missing its inbox thumbnail.");
            }
            if (ChatRows.Any(r => r.Message?.SenderId == 42 && r.Checks.Text != "✓") ||
                chatInboxVisuals.Any(v => v.Value.Checks.IsVisible && v.Value.Checks.Text != "✓"))
                throw new InvalidOperationException("Sent status should have one check mark.");
            // Optional reproduction with a local user-selected photo; uses only the fake media host.
            if (Environment.GetEnvironmentVariable("POOKIE_CHAT_IMAGE_PROBE") is { Length: > 0 } probe)
            {
                var bytes = await File.ReadAllBytesAsync(probe);
                var probeCode = mediaTransport.Add(new(90, 42, "Проверка фотографии", [new(Path.GetFileName(probe), ChatMediaType(probe), bytes)]));
                transport.AddIncoming(probeCode); await RefreshChatsAsync();
                await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == probeCode && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
                chatSelectedFiles = [probe]; UpdateChatAttachments();
                await WaitForLikedLayoutAsync(() => chatAttachmentPreview.Source != null);
                if (ChatMediaSummaryFor(ChatRows.First(r => r.Message?.Body == probeCode).Message!, 90)?.Thumbnail == null)
                    throw new InvalidOperationException("Local photo has no inbox thumbnail.");
                CaptureUiPreview("chats-photo-probe");
                ClearChatAttachments();
            }
            var edgeCode = mediaTransport.Add(new(42, 90, "Фото без рамки", [new("photo.jpg", "image/jpeg", FixtureImage("chat-4k.jpg"))]));
            transport.AddOwn(edgeCode); await RefreshChatsAsync();
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == edgeCode && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null, ActualHeight: > 0 } && !r.Content.IsResizing));
            var edgeRow = ChatRows.First(r => r.Message?.Body == edgeCode);
            var edgeFrame = (ChatPhotoFrame)edgeRow.Attachments.FindVisualChild<ChatPhotoFrame>()!;
            if (edgeRow.Bubble.BorderThickness != 0 || edgeRow.Bubble.Background.A != 0 ||
                Math.Abs(edgeFrame.Bounds.X - edgeRow.Bubble.Bounds.X) > .1 || Math.Abs(edgeFrame.Bounds.Y - edgeRow.Bubble.Bounds.Y) > .1 ||
                Math.Abs(edgeFrame.Bounds.Right - edgeRow.Bubble.Bounds.Right) > .1)
                throw new InvalidOperationException("Caption media does not reach the outer bubble edges.");
            await Task.Delay(300); CaptureUiPreview("chats-media-edges");
            var photoOnlyCode = mediaTransport.Add(new(42, 90, "", [new("heart.png", "image/png", photo)]));
            transport.AddOwn(photoOnlyCode); await RefreshChatsAsync();
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == photoOnlyCode &&
                r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null, ActualWidth: > 0 } && !r.Content.IsResizing));
            var photoOnlyRow = ChatRows.First(r => r.Message?.Body == photoOnlyCode);
            photoOnlyRow.UpdateMediaHover(false); await Task.Delay(240);
            if (!photoOnlyRow.Content.PhotoOnly || photoOnlyRow.Footer.Opacity > .05 || photoOnlyRow.Bubble.Padding != new Thickness(0))
                throw new InvalidOperationException("Image-only message has a normal footer or extra bubble padding.");
            CaptureUiPreview("chats-photo-only");
            photoOnlyRow.UpdateMediaHover(true); await Task.Delay(240);
            var photoOnlyFrame = (ChatPhotoFrame)photoOnlyRow.Attachments.FindVisualChild<ChatPhotoFrame>()!;
            if (photoOnlyRow.Footer.Opacity < .95 || photoOnlyRow.Footer.Bounds.Bottom > photoOnlyFrame.Bounds.Bottom ||
                photoOnlyRow.Footer.Bounds.Y < photoOnlyFrame.Bounds.Y)
                throw new InvalidOperationException("Hovered media timestamp is not an overlay inside the image.");
            CaptureUiPreview("chats-photo-hover");
            photoOnlyRow.UpdateMediaHover(false);
            foreach (var caption in new[] { string.Concat(Enumerable.Repeat("8efcfbf498b99d32.jpg", 22)), "тест" })
            {
                var captionCode = mediaTransport.Add(new(42, 90, caption, [new("heart.png", "image/png", photo)]));
                transport.AddOwn(captionCode); await RefreshChatsAsync();
                await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == captionCode &&
                    r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null, ActualWidth: > 0 } && !r.Content.IsResizing));
                var captionRow = ChatRows.First(r => r.Message?.Body == captionCode);
                var captionFrame = (ChatPhotoFrame)captionRow.Attachments.FindVisualChild<ChatPhotoFrame>()!;
                await WaitForLikedLayoutAsync(() => captionFrame.Backdrop.IsVisible && captionFrame.Backdrop.Source != null);
                var expected = caption == "тест" ? 220 : 440;
                if (captionRow.Content.PhotoOnly || captionFrame.ActualWidth != expected || captionFrame.Picture.ActualWidth != 100 ||
                    captionFrame.Picture.ActualHeight != 100 || captionRow.Body.ActualWidth > 416)
                    throw new InvalidOperationException("Caption media is not bounded or has stretched the small photo.");
                if (caption != "тест" && captionRow.Body.ActualHeight < 100)
                    throw new InvalidOperationException("Long unbroken caption did not wrap.");
                if (caption == "тест" && captionRow.Bubble.ActualHeight > 140)
                    throw new InvalidOperationException("Short caption should share its line with the timestamp.");
                if (!chatInboxBindings.Any(b => b.Value.Recipient.Id == 90 && chatInboxVisuals.TryGetValue(b.Key, out var v) &&
                    v.ThumbnailFrame.IsVisible && v.Preview.Text == ChatPresentation.Preview(caption)))
                    throw new InvalidOperationException("Inbox must display the media caption alongside its thumbnail.");
            }
            await Task.Delay(300);
            CaptureUiPreview("chats-media-captions");
            var stableRow = ChatRows.First(r => r.Message?.SenderId == 42 && r.Body.Text == "тест");
            var stableFrame = (ChatPhotoFrame)stableRow.Attachments.FindVisualChild<ChatPhotoFrame>()!;
            var stableSource = stableFrame.Picture.Source;
            chatInput.Text = "Прикольно"; transport.DelaySend = true; transport.SendReady = null;
            var stableSending = SendChatAsync();
            await WaitForLikedLayoutAsync(() => transport.SendReady != null && ChatRows.Any(r => r.Body.Text == "Прикольно" && r.Content.InlineFooter));
            if (stableRow.Attachments.FindVisualChild<ChatPhotoFrame>() != stableFrame || stableFrame.Picture.Source != stableSource || stableFrame.Skeleton.IsVisible)
                throw new InvalidOperationException("Appending a message rebuilt or faded an existing attachment.");
            transport.SendReady!.SetResult(); await stableSending;
            await WaitForLikedLayoutAsync(() => chatMessageItems.Last().Body == "Прикольно");
            if (stableRow.Attachments.FindVisualChild<ChatPhotoFrame>() != stableFrame || stableFrame.Picture.Source != stableSource || stableFrame.Skeleton.IsVisible)
                throw new InvalidOperationException("Confirming a send rebuilt an existing attachment.");
            foreach (var text in new[] { "окакокакокакокакокакокакокак", "Хз как получается так сделать, но если выйдет, то будет круто" })
            { transport.AddOwn(text); await RefreshChatsAsync(); }
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Body.Text.StartsWith("Хз как") && r.Body.ActualHeight > 0));
            var longText = new string('Ш', 100);
            transport.AddOwn(longText); await RefreshChatsAsync();
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Body.Text == longText && r.Body.ActualHeight > 0));
            if (ChatRows.First(r => r.Body.Text == longText).Content.InlineFooter)
                throw new InvalidOperationException("Long text must put the time on the next row instead of stretching the bubble.");
            foreach (var row in ChatRows.Where(r => r.Bound && r.Message != null && r.Body.IsVisible))
            {
                if (row.Body.ActualHeight < row.Body.FontSize) throw new InvalidOperationException("Message text has insufficient vertical ink bounds.");
                if (!row.Content.PhotoMode && row.Bubble.ClipToBounds) throw new InvalidOperationException("Text bubble clips its padded content to an inner rounded contour.");
                if (row.Content.InlineFooter && row.Footer.Bounds.Bottom < row.Body.Bounds.Bottom + .5)
                    throw new InvalidOperationException("Inline timestamp should sit slightly below the message text.");
            }
            await VerifyChatTimestampAlignmentAsync(transport, mediaTransport);
            await VerifyChatRichAttachmentsAsync(transport, mediaTransport);
            await Task.Delay(300);
            CaptureUiPreview("chats-text-layout");
            chatInput.Text = "Послушай этот трек — как тебе?";
            await WaitForLikedLayoutAsync(() => !chatScrollBottom && chatSend.IsEnabled);
            await Task.Delay(350);
            CaptureUiPreview("chats-wide");
            Window.WindowSize = WindowSize.Resizable(820, 780, minWidth: 760, minHeight: 600);
            await WaitForLikedLayoutAsync(() => chatColumns.ActualWidth < 760 && chatBack.IsVisible && !((FrameworkElement)chatColumns.Children[0]).IsVisible);
            await WaitForLikedLayoutAsync(() => !chatScrollBottom && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 10);
            foreach (var row in ChatRows.Where(r => r.Message != null && r.Root.ActualHeight > 0))
                if (row.Root.ActualWidth > chatMessagesView.ActualWidth + 1) throw new InvalidOperationException("Chat bubble exceeds narrow viewport.");
            CaptureUiPreview("chats-narrow");
            chatColumns.Selected = false;
            await WaitForLikedLayoutAsync(() => ((FrameworkElement)chatColumns.Children[0]).IsVisible && !((FrameworkElement)chatColumns.Children[1]).IsVisible);
            foreach (var button in chatInboxBindings.Keys)
                ((Image)button.FindVisualChild<Image>()!).Source = ChatImagePreview.Decode(FixtureImage("chat-small.png"));
            await WaitForLoginFrameAsync();
            CaptureUiPreview("chats-inbox-narrow");
            foreach (var pair in chatInboxBindings.Where(p => chatInboxVisuals.TryGetValue(p.Key, out var v) && !v.ThumbnailFrame.IsVisible))
            {
                var name = (TextBlock)pair.Key.FindVisualChild<TextBlock>()!;
                if (Math.Abs(name.Bounds.X - chatInboxVisuals.GetValue(pair.Key, _ => throw new InvalidOperationException()).Preview.Bounds.X) > 1)
                    throw new InvalidOperationException("Plain conversation preview is indented relative to the name.");
            }
            var unreadBeforeHidden = chatUnread.States.GetValueOrDefault(90)?.Count ?? 0;
            transport.AddIncoming("Новое в списке чатов");
            await RefreshChatsAsync();
            if (chatUnread.States[90].Count != unreadBeforeHidden + 1)
                throw new InvalidOperationException("A hidden compact thread was marked as read.");
            page.Value = Page.Home; chatTimer.Stop();
            transport.AddIncoming("Новое вне раздела чатов");
            await RefreshChatsAsync();
            if (chatUnread.States[90].Count != unreadBeforeHidden + 2)
                throw new InvalidOperationException("Unread synchronization stopped outside Chats.");
            page.Value = Page.Chats; chatTimer.Stop();
            chatColumns.Selected = true;
            transport.AddOwn("Повторяющийся текст"); await RefreshChatThreadAsync();
            chatInput.Text = "Повторяющийся текст"; transport.RejectBeforeSend = true;
            await SendChatAsync(); await RefreshChatThreadAsync();
            if (chatPending.Count != 1 || chatOutgoing.GetValueOrDefault(90)?.State != ChatDelivery.Failed)
                throw new InvalidOperationException("An older identical message incorrectly confirmed a failed send.");
            if (chatLocalStore!.Load().Pending[0].BeforeIds is not { Length: > 0 })
                throw new InvalidOperationException("Outgoing reconciliation lost its baseline across restart.");
            DiscardPendingChat(); transport.RejectBeforeSend = false;
            var persisted = chatLocalStore!.Load();
            if (!persisted.Drafts.ContainsKey(90)) SaveChatDraft();
            ResetChats();
            if (chatRecipient != null || chatMessages.Count != 0 || chatDrafts.Count != 0 || chatMediaCache.Count != 0)
                throw new InvalidOperationException("Account reset retained another account's chat data.");
            Console.WriteLine("UI_CHATS_OK: skeleton, sending/error/sent indicators, media thumbnails in inbox/composer/bubbles, pinned dates, responsive scrolling, drafts and durable reconciliation without duplicate or false confirmations");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine(error); }
        finally { Window.Close(); }
    }

    private async Task VerifyChatGifAnimationAsync()
    {
        var bytes = Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBCAABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7");
        var image = new Image(); var visible = true;
        using var animation = new ChatGifAnimation(image, bytes, () => visible);
        var colors = new HashSet<int>();
        for (var i = 0; i < 25 && colors.Count < 2; i++)
        {
            await Task.Delay(40, lifetime.Token);
            if (image.Source is not ImageSource source) continue;
            var pixels = new byte[4 * 4 * 4]; source.CopyPixels(pixels, 16);
            colors.Add(BitConverter.ToInt32(pixels));
        }
        if (colors.Count != 2) throw new InvalidOperationException("GIF did not animate distinct decoded frames.");
        visible = false; await Task.Delay(100, lifetime.Token);
        var paused = image.Source; await Task.Delay(160, lifetime.Token);
        if (!ReferenceEquals(paused, image.Source)) throw new InvalidOperationException("Hidden GIF continued decoding frames.");
    }

    private sealed class ChatFixtureTransport : ISoundCloudBrowserTransport
    {
        internal SoundCloudUser Other = new() { Id = 90, Username = "Аня" };
        internal SoundCloudUser Third = new() { Id = 91, Username = "Длинное имя собеседника, которое должно аккуратно обрезаться" };
        private readonly List<object> messages = Enumerable.Range(1, 30).Select(i => Message(i % 3 == 0 ? 42 : 90,
            i == 29 ? "Нашла очень классный альбом. Кажется, тебе понравится — особенно последний трек!" : i % 3 == 0 ? "Да, звучит отлично 🎧" : "Привет! Есть что-нибудь интересное послушать?", DateTimeOffset.UtcNow.AddMinutes(i - 35))).ToList();
        internal int Sends; internal bool DelaySend, ThrowAfterSend, RejectBeforeSend, DelayThread, DelayList; internal TaskCompletionSource? SendReady, ThreadReady, ListReady;
        internal DateTimeOffset? NextSentAt;
        private static object Message(long sender, string body, DateTimeOffset date) => new { sender_urn = $"soundcloud:users:{sender}", content = body, sent_at = date.ToString("O") };
        private readonly HashSet<long> unreadPeers = [];
        internal void AddIncoming(string body) { messages.Add(Message(90, body, DateTimeOffset.UtcNow)); unreadPeers.Add(90); }
        internal void AddOwn(string body) => messages.Add(Message(42, body, DateTimeOffset.UtcNow));
        private object Conversation(SoundCloudUser other, object? last = null) => new { id = $"42:{other.Id}", users = new[] { new SoundCloudUser { Id = 42, Username = "Ты" }, other }.Select(u => new { id = u.Id, username = u.Username }).ToArray(), read = !unreadPeers.Contains(other.Id),
            last_message = last ?? (other.Id == 90 ? messages.Last() : Message(91, "Спасибо за подборку!", DateTimeOffset.UtcNow.AddHours(-1))) };
        public async Task<JsonDocument> RequestChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.IsValid()) throw new InvalidOperationException("Invalid fixture request.");
            object result;
            if (request.Kind == "list")
            {
                if (DelayList) { DelayList = false; ListReady = new(TaskCreationOptions.RunContinuationsAsynchronously); await ListReady.Task.WaitAsync(cancellationToken); }
                result = new { collection = new[] { Conversation(Other), Conversation(Third) }, next_href = (string?)null };
            }
            else if (request.Kind == "thread")
            {
                result = request.RecipientId == 91 ? new { collection = Array.Empty<object>(), next_href = (string?)null } :
                    request.Cursor == null ? new { collection = messages.ToArray(), next_href = (string?)"https://api-v2.soundcloud.com/users/42/conversations/90/messages?older=1" } :
                    new { collection = Enumerable.Range(1, 5).Select(i => Message(90, "Старое сообщение " + i, DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(i))).ToArray(), next_href = (string?)null };
                if (DelayThread) { DelayThread = false; ThreadReady = new(TaskCreationOptions.RunContinuationsAsynchronously); await ThreadReady.Task.WaitAsync(cancellationToken); }
            }
            else if (request.Kind == "send")
            {
                Sends++; if (DelaySend) { DelaySend = false; SendReady = new(TaskCreationOptions.RunContinuationsAsynchronously); await SendReady.Task.WaitAsync(cancellationToken); }
                if (RejectBeforeSend) throw new HttpRequestException("Fixture rejected before accepting send");
                var date = NextSentAt ?? DateTimeOffset.UtcNow; NextSentAt = null;
                messages.Add(Message(42, request.Body!, date));
                if (ThrowAfterSend) throw new HttpRequestException("Fixture lost acknowledgement");
                var last = new { id = "post-id-" + Sends, sender_urn = "soundcloud:users:42", content = request.Body!, sent_at = date.ToString("O") };
                result = Conversation(request.RecipientId == 90 ? Other : Third, last);
            }
            else { if (request.Kind == "read") unreadPeers.Remove(request.RecipientId); result = new { }; }
            return JsonSerializer.SerializeToDocument(result);
        }
        internal TaskCompletionSource? MusicReady;
        private static object MusicTrack(string title = "Fixture track") => new { kind = "track", id = 101, title, artwork_url = "https://i1.sndcdn.com/chat-music-fixture-t500x500.jpg", waveform_url = "https://wave.sndcdn.com/chat-track-fixture.json", permalink_url = "https://soundcloud.com/fixture/track", duration = 120000, user = new { id = 7, username = "Fixture artist" } };
        private static object MusicPlaylist() => new { kind = "playlist", id = 102, title = "Fixture playlist", artwork_url = "https://i1.sndcdn.com/chat-music-fixture-t500x500.jpg", permalink_url = "https://soundcloud.com/fixture/sets/list", track_count = 2, user = new { id = 7, username = "Fixture artist" }, tracks = new[] { MusicTrack() } };
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var animated = uri.AbsolutePath == "/resolve" && uri.Query.Contains("animated", StringComparison.Ordinal);
            if (animated && MusicReady != null) await MusicReady.Task.WaitAsync(cancellationToken);
            object data = uri.AbsolutePath == "/tracks/101" ? MusicTrack() : uri.AbsolutePath == "/resolve" ? Uri.UnescapeDataString(uri.Query).Contains("/sets/", StringComparison.Ordinal) ? MusicPlaylist() : MusicTrack() :
                uri.AbsolutePath.StartsWith("/search", StringComparison.Ordinal) ? new { collection = new[] { MusicTrack(), MusicPlaylist() } } : new { collection = Array.Empty<object>() };
            if (animated) data = MusicTrack("Длинное название загружаемого трека для проверки ширины");
            return JsonSerializer.SerializeToDocument(data);
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ProgressiveFixtureStream(byte[] bytes, int boundary, Task resume) : MemoryStream(bytes, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= boundary) await resume.WaitAsync(token);
            else buffer = buffer[..Math.Min(buffer.Length, boundary - (int)Position)];
            return await base.ReadAsync(buffer, token);
        }
    }
    private sealed class ChatFixtureMediaHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> files = [];
        internal bool DelayDownload;
        internal int Downloads;
        internal TaskCompletionSource? DownloadReady;
        internal string Add(ChatMediaPackage package, bool legacy = false)
        {
            var id = (files.Count + 1).ToString("D8"); var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            files[id] = legacy ? ChatMediaCrypto.EncryptLegacy(package, key) : ChatMediaCrypto.Encrypt(package, key);
            return new ChatMediaReference('p', id, key).Code;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.Host == "pomf.lain.la")
            {
                var form = (MultipartFormDataContent)request.Content!;
                var file = form.Single(f => f.Headers.ContentDisposition?.FileName != null);
                var newId = (files.Count + 1).ToString("D8"); files[newId] = await file.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(
                    JsonSerializer.Serialize(new { success = true, files = new[] { new { url = $"https://pomf2.lain.la/f/{newId}.bin" } } })) };
            }
            var id = Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath);
            if (request.Method != HttpMethod.Get || request.RequestUri.Host != "pomf2.lain.la" || !files.TryGetValue(id, out var bytes))
                throw new InvalidOperationException("Unexpected fixture media request; public hosts must never be called.");
            Downloads++;
            if (DelayDownload)
            {
                DelayDownload = false; DownloadReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var boundary = bytes.AsSpan(0, 4).SequenceEqual("PKM2"u8) ? 36 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) : 0;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new ProgressiveFixtureStream(bytes, boundary, DownloadReady.Task)) };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}
