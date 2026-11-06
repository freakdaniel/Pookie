using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyOptimisticLikeReadsAsync()
    {
        var transport = new LikesEditTransport { LikedIdsResponse = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var session = new WebSession("fixture", "optimistic-token", "Test");
        api.Session = session; api.Transport = transport;
        page.Value = Page.Home; libraryLikes = null;
        likedLoading?.Cancel(); likedTask = null; likedIdsReady = false; likedIds.Clear();
        var track = new SoundCloudTrack { Id = 99001, Title = "Optimistic fixture" };
        current = track; UpdateLikeState();
        var like = ToggleTrackLikeAsync(track);
        if (!isLiked.Value || !IsTrackLiked(track) || transport.Requests != 0)
            throw new InvalidOperationException("Like waited for initial ID/session verification before updating its appearance.");
        await ToggleTrackLikeAsync(track);
        transport.LikedIdsResponse.SetResult(JsonDocument.Parse("[99001]"));
        await WaitForLikedLayoutAsync(() => transport.Requests == 1);
        if (!transport.LastLiked || !isLiked.Value)
            throw new InvalidOperationException("Initial ID loading inverted the user's already-visible like intent.");
        likedIds.Clear(); UpdateLikeState();
        if (!isLiked.Value) throw new InvalidOperationException("A background cache refresh erased the pending like.");
        transport.Complete(track.Id); await like;
        if (!isLiked.Value || !likedIds.Contains(track.Id) || IsLikePending(track))
            throw new InvalidOperationException("Acknowledged like was not committed to the confirmed state.");

        current = track = track with { Id = 99002 }; UpdateLikeState();
        like = ToggleTrackLikeAsync(track);
        if (!isLiked.Value || likedIds.Contains(track.Id)) throw new InvalidOperationException("Pending addition was not independent of confirmed likes.");
        transport.Fail(track.Id);
        var failed = false;
        try { await like; }
        catch (HttpRequestException) { failed = true; }
        if (!failed || isLiked.Value || IsTrackLiked(track) || IsLikePending(track))
            throw new InvalidOperationException("Rejected addition did not roll back its selection.");

        like = ToggleTrackLikeAsync(track);
        likedLoading!.Cancel();
        var canceled = false;
        try { await like; }
        catch (OperationCanceledException) { canceled = true; }
        transport.Complete(track.Id);
        if (!canceled || isLiked.Value || likedIds.Contains(track.Id) || IsLikePending(track))
            throw new InvalidOperationException("Cancellation or its late completion retained an unconfirmed like.");
        likedLoading.Dispose(); likedLoading = null; likedTask = null;

        current = track = track with { Id = 99003 }; UpdateLikeState();
        like = ToggleTrackLikeAsync(track);
        api.Session = session with { AccessToken = "another-account" };
        likedIds.Clear(); UpdateLikeState();
        if (isLiked.Value) throw new InvalidOperationException("Another account inherited a pending selection.");
        transport.Complete(track.Id); await like;
        if (isLiked.Value || likedIds.Contains(track.Id) || pendingLikes.Count != 0)
            throw new InvalidOperationException("Late acknowledgement changed another account.");

        transport = new() { LikedIdsResponse = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        api.Transport = transport; likedIdsReady = false; likedTask = null;
        like = ToggleTrackLikeAsync(track);
        if (!isLiked.Value) throw new InvalidOperationException("Initial check did not retain the optimistic selection.");
        transport.LikedIdsResponse.SetException(new HttpRequestException("Fixture initial verification failed"));
        failed = false;
        try { await like; }
        catch (HttpRequestException) { failed = true; }
        if (!failed || isLiked.Value || transport.Requests != 0 || IsLikePending(track))
            throw new InvalidOperationException("Failed initial verification retained the like or sent a mutation.");
        Console.WriteLine("UI_OPTIMISTIC_LIKES_OK: initial verification, captured intent, background refresh, success, rejection, cancellation and account isolation");
    }
}
