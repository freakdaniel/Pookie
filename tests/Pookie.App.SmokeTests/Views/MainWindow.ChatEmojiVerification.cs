using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatEmojiUiAsync()
    {
        try
        {
            var transport = new ChatFixtureTransport();
            var mediaFixture = new ChatFixtureMediaHandler();
            chatMedia.Dispose(); chatMedia = new(mediaFixture);
            api.Session = new("chatfixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Ты" }; signedIn.Value = true;
            await ShowWorkspaceAsync(); Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            await NavigateAsync(Page.Chats); chatTimer.Stop(); await SelectChatAsync(transport.Other);
            await WaitForLikedLayoutAsync(() => !chatScrollBottom && !chatComposerSurface.IsResizing && chatInput.IsEnabled);
            Window.WindowSize = WindowSize.Resizable(1840, 900, minWidth: 760, minHeight: 600);
            OpenChatInfo(); await WaitForLikedLayoutAsync(() => !chatColumns.Animating && chatColumns.DetailsOpen);
            Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            MoveActionPointer(new Point(2, 2));
            var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
            var column = chatComposer.Bounds; var offset = viewer.VerticalOffset;
            chatInput.Text = "До после"; chatInput.Select(3, 0);
            var frames = new List<double>(); void Sample() => frames.Add(chatEmojiPopup.Reveal);
            Window.FrameRendered += Sample;
            await OpenChatGifsAsync();
            try { await WaitForLikedLayoutAsync(() => chatEmojiPopup.Reveal > .999 && !chatEmojiLoading && chatEmojiResults.ActualHeight > 100); }
            catch { throw new InvalidOperationException($"Emoji opening failed: active={Window.IsActive}, open={chatEmojiPopup.IsOpen}, reveal={chatEmojiPopup.Reveal}, loading={chatEmojiLoading}, height={chatEmojiResults.ActualHeight}, focused={chatEmojiCard.IsFocusWithin}, mode={chatEmojiGifMode}."); }
            Window.FrameRendered -= Sample;
            if (!frames.Any(a => a is > .01 and < .99) || !chatColumns.DetailsOpen ||
                Math.Abs(column.X - chatComposer.Bounds.X) > 1 || Math.Abs(offset - viewer.VerticalOffset) > 1)
                throw new InvalidOperationException("Emoji panel snapped, dismissed details or moved the conversation.");
            if (chatEmojiPopup.SurfaceBounds.Width > 383 || chatEmojiPopup.SurfaceBounds.Height > 421 ||
                chatEmojiPopup.HitTest(new Point(2, 2)) != null || chatEmojiCard.Background != chatComposer.Background)
                throw new InvalidOperationException("Emoji panel lost its compact glass geometry or blocks the workspace.");
            CaptureUiPreview("chat-emoji-panel");
            PickChatEmoji("👨‍👩‍👧‍👦");
            if (chatInput.Text != "До 👨‍👩‍👧‍👦после" || !chatInput.IsFocused || !chatEmojiPopup.IsOpen || transport.Sends != 0)
                throw new InvalidOperationException("Emoji insertion lost the caret, closed the panel or sent a message.");
            chatInput.Undo(); if (chatInput.Text != "До после") throw new InvalidOperationException("Emoji insertion bypassed native undo.");
            chatInput.Text = new string('я', 19999); chatInput.CaretPosition = 19999; PickChatEmoji("👨‍👩‍👧‍👦");
            if (chatInput.Text.Length != 19999) throw new InvalidOperationException("Emoji insertion clipped a Unicode sequence at the message limit.");
            chatInput.Select(19989, 10); PickChatEmoji("👨‍👩‍👧‍👦");
            if (chatInput.Text.Length != 20000 || !chatInput.Text.EndsWith("👨‍👩‍👧‍👦", StringComparison.Ordinal))
                throw new InvalidOperationException("Replacing a selection could not fit a complete emoji at the body limit.");
            chatInput.Text = "До после";
            const string link = "https://soundcloud.com/fixture/track";
            chatInput.Text = link; chatInput.CaretPosition = link.Length;
            chatMusicEditor!.Update([new(link, "Тестовый трек", null)]); PickChatEmoji("🦊");
            if (chatInput.Text != link + " 🦊") throw new InvalidOperationException("An emoji merged with the preceding music URL.");
            chatInput.Text = "До после";
            var searchFrames = new List<double>(); void SearchSample() => searchFrames.Add(chatEmojiLayout.SearchReveal);
            Window.FrameRendered += SearchSample; chatGifSearch.Focus(); chatGifSearch.Text = "сердце";
            await WaitForLikedLayoutAsync(() => chatEmojiLayout.SearchReveal > .999);
            Window.FrameRendered -= SearchSample;
            await WaitForLikedLayoutAsync(() => chatEmojiSlots.Any(pair => pair.Value.Value == "❤️" && pair.Key.IsVisible));
            if (!searchFrames.Any(a => a is > .01 and < .99)) throw new InvalidOperationException("Emoji search snapped instead of sliding its header.");
            CaptureUiPreview("chat-emoji-search");
            foreach (var group in ChatEmojiCatalog.Groups) { SelectChatEmojiCategory(group.Group); await WaitForLoginFrameAsync(); }
            if (ChatEmojiArtwork.CachedCount > 192) throw new InvalidOperationException("Emoji artwork cache exceeded its bound.");
            var gifTransport = new EmojiGifFixture(); klipy.Dispose(); klipy = new(gifTransport) { ApiKey = "local-fixture" };
            SetChatEmojiMode(true);
            try { await WaitForLikedLayoutAsync(() => gifTransport.Active > 0 && chatGifResults.Children.Count == 12); }
            catch { throw new InvalidOperationException($"GIF opening failed: active={Window.IsActive}, open={chatEmojiPopup.IsOpen}, mode={chatEmojiGifMode}, gifs={chatGifsOpen.Value}, results={chatGifResults.Children.Count}, queued={chatGifPreviewQueue.Count}, activeRequests={gifTransport.Active}, focus={chatEmojiCard.IsFocusWithin}, attach={chatAttach.IsMouseOver}/{chatAttachMenu.IsOpen}, status={chatGifStatus.Text}."); }
            if (gifTransport.MaximumActive > 2) throw new InvalidOperationException("GIF preview loading exceeded its concurrency bound.");
            CaptureUiPreview("chat-gif-skeletons");
            gifTransport.Ready.TrySetResult();
            await WaitForLikedLayoutAsync(() => chatGifResults.Children.OfType<Button>().Count(b => b.FindVisualChild<Image>() is Image { Source: not null, Opacity: > .99 }) >= 6);
            CaptureUiPreview("chat-gif-panel");
            var gifButton = chatGifResults.Children.OfType<Button>().First(); RouteWaveformClick(SettingsCenter(gifButton));
            await WaitForLikedLayoutAsync(() => !chatEmojiPopup.IsVisible && chatSelectedGif != null);
            if (transport.Sends != 0 || chatAnimations.Keys.Any(image => chatGifResults.IsAncestorOf(image)))
                throw new InvalidOperationException("Selecting a GIF sent it directly or kept picker animations alive.");
            ClearChatAttachments(); await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing);
            MoveActionPointer(new Point(2, 2)); Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            await OpenChatGifsAsync();
            try { await WaitForLikedLayoutAsync(() => chatEmojiPopup.Reveal > .999 && !chatEmojiLoading); }
            catch { throw new InvalidOperationException($"Emoji reopen failed: active={Window.IsActive}, open={chatEmojiPopup.IsOpen}, amount={chatEmojiPopup.Reveal}, loading={chatEmojiLoading}, focus={Window.FocusManager.FocusedElement?.GetType().Name}, within={chatEmojiCard.IsFocusWithin}, attach={chatAttach.IsMouseOver}/{chatAttachMenu.IsOpen}, importing={chatImporting}, sending={chatSending}."); }
            preferences = preferences with { KlipyEnabled = false }; SetChatEmojiMode(false);
            if (!chatGif.IsVisible || !chatEmojiPopup.IsOpen) throw new InvalidOperationException("Disabling GIFs removed emoji input.");
            frames.Clear(); Window.FrameRendered += Sample; RouteChatEscape();
            await WaitForLikedLayoutAsync(() => !chatEmojiPopup.IsVisible); Window.FrameRendered -= Sample;
            if (!frames.Any(a => a is > .01 and < .99) || !chatColumns.DetailsOpen) throw new InvalidOperationException("Escape snapped emoji dismissal or closed details first.");
            preferences = preferences with { KlipyEnabled = true };
            CloseChatDetails(); await WaitForLikedLayoutAsync(() => !chatColumns.Animating);
            transport.AddIncoming("А"); await RefreshChatsAsync();
            RequestChatBottom(); await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && !chatScrollBottom && chatRestoreOffset == null && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 1);
            var row = ChatRows.Single(r => r.Bound && r.Message?.Body == "А");
            var target = row.Message!;
            var originalWidth = row.Bubble.ActualWidth;
            var point = new Point(row.Bubble.Bounds.Right - 8, row.Bubble.Bounds.Y + 16);
            var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", true)!;
            var mouse = router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Single(m => m.Name == "MouseButton" && m.GetParameters().Length == 10);
            mouse.Invoke(null, [Window, point, new Point(0, 0), MouseButton.Right, true, false, true, false, 1, ModifierKeys.None]);
            mouse.Invoke(null, [Window, point, new Point(0, 0), MouseButton.Right, false, false, false, false, 1, ModifierKeys.None]);
            await WaitForLikedLayoutAsync(() => chatMessageMenu.Reveal > .999);
            if (chatQuickReactions.Count != 6 || chatMessageMenuActions.Count(a => a.Button.IsVisible) != 4)
                throw new InvalidOperationException("Message menu did not separate quick reactions from its real actions.");
            CaptureUiPreview("chat-message-menu");
            await OpenChatMenuEmojiAsync();
            await WaitForLikedLayoutAsync(() => chatEmojiPopup.Reveal > .999 && !chatEmojiLoading);
            if (chatEmojiTarget?.Id != target.Id || chatEmojiModes.Children.OfType<Button>().Last().IsVisible)
                throw new InvalidOperationException("Extended reactions exposed GIFs or lost the target message.");
            var widths = new List<double>(); void SampleWidth() => widths.Add(row.Bubble.ActualWidth);
            Window.FrameRendered += SampleWidth;
            PickChatEmoji("🦊");
            await WaitForLikedLayoutAsync(() => !chatSending && transport.Sends == 1 && chatReactions.GetValueOrDefault(target.Id)?.GetValueOrDefault(42) == "🦊");
            await WaitForLikedLayoutAsync(() => !row.Reactions.Animating && !row.Content.IsResizing && row.Reactions.HasContent);
            Window.FrameRendered -= SampleWidth;
            if (!widths.Any(w => w > originalWidth + .5 && w < row.Bubble.ActualWidth - .5))
                throw new InvalidOperationException($"Adding a reaction snapped the bubble's width: {originalWidth}->{row.Bubble.ActualWidth}; {string.Join(',', widths.Select(w => w.ToString("F2")))}.");
            RequestChatBottom(); await WaitForLikedLayoutAsync(() => !chatScrollBottom && chatRestoreOffset == null && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 1);
            if (chatMessages.Last().Body.Length > 36 || !row.Bubble.Bounds.Contains(row.Reactions.Bounds) || row.Reactions.Bounds.Bottom > viewer.Bounds.Bottom + 1 || row.Footer.Bounds.X < row.Reactions.Buttons.First(b => b.IsVisible).Bounds.Right)
            {
                CaptureUiPreview("chat-reaction-layout-failure");
                throw new InvalidOperationException($"Reaction layout: wire={chatMessages.Last().Body.Length}, bubble={row.Bubble.Bounds}, reactions={row.Reactions.Bounds}, chip={row.Reactions.Buttons.First(b => b.IsVisible).Bounds}, footer={row.Footer.Bounds}, viewport={viewer.Bounds}, scroll={viewer.VerticalOffset}/{SmoothScroll.Maximum(viewer)}.");
            }
            CaptureUiPreview("chat-message-reaction");
            var expandedWidth = row.Bubble.ActualWidth; widths.Clear(); Window.FrameRendered += SampleWidth;
            await ReactToChatAsync(target, "🦊");
            await WaitForLikedLayoutAsync(() => !row.Reactions.HasContent && !row.Reactions.Animating && !row.Content.IsResizing);
            Window.FrameRendered -= SampleWidth;
            if (!widths.Any(w => w > originalWidth + .5 && w < expandedWidth - .5) || Math.Abs(row.Bubble.ActualWidth - originalWidth) > 1)
                throw new InvalidOperationException("Removing a reaction snapped the bubble width or did not restore its original layout.");
            chatReactions[target.Id] = new() { [42] = "🔥", [90] = "❤️" }; row.RefreshExtensions();
            await WaitForLikedLayoutAsync(() => !row.Reactions.Animating && !row.Content.IsResizing);
            if (row.Reactions.Buttons.Count(b => b.IsVisible) != 2 || row.Reactions.Buttons.Where(b => b.IsVisible).Max(b => b.Bounds.Right) > row.Footer.Bounds.X - 1)
                throw new InvalidOperationException("Two participants' reactions overlap the timestamp.");
            await VerifyEmojiPhotoCornersAsync(row);
            foreach (var width in new[] { 780, 1000, 2560 })
            {
                Window.WindowSize = WindowSize.Resizable(width, 900, minWidth: 760, minHeight: 600);
                await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 && !chatColumns.Animating);
                await OpenChatGifsAsync(); await WaitForLikedLayoutAsync(() => chatEmojiPopup.Reveal > .999 && !chatEmojiLoading);
                if (!chatColumns.Bounds.Contains(chatEmojiPopup.SurfaceBounds)) throw new InvalidOperationException($"Emoji panel escaped a resized chat workspace: requested={width}, window={Window.ClientSize}, workspace={chatColumns.Bounds}, popup={chatEmojiPopup.SurfaceBounds}.");
                CloseChatGifs(); await WaitForLikedLayoutAsync(() => !chatEmojiPopup.IsVisible);
            }
            Window.WindowSize = WindowSize.Resizable(1000, 900, minWidth: 760, minHeight: 600);
            await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - 1000) < 1 && !chatColumns.Animating);
            var nativeDpi = Window.Dpi;
            try
            {
                foreach (var dpi in new uint[] { 96, 110, 144, 192 })
                {
                    typeof(Window).GetMethod("SetDpi", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Window, [dpi]);
                    Window.PerformLayout(); await WaitForLoginFrameAsync();
                    await OpenChatGifsAsync(); await WaitForLikedLayoutAsync(() => chatEmojiPopup.Reveal > .999 && !chatEmojiLoading);
                    if (!chatColumns.Bounds.Contains(chatEmojiPopup.SurfaceBounds) || chatEmojiPopup.SurfaceBounds.Width > 383)
                        throw new InvalidOperationException("Emoji panel escaped its bounds after a DPI change.");
                    CloseChatGifs(); await WaitForLikedLayoutAsync(() => !chatEmojiPopup.IsVisible);
                }
            }
            finally { typeof(Window).GetMethod("SetDpi", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Window, [nativeDpi]); Window.PerformLayout(); }
            await OpenChatGifsAsync(); await SelectChatAsync(transport.Third);
            await WaitForLikedLayoutAsync(() => !chatEmojiPopup.IsVisible);
            if (chatEmojiTarget != null || chatGifPreviewQueue.Count != 0) throw new InvalidOperationException("Switching conversation retained picker work or reaction context.");
            VerifyChatMenuOwnership();
            await SelectChatAsync(transport.Other); chatTimer.Stop();
            const string unicode = "Текст 😀 ❤ 👋🏽 👨‍👩‍👧‍👦 🇨🇦 1️⃣ 日本語";
            transport.AddIncoming(unicode); await RefreshChatThreadAsync(); RequestChatBottom();
            chatInput.Text = unicode;
            await WaitForLikedLayoutAsync(() => chatEmojiEditor?.Count == 6 && ChatRows.Any(r => r.Body.Text == unicode && r.Body.EmojiCount == 6));
            RequestChatBottom();
            await WaitForLikedLayoutAsync(() => !chatScrollBottom && !chatComposerSurface.IsResizing &&
                ChatRows.Any(r => r.Body.Text == unicode && r.Body.Bounds.Y >= viewer.Bounds.Y && r.Body.Bounds.Bottom <= viewer.Bounds.Bottom));
            await WaitForLoginFrameAsync();
            if (ChatEmojiArtwork.TextureCount is < 1 or > 192)
                throw new InvalidOperationException("Inline Apple emoji were not rendered or exceeded the GPU texture bound.");
            chatInput.SelectAll();
            if (chatInput.SelectedText != unicode) throw new InvalidOperationException("Emoji presentation changed the selected Unicode text.");
            CaptureUiPreview("chat-apple-emoji-inline");
            chatInput.Text = "a👨‍👩‍👧‍👦b"; chatInput.CaretPosition = chatInput.Text.Length - 1; chatInput.Focus();
            typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", true)!
                .GetMethod("KeyDown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [Window, new KeyEventArgs(Key.Backspace, 0)]);
            if (chatInput.Text != "ab") throw new InvalidOperationException("Backspace split a displayed emoji sequence.");
            chatInput.Undo();
            if (chatInput.Text != "a👨‍👩‍👧‍👦b") throw new InvalidOperationException("Undo did not restore the complete Unicode emoji.");
            chatInput.Text = unicode;
            await SendChatAsync();
            if (!chatMessages.Any(m => m.SenderId == 42 && m.Body == unicode))
                throw new InvalidOperationException("Inline Apple artwork changed the actual message body.");
            await VerifyGlobalEmojiTextAsync(transport);
            await VerifyGifRestorationAsync(transport, mediaFixture);
            Console.WriteLine("APPLE_EMOJI_INLINE_OK: same artwork in composer and messages; Unicode selection, whole-sequence backspace, undo and transport preserved; bounded GPU textures");
            Console.WriteLine("UI_CHAT_EMOJI_OK: native caret/undo, Russian search, bounded offline artwork, animated non-modal picker, quick/extended reactions, compact sends, embedded chips, narrow/wide layout and ownership");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine(error); }
        finally { Window.Close(); }
    }
    private async Task VerifyGlobalEmojiTextAsync(ChatFixtureTransport transport)
    {
        // Ordinary framework controls must use the same presentation as the chat
        // composer, without an editor extension or a custom message control.
        foreach (var box in new[] { chatSearch, chatMessageSearch })
        {
            box.Text = "👨‍👩‍👧‍👦";
            box.Measure(new Size(320, 48)); box.Arrange(new Rect(0, 0, 320, 48));
            var advance = box.GetCharRectInWindow(box.Text.Length).X - box.GetCharRectInWindow(0).X;
            if (Math.Abs(advance - box.FontSize * 1.3) > 1)
                throw new InvalidOperationException($"Standard search control missed emoji layout: {advance}.");
            box.SelectAll();
            if (box.SelectedText != "👨‍👩‍👧‍👦") throw new InvalidOperationException("Search presentation changed Unicode selection.");
            box.Text = "";
        }
        var label = new TextBlock().Text("😀").FontSize(20);
        label.Measure(new Size(300, 100));
        if (Math.Abs(label.DesiredSize.Width - 26) > 1)
            throw new InvalidOperationException($"A standard username/comment label bypassed emoji layout: {label.DesiredSize}.");
        transport.Other = new() { Id = 90, Username = "Друг 😀 👋🏽" };
        chatRecipient = transport.Other;
        await RefreshChatsAsync();
        var message = chatMessages.Last(m => m.Body.Contains("😀", StringComparison.Ordinal));
        ReplyToChat(message); chatSearch.Text = "Друг";
        await SearchChatsAsync(); OpenChatThreadSearch(); chatMessageSearch.Text = "Текст";
        chatMessageSearchTimer.Stop(); await SearchChatMessagesAsync();
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatSearchHits.Length > 0);
        await WaitForLoginFrameAsync(); CaptureUiPreview("application-apple-emoji-search-reply");
        CloseChatSearch(); chatSearch.Text = ""; chatReply = null; RefreshChatReply();
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing);
        Console.WriteLine("GLOBAL_APPLE_EMOJI_OK: ordinary labels and both native search editors share whole-sequence artwork; Unicode selection retained");
    }

    private async Task VerifyGifRestorationAsync(ChatFixtureTransport transport, ChatFixtureMediaHandler media)
    {
        var gifFixture = new EmojiGifFixture(); klipy.Dispose(); klipy = new(gifFixture) { ApiKey = "local-fixture" };
        var code = media.Add(new(42, 90, "", [], "https://static.klipy.com/restored.gif"));
        transport.AddOwn(code); await RefreshChatsAsync(); RequestChatBottom();
        var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
        await WaitForLikedLayoutAsync(() => gifFixture.Active > 0 && !chatScrollBottom &&
            ChatRows.Any(r => r.Message?.Body == code && r.Attachments.FindVisualChild<ChatPhotoFrame>() != null));
        await SelectChatAsync(transport.Third);
        var positions = new List<double>();
        void BeforePaint()
        {
            if (chatRecipient?.Id == 90 && ChatRows.FirstOrDefault(r => r.Bound && r.Message?.Body == code) is { } row)
                positions.Add(viewer.Bounds.Bottom - row.Root.Bounds.Bottom);
        }
        chatViewport.BeforePaint += BeforePaint;
        await SelectChatAsync(transport.Other);
        await WaitForLikedLayoutAsync(() => gifFixture.Active > 0 && !chatScrollBottom && chatRestoreOffset == null);
        await WaitForLoginFrameAsync(); chatViewport.BeforePaint -= BeforePaint;
        if (positions.Count == 0 || positions.Any(gap => gap > 24 || gap < -2))
            throw new InvalidOperationException($"GIF restore painted a blank gap before scrolling: {string.Join(',', positions)}.");
        gifFixture.Ready.TrySetResult();
        await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == code && !r.Content.IsResizing &&
            r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }) &&
            !chatScrollBottom && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 1);
        var downloads = gifFixture.Downloads;
        await SelectChatAsync(transport.Third);
        chatDecodedPhotos.Clear(); chatDecodedRecency.Clear(); chatDecodedBytes = 0;
        chatGifMedia = null; // Simulate a fresh service and empty decoded cache.
        await SelectChatAsync(transport.Other);
        await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == code &&
            r.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: not null }));
        if (gifFixture.Downloads != downloads)
            throw new InvalidOperationException("Recreated GIF service redownloaded encrypted cached media.");
        CaptureUiPreview("chat-gif-restored-bottom");
        Console.WriteLine("GIF_RESTORE_OK: first painted position stable while download waits; intrinsic resize follows bottom; fresh service uses encrypted disk cache");
    }

    private async Task VerifyEmojiPhotoCornersAsync(ChatBubbleRow row)
    {
        var pixels = new byte[180 * 120 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 180; pixels[i + 1] = 80; pixels[i + 2] = 30; pixels[i + 3] = 255; }
        var picture = new ChatPhotoFrame(new Size(180, 120), () => Window.IsActive);
        picture.ShowImmediate(ImageSource.FromBgraPixels(180, 120, pixels), null);
        row.Attachments.Clear(); row.Attachments.Add(picture); row.Attachments.IsVisible = true; row.Body.IsVisible = false;
        row.SetPhotoLayout(new Size(180, 120)); row.Root.InvalidateMeasure();
        RequestChatBottom();
        await WaitForLikedLayoutAsync(() => !chatScrollBottom && chatRestoreOffset == null && picture.ActualHeight > 0);
        if (row.Reactions.Bounds.Y < row.Attachments.Bounds.Bottom - .5 || row.Footer.Bounds.Bottom > row.Attachments.Bounds.Bottom + .5)
            throw new InvalidOperationException("Bare-media reactions moved its floating timestamp below the image.");
        CaptureUiPreview("chat-photo-reactions");
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels((int)Window.ClientSize.Width, (int)Window.ClientSize.Height, 1));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.FillRectangle(new Rect(0, 0, Window.ClientSize.Width, Window.ClientSize.Height), Surface); Window.Content!.Render(context); context.EndFrame();
        var cpu = (Aprillz.MewUI.Rendering.ICpuPixelSurface)surface; var data = cpu.GetReadOnlyPixelSpan();
        var offset = ((int)row.Attachments.Bounds.Bottom - 2) * cpu.StrideBytes + ((int)row.Attachments.Bounds.X + 1) * 4;
        if (Math.Abs(data[offset] - Surface.B) > 2 || Math.Abs(data[offset + 1] - Surface.G) > 2 || Math.Abs(data[offset + 2] - Surface.R) > 2)
            throw new InvalidOperationException("A photo with reactions lost its rounded bottom corner.");
    }
    private sealed class EmojiGifFixture : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Active, MaximumActive, Downloads;
        private static readonly byte[] gif = Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBCAABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "api.klipy.com")
            {
                var items = Enumerable.Range(0, 12).Select(i => new { slug = "local-" + i, file = new { md = new { gif = new { url = "https://static.klipy.com/" + i + ".gif" } }, xs = new { gif = new { url = "https://static.klipy.com/preview-" + i + ".gif" } } } });
                return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { data = new { data = items } })) };
            }
            Interlocked.Increment(ref Downloads);
            var active = Interlocked.Increment(ref Active); MaximumActive = Math.Max(MaximumActive, active);
            try { await Ready.Task.WaitAsync(cancellationToken); return new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(gif) }; }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}
