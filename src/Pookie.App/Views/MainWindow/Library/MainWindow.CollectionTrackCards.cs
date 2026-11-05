using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ItemsControl CreateCollectionTrackCards()
    {
        var grid = new ItemsControl().Items(Array.Empty<CollectionTrackEntry>(), entry => entry.Track.Title)
            .Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 0, 24, 0))
            .WrapPresenter(collectionTrackArtworkSize + 24, collectionTrackArtworkSize + 90).IsVisible(false);
        grid.ItemTemplate = new DelegateTemplate<CollectionTrackEntry>(context =>
        {
            LikedTrackTile tile = null!;
            tile = new(track => Run(() => PlayCollectionTrackAsync(track, collectionTrackTiles[tile.Root].Position)),
                track => Run(() => OpenTrackPageAsync(track)), ArtworkLayer, "music-notes");
            tile.SetSize(collectionTrackArtworkSize);
            collectionTrackTiles.Add(tile.Root, (tile, -1));
            AttachTrackQueueMenu(tile.Root, () => tile.Track, extraActions: true);
            AttachTrackTitle(tile.Title, () => tile.Track);
            context.Register("tile", tile.Root); return tile.Root;
        }, (_, entry, _, context) =>
        {
            var tile = collectionTrackTiles[context.Get<Button>("tile")].Tile;
            tile.Reset(); tile.Track = entry.Track;
            collectionTrackTiles[tile.Root] = (tile, entry.Position);
            tile.Title.Text = entry.Track.Title; tile.Author.Text = entry.Track.Author;
            tile.SetSize(collectionTrackArtworkSize);
            likedArtworkTracks[tile.Cover] = entry.Track.Id;
            SetCardArtwork(tile.Cover, libraryCoverCache.GetValueOrDefault(entry.Track.Id), entry.Track.ArtworkUrl ?? entry.Track.User?.AvatarUrl);
            Run(() => LoadLikedArtworkAsync(tile.Cover, entry.Track));
            RefreshCollectionTrackTile(tile, entry.Position, animate: false);
        }, (_, _, _, context) =>
        {
            var tile = collectionTrackTiles[context.Get<Button>("tile")].Tile;
            tile.Track = null; tile.Reset(); StopCardArtwork(tile.Cover);
            likedArtworkTracks.Remove(tile.Cover);
            collectionTrackTiles[tile.Root] = (tile, -1);
        });
        grid.SizeChanged += e =>
        {
            if (e.NewSize.Width <= 0) return;
            var columns = Math.Clamp((int)Math.Floor((e.NewSize.Width + 24) / 194), 1, 6);
            var cell = Math.Floor(e.NewSize.Width / columns);
            var size = Math.Max(80, cell - 24);
            if (Math.Abs(size - collectionTrackArtworkSize) < .1) return;
            collectionTrackArtworkSize = size;
            grid.WrapPresenter(cell, size + 90);
            foreach (var (tile, _) in collectionTrackTiles.Values) tile.SetSize(size);
        };
        return grid;
    }

    private void RefreshCollectionTrackTile(LikedTrackTile tile, int position, bool animate = true) =>
        tile.SetPlaying(tile.Track != null && tile.Track.Id == current?.Id && playbackQueue.Context?.Key == displayedCollection?.Key &&
            playbackQueue.Current?.SourceIndex == position, isPlaying.Value, animate);

    private void ClearCollectionTrackViews()
    {
        SetPageItems(collectionTrackList, Array.Empty<CollectionTrackEntry>(), LoadingRowStyle.Compact);
        SetPageItems(collectionTrackCards, Array.Empty<CollectionTrackEntry>(), LoadingRowStyle.Card);
        SetPageItems(collectionTrackWaves, Array.Empty<CollectionTrackEntry>(), LoadingRowStyle.Waveform);
        collectionTrackList.IsVisible = collectionMode.Value == TrackViewMode.List;
        collectionTrackCards.IsVisible = collectionMode.Value == TrackViewMode.Cards;
        collectionTrackWaves.IsVisible = collectionMode.Value == TrackViewMode.Waveform;
    }
}
