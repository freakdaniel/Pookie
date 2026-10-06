using Pookie.App.Auth;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class BrowserAudioStateSmokeTest
{
    public static async Task RunAsync()
    {
        var native = new FakeNative();
        var browser = new FakeBrowser();
        await using var player = new WindowsAudioPlayer(native, () => browser);
        var source = new AudioSource("https://media.sndcdn.com/track.m3u8", AudioTransport.WidevineHls, 120)
            { LicenseAuthToken = "fixture-authorization" };
        player.Volume(0);
        await player.PlayAsync(source);
        var first = browser.Commands.Last(c => c.Action == "start");
        Require(native.Played == null && first.Volume == 0, "Protected audio must use browser EME with configured volume.");
        browser.State(first.PlaybackId, new(3, 120, true, false, false));
        Require(player.Poll().Playing && player.Poll().Position == 3, "Browser state must reach the player.");
        await player.SeekAsync(35);
        Require(browser.Commands.Last().Action == "seek" && browser.Commands.Last().Position == 35, "Seek must target the current browser session.");
        player.Pause(true);
        Require(!player.Poll().Playing && browser.Commands.Last().Paused, "Pause must stop the displayed playing state immediately.");
        browser.State(first.PlaybackId, new(120, 120, false, false, true));
        Require(player.Poll().Ended && !player.Poll().Ended, "Track end must be delivered once.");
        await player.PlayAsync(source);
        var second = browser.Commands.Last(c => c.Action == "start");
        browser.State(first.PlaybackId, new(100, 120, true, false, true));
        Require(player.Poll().Position == 0 && !player.Poll().Ended, "Old-track events must not alter a new track.");
        browser.State(second.PlaybackId, new(2, 120, true, false, false));
        browser.Closed();
        var failed = false;
        try { player.Poll(); } catch (InvalidOperationException) { failed = true; }
        Require(failed, "A closed WebView must become a playback error.");
        browser.HoldStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = player.PlayAsync(source);
        var pendingId = browser.Commands.Last(c => c.Action == "start").PlaybackId;
        var local = new AudioSource("fixture.wav", AudioTransport.File);
        await player.PlayAsync(local);
        var cancelled = false;
        try { await pending; } catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && native.Played == local, "Switching to ordinary audio must cancel pending browser startup.");
        Require(browser.Commands.Any(c => c.Action == "stop" && c.PlaybackId == pendingId), "Cancelled startup must stop its browser element.");
        player.Stop();
        Require(!player.Poll().Playing, "Stop must clear playback state.");
        Console.WriteLine("BROWSER_AUDIO_STATE_SMOKE_OK: routing, volume, seek, pause, one-shot end, stale events, closed worker and cancelled startup; no website or CDM");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class FakeBrowser : IBrowserAudioSession
    {
        public event Action<BrowserRequestEvent>? Changed;
        public readonly List<BrowserAudioCommand> Commands = [];
        public TaskCompletionSource? HoldStart;
        public async Task<BrowserRequestEvent> SendAudioAsync(BrowserAudioCommand command, CancellationToken token = default)
        {
            Commands.Add(command);
            if (command.Action == "start" && HoldStart != null) await HoldStart.Task.WaitAsync(token);
            return new("complete", Status: 200);
        }
        public void State(string id, BrowserAudioState state) => Changed?.Invoke(new("audio-state", id, 200, Audio: state));
        public void Closed() => Changed?.Invoke(new("audio-closed"));
    }

    private sealed class FakeNative : IAudioPlayer
    {
        public AudioSource? Played;
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) { Played = source; return Task.CompletedTask; }
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AudioState Poll() => new(0, Played?.Duration ?? 0, Played != null, false, false);
        public void Stop() => Played = null;
        public void Pause(bool paused) => _ = paused;
        public void Volume(double percent) => _ = percent;
        public void Dispose() => Stop();
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
