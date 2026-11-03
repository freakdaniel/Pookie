using Aprillz.MewUI;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatMusicEditor? chatMusicEditor;
    private readonly DispatcherTimer chatMusicRecognizeTimer = new(TimeSpan.FromMilliseconds(200));
    private string? chatMusicDraft;
    private long chatMusicDraftRevision = -1, chatMusicRecognition;
    private readonly Dictionary<string, LinkPreviewData> chatComposerLinks = new(StringComparer.Ordinal);

    private void InitializeChatMusicEditor()
    {
        chatMusicEditor = new(chatInput, url => Run(() => OpenChatMusicUrlAsync(url)));
        chatMusicRecognizeTimer.Tick += () => { chatMusicRecognizeTimer.Stop(); Run(RecognizeChatMusicAsync); };
    }

    private async Task OpenChatMusicUrlAsync(string url)
    {
        var account = chatAccountId;
        var item = await ResolveLinkPreviewItemAsync(url, ChatReadToken());
        if (disposed || account != chatAccountId) return;
        if (item.Track is { } track) await OpenTrackPageAsync(track); else await OpenLibraryItemAsync(item);
    }

    private void RefreshChatMusicComposer()
    {
        if (chatMusicEditor == null || chatMusicDraft == chatInput.Text && chatMusicDraftRevision == chatRevision) return;
        chatMusicDraft = chatInput.Text; chatMusicDraftRevision = chatRevision; ++chatMusicRecognition;
        chatMusicRecognizeTimer.Stop(); UpdateChatMusicChips();
        if (chatRecipient != null && SoundCloudLinks.Find(chatInput.Text).Any(url => !chatComposerLinks.ContainsKey(url) || chatComposerLinks[url].Loading)) chatMusicRecognizeTimer.Start();
    }

    private void UpdateChatMusicChips()
    {
        var urls = SoundCloudLinks.Find(chatInput.Text);
        foreach (var old in chatComposerLinks.Keys.Except(urls).ToArray()) chatComposerLinks.Remove(old);
        var links = urls.Select(url => chatComposerLinks.TryGetValue(url, out var state) ? state :
            chatMusicCache.TryGetValue(url, out var item) && (CachedCollectionArtwork(item) != null || CollectionArtworkUrl(item) == null)
                ? new LinkPreviewData(url, item.Title, CachedCollectionArtwork(item)) : LinkPreviewData.Pending(url)).ToArray();
        foreach (var link in links) chatComposerLinks[link.Url] = link;
        chatMusicEditor?.Update(links);
    }

    private async Task RecognizeChatMusicAsync()
    {
        if (chatRecipient == null || me == null || chatAccountLifetime == null || chatSending) return;
        var generation = chatMusicRecognition; var account = chatAccountId; var revision = chatRevision;
        bool Current() => !disposed && account == chatAccountId && revision == chatRevision && generation == chatMusicRecognition;
        var token = ChatReadToken();
        foreach (var url in SoundCloudLinks.Find(chatInput.Text))
        {
            if (chatComposerLinks.TryGetValue(url, out var known) && !known.Loading) continue;
            try
            {
                var (item, cover) = await LoadLinkPreviewAsync(url, token);
                if (!Current()) return;
                CacheChatMusic(url, item);
                chatComposerLinks[url] = new(url, item.Title, cover);
                UpdateChatMusicChips();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
            {
                if (!Current()) return;
                chatComposerLinks[url] = LinkPreviewData.Missing(url); UpdateChatMusicChips();
                Pookie.Logging.AppLog.For("Pookie.Links").Debug(error, "Не удалось распознать ссылку в сообщении");
            }
        }
    }
}
