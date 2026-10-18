using Aprillz.MewUI;
using Pookie.App.Messages;

namespace Pookie.App.Diagnostics;

internal static class ChatVideoSmokeTest
{
    internal static async Task RunAsync()
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
    }
}
