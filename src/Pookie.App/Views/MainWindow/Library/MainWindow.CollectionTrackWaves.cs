using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ItemsControl CreateCollectionTrackWaves()
    {
        var result = new ItemsControl().Items(Array.Empty<CollectionTrackEntry>(), entry => entry.Track.Title)
            .ItemHeight(TrackRowLayout.Stride).FixedHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(0, 0, 0, TrackRowLayout.Gap)).IsVisible(false);
        result.ItemTemplate = new DelegateTemplate<CollectionTrackEntry>(context =>
        {
            LikedTrackRow row = null!;
            row = CreateLibraryTrackRow(track => Run(() => PlayCollectionTrackAsync(track, collectionWaveRows[row.Root].Position)),
                (track, fraction) => Run(() => SeekCollectionTrackAsync(track, collectionWaveRows[row.Root].Position, fraction)));
            collectionWaveRows.Add(row.Root, (row, -1));
            AttachPlaylistTrackEdits(row.Root, () => row.Track, () => collectionWaveRows[row.Root].Position);
            context.Register("row", row.Root); return row.Root;
        }, (_, entry, _, context) =>
        {
            var row = collectionWaveRows[context.Get<Grid>("row")].Row;
            collectionWaveRows[row.Root] = (row, entry.Position);
            BindLibraryTrackRow(row, entry.Track);
        }, (_, _, _, context) =>
        {
            var row = collectionWaveRows[context.Get<Grid>("row")].Row;
            ClearLibraryTrackRow(row); collectionWaveRows[row.Root] = (row, -1);
        });
        return result;
    }

    private async Task SeekCollectionTrackAsync(SoundCloudTrack track, int position, double fraction)
    {
        if (current?.Id != track.Id || playbackQueue.Context?.Key != displayedCollection?.Key || playbackQueue.Current?.SourceIndex != position)
        { SetQueue(track, position); await PlayAsync(track); }
        if (current?.Id == track.Id) await SeekLikedTrackAsync(track, fraction);
    }
}
