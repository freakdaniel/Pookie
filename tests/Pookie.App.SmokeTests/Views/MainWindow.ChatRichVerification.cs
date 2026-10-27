using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatRichAttachmentsAsync(ChatFixtureTransport transport, ChatFixtureMediaHandler media, bool musicOnly = false)
    {
        var document = media.Add(new(42, 90, "", [new("ghosty-config.txt", "text/plain", new byte[87], true)]));
        transport.AddOwn(document); await RefreshChatsAsync(); RequestChatBottom();
        // Resolving metadata replaces the temporary card. Read the current
        // child only after that replacement and its layout have completed.
        await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Bound && r.Root.FindVisualRoot() == Window &&
            r.Message?.Body == document && r.MediaResolved && !r.Content.IsResizing &&
            r.Attachments.FindVisualChild<ChatFileCard>() is ChatFileCard { IsMeasureDirty: false, IsArrangeDirty: false, ActualHeight: > 0 }));
        var fileRow = ChatRows.First(r => r.Bound && r.Root.FindVisualRoot() == Window && r.Message?.Body == document);
        var fileCard = (ChatFileCard)fileRow.Attachments.FindVisualChild<ChatFileCard>()!;
        if (fileCard.DetailLabel.Text != "87 Б" || fileRow.Bubble.ActualHeight > 72 + 1 / Window.DpiScale || Math.Abs(fileCard.DetailLabel.Bounds.Bottom - fileRow.Time.Bounds.Bottom) > 1)
            throw new InvalidOperationException($"Document bubble lost its compact file/size/time layout: detail={fileCard.DetailLabel.Text}, bubble={fileRow.Bubble.Bounds}, detailBounds={fileCard.DetailLabel.Bounds}, time={fileRow.Time.Bounds}, content={fileRow.Content.Bounds}, attachments={fileRow.Attachments.Bounds}, file={fileCard.Bounds}, dirty={fileCard.IsMeasureDirty}/{fileCard.IsArrangeDirty}, parent={fileCard.Parent?.GetType().Name}.");
        var photo = Fixture("chat-small.png");
        var asFile = media.Add(new(90, 42, "", [new("photo.png", "image/png", photo, true)]));
        transport.AddIncoming(asFile); await RefreshChatsAsync(); RequestChatBottom();
        try
        {
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Bound && r.Root.FindVisualRoot() == Window && r.Message?.Body == asFile && !r.Content.IsResizing && r.Motion.Opacity > .99 &&
                r.Attachments.FindVisualChild<ChatFileCard>() is ChatFileCard && r.Bubble.ActualHeight <= 72 + 1 / Window.DpiScale));
        }
        catch (Exception error)
        {
            var incoming = ChatRows.FirstOrDefault(r => r.Message?.Body == asFile);
            var ancestors = new List<string>();
            for (Element? element = incoming?.Attachments.FindVisualChild<ChatFileCard>(); element != null; element = element.Parent)
                ancestors.Add($"{element.GetType().Name}:{element.IsMeasureDirty}/{element.IsArrangeDirty}:{element.Bounds}");
            Console.Error.WriteLine("DOCUMENT_LAYOUT_CHAIN: " + string.Join(" -> ", ancestors));
            throw new InvalidOperationException($"Incoming document: found={incoming != null}, bound={incoming?.Bound}, rooted={incoming?.Root.FindVisualRoot() == Window}, resolved={incoming?.MediaResolved}, resizing={incoming?.Content.IsResizing}, opacity={incoming?.Motion.Opacity}, bubble={incoming?.Bubble.Bounds}, content={incoming?.Content.Bounds}, file={incoming?.Attachments.FindVisualChild<ChatFileCard>()?.Bounds}, photo={incoming?.Content.PhotoMode}, scrolling={chatScrollBottom}, restore={chatRestoreOffset}, follow={chatFollowLayout}, queued={chatMediaLayoutQueued}/{chatMediaLayoutPending.Count}.", error);
        }
        if (ChatRows.First(r => r.Message?.Body == asFile).Content.PhotoMode) throw new InvalidOperationException("An image sent as a document became a photo.");
        Console.WriteLine("CHAT_DOCUMENT_LAYOUT_OK: resolved replacement cards are measured and resize transitions finish");
        await WaitForLikedLayoutAsync(() => chatInboxVisuals.Any(pair => pair.Value.Preview.Text == "photo.png" && pair.Value.PreviewGlyph == "file" && pair.Value.PreviewIcon.IsVisible));
        CaptureUiPreview("chats-documents");
        Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
        ShowChatAttachMenu();
        if (!chatAttachMenu.IsOpen) throw new InvalidOperationException("Attachment choices did not open.");
        var choices = ((StackPanel)chatAttachMenuPanel.Child!).Children.OfType<Button>()
            .Select(b => ((TextBlock)b.FindVisualChild<TextBlock>()!).Text).ToArray();
        if (!choices.SequenceEqual(new[] { "Трек или плейлист", "Документ", "Фото или видео" })) throw new InvalidOperationException("Attachment choices are incomplete.");
        await WaitForLikedLayoutAsync(() => chatAttachMenuPanel.ActualHeight > 100 && chatAttachMenu.Reveal > .99);
        if (Math.Abs(chatAttachMenuPanel.Bounds.X - chatComposer.Bounds.X) > 1 ||
            Math.Abs(chatComposer.Bounds.Y - chatAttachMenuPanel.Bounds.Bottom - 11) > 1 ||
            chatAttachMenuPanel.Background != chatComposer.Background)
            throw new InvalidOperationException("Attachment menu lost its composer alignment, gap or surface.");
        if (chatAttachMenu.HitTest(new Point(chatAttach.Bounds.X + 10, chatAttach.Bounds.Y + 10)) != null)
            throw new InvalidOperationException("Attachment overlay intercepts the paperclip.");
        CaptureUiPreview("chats-attachment-menu"); CloseChatAttachMenu();
        await WaitForLikedLayoutAsync(() => !chatAttachMenu.IsVisible);
        chatFilePickerOpen = true; ShowChatAttachMenu();
        if (chatAttachMenu.IsOpen) throw new InvalidOperationException("Attachment menu reopened over the file picker.");
        chatFilePickerOpen = false;
        collectionImages["https://i1.sndcdn.com/chat-music-fixture-t500x500.jpg"] = libraryCoverCache[101] = ImageSource.FromBytes(ArtworkFixtureBitmap(160));
        var draft = chatInput.Text;
        await OpenChatMusicPickerAsync(); chatMusicSearch.Text = "fixture"; chatMusicSearchTimer.Stop(); await SearchChatMusicAsync();
        if (chatMusicChoices.Children.Count != 2) throw new InvalidOperationException("Music picker did not include both tracks and playlists.");
        await WaitForLoginFrameAsync();
        await WaitForLikedLayoutAsync(() => chatMusicChoices.ActualHeight > 100 && chatMusicChoices.ActualWidth > 100);
        CaptureUiPreview("chats-music-picker"); CloseChatMusicPicker(); chatInput.Text = draft;
        waveformRequests["https://wave.sndcdn.com/chat-track-fixture.json"] = Task.FromResult<float[]?>(Enumerable.Range(0, 300)
            .Select(i => (float)(.2 + .7 * Math.Abs(Math.Sin(i * .41)) * (.3 + .7 * Math.Abs(Math.Sin(i * .027))))).ToArray());
        transport.AddOwn("Послушай\nhttps://soundcloud.com/fixture/track");
        transport.AddIncoming("https://soundcloud.com/fixture/sets/list"); await RefreshChatsAsync();
        await WaitForLikedLayoutAsync(() => chatMusicCache.Count >= 2 && ChatRows.Any(r => r.Body.Text == "Послушай" && r.Attachments.IsVisible));
        await WaitForLikedLayoutAsync(() => chatInboxVisuals.Any(pair => pair.Value.PreviewGlyph == "music-notes" && pair.Value.PreviewIcon.IsVisible));
        await Task.Delay(250); CaptureUiPreview("chats-music-cards");
        await VerifyChatMusicCardsAsync();
        await VerifyChatMusicComposerAsync();
        await VerifyChatMusicAttachmentsAsync(transport);
        await VerifyChatMusicLoadingAsync(transport);
        if (musicOnly) return;
        foreach (var extension in new[] { "mp4", "webm", "mov", "mkv", "avi" })
            if (!ChatMediaType("clip." + extension).StartsWith("video/", StringComparison.Ordinal)) throw new InvalidOperationException("Missing video MIME: " + extension);
        var videoPath = Path.Combine(Path.GetTempPath(), "pookie-video-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await File.WriteAllBytesAsync(videoPath, Fixture("chat-video-portrait.mp4"));
            await AddChatFilesAsync([videoPath]);
            try { await WaitForLikedLayoutAsync(() => chatAttachmentPreview.Source != null && chatAttachmentPreview.ActualWidth > 0); }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Video composer poster: source={chatAttachmentPreview.Source != null}, width={chatAttachmentPreview.ActualWidth}, files={chatSelectedFiles.Length}, sending={chatSending}, importing={chatImporting}, size={chatAttachmentImageSize}, status={chatThreadStatus.Value}.", error);
            }
            if (Math.Abs(chatAttachmentPreview.ActualWidth - 44) > 1 / Window.DpiScale || Math.Abs(chatAttachmentPreview.ActualHeight - 44) > 1 / Window.DpiScale || chatAttachmentImageSize is not { Height: >= 470 })
                throw new InvalidOperationException("The video attachment is missing its square composer poster.");
            CaptureUiPreview("chats-video-composer");
        }
        finally { ClearChatAttachments(); File.Delete(videoPath); }
        var video = media.Add(new(90, 42, "Посмотри", [new("clip.mp4", "video/mp4", Fixture("chat-video-portrait.mp4"))]));
        transport.AddIncoming(video); await RefreshChatsAsync();
        await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Message?.Body == video && r.MediaResolved && r.Attachments.FindVisualChild<ChatVideoFrame>() is ChatVideoFrame));
        var videoRow = ChatRows.First(r => r.Message?.Body == video);
        var videoFrame = (ChatVideoFrame)videoRow.Attachments.FindVisualChild<ChatVideoFrame>()!;
        Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
        await WaitForLikedLayoutAsync(() => videoFrame.Playing);
        await WaitForLikedLayoutAsync(() => !videoRow.Content.IsResizing && videoRow.Motion.Opacity > .99);
        if (!videoRow.Content.PhotoMode || videoFrame.ActualHeight < 470 || videoFrame.Picture.ToolTip != null)
            throw new InvalidOperationException("Portrait video lost its media geometry or acquired a tooltip.");
        CaptureUiPreview("chats-video-inline");
        var focusProbe = new Window().Title("Pookie video focus test").Fixed(240, 120).Content(new Border());
        try
        {
            focusProbe.Show(Window); await WaitForLoginFrameAsync(); focusProbe.Activate();
            await WaitForLikedLayoutAsync(() => !Window.IsActive && !videoFrame.Playing);
            if (videoFrame.PlayPromptVisible) throw new InvalidOperationException("Temporarily stopped autoplay acquired a central play button.");
        }
        finally { focusProbe.Close(); Window.Activate(); }
        await WaitForLikedLayoutAsync(() => Window.IsActive && videoFrame.Playing);
        await WaitForLoginFrameAsync(); await Task.Delay(100); RememberNativeChatVideoBackdrop();
        var downloadsBeforeOpen = media.Downloads;
        var opening = ObserveChatVideoOpeningAsync(() => ActivateChatAttachmentAsync(videoRow.Message!.Id, 0));
        if (chatVideoStatus.Text.Length != 0 || chatVideoImage.Source == null)
            throw new InvalidOperationException("Opening a cached video showed loading instead of its existing poster.");
        await opening;
        await WaitForLikedLayoutAsync(() => chatVideoVersion > 0 && chatVideoSurface.Reveal > .999);
        await WaitForLikedLayoutAsync(() => !videoFrame.Playing);
        if (videoFrame.PlayPromptVisible) throw new InvalidOperationException("Opening the viewer added a play button to the inline preview.");
        await WaitForLikedLayoutAsync(() => chatVideoControls.ActualWidth is >= 420 and <= 450);
        var gearGlyph = (Image)chatVideoGear.FindVisualChild<Image>()!;
        if (Math.Abs(chatVideoMuteIcon.Bounds.X - chatVideoTime.Bounds.X) > 1 || Math.Abs(gearGlyph.Bounds.Right - chatVideoRemaining.Bounds.Right) > 1)
            throw new InvalidOperationException("Video icons do not share their outer insets with the time labels.");
        await Task.Delay(2200);
        if (!chatVideoControls.IsVisible || chatVideoControls.Opacity != 1) throw new InvalidOperationException("Video controls disappeared while idle.");
        CaptureUiPreview("chats-video-player"); VerifyNativeChatVideoBackdrop();
        chatVideoVolume.Value = 37; SetChatVideoRate(1.5);
        if (chatVideoSession!.PlaybackSettings() is not { Volume: 37, Rate: > 1.49 and < 1.51 }) throw new InvalidOperationException("Video volume/rate controls did not reach the decoder.");
        SetChatVideoMuted(true); SetChatVideoMuted(false);
        SetChatVideoSpeedMenu(true);
        await WaitForLikedLayoutAsync(() => chatVideoSpeedMenu.Opacity > .99);
        if (chatVideoRateChoices.Count != 6 || chatVideoRateChoices[1.5].Check.Opacity != 1 || chatVideoRateChoices.Values.Count(c => c.Check.Opacity == 1) != 1)
            throw new InvalidOperationException("Speed presets lost their descriptive rows or unique selection.");
        chatVideoSpeedSlider.Value = 1.35;
        if (!chatVideoSpeedOpen || chatVideoRateChoices.Values.Any(c => c.Check.Opacity != 0) || Math.Abs(chatVideoSession.PlaybackSettings().Rate - 1.35) > .01)
            throw new InvalidOperationException("Custom speed slider closed its menu or failed to reach the decoder.");
        SetChatVideoRate(1.5, closeMenu: false);
        CaptureUiPreview("chats-video-speed"); SetChatVideoSpeedMenu(false);
        var beforeExpand = chatVideoImage.Bounds;
        SetChatVideoExpanded(true);
        await WaitForLikedLayoutAsync(() => chatVideoExpansion > .05 && chatVideoExpansion < .95);
        if (chatVideoImage.Bounds.Height <= beforeExpand.Height || chatVideoImage.Bounds.Height >= chatVideoSurface.Bounds.Height)
            throw new InvalidOperationException("Video enlargement did not pass through an intermediate geometry.");
        await WaitForLikedLayoutAsync(() => chatVideoExpansion > .999);
        await WaitForLoginFrameAsync(); await Task.Delay(100); VerifyNativeChatVideoBackdrop(0);
        CaptureUiPreview("chats-video-expanded");
        chatVideoInteraction = DateTimeOffset.UtcNow.AddSeconds(-5); RefreshChatVideoControlVisibility();
        await WaitForLikedLayoutAsync(() => chatVideoControls.Opacity < .01 && chatVideoPrevious.Opacity < .01 && chatVideoNext.Opacity < .01);
        RevealChatVideoControls(); await WaitForLikedLayoutAsync(() => chatVideoControls.Opacity > .99 && chatVideoPrevious.Opacity > .99 && chatVideoNext.Opacity > .99);
        SetChatVideoExpanded(false); await WaitForLikedLayoutAsync(() => chatVideoExpansion < .001);
        SetChatVideoRate(1); chatVideoVolume.Value = 100;
        CloseChatVideo();
        await WaitForLikedLayoutAsync(() => chatVideoSurface.Reveal is > .05 and < .95);
        if (chatVideoImage.Source == null) throw new InvalidOperationException("Closing fade discarded the video frame early.");
        await chatVideoClosing;
        for (var reopen = 0; reopen < 3; reopen++)
        {
            await WaitForLoginFrameAsync();
            opening = ObserveChatVideoOpeningAsync(() => ActivateChatAttachmentAsync(videoRow.Message!.Id, 0));
            if (chatVideoStatus.Text.Length != 0 || chatVideoImage.Source == null) throw new InvalidOperationException("Reopening video discarded the cached poster.");
            await opening;
            await WaitForLikedLayoutAsync(() => chatVideoVersion > 0 && chatVideoControls.ActualWidth is >= 420 and <= 450 && chatVideoSurface.Reveal > .999);
            await WaitForLoginFrameAsync(); await Task.Delay(100); VerifyNativeChatVideoBackdrop();
            CaptureUiPreview("chats-video-reopened-" + reopen); CloseChatVideo(); await chatVideoClosing;
        }
        await ObserveChatVideoOpeningAsync(() => ActivateChatAttachmentAsync(videoRow.Message!.Id, 0));
        SetChatVideoExpanded(true); await WaitForLikedLayoutAsync(() => chatVideoExpansion is > .1 and < .9);
        var replacingExpansion = chatVideoExpansion;
        opening = ActivateChatAttachmentAsync(videoRow.Message!.Id, 0);
        if (Math.Abs(chatVideoExpansion - replacingExpansion) > .001)
            throw new InvalidOperationException("Replacing video reset an in-progress enlargement.");
        await opening; await WaitForLikedLayoutAsync(() => chatVideoExpansion == 1 && !chatVideoExpansionClock.IsRunning);
        opening = ActivateChatAttachmentAsync(videoRow.Message!.Id, 0);
        if (chatVideoSurface.Reveal != 1 || chatVideoExpansion != 1)
            throw new InvalidOperationException("Replacing an enlarged video reset its backdrop or replayed the opening fade.");
        await opening;
        SetChatVideoExpanded(false); await WaitForLikedLayoutAsync(() => chatVideoExpansion < .001);
        CloseChatVideo(); await WaitForLikedLayoutAsync(() => chatVideoSurface.Reveal is > .1 and < .9);
        var closingReveal = chatVideoSurface.Reveal;
        opening = ObserveChatVideoOpeningAsync(() => ActivateChatAttachmentAsync(videoRow.Message!.Id, 0));
        if (Math.Abs(chatVideoSurface.Reveal - closingReveal) > .001)
            throw new InvalidOperationException("Quick reopening reset the current backdrop fade.");
        await opening; CloseChatVideo(); await chatVideoClosing;
        if (media.Downloads != downloadsBeforeOpen) throw new InvalidOperationException("Reopening cached video downloaded the media again.");
        if (chatVideoSession != null || chatVideoImage.Source != null || chatVideoBitmap != null) throw new InvalidOperationException("Closing video retained decoder/frame resources.");
        await VerifyChatVideoMusicAsync(videoRow.Message!.Id);
        VerifyChatVideoDirtyClip();
        if (Environment.GetEnvironmentVariable("POOKIE_CHAT_VIDEO_FIXTURE") is { Length: > 0 } playbackFixture)
            await VerifyChatVideoPlaybackBackdropAsync(playbackFixture);
        static byte[] Fixture(string name)
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media." + name)!;
            using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
        }
    }
}
