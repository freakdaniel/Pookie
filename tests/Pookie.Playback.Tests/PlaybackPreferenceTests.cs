using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class PlaybackPreferenceTests
{
    private static SoundCloudTrack Track(long id) => new() { Id = id, Title = "Track " + id };

    [Fact]
    public void DisablingContinuationRetainsCurrentSourceManualAndHistory()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("album", "Album", "Source"), [Track(1), Track(2), Track(3)], 0);
        queue.Next(); queue.Previous();
        var manual = queue.Enqueue(Track(9));
        Assert.True(queue.AppendRecommendations(queue.SourceVersion, [Track(10), Track(11)]));
        var before = queue.Snapshot;
        queue.SetAutoContinue(false);
        var after = queue.Snapshot;
        Assert.Equal(before.SourceVersion, after.SourceVersion);
        Assert.Equal(before.Current, after.Current); Assert.Equal(before.Source, after.Source);
        Assert.Equal(before.History, after.History); Assert.Equal(before.Forward, after.Forward);
        Assert.Equal(before.ContextUpcoming, after.ContextUpcoming);
        Assert.Equal([manual], after.ManualUpcoming); Assert.Empty(after.Recommendations);
        Assert.False(after.CanLoadRecommendations);
        Assert.Equal(new long[] { 9, 2, 3 }, Enumerable.Range(0, 3).Select(_ => queue.Next()!.Track.Id));
        Assert.Null(queue.Next());
        queue.Clear(); Assert.False(queue.AutoContinue);
    }

    [Fact]
    public async Task LateContinuationCannotReturnAfterDisableAndReenable()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("track", "Track", "Source"), [Track(1)], 0);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<SoundCloudTrack[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => throw new InvalidOperationException(),
            () => { }, CancellationToken.None, (_, token) => { started.TrySetResult(token); return response.Task; });
        var request = loader.EnsureAheadAsync();
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.SetAutoContinue(false); loader.PolicyChanged();
        queue.SetAutoContinue(true); loader.PolicyChanged();
        Assert.True(token.IsCancellationRequested);
        response.SetResult([Track(2)]);
        await request;
        Assert.Empty(queue.Snapshot.Recommendations);
    }

    [Fact]
    public async Task DisabledContinuationStillLoadsTheSelectedPlaylistAndWave()
    {
        foreach (var kind in new[] { PlaybackContextKind.Playlist, PlaybackContextKind.Wave })
        {
            var queue = new PlaybackQueue(); queue.SetAutoContinue(false);
            queue.StartContext(new("source", "Source", "Source", "page2", Kind: kind), [Track(1)], 0);
            var calls = 0;
            using var loader = new PlaybackQueueLoader(queue, (_, _, _) => Task.FromResult(new TrackPage([Track(2)], null)),
                () => { }, CancellationToken.None, (_, _) => { calls++; return Task.FromResult(new[] { Track(3) }); });
            await loader.EnsureAheadAsync();
            Assert.Equal(2, queue.Next()!.Track.Id); Assert.Equal(0, calls);
        }
    }
}
