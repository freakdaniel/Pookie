using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyShareMessageSendingAsync(SoundCloudTrack track)
    {
        var transport = api.BrowserTransport!;
        var fixture = new ShareMessageFixture(transport);
        api.BrowserTransport = fixture;
        await EnsureChatAccountAsync();
        var conversations = chatConversations.ToArray();
        var selected = chatRecipient; var input = chatInput.Text;
        var drafts = chatDrafts.ToDictionary();
        var initialPage = page.Value;
        var peer = new SoundCloudUser { Id = 77, Username = "Получатель", FullName = "Александр Соколов" };
        try
        {
            chatTimer.Stop();
            chatConversations.Clear();
            foreach (var index in Enumerable.Range(0, 12))
                chatConversations.Add(new("share-message-" + index, peer with { Id = 77 + index, Username = "Получатель " + index }, null, true));
            ShareTrack(track); SelectShareTab(1);
            await SearchShareRecipientsAsync();
            await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Resizing && shareRecipientChoices.Children.Count == 12);
            shareRecipientResults.AutoHideScrollBars = false;
            var bar = (ScrollBar)VisualTree.Find(shareRecipientResults, element => element is ScrollBar { Orientation: Orientation.Vertical, IsVisible: true })!;
            if (bar == null || Math.Abs(bar.Bounds.Right - shareContent.BodyBounds.Right + 6) > 1 || bar.Bounds.X <= shareSearchGlass.Bounds.Right)
                throw new InvalidOperationException("Recipient scrollbar did not move into the card's outer gutter.");
            if (Window.HitTest(bar.Bounds.Center) is not { } hit || hit != bar && !bar.IsAncestorOf(hit))
                throw new InvalidOperationException("Recipient scrollbar is outside its parents' hit-test bounds.");
            if (shareSearchGlass.Background.A == 255 || shareSearchGlyph.Ink != MutedIcon)
                throw new InvalidOperationException("Recipient search lost glass or its resting icon color.");
            CaptureUiPreview("share-recipients-scroll");
            fixture.SearchHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            shareRecipientSearch.Text = "али";
            var searching = SearchShareRecipientsAsync();
            await WaitForLikedLayoutAsync(() => shareSearchGlyph.Progress is > 0 and < 1);
            CaptureUiPreview("share-search-to-spinner");
            await WaitForLikedLayoutAsync(() => shareSearchGlyph.Progress == 1 && shareSearchGlyph.SpinnerRunning);
            if (shareSearchGlyph.Ink != Color.White) throw new InvalidOperationException("Typing did not turn the magnifier white.");
            shareRecipientSearch.Text = "";
            await WaitForLikedLayoutAsync(() => shareSearchGlyph.Progress is > 0 and < 1);
            CaptureUiPreview("share-search-from-spinner");
            await searching;
            fixture.SearchHold.SetResult(); fixture.SearchHold = null;
            await SearchShareRecipientsAsync();
            await WaitForLikedLayoutAsync(() => !shareSearchGlyph.Animating && !shareSearchGlyph.SpinnerRunning);
            if (shareSearchGlyph.Ink != MutedIcon || shareRecipientChoices.Children.Count != 12)
                throw new InvalidOperationException("Clearing a pending search retained old results or a loading icon.");
            SetShareRecipient(peer);
            shareComposeText.Text = string.Join("\n", Enumerable.Repeat("Длинный комментарий к треку", 20));
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareComposeText.ActualHeight > 0);
            bar = (ScrollBar)VisualTree.Find(shareComposeText, element => element is ScrollBar { Orientation: Orientation.Vertical, IsVisible: true })!;
            if (bar == null || Math.Abs(bar.Bounds.Right - shareContent.BodyBounds.Right + 6) > 1 || bar.Bounds.X <= shareMessageField.Surface.Bounds.Right)
                throw new InvalidOperationException("Message scrollbar did not move outside the glass field.");
            if (Window.HitTest(bar.Bounds.Center) is not { } editorHit || editorHit != bar && !bar.IsAncestorOf(editorHit))
                throw new InvalidOperationException("The message scrollbar is not clickable in the gutter.");
            if (shareMessageField.Surface.Background.A == 255 || shareComposeText.Background.A != 0 ||
                shareRecipientEdit.Content is not Image || shareRecipientEdit.StyleName != TrackButtons.Glass ||
                ((MaterialTrackButton)shareSend).GlassAccentSurface != TrackButtons.BlueGlassAccent || shareSendLabel.Text != "Отправить")
                throw new InvalidOperationException("Message fields lost glass, pencil editing or the blue send button.");
            CaptureUiPreview("share-message-scroll");
            RouteWaveformClick(SettingsCenter(shareRecipientEdit));
            await WaitForLikedLayoutAsync(() => shareRecipient == null && !shareRecipientSearch.IsFocused);
            SetShareRecipient(peer); shareComposeText.Text = "Послушай";
            ApplyShareTime(83);
            var body = "Послушай\n" + shareLink.Text;
            chatRecipient = new() { Id = 91, Username = "Другой чат" };
            chatInput.Text = "Старый черновик";
            chatDrafts[91] = chatInput.Text; chatDrafts[77] = "Отдельный черновик получателю";
            fixture.SendHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RouteWaveformClick(SettingsCenter(shareSend));
            await WaitForLikedLayoutAsync(() => fixture.Sends == 1 && shareSending);
            await WaitForLikedLayoutAsync(() => shareSendBusy.Progress == 1 && shareSendBusy.SpinnerRunning);
            if (shareSend.IsEnabled || shareSendLabel.Text != "Отправить" || fixture.Last?.Body != body ||
                chatLocalStore!.Load().Pending.All(pending => pending.RecipientId != 77 || pending.Body != body))
                throw new InvalidOperationException("Share did not persist a single background send before POST.");
            await SendShareMessageAsync();
            CloseShare(true);
            ShareTrack(track with { Title = "Другой трек" }); SelectShareTab(1); SetShareRecipient(peer with { Id = 78 });
            shareComposeText.Text = "Новый текст";
            fixture.SendHold.SetResult();
            await WaitForLikedLayoutAsync(() => !shareSending);
            if (fixture.Sends != 1 || page.Value != initialPage || chatRecipient?.Id != 91 || chatInput.Text != "Старый черновик" ||
                chatDrafts.GetValueOrDefault(77) != "Отдельный черновик получателю" || !shareDialog.IsOpen ||
                shareTarget?.Title != "Другой трек" || shareComposeText.Text != "Новый текст" || chatPending.ContainsKey(77) ||
                chatConversations.FirstOrDefault(row => row.Recipient.Id == 77)?.LastMessage?.Body != body)
                throw new InvalidOperationException("Background send changed navigation/drafts, duplicated POST or closed a newer Share dialog.");
            SetShareRecipient(peer with { Id = 79 });
            fixture.SendHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var closingSend = SendShareMessageAsync();
            await WaitForLikedLayoutAsync(() => fixture.Sends == 2 && shareSendBusy.Progress == 1 && shareSendBusy.SpinnerRunning);
            CaptureUiPreview("share-send-busy");
            fixture.SendHold.SetResult(); await closingSend;
            if (!shareDialog.IsVisible || !shareSendBusy.Busy || shareSendBusy.Progress != 1 || shareSendLabel.Text != "Отправить")
                throw new InvalidOperationException("Confirmed sending reset the button before the modal finished closing.");
            await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
            if (shareSendBusy.Busy || shareSendBusy.Progress != 0 || shareSendBusy.SpinnerRunning)
                throw new InvalidOperationException("Busy send state did not reset after modal closing.");
            ShareTrack(track with { Title = "Другой трек" }); SelectShareTab(1); SetShareRecipient(peer with { Id = 78 });
            shareComposeText.Text = "Новый текст";
            fixture.SendHold = null; fixture.Reject = true;
            await SendShareMessageAsync();
            if (!shareDialog.IsOpen || shareComposeText.Text != "Новый текст" || !shareSend.IsEnabled || chatPending.ContainsKey(78))
                throw new InvalidOperationException("Rejected sending lost text or prevented an explicit retry.");
            fixture.Reject = false; fixture.LoseAcknowledgement = true;
            await SendShareMessageAsync();
            var sends = fixture.Sends;
            await SendShareMessageAsync();
            if (shareSend.IsEnabled || !chatPending.ContainsKey(78) || fixture.Sends != sends ||
                !shareMessage.Text.Contains("не подтверждена") || shareComposeText.Text != "Новый текст")
                throw new InvalidOperationException("Uncertain sending was replayed or presented as confirmed delivery.");
            Console.WriteLine("SHARE_MESSAGE_OK: glass search/editor, white input glyph, reversible spinner, clickable outer scrollbars, pencil editing, blue direct sending, durable single POST, unchanged navigation/drafts, stale dialog isolation and lost-ack protection");
        }
        finally
        {
            fixture.SearchHold?.TrySetResult(); fixture.SendHold?.TrySetResult();
            CloseShare(true); api.BrowserTransport = transport;
            shareRecipientResults.AutoHideScrollBars = true;
            chatPending.Remove(77); chatPending.Remove(78);
            chatRecipient = selected; chatInput.Text = input;
            chatDrafts.Clear(); foreach (var pair in drafts) chatDrafts[pair.Key] = pair.Value;
            chatConversations.Clear(); chatConversations.AddRange(conversations);
            SaveChatState(); RefreshChatComposer();
        }
    }

    private sealed class ShareMessageFixture(ISoundCloudTransport inner) : ISoundCloudBrowserTransport
    {
        internal TaskCompletionSource? SearchHold, SendHold;
        internal int Sends;
        internal bool Reject, LoseAcknowledgement;
        internal ChatRequest? Last;
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath == "/search/users" && SearchHold != null)
                await SearchHold.Task.WaitAsync(cancellationToken);
            return await inner.GetJsonAsync(uri, cancellationToken);
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) =>
            inner.SetLikedAsync(userId, trackId, liked, cancellationToken);
        public async Task<JsonDocument> RequestChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Kind != "send" || request.UserId != 42) throw new InvalidOperationException("Share opened or read an unrelated chat.");
            Last = request; Sends++;
            if (SendHold != null) await SendHold.Task.WaitAsync(cancellationToken);
            if (Reject) throw new SoundCloudException("Тестовый отказ отправки.", 403);
            if (LoseAcknowledgement) throw new HttpRequestException("Fixture lost acknowledgement after accepting send.");
            return JsonSerializer.SerializeToDocument(new
            {
                id = "42:" + request.RecipientId,
                users = new[] { new { id = 42L, username = "Ты" }, new { id = request.RecipientId, username = "Получатель" } }, read = true,
                last_message = new { id = "share-sent-" + Sends, sender_urn = "soundcloud:users:42", content = request.Body, sent_at = DateTimeOffset.UtcNow.ToString("O") }
            });
        }
    }
}
