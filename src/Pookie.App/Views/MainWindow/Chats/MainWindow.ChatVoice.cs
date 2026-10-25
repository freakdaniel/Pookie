using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Func<float[]?, IChatVoiceRecorder> chatVoiceFactory = seed => new ChatVoiceRecorder(seed);
    private IChatVoiceRecorder? chatVoiceRecorder;
    private ChatVoiceDraft? chatVoiceDraft;
    private ChatVideoSession? chatVoicePlayer;
    private bool chatVoiceAudioActive, chatVoiceBusy;
    private string? chatVoicePlayingId;
    private double? chatVoiceSeekRequest;
    private long chatVoiceGeneration;
    private TaskCompletionSource? chatVoiceIdle;
    private FrameworkElement chatVoiceBar = null!;
    private TextBlock chatVoiceTime = null!;
    private ChatVoiceWave chatVoiceWave = null!;
    private Border chatRecordingDot = null!;
    private Button chatVoicePause = null!, chatVoicePreview = null!, chatVoiceContinue = null!;
    private FrameworkElement chatVoiceDraftPlay = null!, chatVoiceDraftPause = null!;
    private DispatcherTimer chatVoiceTimer = null!;
    private FrameworkElement ChatVoiceView()
    {
        chatVoiceTime = new TextBlock().FontSize(12).Foreground(TrackButtons.OnSurface).CenterVertical().MinWidth(50);
        chatVoiceWave = new ChatVoiceWave { Seek = fraction => Run(async () =>
        {
            if (chatVoiceDraft == null) return;
            if (chatVoicePlayer == null) await PlayChatVoiceDraftAsync();
            if (chatVoicePlayer != null) chatVoiceSeekRequest = fraction;
        }) };
        chatRecordingDot = new Border().Width(8).Height(8).CornerRadius(4).Background(Color.FromRgb(245,91,105)).Center();
        chatRecordingDot.Transitions = [Transition.Create(UIElement.OpacityProperty,200)];
        chatVoicePause = TrackButtons.Icon(Icons.View("pause", 20), () => Run(StopChatVoiceAsync), TrackButtons.Text, 36).ToolTip("Приостановить запись");
        chatVoiceDraftPlay = Icons.PlaybackDisc(false,28,14,TrackButtons.OnSurface).Center();
        chatVoiceDraftPause = Icons.PlaybackDisc(true,28,14,TrackButtons.OnSurface).Center().IsVisible(false);
        chatVoicePreview = TrackButtons.Icon(new Grid().Columns("*").Rows("*").Children(chatVoiceDraftPlay,chatVoiceDraftPause), () => Run(PlayChatVoiceDraftAsync), TrackButtons.Text, 28).ToolTip("Прослушать / пауза");
        ((MaterialTrackButton)chatVoicePreview).SuppressStateLayer();
        chatVoiceContinue = TrackButtons.Icon(Icons.View("microphone",20), () => Run(ContinueChatVoiceAsync), TrackButtons.Text,36).ToolTip("Продолжить запись");
        var lead = new Grid().Columns("*").Rows("*").Children(chatRecordingDot,chatVoicePreview.Center());
        var pill = new Border().CornerRadius(20).Background(Color.FromRgb(39,57,73)).Padding(8,0,12,0).Height(36).Child(
            new Grid().Columns("28,*,Auto").Rows("*").Spacing(8).Children(lead.Column(0),chatVoiceWave.Column(1).CenterVertical(),chatVoiceTime.Column(2)));
        chatVoiceBar = new Grid().Columns("36,*,36").Rows("40").Spacing(4).Children(
            TrackButtons.Icon(Icons.View("trash",20), () => Run(DeleteChatVoiceAsync),TrackButtons.Text,36).ToolTip("Удалить запись").Column(0).CenterVertical(),
            pill.Column(1).CenterVertical(),new Grid().Columns("*").Rows("*").Children(chatVoicePause.Center(),chatVoiceContinue.Center()).Column(2));
        chatVoiceBar.IsVisible = false;
        chatVoiceTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33));
        chatVoiceTimer.Tick += () =>
        {
            RefreshChatVoice();
            if (chatVoiceRecorder is { Recording: true, Duration: >= 300 } && !chatVoiceBusy) Run(StopChatVoiceAsync);
            if (chatVoicePlayer is { } player)
            {
                var state = player.Snapshot();
                if (chatVoiceSeekRequest is { } seek && state.Length > 0) { chatVoiceSeekRequest = null; player.Seek(seek); }
                if (chatVoicePlayingId == null && state.Length > 0) { chatVoiceWave.Progress = state.Time / (double)state.Length;
                    chatVoiceDraftPlay.IsVisible = !state.Playing; chatVoiceDraftPause.IsVisible = state.Playing; }
                foreach (var row in ChatRows.Where(r => r.Bound)) VisualTree.Visit(row.Attachments, child => { if (child is ChatVoiceCard card) card.Update(card.Key == chatVoicePlayingId ? state.Time / 1000d : 0, card.Key == chatVoicePlayingId && state.Playing); });
                if (state.Ended || state.Failed) Run(StopChatVoicePlaybackAsync);
            }
        };
        return chatVoiceBar;
    }
    private async Task ActivateChatSendAsync()
    {
        if (chatVoiceBusy) return;
        if (chatVoiceRecorder is { Recording: true }) { await StopChatVoiceAsync(); return; }
        if (chatInput.Text.Trim().Length == 0 && chatSelectedFiles.Length == 0 && chatSelectedGif == null && chatVoiceDraft == null) { await ContinueChatVoiceAsync(); return; }
        await SendChatAsync();
    }
    private bool ChatVoiceCurrent(long generation, long account, long peer) => !disposed && generation == chatVoiceGeneration && account == chatAccountId && chatRecipient?.Id == peer;
    private async Task ContinueChatVoiceAsync()
    {
        if (chatSending || chatVoiceBusy || chatRecipient == null || chatPending.ContainsKey(chatRecipient.Id)) return;
        chatVoiceBusy = true;
        var idle = chatVoiceIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var generation = ++chatVoiceGeneration; var account = chatAccountId; var peer = chatRecipient.Id;
        var token = chatAccountLifetime?.Token ?? lifetime.Token;
        try
        {
            await StopChatVoicePlaybackAsync();
            if (!ChatVoiceCurrent(generation, account, peer)) return;
            CloseChatVideo(true); chatVoiceAudioActive = true; await SuspendMusicForChatVideo();
            if (!ChatVoiceCurrent(generation, account, peer)) return;
            if (chatVoiceRecorder == null)
            {
                float[]? seed = null;
                try
                {
                    if (chatVoiceDraft is { } draft)
                    {
                        var bytes = await File.ReadAllBytesAsync(draft.Path, token);
                        try { seed = await Task.Run(() => ChatVoicePcm.Decode(bytes), token); }
                        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
                    }
                    if (!ChatVoiceCurrent(generation, account, peer)) return;
                    chatVoiceRecorder = chatVoiceFactory(seed);
                }
                finally { if (seed != null) Array.Clear(seed); }
            }
            var recorder = chatVoiceRecorder;
            await recorder.ResumeAsync(token);
            if (!ChatVoiceCurrent(generation, account, peer)) { await recorder.CancelAsync(); return; }
            chatVoiceTimer.Start(); RefreshChatVoice();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (ChatVoiceCurrent(generation, account, peer))
            { chatThreadStatus.Value = "Не удалось включить микрофон. Проверь устройство и разрешения."; chatVoiceAudioActive = false; RestoreMusicAfterChatVideo(); }
        }
        finally
        {
            if (chatVoiceIdle == idle) chatVoiceBusy = false;
            if (ChatVoiceCurrent(generation, account, peer)) RefreshChatComposer();
            idle.TrySetResult();
        }
    }
    private async Task StopChatVoiceAsync()
    {
        if (chatVoiceRecorder is not { Recording: true } recorder || chatVoiceBusy || chatRecipient == null) return;
        chatVoiceBusy = true;
        var idle = chatVoiceIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = chatRecipient.Id; var account = chatAccountId; var generation = chatVoiceGeneration;
        var token = chatAccountLifetime?.Token ?? lifetime.Token;
        try
        {
            var result = await recorder.FinishAsync(token);
            try
            {
                if (!ChatVoiceCurrent(generation, account, peer)) return;
                var directory = Path.Combine(dataPaths.Root, "Chats", account.ToString(System.Globalization.CultureInfo.InvariantCulture), "VoiceDrafts");
                AppDataPaths.CreatePrivateDirectory(directory);
                var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + result.Extension);
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                var written = false;
                try { await using (var stream = new FileStream(path, options)) await stream.WriteAsync(result.Data, token); written = true; }
                finally { if (!written) TryDeleteChatVoice(path); }
                if (!ChatVoiceCurrent(generation, account, peer)) { TryDeleteChatVoice(path); return; }
                if (chatVoiceDraft is { } old && old.Path != path) TryDeleteChatVoice(old.Path);
                chatVoiceDraft = new(path, result.Metadata);
                var drafts = chatWorkspace.Drafts ?? new Dictionary<long, ChatDraft>(); drafts[peer] = new(chatInput.Text, chatReply, chatVoiceDraft);
                chatWorkspace = chatWorkspace with { Drafts = drafts }; SaveChatWorkspace();
                chatSelectedFiles = [path]; UpdateChatAttachments();
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(result.Data); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (ChatVoiceCurrent(generation, account, peer)) chatThreadStatus.Value = "Не удалось сохранить запись. Можно продолжить запись или удалить её."; }
        finally
        {
            if (chatVoiceIdle == idle) chatVoiceBusy = false;
            if (ChatVoiceCurrent(generation, account, peer)) { chatVoiceAudioActive = false; RestoreMusicAfterChatVideo(); RefreshChatVoice(); RefreshChatComposer(); }
            idle.TrySetResult();
        }
    }
    private void RefreshChatVoice()
    {
        if (chatVoiceBar == null) return;
        var active = chatVoiceRecorder is { Recording: true };
        var finishing = chatVoiceBusy && chatVoiceRecorder is { Recording: false };
        var duration = active || finishing ? chatVoiceRecorder!.Duration : chatVoiceDraft?.Metadata.Duration ?? 0;
        chatComposerLayout.SetVoiceMode(!chatFavoritesOpen && (active || finishing || chatVoiceDraft != null));
        chatVoiceTime.Text = $"{(int)duration / 60}:{(int)duration % 60:00}" + (active ? $",{(int)(duration * 10) % 10}" : "");
        chatRecordingDot.IsVisible = active || finishing; chatRecordingDot.Opacity = active && DateTimeOffset.UtcNow.Millisecond < 500 ? 1 : .35;
        if (active || finishing) chatVoiceWave.UpdateLive(chatVoiceRecorder!.Meter);
        else chatVoiceWave.Peaks = chatVoiceDraft?.Metadata.Peaks ?? [];
        chatVoicePause.IsVisible = active || finishing; chatVoicePreview.IsVisible = !active && !finishing && chatVoiceDraft != null;
        chatVoiceContinue.IsVisible = !active && !finishing && chatVoiceDraft is { Metadata.Duration: < 300 };
        if (!active && chatVoicePlayer == null) chatVoiceTimer.Stop();
    }
    private async Task PlayChatVoiceDraftAsync()
    {
        if (chatVoicePlayer != null) { await chatVoicePlayer.ToggleAsync(); return; }
        if (chatVoiceDraft == null || chatRecipient == null) return;
        var generation = chatVoiceGeneration; var account = chatAccountId; var peer = chatRecipient.Id;
        var bytes = await File.ReadAllBytesAsync(chatVoiceDraft.Path);
        if (!ChatVoiceCurrent(generation,account,peer)) { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); return; }
        await PlayChatVoiceBytesAsync(bytes);
    }
    private async Task PlayChatVoiceBytesAsync(byte[] bytes, string? messageKey = null)
    {
        await StopChatVoicePlaybackAsync(); CloseChatVideo(true); chatVoiceAudioActive = true; var generation = chatVoiceGeneration;
        await SuspendMusicForChatVideo();
        try
        {
            var player = await ChatVideoSession.StartAsync(bytes);
            if (disposed || generation != chatVoiceGeneration) { await player.DisposeAsync(); return; }
            chatVoicePlayer = player; chatVoicePlayingId = messageKey; chatVoiceTimer.Start();
        }
        catch { chatVoiceAudioActive = false; RestoreMusicAfterChatVideo(); chatThreadStatus.Value = "Не удалось воспроизвести голосовое сообщение."; }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    private async Task StopChatVoicePlaybackAsync()
    {
        if (chatVoicePlayer is not { } player) return;
        chatVoicePlayer = null; chatVoicePlayingId = null; chatVoiceSeekRequest = null;
        chatVoiceDraftPlay.IsVisible = true; chatVoiceDraftPause.IsVisible = false; chatVoiceWave.Progress = 0;
        foreach (var row in ChatRows.Where(r => r.Bound))
            VisualTree.Visit(row.Attachments, child => { if (child is ChatVoiceCard card) card.Update(0,false); });
        await player.DisposeAsync(); chatVoiceAudioActive = false; RestoreMusicAfterChatVideo();
    }
    private FrameworkElement CreateChatVoiceCard(string messageId, int index, ChatVoiceMetadata metadata)
    {
        var key = messageId + ":" + index;
        return new ChatVoiceCard(key, metadata, () => Run(() => ActivateChatVoiceMessageAsync(messageId, index)), fraction => Run(async () =>
        { if (chatVoicePlayingId != key) await ActivateChatVoiceMessageAsync(messageId, index); if (chatVoicePlayingId == key) chatVoiceSeekRequest = fraction; }));
    }
    private async Task ActivateChatVoiceMessageAsync(string messageId, int index)
    {
        var key = messageId + ":" + index;
        if (chatVoicePlayingId == key && chatVoicePlayer != null) { await chatVoicePlayer.ToggleAsync(); return; }
        var message = chatMessages.FirstOrDefault(m => m.Id == messageId); var peer = chatRecipient?.Id;
        if (message == null || peer == null) return;
        if (CachedChatMedia(message) == null && !await EnsureChatMediaAsync(message, peer.Value)) return;
        if (chatRecipient?.Id != peer || CachedChatMedia(message)?.Attachments.ElementAtOrDefault(index) is not { Voice: not null } file) return;
        if (chatVoiceRecorder is { Recording: true, Paused: false }) chatVoiceRecorder.Pause();
        await PlayChatVoiceBytesAsync(file.Data.ToArray(), key);
    }
    private async Task DeleteChatVoiceAsync()
    {
        ++chatVoiceGeneration; await StopChatVoicePlaybackAsync(); if (chatVoiceRecorder != null) { await chatVoiceRecorder.DisposeAsync(); chatVoiceRecorder = null; }
        if (chatVoiceDraft is { } draft) TryDeleteChatVoice(draft.Path); chatVoiceDraft = null; chatVoiceWave.ResetLive(); chatVoiceAudioActive = false; RestoreMusicAfterChatVideo(); ClearChatAttachments();
        if (chatRecipient != null) chatWorkspace.Drafts?.Remove(chatRecipient.Id); SaveChatWorkspace(); RefreshChatVoice(); RefreshChatComposer();
    }
    private async Task FinishChatVoiceBeforeCloseAsync()
    {
        if (chatVoiceBusy && chatVoiceIdle is { } idle) await idle.Task;
        if (preferences.ChatSaveDrafts) await StopChatVoiceAsync();
        else if (chatVoiceRecorder != null) await chatVoiceRecorder.CancelAsync();
        SaveChatDraft();
    }
    private void CleanupChatVoiceFiles()
    {
        if (chatAccountId <= 0) return;
        var directory = Path.Combine(dataPaths.Root, "Chats", chatAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture), "VoiceDrafts");
        if (!Directory.Exists(directory)) return;
        var keep = preferences.ChatSaveDrafts ? chatWorkspace.Drafts?.Values.Select(d => d.Voice?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [] : [];
        string[] files;
        try { files = Directory.GetFiles(directory).Where(p => Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "N", out _) && !keep.Contains(p)).ToArray(); }
        catch (IOException) { return; }
        Run(() => Task.Run(() => { foreach (var path in files) TryDeleteChatVoice(path); }));
    }
    private void ResetChatVoice()
    {
        ++chatVoiceGeneration; var recorder = chatVoiceRecorder; var player = chatVoicePlayer;
        chatVoiceRecorder = null; chatVoicePlayer = null; chatVoiceDraft = null; chatVoiceAudioActive = false; chatVoiceBusy = false;
        chatVoiceIdle = null; chatVoiceTimer?.Stop(); chatVoiceWave?.ResetLive(); chatComposerLayout?.SetVoiceMode(false,snap:true);
        if (recorder != null) _ = recorder.DisposeAsync(); if (player != null) _ = player.DisposeAsync();
        RestoreMusicAfterChatVideo();
    }
    private static void TryDeleteChatVoice(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private void RestoreChatVoiceDraft(long peer)
    {
        chatVoiceDraft = null;
        if (chatWorkspace.Drafts?.GetValueOrDefault(peer)?.Voice is { } draft)
        {
            var root = Path.Combine(dataPaths.Root, "Chats", chatAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture), "VoiceDrafts") + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(draft.Path).StartsWith(root, StringComparison.Ordinal) && File.Exists(draft.Path) && new FileInfo(draft.Path).Length <= ChatMediaCrypto.MaxFileBytes)
            { chatVoiceDraft = draft; chatSelectedFiles = [draft.Path]; UpdateChatAttachments(); }
        }
        RefreshChatVoice();
    }
}
