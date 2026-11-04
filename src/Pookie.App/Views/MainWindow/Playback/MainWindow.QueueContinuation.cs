using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task<RecommendationBatch> GetQueueContinuationAsync(PlaybackQueueSnapshot state, CancellationToken token)
    {
        if (demo || api.Session?.IsValid() != true || me == null) return new([], Exhausted: true);
        var batch = await queueContinuation.FetchAsync(state, AllowsRecommendation, async (id, cursor, cancellation) =>
        {
            try { return cursor != null ? await api.GetNextPageAsync(cursor, cancellation) : await api.GetRelatedTracksAsync(id, cancellation); }
            catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 404 or 410)
            { return new([], null); }
        }, token);
        return batch with { Tracks = batch.Tracks.Where(AllowsRecommendation).ToArray() };
    }
}
