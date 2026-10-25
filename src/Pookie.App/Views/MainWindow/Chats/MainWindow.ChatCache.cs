using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Account-scoped bounded hot windows. Full history lives in the SQLite worker.
    private sealed record ChatThreadSnapshot(ChatMessage[] Messages, Dictionary<string, DateTimeOffset> Order,
        string? Cursor, string? LatestCursor, double Offset, bool AtBottom, string? AnchorId, double AnchorY, Dictionary<string,Dictionary<long,string>>? Reactions=null, bool HasNewer=false);
    private readonly Dictionary<long, ChatThreadSnapshot> chatThreadCache = [];
    private readonly LinkedList<long> chatThreadRecency = [];
    private bool chatThreadLoaded;
    private string? chatLatestCursor;
    private double? chatRestoreOffset;
    private int chatRestorePasses;
    private string? chatRestoreAnchor;
    private double chatRestoreAnchorY;

    private void RememberChatThread()
    {
        if (chatRecipient == null || !chatThreadLoaded) return;
        var viewer = chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
        var trimmed = chatMessages.Count > 200;
        var messages = chatMessages.TakeLast(trimmed ? 30 : 200).ToArray();
        var anchor = viewer == null ? null : ChatRows.Where(r => r.Bound && r.Message != null && r.Root.Bounds.Bottom > viewer.Bounds.Y + 1 && r.Root.Bounds.Y < viewer.Bounds.Bottom)
            .OrderBy(r => r.Root.Bounds.Y).FirstOrDefault();
        chatThreadCache[chatRecipient.Id] = new(messages,
            chatMessageOrder.Where(p => messages.Any(m => m.Id == p.Key)).ToDictionary(), trimmed ? chatLatestCursor : chatMessageCursor, chatLatestCursor,
            trimmed ? 0 : chatRestoreOffset ?? viewer?.VerticalOffset ?? 0, trimmed || ChatIsAtBottom(), trimmed ? null : anchor?.Message?.Id, anchor == null || viewer == null ? 0 : anchor.Root.Bounds.Y - viewer.Bounds.Y, chatReactions.Where(p=>messages.Any(m=>m.Id==p.Key)).ToDictionary(p=>p.Key,p=>new Dictionary<long,string>(p.Value)),chatWindowHasNewer);
        chatWorkspace.Positions[chatRecipient.Id] = new(anchor?.Message?.Id, anchor == null || viewer == null ? 0 : anchor.Root.Bounds.Y - viewer.Bounds.Y, ChatIsAtBottom());
        SaveChatWorkspace();
        foreach(var peer in chatThreadCache.Keys.Where(p=>p!=chatRecipient.Id).ToArray())
        {
            var other=chatThreadCache[peer];
            if(other.Messages.Length<=100)continue;
            var start=other.AnchorId==null?Math.Max(0,other.Messages.Length-100):Math.Max(0,Array.FindIndex(other.Messages,m=>m.Id==other.AnchorId)-8);
            var small=other.Messages.Skip(start).Take(100).ToArray();
            chatThreadCache[peer]=other with {Messages=small,Order=other.Order.Where(p=>small.Any(m=>m.Id==p.Key)).ToDictionary()};
        }
        chatThreadRecency.Remove(chatRecipient.Id); chatThreadRecency.AddLast(chatRecipient.Id);
        while (chatThreadRecency.Count > 3 || chatThreadCache.Values.Sum(t => t.Messages.Sum(m => (long)m.Body.Length * 2 + 256)) > 8 * 1024 * 1024)
        { var oldest = chatThreadRecency.First!.Value; chatThreadRecency.RemoveFirst(); chatThreadCache.Remove(oldest); }
    }

    private sealed record ChatDecodedPhoto(ImageSource Source, ImageSource? Backdrop, byte[]? Gif = null)
    { internal long Bytes => (Gif?.Length ?? 0) + ((long)Source.PixelWidth * Source.PixelHeight + (long)(Backdrop?.PixelWidth ?? 0) * (Backdrop?.PixelHeight ?? 0)) * 4; }
    private readonly Dictionary<string, ChatDecodedPhoto> chatDecodedPhotos = [];
    private readonly LinkedList<string> chatDecodedRecency = [];
    private long chatDecodedBytes;
    private static string ChatPhotoKey(string mediaKey, int index) => $"{mediaKey}:{index}";
    private ChatDecodedPhoto? ChatDecoded(string mediaKey, int index)
    {
        var key = ChatPhotoKey(mediaKey, index);
        if (!chatDecodedPhotos.TryGetValue(key, out var photo)) return null;
        chatDecodedRecency.Remove(key); chatDecodedRecency.AddLast(key); return photo;
    }
    private void CacheChatDecoded(string mediaKey, int index, ChatDecodedPhoto photo)
    {
        var key = ChatPhotoKey(mediaKey, index);
        if (chatDecodedPhotos.Remove(key, out var old)) chatDecodedBytes -= old.Bytes;
        chatDecodedRecency.Remove(key);
        chatDecodedPhotos[key] = photo; chatDecodedBytes += photo.Bytes; chatDecodedRecency.AddLast(key);
        while (chatDecodedRecency.Count > 64 || chatDecodedBytes > 24 * 1024 * 1024)
        {
            var oldest = chatDecodedRecency.First!.Value; chatDecodedRecency.RemoveFirst();
            if (chatDecodedPhotos.Remove(oldest, out var removed)) chatDecodedBytes -= removed.Bytes;
        }
    }
    private async Task PrepareOutgoingChatMediaAsync(ChatOutgoingItem outgoing, Messages.ChatMediaPackage package, long peer, CancellationToken token)
    {
        var key = ChatMediaCacheId(outgoing.Message, peer);
        var summary = await PrepareChatMediaSummaryAsync(package, token);
        var decoded = await Task.Run(() => package.Attachments.Select(attachment =>
        {
            if (attachment.AsFile || !attachment.MediaType.StartsWith("image/", StringComparison.Ordinal) || PrepareChatImage(attachment.Data) is not { } source) return null;
            return new ChatDecodedPhoto(source, ArtworkBackdrop.Create(source, .6));
        }).ToArray(), token);
        if (!ChatWriteCurrent(package.SenderId, token)) return;
        AddChatMediaSummary(key, summary);
        for (var index = 0; index < decoded.Length; index++) if (decoded[index] is { } photo) CacheChatDecoded(key, index, photo);
        if (chatRecipient?.Id == peer)
            foreach (var row in ChatRows.Where(r => r.Bound && r.Message?.Id == outgoing.Message.Id).ToArray()) RenderChatMedia(row, package);
    }

    private void CommitOutgoingChatMedia(ChatOutgoingItem outgoing, Messages.ChatMediaPackage package, long peer, string code)
    {
        var oldKey = ChatMediaCacheId(outgoing.Message, peer);
        var newKey = ChatMediaCacheId(outgoing.Message with { Body = code }, peer);
        if (chatMediaSummaries.TryGetValue(oldKey, out var summary)) AddChatMediaSummary(newKey, summary);
        for (var index = 0; index < Math.Max(package.Attachments.Length, package.GifUrl != null ? 1 : 0); index++)
            if (ChatDecoded(oldKey, index) is { } photo)
            {
                var oldPhotoKey = ChatPhotoKey(oldKey, index);
                chatDecodedPhotos.Remove(oldPhotoKey); chatDecodedRecency.Remove(oldPhotoKey); chatDecodedBytes -= photo.Bytes;
                CacheChatDecoded(newKey, index, photo);
                if (package.GifUrl is { } gif && photo.Gif is { } bytes && Messages.ChatMediaReference.Parse(code) is { } reference)
                    Run(() => GifMedia.StoreAsync(reference, gif, package.SenderId, package.RecipientId, bytes, chatAccountLifetime?.Token ?? lifetime.Token));
            }
        // The upload's original buffers are zeroed by its caller. Cache independent
        // originals for Save, and retain the already displayed decoded pixels.
        AddChatMediaCache(newKey, package with { Attachments = package.Attachments.Select(a => a with { Data = a.Data.ToArray() }).ToArray() });
        if (chatRecipient?.Id == peer)
            foreach (var row in ChatRows.Where(r => r.Message?.Id == outgoing.Message.Id)) row.MediaKey = newKey;
    }

    private void ClearChatVisualCache()
    {
        chatReplyLookups.Clear();
        chatMusicCache.Clear(); chatMusicLoads.Clear(); chatMusicAttempted.Clear();
        chatMusicRecognizeTimer.Stop(); ++chatMusicRecognition; chatMusicDraft = null; chatMusicEditor?.Update([]);
        chatDecodedPhotos.Clear(); chatDecodedRecency.Clear(); chatDecodedBytes = 0;
        chatThreadCache.Clear(); chatThreadRecency.Clear(); chatRestoreOffset = null; chatRestoreAnchor = null;
    }
}
