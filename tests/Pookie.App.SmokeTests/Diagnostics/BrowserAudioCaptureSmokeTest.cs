using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Browser;
using Pookie.App.Playback;
using Pookie.App.Preview;
using Pookie.Audio;
using Pookie.Recommendations.Audio;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

// Real Chromium decoder, production Worklet and paired browser pipe. The local
// WAV substitutes for EME media; no SoundCloud traffic or CDM is required.
internal static class BrowserAudioCaptureSmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();
        var path = DemoAudio.Create(220, 12);
        try
        {
            using var resource = typeof(BrowserAudioCaptureSmokeTest).Assembly.GetManifestResourceStream("Pookie.App.Browser.Scripts.audio-normalization.js")!;
            using var reader = new StreamReader(resource); var worklet = reader.ReadToEnd();
            server.MapGet("/worklet.js", () => Results.Text(worklet, "text/javascript"));
            server.MapGet("/audio.wav", () => Results.File(path, "audio/wav", enableRangeProcessing: true));
            server.MapGet("/me", () => Results.Json(new { id = 42, username = "Fixture" }));
            server.MapGet("/", () => Results.Content("""
                <!doctype html><title>Local PCM fixture</title><script>
                window.ddSbh=true;window.dataDomeOptions={};document.cookie='datadome=fixture-audio; Path=/';
                window.dispatchEvent(new Event('dd_ready'));
                const install=setInterval(()=>{
                  if(!window.__pookieRequest || !window.__pookiePost)return;
                  clearInterval(install);
                  let active;
                  window.__pookieAudioRequest=async(request,send)=>{
                    const c=request.audio;
                    if(c.action==='start'){
                      const context=new AudioContext();await context.audioWorklet.addModule('/worklet.js');await context.resume();
                      const audio=document.createElement('audio');audio.src='/audio.wav';document.body.append(audio);
                      const node=new AudioWorkletNode(context,'pookie-track-gain',{processorOptions:{gainDb:c.normalization_gain_db,captureAnalysis:c.capture_analysis}});
                      const master=context.createGain();master.gain.value=c.volume/100;
                      context.createMediaElementSource(audio).connect(node).connect(master).connect(context.destination);
                      active={context,audio,node};
                      node.port.onmessage=event=>{
                        const {offset,samples}=event.data,bytes=new Uint8Array(samples.length*4),view=new DataView(bytes.buffer);
                        for(let i=0;i<samples.length;i++)view.setFloat32(i*4,samples[i],true);
                        send({kind:'audio-sample',request_id:c.playback_id,status:200,sample:{offset,data:btoa(String.fromCharCode(...bytes))}});
                      };
                      audio.addEventListener('playing',()=>node.port.postMessage({active:true}));
                      audio.addEventListener('pause',()=>node.port.postMessage({active:false}));
                      await audio.play();
                    }else if(c.action==='pause'){
                      if(c.paused)active.audio.pause();else await active.audio.play();
                    }else if(c.action==='stop' && active){active.audio.pause();await active.context.close();active=null;}
                    send({kind:'complete',request_id:request.id,status:200});
                  };
                  window.dispatchEvent(new Event('dd_post_done'));
                },20);
                </script>
                """, "text/html"));
            await server.StartAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var browser = new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium"), server.Urls.Single() + "/");
            await browser.GetMeAsync(timeout.Token);
            await using var player = new WindowsAudioPlayer(new SilentFixturePlayer(), () => browser);
            player.Volume(0);
            await player.PlayAsync(new("https://media.sndcdn.com/fixture.m3u8", AudioTransport.WidevineHls, 12)
                { LicenseAuthToken = "fixture-license", NormalizationGainDb = -12, CaptureAnalysis = true }, timeout.Token);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            AudioSample? sample;
            while ((sample = player.TakeAnalysisSample()) == null) await timer.WaitForNextTickAsync(timeout.Token);
            var rms = Math.Sqrt(sample.Mono.Sum(value => value * (double)value) / sample.Mono.Length);
            if (sample.Mono.Length is < 48000 or > 144000 || rms is < .04 or > .06)
                throw new InvalidOperationException("Real browser PCM was muted, changed by track gain, or exceeded the analysis budget.");
            using var encoder = new MusicnnEncoder();
            if (encoder.Encode(sample)?.Values.Length != 200) throw new InvalidOperationException("Real decoded browser PCM did not produce a neural vector.");
            Console.WriteLine($"BROWSER_PCM_CAPTURE_OK: {sample.Mono.Length} actual decoded samples through production Worklet and paired pipe; gain/mute independent; real neural vector; EME not exercised");
        }
        finally { File.Delete(path); }
    }

    private sealed class SilentFixturePlayer : IAudioPlayer
    {
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Pause(bool paused) { }
        public void Volume(double percent) { }
        public AudioState Poll() => new(0, 0, false, false, false);
        public void Stop() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
