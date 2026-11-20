using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const int OverviewMixCount = 7;
    private TrackCollectionGrid libraryMixGrid = null!;
    private LibraryLoadingView libraryMixLoading = null!;
    private TextBlock libraryMixEmpty = null!;
    private LibraryItem[] libraryMixItems = [];

    private FrameworkElement LibraryMixOverview()
    {
        libraryMixGrid = new TrackCollectionGrid(OverviewMixCount, minimumWidth: 0, gap: 12);
        libraryMixEmpty = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        libraryMixLoading = CreateLibraryLoadingView(libraryMixGrid, preview: true);
        return new StackPanel().Vertical().Spacing(18).Children(
            SectionHeader(UiText.Get("library.mixes"), () => Run(() => ShowLibrarySectionAsync(Page.LibraryMixes))),
            libraryMixLoading.Root, libraryMixEmpty);
    }

    private async Task<LibraryPage> LoadPersonalMixesAsync(long userId) =>
        new(await api.GetPersonalMixesAsync(userId, lifetime.Token), null);

    private void CachePersonalMixes(SoundCloudDiscoverShelves discover)
    {
        libraryPages["mixes"] = new(discover.Mixes.Concat(discover.MadeForYou).DistinctBy(item => item.Key).ToArray(), null);
        libraryLoadedAt["mixes"] = DateTimeOffset.UtcNow;
        overviewReady.Add("mixes");
        overviewErrors.Remove("mixes");
    }

    private void RefreshLibraryMixOverview()
    {
        if (libraryMixGrid == null) return;
        var items = LibraryItems("mixes").Take(OverviewMixCount).ToArray();
        if (!libraryMixItems.SequenceEqual(items))
        {
            libraryMixItems = items;
            foreach (var button in libraryMixGrid.Children.OfType<Button>())
            {
                var card = CollectionCard(button);
                card.Item = null;
                StopCardArtwork(card.Cover);
            }
            libraryMixGrid.Clear();
            foreach (var item in items)
                libraryMixGrid.Add(CreateHomeShelfCard(item, responsive: true).Root);
        }
        var pending = overviewPending.Contains("mixes");
        var error = overviewErrors.GetValueOrDefault("mixes");
        libraryMixLoading.SetLoading(items.Length == 0 && pending);
        libraryMixLoading.Root.IsVisible = items.Length > 0 || pending;
        libraryMixEmpty.IsVisible = (items.Length == 0 && !pending) || error != null;
        libraryMixEmpty.Text = error ?? (me == null && !demo ? UiText.Get("auth.required.mixes") : UiText.Get("mixes.empty.personalized"));
    }

    private static bool IsSystemCollection(LibraryItem? item) => item?.Playlist?.IsSystemMix == true;

    private static string[] MixTags(SoundCloudPlaylist playlist, SoundCloudTrack[] tracks)
    {
        var tagList = System.Text.RegularExpressions.Regex.Matches(playlist.TagList ?? "", "\"([^\"]+)\"|(\\S+)")
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        return new[] { playlist.Genre }.Concat(playlist.Tags ?? []).Concat(tagList)
            .Concat(tracks.Select(track => track.Genre).Where(genre => !string.IsNullOrWhiteSpace(genre))
                .GroupBy(genre => genre!.Trim(), StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Select(group => group.Key))
            .Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag!.Trim().TrimStart('#'))
            .Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }

    private static string MixArtists(SoundCloudPlaylist playlist, SoundCloudTrack[] tracks)
    {
        if (playlist.Description is { Length: > 0 and <= 240 } description && description.Contains(',') && !description.Contains('\n')) return description.Trim();
        if (!string.IsNullOrWhiteSpace(playlist.ShortDescription)) return playlist.ShortDescription.Trim();
        return string.Join(", ", tracks.Select(track => track.User?.Username).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
    }
}
