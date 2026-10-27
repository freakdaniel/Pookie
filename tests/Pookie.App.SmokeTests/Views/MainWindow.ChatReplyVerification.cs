using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatReplyUiAsync(ChatMessage target)
    {
        // Saved-message/selection changes invalidate layout before a resize
        // clock starts. Sample the settled composer, not its preceding frame.
        Window.Activate();
        await WaitForLikedLayoutAsync(()=>Window.IsActive);
        await WaitForLoginFrameAsync();
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && !chatColumns.Animating && !chatComposer.IsMeasureDirty && !chatComposer.IsArrangeDirty);
        var height = chatComposer.ActualHeight; var bottom = chatComposer.Bounds.Bottom; var sendY = SettingsCenter(chatSend).Y;
        var opening = new List<(double Height,double Reply,double Bottom,double Send)>();
        void SampleOpening() => opening.Add((chatComposer.ActualHeight,chatComposerLayout.ReplyHeight,chatComposer.Bounds.Bottom,SettingsCenter(chatSend).Y));
        Window.FrameRendered += SampleOpening;
        ReplyToChat(target);
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatComposerLayout.ReplyHeight > 47.9);
        Window.FrameRendered -= SampleOpening;
        var integrated = false; VisualTree.Visit(chatComposer,element => { if (ReferenceEquals(element,chatReplyBar)) integrated = true; });
        if (!integrated || Math.Abs(chatComposer.ActualHeight-height-48)>1/Window.DpiScale || chatReplyQuote.Title.Length == 0 || chatReplyQuote.Preview != ChatReplyPreview(target,90))
            throw new InvalidOperationException("The reply is not a compact two-line quote inside the composer.");
        if (!opening.Any(s => s.Reply is > 1 and < 47) || opening.Any(s => Math.Abs(s.Bottom-bottom)>1/Window.DpiScale || Math.Abs(s.Send-sendY)>1/Window.DpiScale))
            throw new InvalidOperationException($"Reply expansion snapped or moved the composer bottom/send button: baseline={bottom:F2}/{sendY:F2}; frames="+
                string.Join(";",opening.Select(s=>$"{s.Reply:F2}/{s.Bottom:F2}/{s.Send:F2}")));
        if (ChatRows.Any(row => row.Root.FindVisualRoot() == Window && row.Root.Bounds.Bottom > chatViewport.Bounds.Y && row.Root.Bounds.Y < chatViewport.Bounds.Bottom && row.Root.ActualHeight > 0 &&
            (!row.Bound || row.Message == null || row.Body.Text.Length == 0 || row.Body.ActualWidth < 1)))
            throw new InvalidOperationException("Returning from saved messages left cleared message containers behind the reply composer.");
        await WaitForLoginFrameAsync(); CaptureUiPreview("chats-reply-composer");
        var closing = new List<double>(); void SampleClosing() => closing.Add(chatComposerLayout.ReplyHeight);
        Window.FrameRendered += SampleClosing;
        RouteWaveformClick(SettingsCenter(chatReplyCancel));
        try { await WaitForLikedLayoutAsync(() => chatReply == null && !chatComposerSurface.IsResizing && !chatReplyBar.IsVisible); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"REPLY_CLOSE_STATE reply={chatReply != null} resizing={chatComposerSurface.IsResizing} visible={chatReplyBar.IsVisible} height={chatComposerLayout.ReplyHeight} composer={chatComposer.Bounds} button={chatReplyCancel.Bounds} hit={Window.HitTest(SettingsCenter(chatReplyCancel))?.GetType().Name}: {error.Message}");
            throw;
        }
        Window.FrameRendered -= SampleClosing;
        // Compare device pixels: at 110 DPI a one-pixel difference expressed
        // in DIPs can exceed 96/110 by a few floating-point ulps.
        if (!closing.Any(h => h is > 1 and < 47) || Math.Round(Math.Abs(chatComposer.ActualHeight-height)*Window.DpiScale, 9)>1)
            throw new InvalidOperationException($"Reply cancellation discarded its content before the collapse animation: height={height}->{chatComposer.ActualHeight}, frames={string.Join(',', closing.Select(h => h.ToString("F2")))}.");

        // A known media preview belongs in the same quote. No media download or
        // real transport is involved, and the fixture never enters the timeline.
        var photo = new ChatMessage("reply-photo-fixture",90,new ChatMediaReference('c',"quote",new byte[16]).Code,DateTimeOffset.UtcNow);
        var thumbnail = ImageSource.FromBgraPixels(2,2,[40,130,240,255,50,140,230,255,60,150,220,255,70,160,210,255],hasAlpha:false);
        var key = ChatMediaCacheId(photo,90); var favorites = chatWorkspace.Favorites;
        chatMediaSummaries[key] = new("Фото",thumbnail,new Size(2,2),"Смотри это фото");
        chatWorkspace = chatWorkspace with { Favorites = [..favorites,new(90,photo,DateTimeOffset.UtcNow)] };
        try
        {
            ReplyToChat(photo);
            await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatComposerLayout.ReplyHeight > 47.9 && chatReplyQuote.HasThumbnail);
            if (!chatReplyQuote.Title.Contains(transportName()) || chatReplyQuote.Preview != "Смотри это фото")
                throw new InvalidOperationException("Reply media quote lost its author, caption or thumbnail.");
            await WaitForLoginFrameAsync(); CaptureUiPreview("chats-reply-photo-composer");
        }
        finally
        {
            chatWorkspace = chatWorkspace with { Favorites = favorites }; chatMediaSummaries.Remove(key);
            chatReply = null; RefreshChatReply(); SaveChatDraft();
        }
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && !chatReplyBar.IsVisible);

        // An unavailable original keeps its saved preview, rather than claiming
        // deletion or exposing the protocol envelope to the user.
        chatReply = new(90,"reply-not-cached","Сохранённое превью"); RefreshChatReply();
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing);
        if (chatReplyQuote.Preview != "Сохранённое превью") throw new InvalidOperationException("Missing reply original lost its stored preview.");
        ReplyToChat(target);
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatReplyQuote.Preview == ChatReplyPreview(target,90));
        string transportName() => chatConversations.First(c => c.Recipient.Id == 90).Recipient.Username;
    }
}
