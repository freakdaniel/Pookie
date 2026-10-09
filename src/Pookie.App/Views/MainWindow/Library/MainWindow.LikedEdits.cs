using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<LibraryItemMotion, ItemsControl> likedItemMotions = [];
    private readonly Dictionary<ItemsControl, Dictionary<long, Point>> likedEditPositions = [];
    private long likedEdit;
    private long? likedEditAdded;
    private readonly SemaphoreSlim likedEdits = new(1, 1);

    private static void ClearLikedItemMotion(TemplateContext context)
    {
        var motion = context.Get<LibraryItemMotion>("liked-motion");
        motion.TrackId = null; motion.ResetMotion();
    }

    private LibraryItemMotion CreateLikedItemMotion(FrameworkElement content, ItemsControl owner, TemplateContext context)
    {
        var motion = new LibraryItemMotion().Content(content);
        likedItemMotions.Add(motion, owner);
        context.Register("liked-motion", motion);
        return motion;
    }

    private void BindLikedItemMotion(TemplateContext context, long id)
    {
        var motion = context.Get<LibraryItemMotion>("liked-motion");
        var owner = likedItemMotions[motion];
        if (likedEditPositions.TryGetValue(owner, out var positions) &&
            (motion.Edit != likedEdit || motion.TrackId != id))
        {
            motion.Retarget(positions.TryGetValue(id, out var point) ? point : null, appear: likedEditAdded == id);
            motion.Edit = likedEdit;
        }
        else if (motion.TrackId != id) motion.ResetMotion();
        motion.TrackId = id;
    }

    private bool IsActiveLikedView(ItemsControl owner) => page.Value switch
    {
        Page.Library => owner == libraryGrid,
        Page.LibraryTracks => owner == (likesAsList.Value ? likedList : likedGrid),
        _ => false
    };

    private async Task ApplyLibraryLikeAsync(SoundCloudTrack track, bool liked, WebSession session)
    {
        await likedEdits.WaitAsync(lifetime.Token);
        try { await ApplyLibraryLikeCoreAsync(track, liked, session); }
        finally { likedEdits.Release(); }
    }

    private async Task ApplyLibraryLikeCoreAsync(SoundCloudTrack track, bool liked, WebSession session)
    {
        if (disposed || api.Session != session) return;
        if (!liked)
        {
            var leaving = likedItemMotions.Where(pair => IsActiveLikedView(pair.Value) && pair.Key.TrackId == track.Id)
                .Select(pair => pair.Key).ToArray();
            await Task.WhenAll(leaving.Select(motion => motion.DisappearAsync())).WaitAsync(lifetime.Token);
        }
        if (disposed || api.Session != session) return;

        // Keep the complete loaded page and its pagination cursor. A single like
        // must not restart navigation, fetch the first page or clear its viewport.
        if (libraryLikes != null)
            libraryLikes = new(liked ? new[] { track }.Concat(libraryLikes.Tracks.Where(item => item.Id != track.Id)).ToArray() :
                libraryLikes.Tracks.Where(item => item.Id != track.Id).ToArray(), libraryLikes.NextHref);
        if (page.Value is not (Page.Library or Page.LibraryTracks)) return;

        likedEditPositions.Clear(); likedEdit++;
        likedEditAdded = liked ? track.Id : null;
        foreach (var (motion, owner) in likedItemMotions)
        {
            if (!IsActiveLikedView(owner) || motion.TrackId is not { } id || id == track.Id && !liked) continue;
            if (!likedEditPositions.TryGetValue(owner, out var positions)) likedEditPositions[owner] = positions = [];
            var point = motion.PaintedPosition;
            positions[id] = point;
            motion.Retarget(point); motion.Edit = likedEdit;
        }
        // Empty lists still need an entry animation for the newly added item.
        var active = page.Value == Page.Library ? libraryGrid : likesAsList.Value ? likedList : likedGrid;
        likedEditPositions.TryAdd(active, []);
        tracks.RemoveAll(item => item.Id == track.Id);
        if (liked) tracks.Insert(0, track);
        RefreshList(); RefreshLibraryCards();
    }
}
