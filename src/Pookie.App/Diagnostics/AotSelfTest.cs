using System.Runtime.CompilerServices;
using System.Text.Json;
using Aprillz.MewUI;
using LibVLCSharp.Shared;
using Microsoft.Data.Sqlite;
using Pookie.App.Browser;
using Pookie.App.Messages;
using Pookie.App.Preview;
using Pookie.Audio;
using Pookie.Discord;
using Pookie.SoundCloud;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Pookie.App.Diagnostics;

// Runs inside the published production executable: a JIT test host cannot
// establish that native callbacks, trimming and patched framework hooks work.
internal static class AotSelfTest
{
    internal static async Task RunAsync(string? video)
    {
        if (RuntimeFeature.IsDynamicCodeSupported) throw new InvalidOperationException("Publish this executable with PublishAot=true first.");
        CheckLogging();
        ApplicationEmojiText.InstallCompatibility();
        WindowPresentation.YieldDispatcher(null) = null;
        var script = BrowserRequestScript.Read(null, "https://fixture.invalid");
        if (!script.Contains("https://fixture.invalid")) throw new InvalidOperationException("Browser script serialization failed.");
        _ = LoginCaptureScript.Read("https://fixture.invalid");
        var presence = PresenceService.CreatePresence(new("Трек 🎵", "Artist", null, null, 1, 10, true), DateTime.UtcNow)!;
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new PresenceRequest("SET_ACTIVITY", new(1, presence), "fixture"), PresenceJson.Default.PresenceRequest));
        if (json.RootElement.GetProperty("args").GetProperty("activity").GetProperty("details").GetString() != "Трек 🎵")
            throw new InvalidOperationException("Discord serialization failed.");
        using (var db = new SqliteConnection("Data Source=:memory:"))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT 'Native 🎵'";
            if ((string?)command.ExecuteScalar() != "Native 🎵") throw new InvalidOperationException("SQLite native transport failed.");
        }
        Console.WriteLine("AOT_METADATA_OK: generated JSON, Unicode SQLite, Discord payload and patched MewUI hooks");
        if (OperatingSystem.IsLinux())
        {
            using var session = await Pookie.Media.Linux.MprisMediaSession.CreateAsync();
            session.Update(new("aot", "Трек 🎵", "Artist", null, null, 0, 2, Pookie.Media.MediaPlayback.Playing, true, true, true, 1.5, false));
            using var bus = new Tmds.DBus.Protocol.DBusConnection(Tmds.DBus.Protocol.DBusAddress.Session!);
            await bus.ConnectAsync();
            Tmds.DBus.Protocol.MessageBuffer request;
            {
                using var writer = bus.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: Pookie.Media.Linux.MprisMediaSession.ServiceName, path: "/org/mpris/MediaPlayer2",
                    @interface: "org.freedesktop.DBus.Properties", member: "GetAll", signature: "s");
                writer.WriteString("org.mpris.MediaPlayer2.Player");
                request = writer.CreateMessage();
            }
            var properties = await bus.CallMethodAsync(request, static (message, _) => message.GetBodyReader().ReadDictionaryOfStringToVariantValue());
            if (properties["PlaybackStatus"].GetString() != "Playing" || properties["Volume"].GetDouble() != 1.5)
                throw new InvalidOperationException("AOT MPRIS transport failed.");
            Console.WriteLine("AOT_MPRIS_OK: typed D-Bus properties with no runtime emit");
        }
        if (OperatingSystem.IsMacOS())
        {
            using var media = new Pookie.Media.MacOS.MacMediaSession(action => action());
            media.Update(new("aot", "AOT fixture", "Artist", null, null, 0, 2, Pookie.Media.MediaPlayback.Playing, true, true, true, 1, false));
            media.Clear();
            Console.WriteLine("AOT_MAC_MEDIA_OK: Now Playing, native blocks and command handlers");
        }
        var wave = DemoAudio.Create(seconds: 2);
        try
        {
#if WINDOWS
            await Task.Run(() => AotWindowsMediaCheck.Run(wave));
#endif
            await using var audio = new SoundFlowPlayer(silent: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await audio.PlayAsync(new(wave, AudioTransport.File, 2), deadline.Token);
            audio.Volume(150);
            await audio.SeekAsync(.25, deadline.Token);
            if (audio.Poll().Duration < 1) throw new InvalidOperationException("Native audio did not open.");
            await audio.StopAsync();
            Console.WriteLine("AOT_AUDIO_OK: SoundFlow/miniaudio/FFmpeg decode, amplification and seek on a silent device");
        }
        finally { File.Delete(wave); }
        if (video != null)
        {
            await using var session = await ChatVideoSession.StartAsync(await File.ReadAllBytesAsync(video), silent: true);
            WriteableBitmap? bitmap = null; long version = 0;
            try
            {
                var timeout = DateTime.UtcNow.AddSeconds(15);
                while (version == 0 && DateTime.UtcNow < timeout)
                { session.CopyFrame(ref bitmap, ref version); if (version == 0) await Task.Delay(20); }
                if (version == 0 || bitmap == null) throw new InvalidOperationException("Native video did not decode a frame.");
            }
            finally { bitmap?.Dispose(); }
            Console.WriteLine("AOT_VIDEO_OK: bundled libVLC decoded a frame through managed/native callbacks");
        }
        else
        {
            ChatVideoRuntime.Initialize(); using var vlc = new LibVLC("--quiet", "--no-audio"); using var player = new MediaPlayer(vlc);
            Console.WriteLine("AOT_VIDEO_RUNTIME_OK: bundled libVLC initialized");
        }
        Console.WriteLine("NATIVE_AOT_OK: " + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
    }

    private static void CheckLogging()
    {
        var sink = new LogProbeSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.ForContext("SourceContext", "Pookie.AotProbe").Information(
            "Трек {Title}: {Volume:0.0}; {@Metadata}; {Missing}", "Тест 🎵", 1.5, new OpaqueLogValue(), null);
        var entry = sink.Entry ?? throw new InvalidOperationException("Native logging did not emit an event.");
        if (entry.Properties["Title"] is not ScalarValue { Value: "Тест 🎵" } ||
            entry.Properties["Volume"] is not ScalarValue { Value: double volume } || volume != 1.5 ||
            entry.Properties["SourceContext"] is not ScalarValue { Value: "Pookie.AotProbe" } ||
            entry.Properties["Missing"] is not ScalarValue { Value: null } ||
            entry.Properties["Metadata"] is not ScalarValue { Value: "opaque-native-value" } ||
            !entry.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("1.5"))
            throw new InvalidOperationException("Native logging lost structured fields or retained reflection-based destructuring.");
        Console.WriteLine("AOT_LOGGING_OK: structured Unicode/scalar/context fields and formatting; reflection destructuring removed");
    }

    private sealed class LogProbeSink : ILogEventSink
    {
        internal LogEvent? Entry;
        public void Emit(LogEvent logEvent) => Entry = logEvent;
    }
    private sealed class OpaqueLogValue
    {
        public string SensitiveData => throw new InvalidOperationException("Native logs must not invoke arbitrary property getters.");
        public override string ToString() => "opaque-native-value";
    }
}
