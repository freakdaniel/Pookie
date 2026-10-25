using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.Audio;
using Pookie.Lyrics;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyBackgroundPlaybackMemoryAsync()
    {
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("POOKIE_PLAYBACK_SOAK_SECONDS"), out var requested)
            ? Math.Clamp(requested, 40, 3600) : 120;
        var original = player;
        await using var audio = new SoundFlowPlayer(silent: true);
        var focus = new Window().Title("Pookie background playback memory probe").Fixed(240,120).Content(new Border());
        string? file = null;
        timer.Stop(); player = audio;
        try
        {
            var transport = new ChatFixtureTransport();
            api.Session = new("chatfixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Ты" }; signedIn.Value = true;
            preferences = preferences with { WaveLearningEnabled = false, WaveAnalysisEnabled = false, ChatBackgroundRefresh = true, LyricsEnabled = true };
            const string url = "https://soundcloud.com/fixture/track";
            var track = new SoundCloudTrack { Id = 101, Title = "Фоновое воспроизведение", Duration = (seconds+30)*1000,
                PermalinkUrl = url, WaveformUrl = "https://wave.sndcdn.com/chat-track-fixture.json", User = new() { Id = 7, Username = "Pookie local PCM" } };
            var cover = ImageSource.FromBytes(ArtworkFixtureBitmap(500));
            libraryCoverCache[101] = cover; CacheChatMusic(url, LibraryItem.FromTrack(track));
            waveformRequests[track.WaveformUrl] = Task.FromResult<float[]?>(Enumerable.Range(0,300).Select(i => (float)(.2+.6*Math.Abs(Math.Sin(i*.2)))).ToArray());
            transport.AddIncoming(url);
            await ShowWorkspaceAsync(); await NavigateAsync(Page.Chats); chatTimer.Stop();
            await SelectChatAsync(transport.Other);
            await WaitForLikedLayoutAsync(() => ChatRows.Any(r => r.Attachments.Children.OfType<ChatMusicView>().Any(v => v.Item?.Track?.Id == 101)));
            var card = ChatRows.SelectMany(r => r.Attachments.Children.OfType<ChatMusicView>()).First(v => v.Item?.Track?.Id == 101);
            // Use the selected card's production queue policy and the real
            // player/poll/render paths. Only stream resolution is replaced by
            // a local WAV; the native null output never plays sound to the user.
            StartQueueContext(new(card.Group.Key, "Чат с "+card.Group.Name, "Сейчас играет из чата"), [track], track, 0);
            current = track; BeginLyricsTrack(track);
            title.Value = track.Title; artist.Value = track.Author;
            audioReady = true; audioPreparing = false; paused = false;
            isPlaying.Value = true; playerVisible.Value = true;
            SetPlayerArtwork(cover, playGeneration); RefreshLikedPlayback(); RefreshQueue();
            file = await Task.Run(() => Preview.DemoAudio.Create(220, seconds+30));
            await audio.PlayAsync(new(file, AudioTransport.File, seconds+30));
            timer.Start(); chatTimer.Start();
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && !workspace.IsVisible);
            if (page.Value != Page.Chats || current?.Id != track.Id || !audioReady)
                throw new InvalidOperationException("The soak did not start from a selected chat track.");
            focus.Show(Window); focus.Activate();
            await WaitForLikedLayoutAsync(() => !Window.IsActive);
            var watch = Stopwatch.StartNew();
            for (var phase = 0; phase < 4; phase++)
            {
                if (phase == 1) ToggleExpandedPanel(PlayerPanel.Queue);
                if (phase == 2)
                {
                    ToggleExpandedPanel(PlayerPanel.Lyrics);
                    CancelLyricsRequest();
                    ApplyLyrics(new(LyricsStatus.Synced, new LyricsFixtureProvider().Record.ToDocument()));
                }
                if (phase == 3) Window.Minimize();
                await Task.Delay(3000,lifetime.Token);
                var baseline = PlaybackMemorySample(watch.Elapsed.TotalSeconds,phase);
                var until = TimeSpan.FromSeconds((phase+1)*seconds/4d);
                while (watch.Elapsed < until)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(.1,(until-watch.Elapsed).TotalSeconds))),lifetime.Token);
                    var reading = PlaybackMemorySample(watch.Elapsed.TotalSeconds,phase);
                    if (reading.Gpu is { } gpu && baseline.Gpu is { } start && gpu-start > 64L*1024*1024)
                        throw new InvalidOperationException($"Background playback GPU memory grew by {(gpu-start)/1048576d:F1} MiB in phase {phase}.");
                    if (reading.Managed-baseline.Managed > 32L*1024*1024)
                        throw new InvalidOperationException("Background playback retains an increasing managed heap.");
                    if (!audio.Poll().Playing || page.Value != Page.Chats || !expandedOpen)
                        throw new InvalidOperationException("Background playback stopped or left the tested scenario.");
                }
            }
            Window.Restore(); focus.Close(); Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive && expandedHost.IsVisible);
            CaptureUiPreview("playback-memory-after-soak");
            Console.WriteLine($"PLAYBACK_MEMORY_OK: wall_seconds={watch.Elapsed.TotalSeconds:F0}; native silent PCM; chat track; expanded player; inactive window; queue; synced lyrics; minimized");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine(error); }
        finally
        {
            timer.Stop(); focus.Close(); await audio.StopAsync(); player = original;
            if (file != null) File.Delete(file);
            Window.Close();
        }
    }

    private static (long? Gpu,long Managed) PlaybackMemorySample(double seconds,int phase)
    {
        var gpu = ReadGpuAllocationBytes();
        var heap = GC.GetTotalMemory(forceFullCollection:true);
        var resources = RenderResourceMetrics.Snapshot();
        using var process = Process.GetCurrentProcess();
        Console.WriteLine($"PLAYBACK_MEMORY: second={seconds:F0}; phase={phase}; gpu_mib={gpu/1048576d:F1}; managed_mib={heap/1048576d:F1}; rss_mib={process.WorkingSet64/1048576d:F1}; text_mib={resources.TextCacheBytes/1048576d:F1}; scratch_mib={(resources.ScratchActiveBytes+resources.ScratchPooledBytes)/1048576d:F1}; native_images={resources.NativeImageRealizationCount}");
        return (gpu,heap);
    }
}
