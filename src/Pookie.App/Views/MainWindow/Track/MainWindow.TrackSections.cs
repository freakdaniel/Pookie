using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum TrackSection { Overview, Reposts, Albums, Playlists, Related }
    private readonly ObservableValue<TrackSection> trackDetailSection = new(TrackSection.Overview);
    private FrameworkElement trackDetailOverview = null!, trackDetailSectionContent = null!;
    private StackPanel trackDetailSectionTracks = null!;
    private TrackCollectionGrid trackDetailSectionCards = null!;
    private TextBlock trackDetailSectionEmpty = null!;
    private LibrarySkeleton trackDetailSectionSkeleton = null!;
    private (long Track, TrackSection Section) renderedTrackSection;
    private LibraryItem[] renderedTrackSectionItems = [];
    private readonly List<LikedTrackRow> trackSectionRows = [];
    private string? failedTrackSectionCursor;

    private FrameworkElement TrackDetailSectionsView() => new StackPanel().Horizontal().Spacing(24).Margin(24, 0).Children(
        Enum.GetValues<TrackSection>().Select(section => (Element)SectionTab(TrackSectionTitle(section), trackDetailSection, section,
            () => Run(() => OpenTrackSectionAsync(section)))).ToArray());

    private FrameworkElement TrackDetailSectionContentView()
    {
        trackDetailSectionTracks = new StackPanel().Vertical();
        trackDetailSectionCards = new TrackCollectionGrid();
        trackDetailSectionEmpty = new TextBlock().FontSize(14).Foreground(Muted).Margin(0, 12);
        trackDetailSectionSkeleton = new LibrarySkeleton();
        return trackDetailSectionContent = new StackPanel().Vertical().Spacing(16).Margin(24, 0, 24, 0)
            .Children(trackDetailSectionTracks, trackDetailSectionCards, trackDetailSectionSkeleton, trackDetailSectionEmpty).IsVisible(false);
    }

    private static string TrackSectionTitle(TrackSection section) => section switch
    {
        TrackSection.Overview => "Обзор", TrackSection.Reposts => "Репосты", TrackSection.Albums => "Альбомы",
        TrackSection.Playlists => "Плейлисты", _ => "Похожие треки"
    };
    private static string TrackSectionApi(TrackSection section) => section switch
    {
        TrackSection.Reposts => "reposts", TrackSection.Albums => "albums", TrackSection.Playlists => "playlists",
        TrackSection.Related => "related", _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    private Task OpenTrackSectionAsync(TrackSection section)
    {
        if (trackDetailState is not { } state || trackDetailSection.Value == section) return Task.CompletedTask;
        return NavigateRouteAsync(new(Page.Track, Track: state.Track, TrackSection: section), async generation =>
        {
            trackDetailState = state with { SectionLoading = false, SectionError = "" };
            trackDetailSection.Value = section; failedTrackSectionCursor = null;
            // Switching a subsection retains the hero's decoded artwork, backdrop,
            // waveform and playback position; only the body changes.
            RenderTrackDetailHeader(); RenderTrackDetailComments(); RenderTrackDetailRelated();
            RenderTrackDetailSidebar(); RenderTrackSection();
            Run(() => LoadTrackDetailArtworkAsync(state.Track, generation));
            var token = loading!.Token;
            await Task.WhenAll(
                state.CommentsLoading ? LoadTrackDetailCommentsAsync(state.Track.Id, generation, token) : Task.CompletedTask,
                state.RelatedLoading ? LoadTrackDetailRelatedAsync(state.Track.Id, generation, token) : Task.CompletedTask,
                state.SidebarLoading ? LoadTrackDetailSidebarAsync(state.Track.Id, generation, token) : Task.CompletedTask,
                state.MetadataLoading ? LoadTrackDetailMetadataAsync(state.Track.Id, generation, token) : Task.CompletedTask,
                section != TrackSection.Overview && !state.Sections.ContainsKey(section)
                    ? LoadTrackSectionAsync(section, generation, token) : Task.CompletedTask);
        });
    }

    private async Task LoadTrackSectionAsync(TrackSection section, long generation, CancellationToken token, string? cursor = null)
    {
        if (trackDetailState is not { } state) return;
        var id = state.Track.Id;
        trackDetailState = state with { SectionLoading = true, SectionError = "" };
        RenderTrackSection();
        try
        {
            var result = await api.GetTrackSectionAsync(id, TrackSectionApi(section), cursor, token);
            if (!IsTrackDetailCurrent(id, generation) || trackDetailSection.Value != section) return;
            var sections = new Dictionary<TrackSection, LibraryPage>(trackDetailState!.Sections);
            var previous = sections.GetValueOrDefault(section);
            sections[section] = new(cursor == null ? result.Items : (previous?.Items ?? []).Concat(result.Items).DistinctBy(item => item.Key).ToArray(),
                result.NextHref == cursor || cursor != null && result.Items.Length == 0 ? null : result.NextHref);
            trackDetailState = trackDetailState with { Sections = sections, SectionLoading = false };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsTrackDetailCurrent(id, generation) || trackDetailSection.Value != section) return;
            failedTrackSectionCursor = cursor;
            trackDetailState = trackDetailState! with { SectionLoading = false, SectionError = FriendlyError(error) };
        }
        RenderTrackSection();
    }

    private void RenderTrackSection()
    {
        if (trackDetailState is not { } state) return;
        var section = trackDetailSection.Value;
        var overview = section == TrackSection.Overview;
        trackDetailOverview.IsVisible = overview; trackDetailSectionContent.IsVisible = !overview;
        trackDetailSectionSkeleton.SetActive(!overview && state.SectionLoading);
        if (overview) return;
        var items = state.Sections.GetValueOrDefault(section)?.Items ?? [];
        var related = section == TrackSection.Related;
        trackDetailSectionCards.IsVisible = !related; trackDetailSectionTracks.IsVisible = related;
        trackDetailSectionSkeleton.CompactList = false;
        trackDetailSectionSkeleton.SetGeometry(related ? 160 : 170, 4, related);
        trackDetailSectionSkeleton.Height = related ? TrackRowLayout.Stride * 3 : 240;
        trackDetailSectionSkeleton.IsVisible = state.SectionLoading;
        trackDetailSectionEmpty.Text = state.SectionError.Length > 0 ? "Не удалось загрузить этот раздел." : section switch
        {
            TrackSection.Reposts => "Этот трек ещё не репостили.", TrackSection.Albums => "Этот трек пока не входит в альбомы.",
            TrackSection.Playlists => "Этот трек пока не добавлен в плейлисты.", _ => "Похожих треков пока нет."
        };
        trackDetailSectionEmpty.IsVisible = !state.SectionLoading && items.Length == 0;
        if (renderedTrackSection != (state.Track.Id, section) || !items.Take(renderedTrackSectionItems.Length).SequenceEqual(renderedTrackSectionItems))
        {
            foreach (var row in trackSectionRows) { ClearLibraryTrackRow(row); likedRows.Remove(row.Root); }
            trackSectionRows.Clear(); trackDetailSectionTracks.Clear(); trackDetailSectionCards.Clear();
            renderedTrackSectionItems = []; renderedTrackSection = (state.Track.Id, section);
        }
        foreach (var item in items.Skip(renderedTrackSectionItems.Length))
            if (item.Track is { } track)
            {
                var row = CreateLibraryTrackRow(); row.Root.Margin = new Thickness(0, 0, 0, TrackRowLayout.Gap);
                BindLibraryTrackRow(row, track); trackSectionRows.Add(row); trackDetailSectionTracks.Add(row.Root);
            }
            else trackDetailSectionCards.Add(TrackCollectionCard(item, state.Track.Id));
        renderedTrackSectionItems = items;
    }

    private void CheckTrackSectionEnd()
    {
        if (page.Value != Page.Track || trackDetailSection.Value == TrackSection.Overview ||
            trackDetailState is not { SectionLoading: false } state || navigationHistory[navigationIndex].Pending) return;
        var cursor = state.Sections.GetValueOrDefault(trackDetailSection.Value)?.NextHref;
        if (cursor == null || cursor == failedTrackSectionCursor || trackDetailScroll.ViewportHeight <= 0) return;
        if (SmoothScroll.Maximum(trackDetailScroll) - trackDetailScroll.VerticalOffset > Math.Max(160, trackDetailScroll.ViewportHeight * .5)) return;
        Run(() => LoadTrackSectionAsync(trackDetailSection.Value, navigationGeneration, loading!.Token, cursor));
    }
}
