using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class PaginationSmokeTest
{
    public static void Run()
    {
        var source = new PaginationItems(LoadingRowStyle.Waveform, item =>
            item is SoundCloudTrack track ? track.Id : item);
        var changes = new List<ItemsChange>();
        source.View.Changed += changes.Add;
        var tracks = Enumerable.Range(1, 5000).Select(id => new SoundCloudTrack { Id = id, Title = $"Track {id}" }).ToArray();
        source.SetData(tracks.Take(4900));
        if (changes.Count != 1 || changes[0].Kind != ItemsChangeKind.Add || source.View.Count != 4900)
            throw new InvalidOperationException("Initial tracks were not published as one range.");
        source.View.SelectedIndex = 3200;
        var selected = source.View.GetItem(3200);
        changes.Clear();
        source.SetLoading(3);
        source.SetData(tracks);
        source.SetLoading(0);
        if (changes.Count > 6 || changes.Any(change => change.Kind == ItemsChangeKind.Reset) ||
            source.View.Count != 5000 || source.View.SelectedIndex != 3200 || !ReferenceEquals(selected, source.View.GetItem(3200)))
            throw new InvalidOperationException("Appending tracks reset the view/selection or emitted per-track changes.");
        for (var index = 0; index < tracks.Length; index++)
            if (!ReferenceEquals(source.View.GetItem(index), tracks[index]))
                throw new InvalidOperationException("Appending tracks lost the content order.");
        changes.Clear();
        source.SetData(tracks);
        if (changes.Count != 0) throw new InvalidOperationException("Unchanged data invalidated the view.");
        changes.Clear();
        source.SetData(Array.Empty<object>());
        if (changes.Count != 1 || changes[0].Kind != ItemsChangeKind.Remove || source.View.Count != 0 || source.View.SelectedIndex != -1)
            throw new InvalidOperationException("Clearing the view did not remove a single range.");
        Console.WriteLine("PAGINATION_SMOKE_OK: 5000 tracks, bounded range notifications, loading slots, stable keys/selection and clear");
    }
}
