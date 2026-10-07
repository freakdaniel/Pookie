using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal interface IBrowserAudioSession
{
    event Action<BrowserRequestEvent>? Changed;
    Task<BrowserRequestEvent> SendAudioAsync(BrowserAudioCommand command, CancellationToken token = default);
}
