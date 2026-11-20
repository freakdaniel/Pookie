using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // A new foreground decoder waits only for the previous viewer close; the
    // account cleanup owner separately retains every older close/start operation.
    private Task chatVideoClosing = Task.CompletedTask;
    private readonly ObservableValue<bool> chatVideoOpen = new(false);
    private readonly DispatcherTimer chatVideoTimer = new(TimeSpan.FromMilliseconds(33));
    private ChatVideoSession? chatVideoSession;
    private WriteableBitmap? chatVideoBitmap;
    private long chatVideoVersion, chatVideoGeneration;
    private Image chatVideoImage = null!, chatVideoPlayIcon = null!, chatVideoMuteIcon = null!;
    private TextBlock chatVideoTitle = null!, chatVideoStatus = null!, chatVideoTime = null!;
    private Slider chatVideoSeek = null!;
    private bool chatVideoUpdating, chatVideoMuted;
    private DateTimeOffset chatVideoStarted;
    private ChatVideoSurface chatVideoSurface = null!;
    private Border chatVideoControls = null!;
    private Grid chatVideoHeader = null!;
    private Button chatVideoPrevious = null!, chatVideoNext = null!, chatVideoClose = null!;
    private (string Id, int Index)? chatVideoCurrent;
    private bool chatVideoShowingImage;
    private UIElement? chatVideoReturnFocus;
    private bool chatVideoReturnFocusKeyboard;
    private long chatImagePreviewRequest;
    private readonly SemaphoreSlim chatImagePreviewGate = new(1, 1);
    private List<(string Id, int Index)> ChatViewerEntries() => chatMessageItems.SelectMany(item =>
        (CachedChatMedia(item.Message)?.Attachments ?? []).Select((file, index) => (file, index))
            .Where(pair => !pair.file.AsFile && (pair.file.MediaType.StartsWith("video/", StringComparison.Ordinal) ||
                pair.file.MediaType.StartsWith("image/", StringComparison.Ordinal)))
            .Select(pair => (item.Id, pair.index))).ToList();
    private async Task NavigateChatVideoAsync(int direction)
    {
        var entries = ChatViewerEntries(); var index = chatVideoCurrent is { } current ? entries.IndexOf(current) : -1;
        if (index >= 0 && index + direction >= 0 && index + direction < entries.Count)
        { var entry = entries[index + direction]; await ActivateChatAttachmentAsync(entry.Id, entry.Index); }
    }
    private void RevealChatVideoControls()
    {
        chatVideoInteraction = DateTimeOffset.UtcNow;
        chatVideoControls.Opacity = chatVideoPrevious.Opacity = chatVideoNext.Opacity = 1;
    }
    private FrameworkElement ChatVideoView()
    {
        chatVideoTitle = new TextBlock().FontSize(16).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        chatVideoImage = new Image().StretchMode(Stretch.Uniform);
        artworkPreviewMenu = AttachArtworkMenu(chatVideoImage, () => chatVideoShowingImage ? previewArtwork : null,
            point => ChatVideoPictureBounds().Contains(point));
        chatVideoStatus = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Center();
        BuildChatVideoControls();
        chatVideoClose = TrackButtons.Icon(Icons.View("x", 22, Color.White), () => CloseChatVideo(), TrackButtons.Glass, 40).Right();
        ((MaterialTrackButton)chatVideoClose).UseLightGlassFinish();
        chatVideoHeader = new Grid().Columns("*,40").Rows("40").Children(chatVideoTitle.CenterVertical(), chatVideoClose.Column(1));
        chatVideoPrevious = PlayerButton(Icons.View("arrow-left", 24, Color.White), () => Run(() => NavigateChatVideoAsync(-1))).Width(40).Height(40).Left().CenterVertical();
        chatVideoNext = PlayerButton(Icons.View("arrow-right", 24, Color.White), () => Run(() => NavigateChatVideoAsync(1))).Width(40).Height(40).Right().CenterVertical();
        foreach (var arrow in new[] { chatVideoPrevious, chatVideoNext })
        {
            arrow.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
            arrow.MouseEnter += RevealChatVideoControls;
            arrow.MouseMove += _ => RevealChatVideoControls();
        }
        chatVideoLayout = new ChatVideoLayout(chatVideoImage, chatVideoStatus, chatVideoHeader, chatVideoControls,
            chatVideoPrevious, chatVideoNext, chatVideoSpeedMenu, chatVideoGear).Initialize();
        chatVideoTimer.Tick += RefreshChatVideo;
        chatVideoSurface = new ChatVideoSurface(chatVideoLayout).Initialize();
        ((MaterialTrackButton)chatVideoClose).UseOverlayBackdrop(chatVideoSurface, chatVideoSurface.RenderGlassBackdrop);
        chatVideoSurface.MouseDown += e =>
        {
            if (e.Button != MouseButton.Left || e.Handled) return;
            var local = e.GetPosition(chatVideoSurface);
            var point = new Point(local.X + chatVideoSurface.Bounds.X, local.Y + chatVideoSurface.Bounds.Y);
            if (chatVideoControls.IsVisible && chatVideoControls.Bounds.Contains(point) || chatVideoHeader.IsHitTestVisible && chatVideoClose.Bounds.Contains(point) ||
                chatVideoPrevious.IsVisible && chatVideoPrevious.Bounds.Contains(point) || chatVideoNext.IsVisible && chatVideoNext.Bounds.Contains(point) ||
                chatVideoSpeedOpen && chatVideoSpeedMenu.Bounds.Contains(point)) return;
            e.Handled = true;
            if (ChatVideoPictureBounds().Contains(point))
            { if (chatVideoSession is { } session) Run(session.ToggleAsync); RevealChatVideoControls(); }
            else CloseChatVideo();
        };
        return chatVideoSurface;
    }
    private Rect ChatVideoPictureBounds()
    {
        var size = chatVideoImage.Source switch
        {
            ImageSource source => new Size(source.PixelWidth, source.PixelHeight),
            WriteableBitmap bitmap => new Size(bitmap.PixelWidth, bitmap.PixelHeight),
            _ => default
        };
        if (size.Width <= 0 || size.Height <= 0) return default;
        var b = chatVideoImage.Bounds; var scale = Math.Min(b.Width / size.Width, b.Height / size.Height);
        var width = size.Width * scale; var height = size.Height * scale;
        return new Rect(b.X + (b.Width - width) / 2, b.Y + (b.Height - height) / 2, width, height);
    }

    private async Task ActivateChatAttachmentAsync(string messageId, int index)
    {
        var revision = chatRevision;
        var message = chatMessageItems.FirstOrDefault(m => m.Id == messageId)?.Message; var peer = chatRecipient?.Id;
        if (message == null || peer == null) return;
        if (CachedChatMedia(message) == null && !await EnsureChatMediaAsync(message, peer.Value)) return;
        if (disposed || chatClosing || page.Value != Page.Chats || chatRevision != revision || chatRecipient?.Id != peer ||
            CachedChatMedia(message) is not { } package || index < 0 || index >= package.Attachments.Length) return;
        var file = package.Attachments[index];
        if (!file.AsFile && file.MediaType.StartsWith("video/", StringComparison.Ordinal))
        {
            await OpenChatVideoAsync(file, messageId, index);
        }
        else if (!file.AsFile && file.MediaType.StartsWith("image/", StringComparison.Ordinal))
            await TrackChatOperation(OpenChatImageAsync(file, messageId, index));
        else if(file.Voice!=null) await ActivateChatVoiceMessageAsync(messageId,index);
        else await SaveChatAttachmentAsync(messageId, index);
    }
    private async Task OpenChatImageAsync(ChatAttachment file, string messageId, int index)
    {
        var revision = chatRevision; var request = ++chatImagePreviewRequest;
        var token = ChatReadToken();
        ImageSource? source;
        await chatImagePreviewGate.WaitAsync(token);
        try
        {
            if (request != chatImagePreviewRequest) return;
            // Cache retirement can clear the package during an account change.
            // Decode our own bytes, and serialize full-resolution decodes.
            var encoded = file.Data.ToArray();
            try { source = await Task.Run(() => PrepareChatImage(encoded, 8192, 8192), token); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded); }
        }
        finally { chatImagePreviewGate.Release(); }
        if (disposed || chatClosing || token.IsCancellationRequested || chatRevision != revision ||
            request != chatImagePreviewRequest || page.Value != Page.Chats) return;
        if (source == null) { chatThreadStatus.Value = UiText.Get("media.image.openFailed"); return; }
        var row = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == messageId);
        UIElement? trigger = index >= 0 && row != null && index < row.Attachments.Children.Count
            ? row.Attachments.Children[index].FindVisualChild<ChatPhotoFrame>() as ChatPhotoFrame : null;
        // The thumbnail may have been recycled while the original was decoded.
        OpenArtworkPreview(source, file.Name, trigger ?? chatInput);
        chatVideoCurrent = (messageId, index);
        RefreshChatViewerNavigation();
    }
    private void RefreshChatViewerNavigation()
    {
        var entries = ChatViewerEntries(); var selected = chatVideoCurrent is { } current ? entries.IndexOf(current) : -1;
        chatVideoPrevious.IsVisible = selected > 0;
        chatVideoNext.IsVisible = selected >= 0 && selected < entries.Count - 1;
    }
    private Task OpenChatVideoAsync(ChatAttachment file, string messageId, int index) =>
        TrackChatOperation(OpenChatVideoCoreAsync(file, messageId, index));

    private async Task OpenChatVideoCoreAsync(ChatAttachment file, string messageId, int index)
    {
        if (disposed || chatClosing) return;
        await StopChatVoicePlaybackAsync();
        if(chatVoiceRecorder is {Recording:true,Paused:false})chatVoiceRecorder.Pause();
        var message = chatMessageItems.FirstOrDefault(item => item.Id == messageId)?.Message;
        var row = ChatRows.FirstOrDefault(item => item.Message?.Id == messageId);
        ChatVideoFrame? inline = null;
        if (row != null && index >= 0 && index < row.Attachments.Children.Count)
            VisualTree.Visit(row.Attachments.Children[index], child => { if (child is ChatVideoFrame frame) inline = frame; });
        var poster = inline?.FreezeFrame();
        poster ??= message == null ? null : ChatMediaSummaryFor(message, chatRecipient?.Id ?? 0)?.Photos?.ElementAtOrDefault(index)?.Source;
        if (poster == null && file.Video is { Valid: true } info) poster = ImageSource.FromBgraPixels(info.ThumbWidth, info.ThumbHeight, info.Pixels);
        var replacingVideo = chatVideoOpen.Value;
        CloseChatVideo(true, replacing: true);
        var previousClosing = chatVideoClosing;
        chatVideoShowingImage = false; chatVideoReturnFocus = null;
        chatVideoLayout.ImageMode = false; chatVideoControls.IsVisible = true;
        if (!replacingVideo) SetChatVideoExpanded(false, immediate: true);
        RevealChatVideoControls(); var generation = chatVideoGeneration;
        chatVideoImage.Source = poster;
        chatVideoCurrent = (messageId, index);
        RefreshChatViewerNavigation();
        chatVideoTime.Text = "0:00"; chatVideoRemaining.Text = "−0:00"; chatVideoSeek.Value = 0; SetChatVideoMuted(false);
        chatVideoTitle.Text = file.Name; chatVideoStatus.Text = ""; chatVideoOpen.Value = true;
        _ = chatVideoSurface.SetOpen(true);
        chatVideoSurface.InvalidateMeasure(); Window.PerformLayout();
        VisualTree.Visit(chatVideoSurface, child => { if (child is FrameworkElement element) element.InvalidateVisual(); });
        Window.InvalidateVisual(); chatVideoClose.Focus();
        var fadingMusic = SuspendMusicForChatVideo();
        try
        {
            await Task.WhenAll(previousClosing, fadingMusic);
            if (disposed || generation != chatVideoGeneration || !chatVideoOpen.Value) return;
            using var startup = StartOwnedChatSession(file.Data, Environment.GetEnvironmentVariable("POOKIE_VIDEO_SILENT") == "1");
            await startup.Ready;
            if (disposed || chatClosing || generation != chatVideoGeneration || !chatVideoOpen.Value || !startup.TryTake(out var session)) return;
            chatVideoSession = session; session.Volume((int)Math.Round(chatVideoVolume.Value)); SetChatVideoMuted(chatVideoVolume.Value == 0); session.Rate(chatVideoRate); chatVideoStarted = DateTimeOffset.UtcNow; chatVideoTimer.Start();
        }
        catch (OperationCanceledException) when (disposed || chatClosing || generation != chatVideoGeneration) { }
        catch (Exception error)
        {
            ChatVideoRuntime.LogFailure(error);
            if (!disposed && generation == chatVideoGeneration)
                chatVideoStatus.Text = UiText.Get("chats.video.decodeFailed");
        }
    }
    private void RefreshChatVideo()
    {
        if (!chatVideoOpen.Value || chatVideoSession is not { } session) return;
        if (session.CopyFrame(ref chatVideoBitmap, ref chatVideoVersion))
        { chatVideoImage.Source = chatVideoBitmap; chatVideoImage.InvalidateVisual(); chatVideoStatus.Text = ""; }
        RefreshChatVideoControlVisibility();
        var state = session.Snapshot();
        if (state.Failed || chatVideoVersion == 0 && (state.Ended || DateTimeOffset.UtcNow - chatVideoStarted > TimeSpan.FromSeconds(15)))
        { session.LogPlaybackFailure(); chatVideoStatus.Text = UiText.Get("chats.video.saveInstead"); chatVideoTimer.Stop(); }
        chatVideoPlayIcon.Source = Icons.Source(state.Playing ? "pause-solid" : "play-solid", Color.White);
        static string Time(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");
        chatVideoTime.Text = Time(state.Ended ? state.Length : state.Time); chatVideoRemaining.Text = "−" + Time(state.Ended ? 0 : Math.Max(0, state.Length - state.Time));
        chatVideoUpdating = true;
        try { chatVideoSeek.Value = state.Ended ? 1 : state.Length > 0 ? (double)Math.Max(0, state.Time) / state.Length : 0; }
        finally { chatVideoUpdating = false; }
    }
    private void CloseChatVideo(bool immediate = false, bool replacing = false)
    {
        ++chatImagePreviewRequest;
        var wasOpen = chatVideoOpen.Value;
        var wasImage = chatVideoShowingImage;
        var returnFocus = chatVideoReturnFocus;
        var returnFocusKeyboard = chatVideoReturnFocusKeyboard;
        artworkPreviewMenu?.Close(true); previewArtwork = null;
        chatVideoShowingImage = false; chatVideoReturnFocus = null; chatVideoReturnFocusKeyboard = false;
        var generation = ++chatVideoGeneration; chatVideoTimer.Stop(); chatVideoOpen.Value = false; chatVideoCurrent = null;
        var session = chatVideoSession; chatVideoSession = null;
        var bitmap = chatVideoBitmap; chatVideoBitmap = null; chatVideoVersion = 0;
        ResetChatVideoControls(keepExpansion: replacing);
        // Replacing the decoder must not hide/reset the presentation surface.
        // Reopening during a close continues the current fade in the other direction.
        var hiding = replacing ? Task.CompletedTask : chatVideoSurface?.SetOpen(false, immediate || disposed || !wasOpen) ?? Task.CompletedTask;
        var releasing = session == null ? Task.CompletedTask : DisposeChatResourceAsync(session);
        if (disposed && !wasImage) RestoreMusicAfterChatVideo();
        // The pixel buffer owns managed, lock-protected memory; native GPU image
        // views remain owned by the UI Image/renderer. Once the surface is hidden
        // (or replaced), retire the old pixels/decoder without a dispatcher pump.
        async Task Retire()
        {
            await hiding.ConfigureAwait(false);
            bitmap?.Dispose();
            await releasing.ConfigureAwait(false);
        }
        var retirement = Retire();
        TrackChatCleanup(retirement);
        async Task FinishPresentation()
        {
            await hiding;
            if (generation == chatVideoGeneration && chatVideoImage != null) chatVideoImage.Source = null;
            await retirement;
            if (!replacing && generation == chatVideoGeneration && !disposed && !chatClosing && !wasImage) RestoreMusicAfterChatVideo();
        }
        var finishing = FinishPresentation();
        chatVideoClosing = finishing;
        _ = TrackChatOperation(finishing);
        Run(() => finishing);
        if (wasOpen && !disposed && !replacing)
        {
            Window.InvalidateVisual();
            if (wasImage && returnFocus != null) KeyboardFocusFeedback.Restore(returnFocus, returnFocusKeyboard);
            else if (chatInput.IsEnabled) chatInput.Focus();
        }
    }
}
