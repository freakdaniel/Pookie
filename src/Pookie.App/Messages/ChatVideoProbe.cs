using Aprillz.MewUI;

namespace Pookie.App.Messages;

internal static class ChatVideoProbe
{
    internal static async Task<ChatVideoInfo?> ReadAsync(byte[] bytes, CancellationToken token, Action<ImageSource>? poster = null)
    {
        try
        {
            await using var session = await ChatVideoSession.StartAsync(bytes, silent: true, preview: true);
            for (var i = 0; i < 60; i++)
            {
                token.ThrowIfCancellationRequested();
                if (session.PreviewInfo() is { } info && (info.DurationMs > 0 || i == 59)) { if (session.Poster() is { } image) poster?.Invoke(image); return info.Valid ? info : null; }
                if (session.Snapshot().Failed) return null;
                await Task.Delay(50, token);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { } // Sending/downloading a video remains possible without LibVLC.
        return null;
    }
}
