using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> chatVideoOpen = new(false);
    private readonly DispatcherTimer chatVideoTimer = new(TimeSpan.FromMilliseconds(33));
    private ChatVideoSession? chatVideoSession;
    private Task chatVideoClosing = Task.CompletedTask;
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
    private List<(string Id, int Index)> ChatVideoEntries() => chatMessageItems.SelectMany(item =>
        (CachedChatMedia(item.Message)?.Attachments ?? []).Select((file, index) => (file, index))
            .Where(pair => !pair.file.AsFile && pair.file.MediaType.StartsWith("video/", StringComparison.Ordinal))
            .Select(pair => (item.Id, pair.index))).ToList();
    private async Task NavigateChatVideoAsync(int direction)
    {
        var entries = ChatVideoEntries(); var index = chatVideoCurrent is { } current ? entries.IndexOf(current) : -1;
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
        chatVideoStatus = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Center();
        BuildChatVideoControls();
        chatVideoClose = PlayerButton(Icons.View("x", 22, Color.White), () => CloseChatVideo()).Width(40).Height(40).Right();
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
        chatVideoSurface.MouseDown += e =>
        {
            if (e.Button != MouseButton.Left || e.Handled) return;
            var local = e.GetPosition(chatVideoSurface);
            var point = new Point(local.X + chatVideoSurface.Bounds.X, local.Y + chatVideoSurface.Bounds.Y);
            if (chatVideoControls.Bounds.Contains(point) || chatVideoHeader.IsHitTestVisible && chatVideoClose.Bounds.Contains(point) ||
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
        var message = chatMessageItems.FirstOrDefault(m => m.Id == messageId)?.Message; var peer = chatRecipient?.Id;
        if (message == null || peer == null) return;
        if (CachedChatMedia(message) == null && !await EnsureChatMediaAsync(message, peer.Value)) return;
        if (chatRecipient?.Id != peer || CachedChatMedia(message) is not { } package || index < 0 || index >= package.Attachments.Length) return;
        var file = package.Attachments[index];
        if (!file.AsFile && file.MediaType.StartsWith("video/", StringComparison.Ordinal))
        {
            await OpenChatVideoAsync(file, messageId, index);
        }
        else if(file.Voice!=null) await ActivateChatVoiceMessageAsync(messageId,index);
        else await SaveChatAttachmentAsync(messageId, index);
    }
    private async Task OpenChatVideoAsync(ChatAttachment file, string messageId, int index)
    {
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
        if (!replacingVideo) SetChatVideoExpanded(false, immediate: true);
        RevealChatVideoControls(); var generation = chatVideoGeneration;
        chatVideoImage.Source = poster;
        chatVideoCurrent = (messageId, index);
        var entries = ChatVideoEntries(); var selected = entries.IndexOf((messageId, index));
        chatVideoPrevious.IsVisible = selected > 0; chatVideoNext.IsVisible = selected >= 0 && selected < entries.Count - 1;
        chatVideoTime.Text = "0:00"; chatVideoRemaining.Text = "−0:00"; chatVideoSeek.Value = 0; SetChatVideoMuted(false);
        chatVideoTitle.Text = file.Name; chatVideoStatus.Text = ""; chatVideoOpen.Value = true;
        _ = chatVideoSurface.SetOpen(true);
        chatVideoSurface.InvalidateMeasure(); Window.PerformLayout();
        VisualTree.Visit(chatVideoSurface, child => { if (child is FrameworkElement element) element.InvalidateVisual(); });
        Window.InvalidateVisual(); chatVideoClose.Focus();
        var fadingMusic = SuspendMusicForChatVideo();
        try
        {
            await fadingMusic;
            if (disposed || generation != chatVideoGeneration || !chatVideoOpen.Value) return;
            var session = await ChatVideoSession.StartAsync(file.Data, Environment.GetEnvironmentVariable("POOKIE_VIDEO_SILENT") == "1");
            if (disposed || generation != chatVideoGeneration || !chatVideoOpen.Value) { await session.DisposeAsync(); return; }
            chatVideoSession = session; session.Volume((int)Math.Round(chatVideoVolume.Value)); SetChatVideoMuted(chatVideoVolume.Value == 0); session.Rate(chatVideoRate); chatVideoStarted = DateTimeOffset.UtcNow; chatVideoTimer.Start();
        }
        catch
        {
            if (!disposed && generation == chatVideoGeneration)
                chatVideoStatus.Text = OperatingSystem.IsLinux() ? "Не удалось открыть видео. Проверь, установлен ли libvlc и его видеокодеки." : "Не удалось открыть видео. Возможно, его кодек не поддерживается.";
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
        { chatVideoStatus.Text = "Не удалось прочитать видеодорожку. Файл можно сохранить из меню сообщения."; chatVideoTimer.Stop(); }
        chatVideoPlayIcon.Source = Icons.Source(state.Playing ? "pause-solid" : "play-solid", Color.White);
        static string Time(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");
        chatVideoTime.Text = Time(state.Ended ? state.Length : state.Time); chatVideoRemaining.Text = "−" + Time(state.Ended ? 0 : Math.Max(0, state.Length - state.Time));
        chatVideoUpdating = true;
        try { chatVideoSeek.Value = state.Ended ? 1 : state.Length > 0 ? (double)Math.Max(0, state.Time) / state.Length : 0; }
        finally { chatVideoUpdating = false; }
    }
    private void CloseChatVideo(bool immediate = false, bool replacing = false)
    {
        var wasOpen = chatVideoOpen.Value;
        var generation = ++chatVideoGeneration; chatVideoTimer.Stop(); chatVideoOpen.Value = false; chatVideoCurrent = null;
        var session = chatVideoSession; chatVideoSession = null;
        var bitmap = chatVideoBitmap; chatVideoBitmap = null; chatVideoVersion = 0;
        ResetChatVideoControls(keepExpansion: replacing);
        // Replacing the decoder must not hide/reset the presentation surface.
        // Reopening during a close continues the current fade in the other direction.
        var hiding = replacing ? Task.CompletedTask : chatVideoSurface?.SetOpen(false, immediate || disposed || !wasOpen) ?? Task.CompletedTask;
        var releasing = session?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        if (disposed) RestoreMusicAfterChatVideo();
        async Task Finish()
        {
            await hiding;
            if (generation == chatVideoGeneration && chatVideoImage != null) chatVideoImage.Source = null;
            bitmap?.Dispose();
            await releasing;
            if (!replacing && generation == chatVideoGeneration && !disposed) RestoreMusicAfterChatVideo();
        }
        chatVideoClosing = Task.WhenAll(chatVideoClosing, Finish());
        Run(() => chatVideoClosing);
        if (wasOpen && !disposed) { Window.InvalidateVisual(); if (chatInput.IsEnabled) chatInput.Focus(); }
    }
}
