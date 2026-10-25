using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void ApplyChatPreferences(AppConfiguration before)
    {
        if (before.ChatSaveDrafts != preferences.ChatSaveDrafts)
        {
            EnsureChatAccount();
            chatDraftTimer.Stop();
            if (!preferences.ChatSaveDrafts) chatDrafts.Clear();
            SaveChatDraft(); // Pending sends and read positions are always durable.
        }
        if (before.ChatBackgroundRefresh != preferences.ChatBackgroundRefresh)
        {
            if (!AllowChatRefresh) CancelChatPolling();
            chatNextPoll = default;
        }
        if (before.ChatAutoLoad != preferences.ChatAutoLoad) CancelChatAutomaticMedia();
        if (before.KlipyEnabled != preferences.KlipyEnabled && !preferences.KlipyEnabled)
        {
            CloseChatGifs(); CancelKlipyReads();
            if (chatSelectedGif != null) { chatSelectedGif = null; UpdateChatAttachments(); }
        }
        if (before.ChatAnimateGifs != preferences.ChatAnimateGifs)
        {
            foreach (var animation in chatAnimations.Values) animation.Dispose();
            chatAnimations.Clear();
            if (preferences.ChatAnimateGifs && chatGifsOpen.Value) Run(SearchChatGifsAsync);
        }
        if (before.ChatAutoPlayVideo != preferences.ChatAutoPlayVideo)
            foreach (var row in ChatRows)
                VisualTree.Visit(row.Attachments, child => { if (child is ChatVideoFrame frame) frame.RefreshAutoPlay(); });
        if (before.ChatAutoLoad != preferences.ChatAutoLoad || before.KlipyEnabled != preferences.KlipyEnabled ||
            before.ChatAnimateGifs != preferences.ChatAnimateGifs)
            RefreshChatMediaPreferences();
        RefreshChatComposer();
    }

    private void RefreshChatMediaPreferences()
    {
        foreach (var row in ChatRows.Where(row => row.Bound && row.Message != null &&
            (ChatMediaReference.Parse(row.Message.Body) != null || ChatOutgoing(row.Message)?.HasMedia == true)).ToArray())
        {
            row.Load?.Cancel(); row.Load?.Dispose(); row.Load = null;
            ++row.MediaGeneration; row.MediaResolved = false;
            StopChatAnimations(row.Attachments); row.Attachments.Clear(); row.ResetMediaLayout();
            BindChatMediaPreview(row); row.RefreshDelivery(); row.Root.InvalidateMeasure();
        }
        chatMessagesView.InvalidateMeasure();
    }

    private async Task ClearChatDraftsAsync()
    {
        EnsureChatAccount();
        chatDraftTimer.Stop(); chatDrafts.Clear();
        var voices=chatWorkspace.Drafts?.Values.Select(d=>d.Voice).OfType<ChatVoiceDraft>().ToArray()??[];
        await DeleteChatVoiceAsync();
        foreach(var draft in voices)
        {
            var root=Path.Combine(dataPaths.Root,"Chats",chatAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture),"VoiceDrafts")+Path.DirectorySeparatorChar;
            if(Path.GetFullPath(draft.Path).StartsWith(root,StringComparison.Ordinal))TryDeleteChatVoice(draft.Path);
        }
        chatWorkspace=chatWorkspace with{Drafts=null};chatReply=null;RefreshChatReply();SaveChatWorkspace();
        chatRenderingDraft = true;
        try { chatInput.Text = ""; }
        finally { chatRenderingDraft = false; }
        ClearChatAttachments();
        SaveChatState(); RefreshChatComposer();
    }

    private void ClearChatMemoryCache()
    {
        CancelChatReads();
        foreach (var animation in chatAnimations.Values) animation.Dispose();
        chatAnimations.Clear();
        ClearChatMediaCache(clearSummaries: true); ClearChatVisualCache();
        if (page.Value == Page.Chats) RefreshChatTimeline();
    }
}
