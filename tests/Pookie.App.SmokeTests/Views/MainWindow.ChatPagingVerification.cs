using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatAutomaticHistoryAsync(ChatFixtureTransport transport, ScrollViewer viewer)
    {
        transport.DelayOlder = true; transport.OlderReady = null;
        viewer.SetScrollOffsets(0, 0);
        await WaitForLikedLayoutAsync(() => transport.OlderReady != null && chatThreadBusy);
        var canceled = transport.OlderReady;
        await SelectChatAsync(transport.Third);
        canceled!.TrySetResult();
        await WaitForLikedLayoutAsync(() => chatRecipient?.Id == transport.Third.Id && !chatThreadBusy);
        if (chatWindowBusy || chatMessages.Count != 0)
            throw new InvalidOperationException("Switching chats retained a canceled history read or leaked the previous conversation.");
        transport.DelayOlder = true; transport.OlderReady = null;
        await SelectChatAsync(transport.Other);
        await WaitForLikedLayoutAsync(() => transport.OlderReady != null && chatThreadBusy);
        var anchor = ChatRows.Where(row => row.Bound && row.Message != null && row.Root.Bounds.Bottom > viewer.Bounds.Y + 1 &&
            row.Root.Bounds.Y < viewer.Bounds.Bottom).OrderBy(row => row.Root.Bounds.Y).First();
        var id = anchor.Message!.Id; var y = anchor.Bubble.Bounds.Y - viewer.Bounds.Y;
        // The second page is held until the automatic read is observed. No
        // pagination command, button click or fixed network delay triggers it.
        transport.OlderReady!.SetResult();
        await WaitForLikedLayoutAsync(() => chatMessages.Count == 35 && chatMessageCursor == null &&
            !chatThreadBusy && !chatWindowBusy && chatRestoreOffset == null);
        var restored = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == id);
        if (restored == null || Math.Abs(restored.Bubble.Bounds.Y - viewer.Bounds.Y - y) > 1.5 ||
            chatMessages.Select(message => message.Id).Distinct().Count() != 35)
            throw new InvalidOperationException("Automatic chat history paging moved the reading anchor or duplicated messages.");
        var reads = transport.OlderReads;
        viewer.SetScrollOffsets(0, 0);
        await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
        if (transport.OlderReads != reads)
            throw new InvalidOperationException("Exhausted chat history restarted its network cursor.");
        RequestChatBottom();
        await WaitForLikedLayoutAsync(() => !chatScrollBottom && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 10);
        Console.WriteLine("CHAT_AUTOMATIC_HISTORY_OK: first 30 messages page without a button; canceled reads stay with the previous chat; returning resumes automatic paging; delayed older page preserves the anchor, no duplicate rows or exhausted-cursor reads");
        await VerifyChatBoundedHistoryAsync(viewer);
    }

    private async Task VerifyChatBoundedHistoryAsync(ScrollViewer viewer)
    {
        var originals=chatMessages.ToArray();var originalHistory=chatHistory;var originalCursor=chatMessageCursor;
        var fixtures=Enumerable.Range(0,300).Select(index=>new ChatMessage("window-"+index,90,"Cached history "+index,DateTimeOffset.UnixEpoch.AddMinutes(index))).ToArray();
        await using var store=new ChatHistoryStore(new Storage.AppDataPaths(Path.Combine(dataPaths.Root,"PagingFixture-"+Guid.NewGuid().ToString("N"))),42);
        await store.SavePageAsync(42,90,new(fixtures,null));
        try
        {
            chatHistory=store;chatMessageCursor=null;chatOlderCacheBoundary=null;chatWindowHasNewer=false;
            chatMessages.Clear();chatMessages.AddRange(fixtures.Skip(100));RefreshChatTimeline();RequestChatBottom();
            await WaitForLikedLayoutAsync(()=>!chatScrollBottom&&!chatWindowBusy&&SmoothScroll.Maximum(viewer)-viewer.VerticalOffset<10);
            viewer.SetScrollOffsets(0,0);Window.PerformLayout();
            var anchor=ChatRows.Where(row=>row.Bound&&row.Message!=null&&row.Root.Bounds.Bottom>viewer.Bounds.Y+1&&row.Root.Bounds.Y<viewer.Bounds.Bottom).OrderBy(row=>row.Root.Bounds.Y).First();
            var id=anchor.Message!.Id;var y=anchor.Bubble.Bounds.Y-viewer.Bounds.Y;
            var trace=new List<string>();
            void Trace()=>trace.Add($"offset={viewer.VerticalOffset:F2}, restore={chatRestoreAnchor}, target={chatRestoreAnchorY:F2}, bubble={chatRestoreBubbleAnchor}, pass={chatRestorePasses}, row={ChatRows.FirstOrDefault(row=>row.Bound&&row.Message?.Id==id)?.Bubble.Bounds.Y-viewer.Bounds.Y:F2}");
            Window.FrameRendered+=Trace;
            try
            {
            await WaitForLikedLayoutAsync(()=>!chatWindowBusy&&chatRestoreOffset==null&&chatMessages[0].Id=="window-0");
            }
            finally { Window.FrameRendered-=Trace; }
            var restored=ChatRows.FirstOrDefault(row=>row.Bound&&row.Message?.Id==id);
            if(chatMessages.Count!=200||!chatWindowHasNewer||restored==null||Math.Abs(restored.Bubble.Bounds.Y-viewer.Bounds.Y-y)>1.5)
                throw new InvalidOperationException($"Cached older paging exceeded the 200-message window or moved its reading anchor: count={chatMessages.Count}, newer={chatWindowHasNewer}, id={id}, before={y:F2}, after={restored?.Bubble.Bounds.Y-viewer.Bounds.Y:F2}; {string.Join(';',trace.Take(24))}");
            viewer.SetScrollOffsets(0,SmoothScroll.Maximum(viewer));
            await WaitForLikedLayoutAsync(()=>!chatWindowBusy&&chatRestoreOffset==null&&chatMessages[^1].Id=="window-299");
            if(chatMessages.Count!=200||chatWindowHasNewer)
                throw new InvalidOperationException("Automatic forward paging failed to restore the newer cached window.");
            Console.WriteLine("CHAT_BOUNDED_HISTORY_OK: 300 cached messages page automatically in both directions, 200-message cap and stable visible anchor");
        }
        finally
        {
            chatHistory=originalHistory;chatMessageCursor=originalCursor;chatOlderCacheBoundary=null;chatWindowHasNewer=false;
            chatRestoreAnchor=null;chatRestoreOffset=null;chatMessages.Clear();chatMessages.AddRange(originals);
            RefreshChatTimeline();RequestChatBottom();
        }
        await WaitForLikedLayoutAsync(()=>!chatScrollBottom&&!chatWindowBusy);
        RememberChatThread();
    }
}
