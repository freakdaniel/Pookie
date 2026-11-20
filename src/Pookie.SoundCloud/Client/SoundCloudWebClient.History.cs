using Pookie.Localization;
namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public Task RecordPlaybackAsync(long trackId, string? contextUrn = null, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        if (!BrowserRequestCommand.IsHistoryContextUrn(contextUrn)) throw new ArgumentException(UiText.Get("history.invalidContext"), nameof(contextUrn));
        if (Session?.IsValid() != true) throw new SoundCloudException(UiText.Get("auth.required.history"));
        if (BrowserTransport is not { } browser) throw new SoundCloudException(UiText.Get("soundcloud.browser.disconnected"));
        return browser.RecordPlaybackAsync(trackId, contextUrn, token);
    }
}
