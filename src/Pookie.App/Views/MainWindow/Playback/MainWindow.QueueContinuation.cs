using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task<SoundCloudTrack[]> GetQueueContinuationAsync(PlaybackQueueSnapshot state, CancellationToken token)
    {
        if (demo || api.Session?.IsValid() != true || me == null) return [];
        EnsureWaveTaste();
        return await queueContinuation.FetchAsync(state, waveTaste, async (id, cursor, cancellation) =>
        {
            try { return cursor != null ? await api.GetNextPageAsync(cursor, cancellation) : await api.GetRelatedTracksAsync(id, cancellation); }
            catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 404 or 410)
            { return new([], null); }
        }, token);
    }
}
