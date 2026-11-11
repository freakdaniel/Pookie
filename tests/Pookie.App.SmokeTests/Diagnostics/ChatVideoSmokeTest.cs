using Aprillz.MewUI;
using Pookie.App.Messages;
using Pookie.Logging;
using System.Runtime.InteropServices;

namespace Pookie.App.Diagnostics;

internal static class ChatVideoSmokeTest
{
    internal static async Task RunAsync()
    {
        var logs = Path.Combine(Path.GetTempPath(), "pookie-video-log-" + Guid.NewGuid().ToString("N"));
        var previousLogDirectory = Environment.GetEnvironmentVariable("POOKIE_LOG_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("POOKIE_LOG_DIRECTORY", Path.Combine(logs, "Logs"));
            AppLog.Initialize(logs, new LoggingOptions());
            await VerifyPlaybackAsync();
            if (OperatingSystem.IsLinux() && Directory.Exists(Path.Combine(AppContext.BaseDirectory, "libvlc")))
            {
                var mapped = File.ReadAllLines("/proc/self/maps").Where(line => line.Contains("/libvlc.so") || line.Contains("/libvlccore.so")).ToArray();
                if (mapped.Length == 0 || mapped.Any(line => !line.Contains(Path.Combine(AppContext.BaseDirectory, "libvlc"))))
                    throw new InvalidOperationException("Video playback did not use the published libVLC runtime.");
                Console.WriteLine("VIDEO_BUNDLED_RUNTIME_OK: libvlc and libvlccore loaded from the application directory");
            }
            else if (OperatingSystem.IsMacOS())
            {
                var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
                var directory = Path.Combine(AppContext.BaseDirectory, "libvlc", rid, "lib");
                var images = new List<string>();
                var count = DyldImageCount();
                for (uint index = 0; index < count; index++)
                    if (Marshal.PtrToStringUTF8(DyldImageName(index)) is { } path) images.Add(path);
                foreach (var library in new[] { "libvlc.dylib", "libvlccore.dylib" })
                    if (!images.Contains(Path.Combine(directory, library)))
                        throw new InvalidOperationException($"Video playback did not load the bundled {rid} {library}: {string.Join(", ", images.Where(path => path.Contains("libvlc")))}");
                Console.WriteLine($"VIDEO_BUNDLED_RUNTIME_OK: {rid} libvlc and libvlccore loaded from the application directory");
            }
            // Bad input reaches native demux/codec errors, which must survive the
            // managed log callback. Opening can fail synchronously or asynchronously.
            try
            {
                await using var invalid = await ChatVideoSession.StartAsync(new byte[128], silent: true);
                for (var tries = 0; tries < 100 && !invalid.Snapshot().Failed && !invalid.Snapshot().Ended; tries++) await Task.Delay(50);
                WriteableBitmap? bitmap = null; long version = 0;
                try
                {
                    invalid.CopyFrame(ref bitmap, ref version);
                    if (version != 0) throw new InvalidOperationException("Invalid video produced a decoded frame.");
                }
                finally { bitmap?.Dispose(); }
                invalid.LogPlaybackFailure();
            }
            catch (Exception error) when (error is not InvalidOperationException) { ChatVideoRuntime.LogFailure(error); }
            try { throw new DllNotFoundException("synthetic missing video dependency"); }
            catch (Exception inner) { ChatVideoRuntime.LogFailure(new InvalidOperationException("synthetic video open failure", inner)); }
            AppLog.Shutdown();
            var text = string.Join("\n", Directory.GetFiles(Path.Combine(logs, "Logs"), "pookie-*.log").Select(File.ReadAllText));
            foreach (var expected in new[] { "Pookie.Video", "synthetic video open failure", "synthetic missing video dependency", "DllNotFoundException", nameof(RunAsync), "libVLC [" })
                if (!text.Contains(expected))
                    throw new InvalidOperationException("Video diagnostic missing from log: " + expected);
            Console.WriteLine("VIDEO_LOG_OK: native codec errors, exception messages, inner exceptions and stacks");
        }
        finally
        {
            AppLog.Shutdown();
            Environment.SetEnvironmentVariable("POOKIE_LOG_DIRECTORY", previousLogDirectory);
            if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
        }
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")]
    private static extern uint DyldImageCount();

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")]
    private static extern nint DyldImageName(uint index);

    private static async Task VerifyPlaybackAsync()
    {
        foreach (var extension in new[] { "mp4", "webm", "mov", "mkv", "avi" })
        {
            using var fixture = typeof(ChatVideoSmokeTest).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-video." + extension)!;
            using var output = new MemoryStream(); fixture.CopyTo(output); var bytes = output.ToArray();
            await using var session = await ChatVideoSession.StartAsync(bytes, silent: true);
            Array.Clear(bytes); // The media original can be evicted while video is open.
            WriteableBitmap? bitmap = null; long version = 0;
            try
            {
                for (var tries = 0; tries < 100 && version == 0; tries++) { await Task.Delay(50); session.CopyFrame(ref bitmap, ref version); }
                // VLC can include a few codec padding rows in its output format.
                if (bitmap == null || version == 0 || bitmap.PixelWidth != 96 || bitmap.PixelHeight is < 64 or > 96 || session.Snapshot().Failed)
                    throw new InvalidOperationException($"No decoded video frame: {extension}; size={bitmap?.PixelWidth}x{bitmap?.PixelHeight}; version={version}; state={session.Snapshot()}");
                await session.ToggleAsync(); var paused = session.Snapshot().Time; await Task.Delay(180);
                if (session.Snapshot().Playing || Math.Abs(session.Snapshot().Time - paused) > 150) throw new InvalidOperationException("Video did not pause: " + extension);
                await session.ToggleAsync(); session.Seek(.5);
                for (var tries = 0; tries < 30 && session.Snapshot().Time < 700; tries++) await Task.Delay(50);
                if (session.Snapshot().Time < 700) throw new InvalidOperationException("Video did not seek: " + extension);
                Console.WriteLine("VIDEO_OK: " + extension);
            }
            finally { bitmap?.Dispose(); }
        }
        using var portrait = typeof(ChatVideoSmokeTest).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-video-portrait.mp4")!;
        using var portraitOutput = new MemoryStream(); portrait.CopyTo(portraitOutput);
        ImageSource? poster = null;
        var info = await ChatVideoProbe.ReadAsync(portraitOutput.ToArray(), CancellationToken.None, image => poster = image);
        if (info is not { Valid: true, Height: >= 470 } || poster == null || Math.Max(poster.PixelWidth, poster.PixelHeight) > 380)
            throw new InvalidOperationException($"Portrait video probe failed: info={info}, poster={poster?.PixelWidth}x{poster?.PixelHeight}.");
        Console.WriteLine("VIDEO_POSTER_OK: portrait metadata and bounded 380px preview");
    }
}
