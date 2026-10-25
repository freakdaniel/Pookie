using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatWorkspaceAsync(ChatFixtureTransport transport)
    {
        chatInput.Text="";SaveChatDraft();
        ToggleChatPin(transport.Third.Id);
        if(chatInboxItems[0].Recipient.Id!=transport.Third.Id)throw new InvalidOperationException("Pinned dialog did not move to the front.");
        OpenChatFolderEditor(null);await WaitForLikedLayoutAsync(() => !chatColumns.Animating && !chatDetailsTransition.Animating && chatDetailsCard.Opacity > .99);
        var name=chatDetailsBody.FindVisualChild<TextBox>() as TextBox??throw new InvalidOperationException("Folder name editor missing.");
        name.Text="Музыка";
        var choices=chatDetailsBody.Children.OfType<StackPanel>().Single();
        var peerButton=choices.Children.OfType<Button>().First();RouteWaveformClick(SettingsCenter(peerButton));
        var save=chatDetailsBody.Children.OfType<Button>().First();RouteWaveformClick(SettingsCenter(save));
        if(chatWorkspace.Folders is not [{Name:"Музыка",Peers.Length:1}])throw new InvalidOperationException("Folder editor did not save its name/membership.");
        chatFolder=chatWorkspace.Folders[0].Id;RenderChatInbox();if(chatInboxItems.Count!=1)throw new InvalidOperationException("Folder membership did not filter inbox.");
        OpenChatFolderEditor(chatWorkspace.Folders[0]);await WaitForLikedLayoutAsync(() => !chatColumns.Animating && !chatDetailsTransition.Animating && chatDetailsCard.Opacity > .99);
        var delete=chatDetailsBody.Children.OfType<Button>().Last();RouteWaveformClick(SettingsCenter(delete));
        if(chatWorkspace.Folders.Length!=0||chatInboxItems.Count!=2)throw new InvalidOperationException("Deleting folder deleted dialogs.");
        var savedRows = new List<TextBlock>();
        VisualTree.Visit(chatSidebarNormal, element => { if (element is TextBlock { Text: "Избранное" } label) savedRows.Add(label); });
        if (savedRows.Count != 0) throw new InvalidOperationException("Saved messages still occupy a row in the dialog list.");
        var savedShortcut = new List<Button>();
        VisualTree.Visit((FrameworkElement)chatColumns.Children[0], element => { if (element is Button { ToolTip: TextBlock { Text: "Избранное" } } button) savedShortcut.Add(button); });
        if (savedShortcut.Count != 1) throw new InvalidOperationException("The saved-messages shortcut is missing from the sidebar toolbar.");
        var target=chatMessages.Last();ChatFavoriteMessage(target);RouteWaveformClick(SettingsCenter(savedShortcut[0]));
        await WaitForLikedLayoutAsync(() => chatFavoritesOpen && chatSavedMessages.ActualHeight > 0 && !chatColumns.Animating);
        if(chatWorkspace.Favorites.Length!=1||chatName.Text!="Избранное"||chatComposer.IsVisible||chatColumns.DetailsOpen||!new ChatWorkspaceStore(dataPaths,42).Load().SavedOpen)
            throw new InvalidOperationException("Saved messages did not open as a durable standalone local conversation.");
        CaptureUiPreview("chats-saved-conversation");
        await SelectChatAsync(transport.Other); ToggleChatSelection(target);
        if(!chatSelectionBar.IsVisible||chatSelected.Count!=1)throw new InvalidOperationException("Multiple selection toolbar missing.");
        RouteChatEscape();if(chatSelectionBar.IsVisible)throw new InvalidOperationException("Escape did not close selection.");
        await VerifyChatReplyUiAsync(target);chatInput.Text="Связанный ответ";await SendChatAsync();
        if(chatReply!=null||!chatMessages.Any(m=>ChatProtocol.Project(m,42,90).Reply?.MessageId==target.Id))throw new InvalidOperationException("Reply was not confirmed and projected through the outbox.");
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && ChatRows.Any(row => row.Bound && row.Quote.IsVisible && row.Quote.Title.Length > 0));
        await WaitForLoginFrameAsync(); CaptureUiPreview("chats-reply-message");
        await VerifyChatFooterUiAsync(target);
        var visibleCount=chatMessageItems.Count;await ReactToChatAsync(target,"🔥");
        if(chatMessageItems.Count!=visibleCount||chatReactions.GetValueOrDefault(target.Id)?.GetValueOrDefault(42)!="🔥")throw new InvalidOperationException("Reaction became a text bubble or was not projected.");
        await ReactToChatAsync(target,"🔥");if(chatReactions.GetValueOrDefault(target.Id)?.ContainsKey(42)==true)throw new InvalidOperationException("Second reaction did not remove own emoji.");
        var avatarBeforeSearch = me!.AvatarUrl;
        const string cachedAvatarUrl = "https://i1.sndcdn.com/search-avatar-fixture.jpg";
        var cachedAvatar = ImageSource.FromBgraPixels(1,1,[220,140,80,255],hasAlpha:false);
        me = me with { AvatarUrl = cachedAvatarUrl }; artworkRequests[cachedAvatarUrl] = Task.FromResult<ImageSource?>(cachedAvatar);
        // Same own-author path as a thread result, before the result enters a visual tree.
        OpenChatThreadSearch();
        var detachedResult = ChatSearchResult(new(90,new("cached-avatar-test",42,"Аватар",DateTimeOffset.UtcNow)),()=>{});
        if (detachedResult.FindVisualChild<Image>() is not Image { Source: var avatar } || !ReferenceEquals(avatar,cachedAvatar))
            throw new InvalidOperationException("Synchronously cached search avatar was discarded before attachment.");
        me = me with { AvatarUrl = avatarBeforeSearch };
        transport.DelayThread = true; transport.ThreadReady = null;
        OpenChatThreadSearch(); chatMessageSearch.Text="Связанный"; await SearchChatMessagesAsync();
        await WaitForLikedLayoutAsync(() => chatThreadSearchPopup.ActualHeight > 50 && chatSearchCount.ActualWidth > 0 && chatThreadSearchResults.Children.OfType<Button>().FirstOrDefault() is { ActualWidth: > 100, ActualHeight: > 0 } && !chatThreadSearchResults.IsMeasureDirty && !chatThreadSearchResults.IsArrangeDirty && !chatThreadSearchPopup.Animating && !chatSearchResultsTransition.Animating);
        await WaitForLoginFrameAsync();
        if(chatSearchHits.Length==0||!chatThreadSearchOpen||chatColumns.DetailsOpen)
            throw new InvalidOperationException("Thread search did not show local FTS results below the header.");
        var expandedSearchHeight = chatThreadSearchPopup.ActualHeight;
        var searchHeights = new List<double>(); var searchContentVanished = false;
        void SampleSearchReveal()
        {
            searchHeights.Add(chatThreadSearchPopup.ActualHeight);
            if (chatThreadSearchPopup.ActualHeight > 50 && chatThreadSearchResults.ActualHeight < 1)
                searchContentVanished = true;
        }
        Window.FrameRendered += SampleSearchReveal; chatThreadSearchPopup.Expanded = false;
        await WaitForLikedLayoutAsync(() => !chatThreadSearchPopup.Animating && chatThreadSearchPopup.ActualHeight < 41);
        Window.FrameRendered -= SampleSearchReveal;
        if (!searchHeights.Any(h => h > 41 && h < expandedSearchHeight - 1))
            throw new InvalidOperationException($"Search results collapse without intermediate animation frames: start={expandedSearchHeight}, frames={string.Join(",",searchHeights)}.");
        if (searchContentVanished) throw new InvalidOperationException("Search contents disappear before the collapsing surface.");
        chatThreadSearchPopup.Expanded = true;
        await WaitForLikedLayoutAsync(() => !chatThreadSearchPopup.Animating && chatThreadSearchPopup.ActualHeight > 50);
        CaptureUiPreview("chats-inline-search");
        await WaitForLikedLayoutAsync(() => transport.ThreadReady != null);
        transport.ThreadReady!.TrySetResult(); await WaitForLikedLayoutAsync(() => !chatHistoryLoading);
        chatMessageSearch.Text = "Старое"; await SearchChatMessagesAsync();
        if (chatSearchHits.Length != 5) throw new InvalidOperationException("Default search did not automatically index older server pages.");
        chatMessageSearch.Text = "Связанный"; await SearchChatMessagesAsync();
        await OpenChatSearchHitAsync(0);
        if (!chatThreadSearchOpen || chatThreadSearchPopup.Expanded || !chatSearchCount.Text.Contains('/'))
            throw new InvalidOperationException("Search navigation lost its query or result counter.");
        await chatHistory!.SavePageAsync(42,90,new([],"https://api-v2.soundcloud.com/users/42/conversations/90/messages?older=2"));
        transport.DelayThread=true;transport.ThreadReady=null;
        chatMessageSearch.Text="Отменяемый";await SearchChatMessagesAsync();
        await WaitForLikedLayoutAsync(()=>transport.ThreadReady!=null);
        chatHistorySearch!.Cancel();await WaitForLikedLayoutAsync(()=>!chatHistoryLoading);
        CloseChatSearch();
        await WaitForLikedLayoutAsync(() => !chatSidebarTransition.Animating);
        var sidebarOpacities = new List<double>();
        void SampleSidebarReveal() => sidebarOpacities.Add(chatSidebarSearch.Opacity);
        Window.FrameRendered += SampleSidebarReveal; RouteWaveformClick(SettingsCenter(chatSearch));
        await WaitForLikedLayoutAsync(() => chatSidebarSearching && !chatSidebarTransition.Animating);
        Window.FrameRendered -= SampleSidebarReveal;
        if (!sidebarOpacities.Any(o => o > .01 && o < .99) || chatSidebarBack.Bounds.Right >= chatSearch.Bounds.X)
            throw new InvalidOperationException("Focusing sidebar search did not animate its content or keep the back button outside the field.");
        await WaitForLikedLayoutAsync(() => chatSearchLanding.IsVisible && chatSearchLanding.FindVisualChild<Image>() is Image { ActualHeight: > 0 });
        await WaitForLoginFrameAsync(); CaptureUiPreview("chats-search-recent");
        OpenChatGlobalSearch(); chatSearch.Text="Связанный"; await SearchChatsAsync();
        if (!chatGlobalSearchFrame.IsVisible || !chatGlobalSearchResults.Children.OfType<Button>().Any() || chatColumns.DetailsOpen)
            throw new InvalidOperationException("Global message search did not stay in the left sidebar.");
        await WaitForLikedLayoutAsync(() => chatGlobalSearchResults.Children.OfType<Button>().FirstOrDefault() is { ActualWidth: > 100, ActualHeight: > 0 } && !chatGlobalSearchResults.IsMeasureDirty && !chatGlobalSearchResults.IsArrangeDirty && !chatGlobalResultsTransition.Animating && !chatSidebarTransition.Animating);
        await WaitForLikedLayoutAsync(() => ChatRows.Where(r=>r.Bound).All(r=>r.Root.Background.A==0));
        await WaitForLoginFrameAsync(); CaptureUiPreview("chats-global-search");
        RouteWaveformClick(SettingsCenter(chatGlobalSearchClear));
        await WaitForLikedLayoutAsync(() => chatSearch.Text.Length==0 && chatSearchLanding.IsVisible);
        if (!chatSidebarSearching || chatGlobalSearchFrame.IsVisible) throw new InvalidOperationException("Clearing the query exited search instead of showing recent contacts.");
        CloseChatGlobalSearch(); await SearchChatsAsync();
        var avatarPixels = new byte[96 * 96 * 4];
        for (var y = 0; y < 96; y++)
        for (var x = 0; x < 96; x++)
        {
            var pixel = (y * 96 + x) * 4;
            avatarPixels[pixel] = (byte)(x < 48 ? 180 : 35);
            avatarPixels[pixel + 1] = (byte)(y < 48 ? 70 : 180);
            avatarPixels[pixel + 2] = (byte)(x < 48 ? 60 : 230);
            avatarPixels[pixel + 3] = 255;
        }
        var avatarSource = ImageSource.FromBgraPixels(96, 96, avatarPixels, hasAlpha: false);
        chatHeaderAvatar.Source = avatarSource;
        chatInput.Text = ""; RefreshChatComposer();
        Window.WindowSize=WindowSize.Resizable(1840,900,minWidth:760,minHeight:600);
        await WaitForLikedLayoutAsync(()=>Math.Abs(Window.ClientSize.Width-1840)<1&&chatColumns.ActualWidth>1300&&!chatColumns.IsArrangeDirty&&!chatDetailsCard.IsVisible);
        if (chatColumns.Children[1] is not ChatStage stage || stage.Children.Count != 1 || ChatOutgoingColor.B <= ChatOutgoingColor.R)
            throw new InvalidOperationException("The conversation still has a wallpaper or lost the blue outgoing-message palette.");
        CaptureUiPreview("chats-neutral-wide");
        VerifyChatScrollColumn();
        chatRecipient = chatRecipient! with { AvatarUrl = cachedAvatarUrl };
        foreach(var width in new[]{1840,1000,800})
        {
            Window.WindowSize=WindowSize.Resizable(width,900,minWidth:760,minHeight:600);
            await WaitForLikedLayoutAsync(() => !chatColumns.IsArrangeDirty && !chatColumns.Animating);
            var beforeWidth = ((FrameworkElement)chatColumns.Children[1]).ActualWidth;
            var motionFrames = new List<double>();
            void SampleMotion() => motionFrames.Add(((FrameworkElement)chatColumns.Children[1]).ActualWidth);
            chatViewport.BeforePaint += SampleMotion;
            OpenChatInfo();
            await WaitForLikedLayoutAsync(()=>Math.Abs(Window.ClientSize.Width-width)<1&&Math.Abs(chatColumns.ActualWidth-(Math.Min(1440,width)-72))<1&&!chatColumns.IsMeasureDirty&&!chatColumns.IsArrangeDirty&&!chatColumns.Animating&&chatDetailsCard.Opacity>.99 && !chatHistoryLoading);
            chatViewport.BeforePaint -= SampleMotion;
            VerifyChatScrollColumn();
            await WaitForLikedLayoutAsync(() => chatProfileBackdrop.Target != new PlayerPalette(ChatGlassSurface,ChatGlassSurface,ChatGlassSurface) && !chatProfileBackdrop.Animating);
            if (chatColumns.Children[0] is not GlassCard inboxCard || inboxCard.Background != chatIdentityCard.Background || chatDetailsCard.Background != chatComposer.Background)
                throw new InvalidOperationException("Chat side panels do not share the header and composer glass surface.");
            if (Math.Abs(chatDetailsCard.Bounds.Bottom - chatComposer.Bounds.Bottom) > 1 / Window.DpiScale ||
                inboxCard.IsVisible && Math.Abs(inboxCard.Bounds.Bottom - chatComposer.Bounds.Bottom) > 1 / Window.DpiScale)
                throw new InvalidOperationException("Chat side panels lost the composer bottom inset.");
            var shouldDock=chatColumns.ActualWidth>=1232;
            var afterWidth = ((FrameworkElement)chatColumns.Children[1]).ActualWidth;
            if (shouldDock && !motionFrames.Any(w => w > afterWidth + 2 && w < beforeWidth - 2))
                throw new InvalidOperationException("The conversation snapped to its final width instead of animating with the information panel.");
            if (!shouldDock && chatColumns.Children[2] is Border scrim && scrim.Bounds.X < ((FrameworkElement)chatColumns.Children[1]).Bounds.X - 1)
                throw new InvalidOperationException("Information scrim dims the unrelated inbox.");
            if(shouldDock&&(chatOptions.Bounds.Right>chatDetailsCard.Bounds.X-15||chatComposer.Bounds.Right>chatDetailsCard.Bounds.X-15))throw new InvalidOperationException("Docked details overlap conversation.");
            CaptureUiPreview("chats-workspace-"+width);
            if (width == 1840)
            {
                await VerifyChatProfileGradientAsync();
                OpenChatThreadSearch(); chatMessageSearch.Text = "Связанный"; await SearchChatMessagesAsync();
                await WaitForLikedLayoutAsync(() => !chatThreadSearchPopup.Animating && !chatSearchResultsTransition.Animating);
                if (!chatColumns.DetailsOpen || !chatDetailsCard.IsVisible || chatSearchHits.Length == 0)
                    throw new InvalidOperationException("Opening thread search closed the information panel.");
                CaptureUiPreview("chats-search-with-info");
                await OpenChatSearchHitAsync(0);
                if (!chatColumns.DetailsOpen) throw new InvalidOperationException("Jumping to a thread search result closed the information panel.");
                CloseChatSearch();
            }
            var close = chatDetailsCard.FindVisualChild<Button>() as Button ?? throw new InvalidOperationException("Details close button missing.");
            RouteWaveformPointer(SettingsCenter(close)); await Task.Delay(250);
            if (chatDetailsCard.HoverEnabled) throw new InvalidOperationException("The drawer animates a full-panel glass reflection when the pointer leaves its close button.");
            var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
            var anchor = ChatRows.Where(row => row.Bound && row.Message != null && row.Root.Bounds.Bottom > viewer.Bounds.Y+1 && row.Root.Bounds.Y < viewer.Bounds.Bottom)
                .OrderBy(row => row.Root.Bounds.Y).First();
            var anchorId = anchor.Message!.Id; var anchorY = anchor.Root.Bounds.Y-viewer.Bounds.Y;
            var closeSamples = new List<(double Reveal,int Passes)>();
            void SampleClose() => closeSamples.Add((chatColumns.Reveal,chatRestorePasses));
            Window.FrameRendered += SampleClose;
            if (width == 1840) RouteWaveformClick(SettingsCenter(close)); else RouteChatEscape();
            if(chatColumns.DetailsOpen)throw new InvalidOperationException("Closing attachments did not start.");
            await WaitForLikedLayoutAsync(() => !chatColumns.Animating && !chatDetailsCard.IsVisible && chatRestoreOffset == null);
            Window.FrameRendered -= SampleClose;
            if (!closeSamples.Any(sample => sample.Reveal > .01 && sample.Reveal < .99) || closeSamples.Zip(closeSamples.Skip(1)).Any(pair => pair.Second.Reveal > pair.First.Reveal+.001))
                throw new InvalidOperationException("Closing details snapped or reversed instead of sliding through intermediate frames.");
            // This fixture keeps the reading width unchanged. Ordinary window
            // and header-capture paints must not each run the full eight-pass
            // cold-restore loop just because a drawer is still animating.
            if (closeSamples.Zip(closeSamples.Skip(1)).Any(pair => pair.Second.Reveal is > .05 and < .95 && pair.Second.Passes-pair.First.Passes > 8))
                throw new InvalidOperationException("Closing details repeats the full history-restoration loop on every animation frame.");
            var closedAnchor = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == anchorId);
            if (closedAnchor == null || Math.Abs(closedAnchor.Root.Bounds.Y-viewer.Bounds.Y-anchorY) > 2/Window.DpiScale)
                throw new InvalidOperationException("Closing details displaced the visible history anchor.");
        }
        Window.WindowSize=WindowSize.Resizable(1280,900,minWidth:760,minHeight:600);
        var nativeDpi=Window.Dpi;
        try
        {
            foreach(var dpi in new uint[]{96,110,144,192})
            {
                typeof(Window).GetMethod("SetDpi",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(Window,[dpi]);
                Window.PerformLayout();await WaitForLoginFrameAsync();
                if(chatIdentityCard.ActualWidth>696+1/Window.DpiScale||chatComposer.ActualWidth>696+1/Window.DpiScale||Math.Abs(chatIdentityCard.ActualWidth-chatComposer.ActualWidth)>1/Window.DpiScale||Math.Abs(chatColumns.Bounds.Bottom-chatComposer.Bounds.Bottom-16)>2/Window.DpiScale)
                    throw new InvalidOperationException("Chat column or composer inset failed at DPI "+dpi+$": header={chatIdentityCard.ActualWidth}, composer={chatComposer.ActualWidth}, gap={chatColumns.Bounds.Bottom-chatComposer.Bounds.Bottom}");
                if (chatSend.Content is not FrameworkElement glyph || Math.Abs(glyph.Bounds.X + glyph.ActualWidth / 2 - chatSend.Bounds.X - chatSend.ActualWidth / 2) > 1 / Window.DpiScale ||
                    Math.Abs(glyph.Bounds.Y + glyph.ActualHeight / 2 - chatSend.Bounds.Y - chatSend.ActualHeight / 2) > 1 / Window.DpiScale)
                    throw new InvalidOperationException("Dynamic microphone glyph lost its button alignment at DPI " + dpi);
                CaptureUiPreview("chats-dpi-"+dpi);
            }
        }
        finally{typeof(Window).GetMethod("SetDpi",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(Window,[nativeDpi]);}
        const string mediaTrackUrl = "https://soundcloud.com/fixture/track", mediaPlaylistUrl = "https://soundcloud.com/fixture/sets/list";
        chatMusicCache[mediaTrackUrl] = Pookie.SoundCloud.LibraryItem.FromTrack(new Pookie.SoundCloud.SoundCloudTrack { Id = 101, Title = "Трек из переписки", Duration = 120000 });
        chatMusicCache[mediaPlaylistUrl] = new("playlist:102", "Подборка из переписки", "", null,
            Playlist: new Pookie.SoundCloud.SoundCloudPlaylist { Id = 102, Title = "Подборка из переписки", TrackCount = 2 });
        await chatHistory!.SavePageAsync(42, 90, new([
            new("media-library-track", 90, mediaTrackUrl, DateTimeOffset.UtcNow.AddHours(-2)),
            new("media-library-playlist", 90, mediaPlaylistUrl, DateTimeOffset.UtcNow.AddHours(-2))], null));
        OpenChatInfo(); await RenderChatMediaLibraryAsync("Музыка");
        var libraryTracks = compactTrackRows.Where(row => chatMediaLibrary!.Children.Contains(row.Root)).ToArray();
        var playlistShown = false;
        VisualTree.Visit(chatMediaLibrary!, element => { if (element is TextBlock { Text: "Плейлист" }) playlistShown = true; });
        if (libraryTracks.Length != 1 || libraryTracks[0].Track?.Id != 101 || !playlistShown)
            throw new InvalidOperationException("Media library rendered a playlist as an unknown zero-duration track.");
        await WaitForLikedLayoutAsync(() => chatDetailsCard.Opacity > .99);
        CaptureUiPreview("chats-media-library-types");
        await WaitForLikedLayoutAsync(() => !chatMediaTransition.Animating && chatMediaPill is { Animating: false });
        var startPill = chatMediaPill!.SelectionBounds.X; var pillFrames = new List<double>();
        void SamplePill() => pillFrames.Add(chatMediaPill.SelectionBounds.X);
        Window.FrameRendered += SamplePill;
        RouteWaveformClick(SettingsCenter(chatMediaPill.Children.OfType<Button>().ElementAt(1)));
        await WaitForLikedLayoutAsync(() => chatMediaLibraryKind == "Фото" && !chatMediaPill.Animating && !chatMediaTransition.Animating);
        Window.FrameRendered -= SamplePill;
        var endPill = chatMediaPill.SelectionBounds.X;
        if (!pillFrames.Any(x => x > startPill + .5 && x < endPill - .5))
            throw new InvalidOperationException("The media-tab pill snapped instead of moving through intermediate positions.");
        var generationBeforeActiveClick = chatDetailsGeneration;
        var selectedTab = chatMediaPill.Children.OfType<Button>().ElementAt(1);
        RouteWaveformClick(SettingsCenter(selectedTab));
        if (chatDetailsGeneration != generationBeforeActiveClick || selectedTab.IsHitTestVisible || selectedTab.Background.A != 0)
            throw new InvalidOperationException("Active media pill accepts input or draws a hover surface.");
        CaptureUiPreview("chats-sidebar-photo-pill");
        await VerifyChatLibraryPreviewsAsync(); CloseChatDetails();
        chatVoiceFactory=_=>throw new InvalidOperationException("Synthetic unavailable microphone");
        await ContinueChatVoiceAsync();
        if(chatVoiceRecorder!=null||chatVoiceAudioActive||chatVoiceBusy||!chatThreadStatus.Value.Contains("микрофон"))throw new InvalidOperationException("Capture failure left a microphone session or music suspension behind.");
        chatThreadStatus.Value="";
        chatVoiceFactory=_=>new SyntheticChatRecorder();
        var voiceFrames = new List<(double Amount,double Text,double Voice)>();
        void SampleVoiceMode() => voiceFrames.Add((chatComposerLayout.VoiceAmount,chatInput.Opacity,chatVoiceBar.Opacity));
        Window.FrameRendered += SampleVoiceMode;
        await ContinueChatVoiceAsync();if(chatVoiceRecorder is not {Recording:true}||!chatVoiceBar.IsVisible)throw new InvalidOperationException("Synthetic recorder did not start.");
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && !chatComposerLayout.IsChangingMode && chatComposerLayout.VoiceAmount>.99 && chatVoiceBar.ActualWidth > 100 && !chatColumns.Animating);
        Window.FrameRendered -= SampleVoiceMode;
        if (!voiceFrames.Any(frame=>frame.Amount>.02 && frame.Amount<.98 && frame.Text>.02 && frame.Voice>.02))
            throw new InvalidOperationException("Starting voice capture snapped the editor instead of cross-fading both surfaces.");
        if (chatVoiceBar.Bounds.Y < chatComposer.Bounds.Y || chatVoiceBar.Bounds.Bottom > chatComposer.Bounds.Bottom || chatInput.IsVisible)
            throw new InvalidOperationException("Voice recording is not integrated into the composer surface.");
        CaptureUiPreview("chats-synthetic-recording");
        var synthetic = (SyntheticChatRecorder)chatVoiceRecorder;
        var beforePeak = chatVoiceWave.LastLiveFrame;
        var livePositions = new List<double>();
        void SampleLivePosition() => livePositions.Add(chatVoiceWave.LivePosition);
        Window.FrameRendered += SampleLivePosition;
        synthetic.AppendLevel(.005f,2);
        await WaitForLikedLayoutAsync(()=>chatVoiceWave.IsLive && chatVoiceWave.LastLiveFrame >= beforePeak+2);
        synthetic.AppendLevel(.7f,4);
        await WaitForLikedLayoutAsync(()=>chatVoiceWave.LastLiveFrame >= beforePeak+6);
        if (chatVoiceWave.LiveFrameCount<6) throw new InvalidOperationException("Live PCM did not reach the recording waveform.");
        await WaitForLikedLayoutAsync(()=>!chatVoiceWave.IsScrolling);
        Window.FrameRendered -= SampleLivePosition;
        if (livePositions.Count<3 || livePositions.Zip(livePositions.Skip(1)).Any(pair=>pair.Second<pair.First-.00001) || !livePositions.Any(p=>Math.Abs(p-Math.Round(p))>.05))
            throw new InvalidOperationException("Live voice bars moved backwards or advanced only in whole capture packets.");
        await WaitForLoginFrameAsync();
        if (chatVoiceWave.Bounds.Right > chatVoiceTime.Bounds.X-4)
            throw new InvalidOperationException($"The live waveform overlaps its timer: wave={chatVoiceWave.Bounds}, timer={chatVoiceTime.Bounds}.");
        CaptureUiPreview("chats-live-voice-wave");
        chatVoiceRecorder.Pause(); var pausedFrame=chatVoiceWave.LastLiveFrame;
        synthetic.AppendLevel(1,3); await Task.Delay(100);
        if (chatVoiceWave.LastLiveFrame!=pausedFrame) throw new InvalidOperationException("Paused capture kept advancing its live waveform.");
        RefreshChatVoice();await chatVoiceRecorder.ResumeAsync();
        synthetic.FinishReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishingVoice=StopChatVoiceAsync();await Task.Delay(180);
        if (!chatVoiceBar.IsVisible || chatComposerLayout.VoiceAmount<.99 || chatInput.IsVisible)
            throw new InvalidOperationException("Encoding a stopped recording flashed the text editor before its preview.");
        synthetic.FinishReady.TrySetResult();await finishingVoice;
        if(chatVoiceDraft==null||!File.Exists(chatVoiceDraft.Path))throw new InvalidOperationException("Voice draft was not saved.");
        await WaitForLikedLayoutAsync(()=>!chatVoiceWave.IsLive && chatVoiceWave.PeakCount>0 && chatVoiceWave.ActualWidth>100);
        await WaitForLoginFrameAsync();CaptureUiPreview("chats-paused-voice-wave");
        var progressFrames = new List<double>();
        void SampleVoiceProgress() => progressFrames.Add(chatVoiceWave.DisplayedProgress);
        Window.FrameRendered += SampleVoiceProgress;
        chatVoiceWave.Progress = .82;
        await WaitForLikedLayoutAsync(() => !chatVoiceWave.ProgressAnimating);
        Window.FrameRendered -= SampleVoiceProgress;
        if (!progressFrames.Any(p => p > .02 && p < .8) || Math.Abs(chatVoiceWave.DisplayedProgress-.82)>.001)
            throw new InvalidOperationException("Stopped voice preview jumped to its new waveform position.");
        progressFrames.Clear(); Window.FrameRendered += SampleVoiceProgress;
        chatVoiceWave.Progress = .2;
        await WaitForLikedLayoutAsync(() => !chatVoiceWave.ProgressAnimating);
        Window.FrameRendered -= SampleVoiceProgress;
        if (!progressFrames.Any(p => p > .22 && p < .8) || Math.Abs(chatVoiceWave.DisplayedProgress-.2)>.001)
            throw new InvalidOperationException("Backward voice seek did not animate to its paused position.");
        CaptureUiPreview("chats-paused-voice-progress");
        var draftPath=chatVoiceDraft.Path;SaveChatDraft();
        if(new ChatWorkspaceStore(dataPaths,42).Load().Drafts?.GetValueOrDefault(90)?.Voice?.Path!=draftPath)throw new InvalidOperationException("Voice draft was not durable across a fresh store.");
        await SelectChatAsync(transport.Third);await SelectChatAsync(transport.Other);
        if(chatVoiceDraft?.Path!=draftPath)throw new InvalidOperationException("Changing dialog lost voice draft.");
        await WaitForLikedLayoutAsync(()=>!chatComposerLayout.IsChangingMode);
        voiceFrames.Clear();Window.FrameRendered+=SampleVoiceMode;
        await DeleteChatVoiceAsync();if(File.Exists(draftPath))throw new InvalidOperationException("Deleting recording left private draft file.");
        await WaitForLikedLayoutAsync(()=>!chatComposerLayout.IsChangingMode && !chatVoiceBar.IsVisible);
        Window.FrameRendered-=SampleVoiceMode;
        if (!voiceFrames.Any(frame=>frame.Amount>.02 && frame.Amount<.98 && frame.Text>.02 && frame.Voice>.02))
            throw new InvalidOperationException("Discarding voice capture did not fade back to the editor.");
        ReplyToChat(target);await ContinueChatVoiceAsync();await StopChatVoiceAsync();await SendChatAsync();
        await WaitForLikedLayoutAsync(()=>ChatRows.Any(r=>r.Bound&&r.Attachments.FindVisualChild<ChatVoiceCard>() is ChatVoiceCard {ActualHeight:>0}&&!r.Content.IsResizing&&r.Motion.Opacity>.99));
        var voiceMessage=chatMessages.Last(m=>ChatMediaReference.Parse(m.Body)!=null);
        var voiceRow = ChatRows.Single(r=>r.Bound && r.Message?.Id == voiceMessage.Id);
        var voiceCard = (ChatVoiceCard)voiceRow.Attachments.FindVisualChild<ChatVoiceCard>()!;
        if (Math.Abs(voiceRow.Time.Bounds.Bottom-voiceCard.DurationLabel.Bounds.Bottom)>2/Window.DpiScale ||
            voiceCard.DurationLabel.Bounds.Right>voiceRow.Footer.Bounds.X-4)
            throw new InvalidOperationException("Voice duration and sent time do not share a row or overlap each other.");
        if(CachedChatMedia(voiceMessage)?.Reply?.MessageId!=target.Id)throw new InvalidOperationException("Voice attachment lost its linked reply.");
        CaptureUiPreview("chats-synthetic-voice-message");
        await chatHistory!.SaveDialogsAsync(chatConversations);RememberChatThread();SaveChatDraft();
        var previousHistory=chatHistory;ResetChats();await previousHistory.DisposeAsync();
        BeginNavigation(new(Page.Home));await WaitForLoginFrameAsync();
        transport.DelayThread=true;transport.ThreadReady=null;
        var restored=OpenChatsAsync();
        await WaitForLikedLayoutAsync(()=>transport.ThreadReady!=null&&chatRecipient?.Id==90&&chatMessageItems.Count>0&&!chatThreadLoading.Skeleton.IsVisible);
        CaptureUiPreview("chats-cold-cache-before-network");
        transport.ThreadReady!.SetResult();await restored;chatTimer.Stop();
        var restoredIds=chatMessageItems.Select(m=>m.Id).ToArray();
        transport.FailReads=true;await RefreshChatsAsync();
        if(!restoredIds.SequenceEqual(chatMessageItems.Select(m=>m.Id)))throw new InvalidOperationException("Offline refresh discarded restored history.");
        OpenChatGlobalSearch(); chatSearch.Text="Связанный"; await SearchChatsAsync();
        me=new(){Id=7,Username="Другой аккаунт"};EnsureChatAccount();await RestoreChatHistoryAsync();
        if(chatMessageItems.Count!=0||chatWorkspace.Favorites.Length!=0||chatRecipient!=null||chatGlobalSearchResults.Children.Count!=0||chatSearch.Text.Length!=0||chatGlobalSearchFrame.IsVisible||chatColumns.DetailsOpen)throw new InvalidOperationException("Account switch exposed previous chats.");
        me=new(){Id=42,Username="Ты"};EnsureChatAccount();await RestoreChatHistoryAsync();chatTimer.Stop();transport.FailReads=false;
        if(chatRecipient?.Id!=90||chatWorkspace.Favorites.Length!=1)throw new InvalidOperationException("Account return lost durable favorites or last conversation.");
        Console.WriteLine("UI_CHAT_WORKSPACE_OK: folders, pins, favorites, selection, replies, reactions, FTS, dock/overlay, Escape and synthetic voice drafts");
    }

    private void VerifyChatScrollColumn()
    {
        var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
        var stage = (FrameworkElement)chatColumns.Children[1];
        if (Math.Abs(viewer.Bounds.Right-stage.Bounds.Right)>1/Window.DpiScale || Math.Abs(viewer.ActualWidth-stage.ActualWidth)>1/Window.DpiScale)
            throw new InvalidOperationException("The history scrollbar follows the reading column instead of the full conversation edge.");
        var bars = new List<ScrollBar>(); VisualTree.Visit(viewer,e=>{if(e is ScrollBar {Orientation:Orientation.Vertical,IsVisible:true} bar)bars.Add(bar);});
        if (bars.Count != 1 || Math.Abs(bars[0].Bounds.Right-stage.Bounds.Right)>1/Window.DpiScale)
            throw new InvalidOperationException("The history scrollbar did not move with the conversation's outer edge.");
        if (chatColumns.ScrollGutter > 15)
        {
            if (Math.Abs(bars[0].Bounds.Right-chatDetailsCard.Bounds.X)>1/Window.DpiScale || bars[0].Bounds.X < chatComposer.Bounds.Right)
                throw new InvalidOperationException("Docked history scrollbar overlaps the reading column instead of using the gap next to details.");
            var point = new Point(bars[0].Bounds.X+bars[0].ActualWidth/2, bars[0].Bounds.Y+bars[0].ActualHeight/2);
            if (!ReferenceEquals(chatColumns.HitTest(point),bars[0]))
                throw new InvalidOperationException("The scrollbar gutter is visible but does not accept pointer input.");
        }
        foreach (var row in ChatRows.Where(r=>r.Bound&&r.Root.ActualWidth>0))
            if (row.Root.ActualWidth>696+1/Window.DpiScale || Math.Abs(row.Root.Bounds.X+row.Root.ActualWidth/2-chatComposer.Bounds.X-chatComposer.ActualWidth/2)>2/Window.DpiScale)
                throw new InvalidOperationException("Expanding the history viewport displaced the centered message column.");
    }

    private async Task VerifyChatProfileGradientAsync()
    {
        var library = chatMediaLibrary!;
        for (var i = 0; i < 40; i++) library.Add(new TextBlock().Text("Вложение " + i).Height(36));
        var scroll = (ScrollViewer)chatDetailsCard.FindVisualChild<ScrollViewer>()!;
        await WaitForLikedLayoutAsync(() => SmoothScroll.Maximum(scroll) > 500 && !chatDetailsCard.IsArrangeDirty && !chatMediaTransition.Animating);
        await WaitForLoginFrameAsync();
        var beforeBounds = chatDetailsCard.Bounds; var before = ReadBackground();
        CaptureUiPreview("chats-profile-gradient-before-scroll");
        scroll.SetScrollOffsets(0, 180);
        await WaitForLikedLayoutAsync(() => Math.Abs(scroll.VerticalOffset-180)<1 && !scroll.IsArrangeDirty);
        await WaitForLoginFrameAsync();
        var after = ReadBackground();
        if (chatDetailsCard.Bounds != beforeBounds || before.SequenceEqual(after) || Math.Abs(chatProfileBackdrop.ScrollOffset-180)>1)
            throw new InvalidOperationException("The profile gradient stayed fixed instead of scrolling away with the avatar.");
        CaptureUiPreview("chats-profile-gradient-after-scroll");
        // The transition must meet the real glass without a one-pixel colour seam.
        var scale = Window.DpiScale;
        var boundary = (int)Math.Round((270-80)*scale);
        for (var row = boundary-3; row <= boundary+3; row++)
        for (var channel = 0; channel < 3; channel++)
            if (Math.Abs(before[row*3+channel]-before[(row-1)*3+channel])>2)
                throw new InvalidOperationException("The profile gradient ends in a different colour than its glass panel.");
        var shiftedBoundary = (int)Math.Round((270-180-80)*scale);
        for (var row = shiftedBoundary-3; row <= shiftedBoundary+3; row++)
        for (var channel = 0; channel < 3; channel++)
            if (Math.Abs(after[row*3+channel]-after[(row-1)*3+channel])>2)
                throw new InvalidOperationException("The scrolling profile gradient leaves a colour seam.");
        scroll.SetScrollOffsets(0,360);
        await WaitForLikedLayoutAsync(() => Math.Abs(scroll.VerticalOffset-360)<1 && !scroll.IsArrangeDirty);
        await WaitForLoginFrameAsync();
        var cleared = ReadBackground();
        var plainStart = (int)Math.Ceiling(14*scale)*3;
        if (chatProfileBackdrop.ScrollOffset != 270 || !after.AsSpan(plainStart).SequenceEqual(cleared.AsSpan(plainStart)))
            throw new InvalidOperationException("The profile gradient remains behind attachments after its header has scrolled away.");
        CaptureUiPreview("chats-profile-gradient-scrolled-away");
        library.Clear(); scroll.SetScrollOffsets(0,0);
        await WaitForLikedLayoutAsync(() => scroll.VerticalOffset == 0 && !scroll.IsArrangeDirty);

        byte[] ReadBackground()
        {
            var dpi = Window.DpiScale;
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(
                (int)Math.Ceiling(Window.ClientSize.Width*dpi),(int)Math.Ceiling(Window.ClientSize.Height*dpi),dpi));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(HeaderSurface); Window.Content!.Render(context); context.EndFrame();
            var cpu = (Aprillz.MewUI.Rendering.ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan();
            var x = (int)Math.Round((chatDetailsCard.Bounds.X+6)*dpi);
            var y = (int)Math.Round((chatDetailsCard.Bounds.Y+80)*dpi);
            var result = new byte[(int)Math.Round(320*dpi)*3];
            for (var row = 0; row < result.Length/3; row++) pixels.Slice((y+row)*cpu.StrideBytes+x*4,3).CopyTo(result.AsSpan(row*3,3));
            return result;
        }
    }
    private void RouteChatEscape()
    {
        var args=new KeyEventArgs(Key.Escape,0);
        typeof(Window).GetMethod("RaisePreviewKeyDown",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(Window,[args]);
        if(!args.Handled)RouteSettingsKey(Key.Escape);
    }
    private sealed class SyntheticChatRecorder:IChatVoiceRecorder
    {
        private readonly ChatVoicePcm pcm = new();
        internal SyntheticChatRecorder() => pcm.Append(Enumerable.Range(0,16000).Select(i => (float)(Math.Sin(i * Math.PI * 440 / 16000) * (.1 + .6 * Math.Pow(Math.Sin(i * Math.PI / 16000),2)))).ToArray());
        public bool Recording{get;private set;}
        public bool Paused{get;private set;}
        public double Duration=>pcm.Duration;
        public byte[] Peaks=>pcm.Peaks();
        public ChatVoiceMeter Meter=>pcm.Meter();
        internal void AppendLevel(float level, int windows=1)
        { if (Recording && !Paused) pcm.Append(Enumerable.Repeat(level,ChatVoicePcm.MeterSamples * windows).ToArray()); }
        public Task StartAsync(CancellationToken token=default){Recording=true;return Task.CompletedTask;}
        public Task ResumeAsync(CancellationToken token=default){Recording=true;Paused=false;return Task.CompletedTask;}
        public void Pause()=>Paused=true;
        internal TaskCompletionSource? FinishReady;
        public async Task<ChatVoiceRecording> FinishAsync(CancellationToken token=default)
        {
            Recording=false;
            if (FinishReady != null) await FinishReady.Task.WaitAsync(token);
            return ChatVoicePcm.Encode(pcm.Snapshot());
        }
        public Task CancelAsync(){Recording=false;return Task.CompletedTask;}
        public ValueTask DisposeAsync(){Recording=false;return ValueTask.CompletedTask;}
    }
}
