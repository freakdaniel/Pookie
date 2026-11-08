using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyCompactTrackPresentationAsync(CompactTrackRow row)
    {
        Window.FocusManager.ClearFocus(); MoveActionPointer(new Point(1, 1));
        await WaitForLikedLayoutAsync(() => row.Summary.Strength > .999 && row.Actions.Strength < .001);
        var original = row.Track!;
        double LikeX() => row.Like.TranslatePoint(default, Window).X;
        var likeX = LikeX(); var right = row.Duration.Bounds.Right; var titleWidth = row.Title.ActualWidth;
        if (Math.Abs(row.Duration.Bounds.X - likeX - 32 - 8) > 1 / Window.DpiScale)
            throw new InvalidOperationException("Resting like is separated from its statistics.");
        foreach (var seconds in new[] { 59, 200, 3341, 6000 })
        {
            BindCompactTrackRow(row, original with { Duration = seconds * 1000, PlaybackCount = 123456 });
            await WaitForLoginFrameAsync();
            if (Math.Abs(LikeX() - likeX) > 1 / Window.DpiScale || Math.Abs(row.Duration.Bounds.Right - right) > 1 / Window.DpiScale ||
                row.Plays.Text != "123.5K" || row.Plays.Bounds.Y < row.Duration.Bounds.Bottom)
                throw new InvalidOperationException("Track statistics changed action columns or lost playback counts below the duration.");
        }
        BindCompactTrackRow(row, original);
        var opening = new List<double>(); var closing = new List<double>(); var positions = new List<double>();
        void SampleOpen() { opening.Add(row.Actions.Strength); positions.Add(LikeX()); }
        void SampleClose() => closing.Add(row.Actions.Strength);
        Window.FrameRendered += SampleOpen;
        var hoverPoint = new Point(row.Cover.Bounds.X + 4, row.Cover.Bounds.Y + 4);
        MoveActionPointer(hoverPoint);
        try { await WaitForLikedLayoutAsync(() => row.Actions.Strength > .999 && row.Summary.Strength < .001 && row.Overlay.Opacity > .999); }
        catch (InvalidOperationException error)
        {
            var actual = hoverPoint;
            if (OperatingSystem.IsWindows() && GetLikedActionCursor(out var cursor)) actual = Window.ScreenToClient(new Point(cursor.X, cursor.Y));
            throw new InvalidOperationException($"Row reveal did not settle: wanted={hoverPoint}, actual={actual}, hit={Window.HitTest(actual)?.GetType().Name}, hovered={row.Hovered}, mouse={row.Root.IsMouseOver}, actions={row.Actions.Strength}, summary={row.Summary.Strength}, overlay={row.Overlay.Opacity}, buttons={row.ActionButtons.Length}, root={row.Root.Bounds}, cover={row.Cover.Bounds}, visible={row.Actions.IsVisible}.", error);
        }
        finally { Window.FrameRendered -= SampleOpen; }
        if (!opening.Any(value => value is > .01 and < .99) || positions.Select(x => Math.Round(x, 1)).Distinct().Count() < 3 ||
            Math.Abs(row.Title.ActualWidth - titleWidth) > 1 / Window.DpiScale || likeX - LikeX() < 60 ||
            row.Summary.IsHitTestVisible || !row.Actions.IsHitTestVisible || row.Overlay.Bounds != row.Cover.Bounds)
            throw new InvalidOperationException("Hover actions snapped, changed title layout, left hidden hit targets or failed to cover the complete artwork.");
        VerifyCompactCoverDimming(row);
        CaptureUiPreview("track-row-hover-actions");
        row.More!.Focus(); MoveActionPointer(new Point(1, 1)); await WaitForLoginFrameAsync();
        if (row.Actions.Strength < .999) throw new InvalidOperationException("Keyboard focus dismissed the row actions.");
        Window.FrameRendered += SampleClose; Window.FocusManager.ClearFocus();
        try { await WaitForLikedLayoutAsync(() => row.Actions.Strength < .001 && row.Summary.Strength > .999); }
        finally { Window.FrameRendered -= SampleClose; }
        if (!closing.Any(value => value is > .01 and < .99) || row.Actions.IsHitTestVisible || !row.Summary.IsHitTestVisible || row.ActionButtons.Any(button => button.IsTabStop))
            throw new InvalidOperationException("Hover actions did not animate out or retained hidden hit targets.");
        if (Math.Abs(LikeX() - likeX) > 1 / Window.DpiScale) throw new InvalidOperationException("Like did not return beside its statistics.");
        MoveActionPointer(new(row.Cover.Bounds.X + 4, row.Cover.Bounds.Y + 4));
        await WaitForLikedLayoutAsync(() => row.Trailing.Reveal > .999);
        var origin = row.Like.TranslatePoint(default, Window); var wasLiked = likedIds.Contains(original.Id);
        var likePoint = new Point(origin.X + 16, origin.Y + 16);
        MoveActionPointer(likePoint); RouteWaveformClick(likePoint); MoveActionPointer(new Point(1, 1));
        await WaitForLikedLayoutAsync(() => row.Summary.Strength > .999 && row.Actions.Strength < .001 && !IsLikePending(original) && likedIds.Contains(original.Id) != wasLiked);
        Window.FocusManager.ClearFocus();
        CaptureUiPreview("track-row-statistics");

        var likesView = likesMode.Value; var queueVersion = playbackQueue.SourceVersion; var filter = collectionFilter.Text;
        collectionMode.Value = TrackViewMode.Cards;
        await WaitForLikedLayoutAsync(() => collectionTrackCards.IsVisible && collectionTrackTiles.Values.Any(entry => entry.Tile.Track != null && entry.Tile.Root.ActualWidth > 80));
        if (collectionTrackList.IsVisible || likesMode.Value != likesView || pageItems[collectionTrackCards].DataCount != 12 ||
            !ReferenceEquals(PageContentScroll(), CollectionScroll))
            throw new InvalidOperationException("Playlist card mode changed the likes preference or kept scrolling the hidden list.");
        CaptureUiPreview("playlist-track-cards");
        collectionFilter.Text = "Трек 10";
        await WaitForLikedLayoutAsync(() => pageItems[collectionTrackCards].DataCount == 1 && collectionTrackTiles.Values.Any(entry => entry.Position == 9 && entry.Tile.Track?.Id == 9009));
        collectionMode.Value = TrackViewMode.Waveform;
        await WaitForLikedLayoutAsync(() => collectionTrackWaves.IsVisible && collectionWaveRows.Values.Any(entry => entry.Position == 9 && entry.Row.Track?.Id == 9009 && entry.Row.Root.ActualHeight > 180));
        if (pageItems[collectionTrackWaves].DataCount != 1 || collectionTrackCards.IsVisible || collectionTrackList.IsVisible)
            throw new InvalidOperationException("Playlist waveform mode lost filtering or overlaps another view.");
        CaptureUiPreview("playlist-track-waveforms");
        collectionMode.Value = TrackViewMode.List;
        await WaitForLikedLayoutAsync(() => collectionTrackList.IsVisible && !collectionTrackCards.IsVisible && pageItems[collectionTrackList].DataCount == 1);
        if (collectionFilter.Text != "Трек 10" || playbackQueue.SourceVersion != queueVersion || activeCollection?.Items.Length != 12)
            throw new InvalidOperationException("Playlist view switching lost the filter, original source positions or playback queue.");
        collectionFilter.Text = filter;
        await WaitForLikedLayoutAsync(() => pageItems[collectionTrackList].DataCount == 12);
        Console.WriteLine("COMPACT_TRACK_PRESENTATION_OK: fixed columns, counts, full-cover dimming, animated reversible actions, focus and hidden hit targets; virtualized playlist card/list switching preserves filtering and queue");
    }

    private void VerifyCompactCoverDimming(CompactTrackRow row)
    {
        var bounds = row.Overlay.Bounds; var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.White); context.Translate(-bounds.X, -bounds.Y);
        row.Overlay.Render(context); context.EndFrame();
        var cpu = (ICpuPixelSurface)surface; var pixels = cpu.GetReadOnlyPixelSpan();
        foreach (var point in new[] { new Point(3, 22), new Point(22, 3), new Point(41, 22), new Point(22, 41) })
        {
            var index = (int)(point.Y * scale) * cpu.StrideBytes + (int)(point.X * scale) * 4;
            if (pixels[index] is < 80 or > 90 || pixels[index + 1] is < 80 or > 90 || pixels[index + 2] is < 80 or > 90)
                throw new InvalidOperationException("Play scrim does not dim all four edges of the cover.");
        }
    }

}
