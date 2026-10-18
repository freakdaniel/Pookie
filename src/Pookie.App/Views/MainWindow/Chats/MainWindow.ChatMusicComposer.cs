using Aprillz.MewUI;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatMusicEditor? chatMusicEditor;
    private readonly DispatcherTimer chatMusicRecognizeTimer = new(TimeSpan.FromMilliseconds(250));
    private string? chatMusicDraft;
    private long chatMusicDraftRevision = -1, chatMusicRecognition;

    private void InitializeChatMusicEditor()
    {
        chatMusicEditor = new(chatInput, url => Run(async () =>
        {
            var account = chatAccountId;
            var item = await ResolveChatMusicAsync(url);
            if (disposed || account != chatAccountId) return;
            if (item.Track is { } track) await OpenTrackPageAsync(track); else await OpenLibraryItemAsync(item);
        }));
        chatMusicRecognizeTimer.Tick += () => { chatMusicRecognizeTimer.Stop(); Run(RecognizeChatMusicAsync); };
    }

    private void RefreshChatMusicComposer()
    {
        if (chatMusicEditor == null || chatMusicDraft == chatInput.Text && chatMusicDraftRevision == chatRevision) return;
        chatMusicDraft = chatInput.Text; chatMusicDraftRevision = chatRevision; ++chatMusicRecognition;
        chatMusicRecognizeTimer.Stop(); UpdateChatMusicChips();
        if (chatRecipient != null && ChatMusicLink.Find(chatInput.Text).Length > 0) chatMusicRecognizeTimer.Start();
    }

    private void UpdateChatMusicChips()
    {
        var links = ChatMusicLink.Find(chatInput.Text).Select(url => (Url: url, Item: chatMusicCache.GetValueOrDefault(url)))
            .Where(value => value.Item != null).Select(value => new ChatMusicEditor.Link(value.Url,
                value.Item!.Title, CachedCollectionArtwork(value.Item))).ToArray();
        chatMusicEditor?.Update(links);
    }

    private async Task RecognizeChatMusicAsync()
    {
        if (chatRecipient == null || me == null || chatAccountLifetime == null || chatSending) return;
        var generation = chatMusicRecognition; var account = chatAccountId; var revision = chatRevision;
        bool Current() => !disposed && account == chatAccountId && revision == chatRevision && generation == chatMusicRecognition;
        foreach (var url in ChatMusicLink.Find(chatInput.Text))
        {
            try
            {
                var item = await ResolveChatMusicAsync(url);
                if (!Current()) return;
                UpdateChatMusicChips();
                await GetCollectionArtworkAsync(item);
                if (!Current()) return;
                UpdateChatMusicChips();
            }
            catch (OperationCanceledException) { return; }
            catch { } // An unavailable link remains editable as ordinary text.
        }
    }
}
