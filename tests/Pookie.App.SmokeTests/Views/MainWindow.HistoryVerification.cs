using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyNavigationAsync()
    {
        var fixtures = tracks.ToArray();
        var playing = current?.Id;
        var queue = queueTracks.Select(track => track.Id).ToArray();
        ResetNavigationHistory();
        ReplaceTracks(new(fixtures, null));
        if (backButton.IsEnabled || forwardButton.IsEnabled) throw new InvalidOperationException("Initial navigation arrows are enabled");
        await NavigateAsync(Page.Library);
        ShowLibraryTracks();
        likedFilter.Text = "needle";
        likesAsList.Value = true;
        await ShowLibrarySectionAsync(Page.LibraryPlaylists);
        var first = libraryPages["collections"].Items[0];
        var second = libraryPages["collections"].Items[1];
        await OpenLibraryItemAsync(first);
        await OpenLibraryItemAsync(second);
        await MoveNavigationAsync(-1);
        if (page.Value != Page.LibraryCollection || librarySectionTitle.Value != first.Title)
            throw new InvalidOperationException("History confused two collections with the same page type");
        await MoveNavigationAsync(-1);
        if (page.Value != Page.LibraryPlaylists || collectionGrid.ItemsSource.Count != 1)
            throw new InvalidOperationException("Back failed to restore the playlist tab");
        await MoveNavigationAsync(-1);
        if (page.Value != Page.LibraryTracks || likedFilter.Text != "needle" || !likesAsList.Value || likedList.ItemsSource.Count != 1)
            throw new InvalidOperationException("Back lost the likes filter or view");
        await MoveNavigationAsync(1);
        if (page.Value != Page.LibraryPlaylists || !backButton.IsEnabled || !forwardButton.IsEnabled)
            throw new InvalidOperationException("Forward did not restore the previous page");
        await ShowLibrarySectionAsync(Page.LibraryFollowing);
        if (canGoForward.Value || forwardButton.IsEnabled) throw new InvalidOperationException("A new route kept the abandoned forward branch");
        var count = navigationHistory.Count;
        await ShowLibrarySectionAsync(Page.LibraryFollowing);
        if (navigationHistory.Count != count) throw new InvalidOperationException("Repeated navigation created a duplicate entry");
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Any(card => card.Item.User != null && card.Frame.ActualWidth > 100));
        var artist = collectionCards.Values.First(card => card.Item.User != null && card.Frame.ActualWidth > 100);
        var nameCenter = artist.Title.Bounds.X + artist.Title.Bounds.Width / 2;
        var coverCenter = artist.Frame.Bounds.X + artist.Frame.Bounds.Width / 2;
        var body = (StackPanel)artist.Title.Parent!;
        var subtitle = (StackPanel)body.Children[2];
        if (artist.Title.TextAlignment != TextAlignment.Center || Math.Abs(nameCenter - coverCenter) > 1 ||
            Math.Abs(subtitle.Bounds.X + subtitle.Bounds.Width / 2 - coverCenter) > 1 ||
            !subtitle.Children.OfType<Image>().Single().IsVisible || Math.Abs(artist.Frame.CornerRadius - artist.Frame.ActualWidth / 2) > 1)
            throw new InvalidOperationException("Artist captions, followers icon or circular artwork are not centered");
        CaptureUiPreview("artists");
        await ShowLibrarySectionAsync(Page.LibraryAlbums);
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Any(card => card.Item.IsAlbum && card.Frame.ActualWidth > 100));
        var album = collectionCards.Values.First(card => card.Item.IsAlbum && card.Frame.ActualWidth > 100);
        if (album.Title.TextAlignment != TextAlignment.Left || ((StackPanel)((StackPanel)album.Title.Parent!).Children[2]).Children.OfType<Image>().Single().IsVisible)
            throw new InvalidOperationException("Recycled artist captions leaked into album cards");

        ShowLibraryTracks(); likedFilter.Text = ""; likesAsList.Value = true;
        await WaitForLikedLayoutAsync(() => likedRows.Values.Count(row => row.Track != null && row.Root.ActualWidth > 100) > 2);
        var likesScroll = NavigationScroll()!;
        likesScroll.SetScrollOffsets(0, 320);
        await Task.Delay(80, lifetime.Token);
        var savedOffset = likesScroll.VerticalOffset;
        if (savedOffset < 300) throw new InvalidOperationException("History scroll fixture did not scroll the likes list");
        await NavigateAsync(Page.Feed);
        await MoveNavigationAsync(-1);
        await Task.Delay(80, lifetime.Token);
        if (Math.Abs(NavigationScroll()!.VerticalOffset - savedOffset) > 1)
            throw new InvalidOperationException("Back lost the likes scroll position");

        // Search fixtures use the navigation boundary with distinct query/result pairs, without remote reads.
        async Task SearchFixture(string search, SoundCloudTrack track) => await NavigateRouteAsync(new(Page.Home, Search: search), _ =>
        {
            query.Value = search; eyebrow.Value = "ПОИСК"; heading.Value = "Результаты поиска";
            ReplaceTracks(new([track], null));
            return Task.CompletedTask;
        });
        await SearchFixture("ambient", fixtures[0]);
        await SearchFixture("electronic", fixtures[1]);
        await MoveNavigationAsync(-1);
        if (page.Value != Page.Home || query.Value != "ambient" || tracks.Single().Id != fixtures[0].Id)
            throw new InvalidOperationException("Search history restored a different query or result set");
        await MoveNavigationAsync(1);
        if (query.Value != "electronic" || tracks.Single().Id != fixtures[1].Id)
            throw new InvalidOperationException("Search forward history lost its query/results");
        var gate = new TaskCompletionSource();
        var delayed = NavigateRouteAsync(new(Page.Feed), async _ => { BeginLoad(); await gate.Task; throw new IOException("Obsolete page error"); });
        await MoveNavigationAsync(-1);
        gate.SetResult(); await delayed;
        if (page.Value != Page.Home || query.Value != "electronic" || tracks.Single().Id != fixtures[1].Id)
            throw new InvalidOperationException("Late page response changed the restored route");
        await MoveNavigationAsync(1);
        if (page.Value != Page.Feed || navigationHistory[navigationIndex].Pending)
            throw new InvalidOperationException("Incomplete page could not be reopened from history");
        if (current?.Id != playing || !queue.SequenceEqual(queueTracks.Select(track => track.Id)))
            throw new InvalidOperationException("Navigation history changed playback or the queue");
        ResetNavigationHistory();
        if (navigationHistory.Count != 1 || canGoBack.Value || canGoForward.Value || tracks.Count != 0)
            throw new InvalidOperationException("Account reset left private history accessible");
        Console.WriteLine("UI_NAVIGATION_OK: back/forward, collection identity, likes filter/view/scroll, search queries/results, forward branching, duplicate suppression, late replies, unfinished routes, account reset and unchanged playback");
        Console.WriteLine("UI_ARTISTS_OK: centered captions and followers icon, circular artwork and isolated playlist/album styling");
    }
}
