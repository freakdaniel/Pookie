using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record ChatLibraryPreviewBinding(ChatLibraryPreview Tile, ChatMessage Message, long Peer, int Index, StackPanel Library, long Revision, long Account);
    private readonly List<ChatLibraryPreviewBinding> chatLibraryPreviews = [];
    private int chatLibraryPreviewActive;
    private void ClearChatLibraryPreviews()
    {
        foreach (var entry in chatLibraryPreviews) entry.Tile.Stop();
        chatLibraryPreviews.Clear();
    }
    private FrameworkElement CreateChatLibraryPreview(ChatMessage message, long peer, int index, ImageSource? source, string name, StackPanel library)
    {
        var preview = new ChatLibraryPreview();
        var binding = new ChatLibraryPreviewBinding(preview,message,peer,index,library,chatRevision,chatAccountId);
        chatLibraryPreviews.Add(binding);
        if (source != null) preview.Show(source);
        else if (!AllowAutomaticChatMedia(message)) preview.Defer();
        var button = TrackButtons.Action(new Border().CornerRadius(8).ClipToBounds().Background(TrackButtons.TonalSurface).BorderThickness(0).Padding(0).Child(preview),
            () =>
            {
                if (preview.Ready) Run(() => JumpChatMessageAsync(peer,message.Id));
                else if (!preview.Loading) { preview.Retry(); QueueChatLibraryPreviews(); }
            }, TrackButtons.Text,94).Padding(0).CornerRadius(8).ToolTip(name);
        ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        return button;
    }
    private bool ChatLibraryPreviewCurrent(ChatLibraryPreviewBinding entry) => !disposed && page.Value == Page.Chats && chatColumns.DetailsOpen &&
        entry.Account == chatAccountId && entry.Revision == chatRevision && chatRecipient?.Id == entry.Peer && chatMediaLibrary == entry.Library && chatLibraryPreviews.Contains(entry);
    private void QueueChatLibraryPreviews()
    {
        if (disposed || !workspace.IsVisible || Window.WindowState == WindowState.Minimized || chatLibraryPreviews.Count == 0 || chatLibraryPreviewActive >= 2 || chatDetailsScroll == null || !chatColumns.DetailsOpen || chatMediaTransition is { Animating: true }) return;
        foreach (var entry in chatLibraryPreviews.ToArray())
        {
            if (chatLibraryPreviewActive >= 2) break;
            if (!entry.Tile.Waiting || !ChatLibraryPreviewCurrent(entry) || entry.Tile.ActualHeight <= 0 ||
                entry.Tile.Bounds.Bottom <= chatDetailsScroll.Bounds.Y || entry.Tile.Bounds.Y >= chatDetailsScroll.Bounds.Bottom) continue;
            entry.Tile.Start(); chatLibraryPreviewActive++;
            Run(() => LoadChatLibraryPreviewAsync(entry));
        }
    }
    private async Task LoadChatLibraryPreviewAsync(ChatLibraryPreviewBinding entry)
    {
        try
        {
            var loaded = await EnsureChatMediaAsync(entry.Message,entry.Peer,automatic:!entry.Tile.Manual);
            if (!ChatLibraryPreviewCurrent(entry)) return;
            if (loaded && entry.Index < 0 && chatMediaLibraryKind is { } kind)
            { await RenderChatMediaLibraryAsync(kind); return; }
            var source = ChatDecoded(ChatMediaCacheId(entry.Message,entry.Peer),entry.Index)?.Source ?? ChatMediaSummaryFor(entry.Message,entry.Peer)?.Photos?.ElementAtOrDefault(entry.Index)?.Source;
            if (source != null) entry.Tile.Show(source); else if (!entry.Tile.Ready) entry.Tile.Fail();
        }
        catch (OperationCanceledException) { }
        catch { if (ChatLibraryPreviewCurrent(entry) && !entry.Tile.Ready) entry.Tile.Fail(); }
        finally { chatLibraryPreviewActive--; QueueChatLibraryPreviews(); }
    }
    private void RefreshChatLibraryPreviews(string cacheId)
    {
        foreach (var entry in chatLibraryPreviews.Where(e => e.Index >= 0 && ChatMediaCacheId(e.Message,e.Peer) == cacheId).ToArray())
            if (ChatLibraryPreviewCurrent(entry) && ChatMediaSummaryFor(entry.Message,entry.Peer)?.Photos?.ElementAtOrDefault(entry.Index)?.Source is { } source) entry.Tile.Show(source);
    }
}
