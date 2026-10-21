using Pookie.App.Browser;
using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class AnalysisCaptureSettingsTests
{
    [Fact]
    public async Task DisablingBrowserAnalysisDropsSamplesWithoutStoppingPlayback()
    {
        var native = new FakeNative(); var browser = new FakeBrowser();
        await using var player = new WindowsAudioPlayer(native, () => browser);
        var source = new AudioSource("https://media.sndcdn.com/track.m3u8", AudioTransport.WidevineHls, 120)
            { LicenseAuthToken = "fixture", CaptureAnalysis = true };
        await player.PlayAsync(source);
        var start = browser.Commands.Single(command => command.Action == "start");
        browser.Publish(new("audio-state", start.PlaybackId, 200, Audio: new(8, 120, true, false, false)));
        player.SetAnalysisCaptureEnabled(false);
        for (var offset = 0; offset < 48000; offset += 1024)
        {
            var count = Math.Min(1024, 48000 - offset);
            browser.Publish(new("audio-sample", start.PlaybackId, 200, Sample: new(offset, Convert.ToBase64String(new byte[count * 4]))));
        }
        var command = browser.Commands.Last();
        Assert.Equal("analysis", command.Action); Assert.False(command.CaptureAnalysis); Assert.True(command.IsValid());
        Assert.False(native.AnalysisEnabled); Assert.Null(player.TakeAnalysisSample());
        Assert.True(player.Poll().Playing); Assert.Equal(8, player.Poll().Position);
        Assert.DoesNotContain(browser.Commands, command => command.Action == "stop");
        await player.PlayAsync(source);
        Assert.False(browser.Commands.Last(command => command.Action == "start").CaptureAnalysis);
        player.SetAnalysisCaptureEnabled(true);
        await player.PlayAsync(source);
        Assert.True(browser.Commands.Last(command => command.Action == "start").CaptureAnalysis);
    }

    private sealed class FakeBrowser : IBrowserAudioSession
    {
        public event Action<BrowserRequestEvent>? Changed;
        internal List<BrowserAudioCommand> Commands { get; } = [];
        public Task<BrowserRequestEvent> SendAudioAsync(BrowserAudioCommand command, CancellationToken token = default)
        { Commands.Add(command); return Task.FromResult(new BrowserRequestEvent("complete", Status: 200)); }
        internal void Publish(BrowserRequestEvent value) => Changed?.Invoke(value);
    }

    private sealed class FakeNative : IAudioPlayer
    {
        internal bool AnalysisEnabled = true;
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AudioState Poll() => new(0, 0, false, false, false);
        public void SetAnalysisCaptureEnabled(bool enabled) => AnalysisEnabled = enabled;
        public void Stop() { }
        public void Pause(bool paused) { }
        public void Volume(double percent) { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
