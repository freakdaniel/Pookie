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
        catch (Exception error)
        {
            // Poster extraction is optional; keep sending/downloading available.
            Pookie.Logging.AppLog.Failure("Pookie.Video", "Не удалось подготовить кадр видео", error, Serilog.Events.LogEventLevel.Debug);
        }
        return null;
    }
}
