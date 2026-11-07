namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public Task RecordPlaybackAsync(long trackId, string? contextUrn = null, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        if (!BrowserRequestCommand.IsHistoryContextUrn(contextUrn)) throw new ArgumentException("Некорректный контекст прослушивания.", nameof(contextUrn));
        if (Session?.IsValid() != true) throw new SoundCloudException("Для истории нужен вход в SoundCloud.");
        if (BrowserTransport is not { } browser) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        return browser.RecordPlaybackAsync(trackId, contextUrn, token);
    }
}
