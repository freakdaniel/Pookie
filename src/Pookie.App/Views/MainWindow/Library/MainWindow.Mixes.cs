using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private HomeShelf libraryMixShelf = null!;
    private LibraryLoadingView libraryMixLoading = null!;
    private TextBlock libraryMixEmpty = null!;
    private LibraryItem[] libraryMixItems = [];

    private FrameworkElement LibraryMixOverview()
    {
        libraryMixShelf = new HomeShelf(this);
        libraryMixEmpty = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        libraryMixLoading = CreateLibraryLoadingView(libraryMixShelf.Root, preview: true);
        return new StackPanel().Vertical().Spacing(18).Children(
            SectionHeader("Миксы", () => Run(() => ShowLibrarySectionAsync(Page.LibraryMixes))),
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
        if (libraryMixShelf == null) return;
        var items = LibraryItems("mixes");
        if (!libraryMixItems.SequenceEqual(items))
        {
            libraryMixItems = items;
            libraryMixShelf.SetItems(items);
        }
        var pending = overviewPending.Contains("mixes");
        var error = overviewErrors.GetValueOrDefault("mixes");
        libraryMixLoading.SetLoading(items.Length == 0 && pending);
        libraryMixLoading.Root.IsVisible = items.Length > 0 || pending;
        libraryMixEmpty.IsVisible = (items.Length == 0 && !pending) || error != null;
        libraryMixEmpty.Text = error ?? (me == null && !demo ? "Войди в SoundCloud, чтобы открыть миксы." : "SoundCloud пока не подготовил для тебя миксы.");
    }

    private static bool IsSystemCollection(LibraryItem? item) => item?.Playlist is { } playlist &&
        (playlist.IsSystemPlaylist || playlist.Urn.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal));

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

    private void RefreshMixTags(string[] tags)
    {
        collectionMixTags.Clear();
        foreach (var tag in tags)
            collectionMixTags.Add(new Border().CornerRadius(11).Background(TrackButtons.GlassSurface)
                .BorderBrush(Color.FromArgb(28, 255, 255, 255)).BorderThickness(1).Padding(9, 3).Hint(tag)
                .Child(new TextBlock().Text("#" + tag).FontSize(11).Foreground(TrackButtons.OnSurface)
                    .MaxWidth(136).TextTrimming(TextTrimming.CharacterEllipsis)));
        collectionMixTags.IsVisible = tags.Length > 0;
    }
}
