using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatCachingAsync(ChatFixtureTransport transport, ChatFixtureMediaHandler media)
    {
        byte[] photo;
        using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-4k.jpg")!)
        using (var output = new MemoryStream()) { resource.CopyTo(output); photo = output.ToArray(); }
        var legacy = media.Add(new(90, 42, "Старое фото", [new("legacy.jpg", "image/jpeg", photo)]), legacy: true);
        media.DelayDownload = true; media.DownloadReady = null;
        transport.AddIncoming(legacy); await RefreshChatsAsync();
        await WaitForLikedLayoutAsync(() => media.DownloadReady != null && ChatRows.Any(r => r.Message?.Body == legacy &&
            r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Skeleton.Running: true, ActualHeight: > 0 }));
        var row = ChatRows.First(r => r.Message?.Body == legacy);
        var skeleton = (ChatPhotoFrame)row.Attachments.FindVisualChild<ChatPhotoFrame>()!;
        if (skeleton.IntrinsicSize != new Size(240, 180) || row.Attachments.FindVisualChild<TextBlock>() != null)
            throw new InvalidOperationException("Unknown legacy media did not reserve a medium skeleton.");
        var before = row.Bubble.ActualWidth;
        CaptureUiPreview("chats-legacy-skeleton");
        media.DownloadReady!.SetResult();
        await WaitForLikedLayoutAsync(() => row.Content.IsResizing);
        var widths = new List<double>();
        while (row.Content.IsResizing) { widths.Add(row.Bubble.ActualWidth); await Task.Delay(16); }
        await WaitForLikedLayoutAsync(() => row.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null });
        if (!widths.Any(width => width > Math.Min(before, row.Bubble.ActualWidth) + 1 && width < Math.Max(before, row.Bubble.ActualWidth) - 1))
            throw new InvalidOperationException("Legacy media size snapped instead of interpolating.");
        CaptureUiPreview("chats-legacy-loaded");
        var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
        await WaitForLikedLayoutAsync(() => !chatScrollBottom);
        var source = ((ChatPhotoFrame)row.Attachments.FindVisualChild<ChatPhotoFrame>()!).Picture.Source;
        // Evict original files while their decoded display remains cached.
        for (var index = 0; index < 17; index++) AddChatMediaCache("eviction-fixture-" + index, new(90, 42, "", [new("file.bin", "application/octet-stream", [1])]));
        if (!ReferenceEquals(source, ((ChatPhotoFrame)row.Attachments.FindVisualChild<ChatPhotoFrame>()!).Picture.Source))
            throw new InvalidOperationException("Original-file cache eviction tore down a visible image.");
        var downloads = media.Downloads;
        var messages = chatMessages.Select(m => m.Id).ToArray();
        viewer.SetScrollOffsets(0, Math.Max(0, SmoothScroll.Maximum(viewer) - 180));
        await WaitForLoginFrameAsync();
        var savedAnchor = ChatRows.Where(r => r.Bound && r.Message != null && r.Root.Bounds.Bottom > viewer.Bounds.Y + 1 && r.Root.Bounds.Y < viewer.Bounds.Bottom)
            .OrderBy(r => r.Root.Bounds.Y).First();
        var savedAnchorId = savedAnchor.Message!.Id;
        var savedAnchorY = savedAnchor.Root.Bounds.Y - viewer.Bounds.Y;
        var avatars = chatInboxBindings.ToDictionary(p => p.Value.Recipient.Id, p => ((Image)p.Key.FindVisualChild<Image>()!).Source);
        await SelectChatAsync(transport.Third);
        transport.DelayThread = true; transport.ThreadReady = null;
        var openingFrames = new List<double>();
        void PaintOpening()
        {
            if (chatRecipient?.Id != transport.Other.Id) return;
            var anchor = ChatRows.FirstOrDefault(r => r.Bound && r.Message?.Id == savedAnchorId);
            openingFrames.Add(anchor == null ? double.PositiveInfinity : anchor.Root.Bounds.Y - viewer.Bounds.Y);
            if(anchor==null||Math.Abs(anchor.Root.Bounds.Y-viewer.Bounds.Y-savedAnchorY)>1)
                Console.WriteLine($"CHAT_RESTORE_FRAME: restore={chatRestoreOffset}, id={chatRestoreAnchor}, passes={chatRestorePasses}, offset={viewer.VerticalOffset}, max={SmoothScroll.Maximum(viewer)}, y={anchor?.Root.Bounds.Y-viewer.Bounds.Y}, target={savedAnchorY}, bottom={chatScrollBottom}, wasBottom={chatWasAtBottom}, dirty={chatMessagesView.IsMeasureDirty}/{chatMessagesView.IsArrangeDirty}");
        }
        chatViewport.BeforePaint += PaintOpening;
        var returning = SelectChatAsync(transport.Other);
        if (chatThreadLoading.Skeleton.IsVisible || !chatMessages.Select(m => m.Id).SequenceEqual(messages))
            throw new InvalidOperationException("Returning to a cached thread hid its messages behind a skeleton.");
        await WaitForLikedLayoutAsync(() => transport.ThreadReady != null && ChatRows.Any(r => r.Bound && r.Message?.Id == savedAnchorId &&
            Math.Abs(r.Root.Bounds.Y - viewer.Bounds.Y - savedAnchorY) < 1));
        transport.ThreadReady!.SetResult(); await returning;
        await WaitForLoginFrameAsync(); chatViewport.BeforePaint -= PaintOpening;
        if (openingFrames.Count == 0 || openingFrames.Any(y => Math.Abs(y - savedAnchorY) > 1))
            throw new InvalidOperationException($"Cached thread painted an intermediate scroll position: expected={savedAnchorY}, actual={string.Join(',', openingFrames)}");
        await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == legacy && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
        var restored = ChatRows.First(r => r.Message?.Body == legacy);
        if (!ReferenceEquals(source, ((ChatPhotoFrame)restored.Attachments.FindVisualChild<ChatPhotoFrame>()!).Picture.Source) || media.Downloads != downloads)
            throw new InvalidOperationException("Switching chats redownloaded media or discarded cached decoded pixels.");
        await RefreshChatsAsync();
        foreach (var pair in chatInboxBindings)
            if (avatars.TryGetValue(pair.Value.Recipient.Id, out var avatar) && !ReferenceEquals(avatar, ((Image)pair.Key.FindVisualChild<Image>()!).Source))
                throw new InvalidOperationException("Inbox polling replaced an unchanged avatar.");
        RequestChatBottom(); await WaitForLikedLayoutAsync(() => !chatScrollBottom);
        if (chatThreadCache.Count > 8 || chatDecodedBytes > 24 * 1024 * 1024 || chatMediaBytes > ChatMediaCrypto.MaxPayloadBytes)
            throw new InvalidOperationException("Chat caches exceeded their memory bounds.");
    }
    private async Task VerifyChatTimestampAlignmentAsync(ChatFixtureTransport transport, ChatFixtureMediaHandler media)
    {
        byte[] photo;
        using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.App.Assets.Stickle.pixel-heart.png")!)
        using (var output = new MemoryStream()) { resource.CopyTo(output); photo = output.ToArray(); }
        transport.AddIncoming("Входящее"); transport.AddOwn("Исходящее");
        var code = media.Add(new(42, 90, "Подпись", [new("photo.png", "image/png", photo)]));
        transport.AddOwn(code); await RefreshChatsAsync(); RequestChatBottom();
        await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Bound && r.Body.Text == "Подпись" && !r.Content.IsResizing && r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
        await WaitForLoginFrameAsync();
        var rows = new[] { "Входящее", "Исходящее", "Подпись" }.Select(text => ChatRows.First(r => r.Bound && r.Body.Text == text)).ToArray();
        var reference = rows[0].Time.Bounds.Bottom - rows[0].Body.Bounds.Bottom;
        if (Math.Abs(reference - 1) > .5 || rows.Any(row => !row.Content.InlineFooter || Math.Abs(row.Time.Bounds.Bottom - row.Body.Bounds.Bottom - reference) > .5))
            throw new InvalidOperationException($"Timestamp baselines differ: {string.Join(',', rows.Select(row => row.Time.Bounds.Bottom - row.Body.Bounds.Bottom))}");
        CaptureUiPreview("chats-timestamp-baselines");
    }

}
