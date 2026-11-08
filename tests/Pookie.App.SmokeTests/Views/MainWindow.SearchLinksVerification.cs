using System.Numerics;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Resources;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySearchLinksUiAsync()
    {
        var previousSession = api.Session; var previousTransport = api.BrowserTransport;
        var previousMe = me;
        var sawSearchPage = false;
        void ObservePage() { if (page.Value == Page.Search) sawSearchPage = true; }
        try
        {
            var fixture = new SearchLinksFixture();
            api.Session = new("searchfixture", "fixture-token", "Test"); api.BrowserTransport = fixture;
            me = new() { Id = 42, Username = "Search fixture" }; signedIn.Value = true;
            await ShowWorkspaceAsync();
            Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive && !startupSplash.IsVisible && workspace.IsVisible);
            var playback = current?.Id; var playbackGeneration = playGeneration;
            page.Changed += ObservePage;
            var historyCount = navigationHistory.Count;
            var home = (Button)topNavigation.Children[0];
            var library = (Button)topNavigation.Children[2];
            var search = (Button)topNavigation.Children[^1];

            await VerifyHeaderSelectionAsync(home);
            CaptureUiPreview("header-home-selected");
            await OpenSearchFromHeaderAsync();
            await WaitForLikedLayoutAsync(() => topSearchInput.ActualWidth > 100);
            topSearchInput.Focus();
            ((ITextInputClient)topSearchInput).HandleTextInput(new("https://soundcloud.com/ar"));
            ((ITextInputClient)topSearchInput).HandleTextInput(new("tist/song"));
            if (query.Value != "https://soundcloud.com/artist/song")
                throw new InvalidOperationException("Pending link presentation interrupted a manually typed URL");
            query.Value = "";
            ((ITextInputClient)topSearchInput).HandleTextInput(new("  https://soundcloud.com/artist/song?secret_token=s-fixture  "));
            if (!topSearchInput.IsFocused || query.Value != topSearchInput.Text)
                throw new InvalidOperationException($"Header search did not acquire focus or bind its input: focused={topSearchInput.IsFocused}, query={query.Value}, text={topSearchInput.Text}");
            if (!topSearchBar.ShowsLink || topSearchInput.Opacity != 0 || topSearchBar.LinkLabel != "Ищу...")
                throw new InvalidOperationException("Pasted URL was displayed as raw text before recognition");
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == SearchLinksFixture.TrackTitle && !topSearchBar.LinkTransitioning);
            if (page.Value != Page.Home || fixture.Resolutions != 1)
                throw new InvalidOperationException("URL recognition navigated before Enter or made duplicate requests");
            await WaitForLoginFrameAsync(); CaptureUiPreview("search-link-recognized", topSearchBar);
            RouteSettingsKey(Key.Enter);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == 91 &&
                !navigationHistory[navigationIndex].Pending);
            if (current?.Id != playback || playGeneration != playbackGeneration)
                throw new InvalidOperationException("Opening a track URL started playback");
            if (sawSearchPage || fixture.Resolutions != 1 || navigationHistory.Count != historyCount + 1)
                throw new InvalidOperationException("Enter repeated URL recognition or inserted an intermediate search page");

            OpenTopSearch(); query.Value = "https://soundcloud.com/artist/slow-preview";
            var oldPreview = searchLinkTask!;
            await fixture.PreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            query.Value = "Новый запрос";
            fixture.PreviewReady.SetResult();
            try { await oldPreview; }
            catch (OperationCanceledException)
            {
                if (query.Value != "Новый запрос") throw;
            }
            if (topSearchBar.ShowsLink || topSearchInput.Opacity != 1 || query.Value != "Новый запрос")
                throw new InvalidOperationException("A stale metadata reply replaced an edited search draft");
            CloseTopSearch(clear: false);

            foreach (var (url, expectedPage, expectedKey) in new[]
            {
                ("https://www.soundcloud.com/artist", Page.UserProfile, "user:7"),
                ("https://soundcloud.com/artist/sets/list", Page.LibraryCollection, "playlist:94"),
                ("https://soundcloud.com/artist/sets/album", Page.LibraryCollection, "playlist:95"),
                ("https://soundcloud.com/discover/sets/mix", Page.LibraryCollection, "playlist:soundcloud:system-playlists:mix")
            })
            {
                query.Value = url; await SearchAsync(SearchSection.Tracks);
                if (page.Value != expectedPage || navigationHistory[navigationIndex].Route.Item?.Key != expectedKey)
                    throw new InvalidOperationException($"Search URL opened the wrong page: {url}");
                if (url.EndsWith("/album", StringComparison.Ordinal) && !navigationHistory[navigationIndex].Route.Item!.IsAlbum)
                    throw new InvalidOperationException("Album URL lost its album type");
            }

            await MoveNavigationAsync(-1);
            if (sawSearchPage || page.Value != Page.LibraryCollection || navigationHistory[navigationIndex].Route.Item?.Key != "playlist:95")
                throw new InvalidOperationException("Link navigation inserted search results into history");
            await VerifyHeaderSelectionAsync(library, home);
            if (Environment.GetEnvironmentVariable("POOKIE_UI_PREVIEW") is { Length: > 0 })
            {
                MoveActionPointer(SettingsCenter(forwardButton));
                await WaitForLikedLayoutAsync(() => forwardButton.IsMouseOver && forwardButton.IsEnabled);
                await Task.Delay(150, lifetime.Token);
                CaptureUiPreview("header-forward-hover");
            }
            page.Changed -= ObservePage;

            OpenTopSearch();
            query.Value = "https://www.soundcloud.com/artist";
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == "Artist");
            topSearchInput.Focus(); RouteSettingsKey(Key.Backspace);
            if (query.Value.Length != 0 || topSearchBar.ShowsLink || topSearchInput.Opacity != 1)
                throw new InvalidOperationException("Backspace did not remove the entire URL chip");
            RouteSelectableTextKey(Key.Z, ModifierKeys.Control);
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == "Artist");
            ((ITextInputClient)topSearchInput).HandleTextInput(new("Обычный поиск"));
            if (topSearchBar.ShowsLink || query.Value != "Обычный поиск" || topSearchInput.Opacity != 1)
                throw new InvalidOperationException("Typing did not replace the URL chip with ordinary text");
            CloseTopSearch(clear: false);

            query.Value = "Обычный поиск"; await SearchAsync(SearchSection.People);
            if (page.Value != Page.Search || fixture.Searches != 1 || fixture.LastSearchPath != "/search/users")
                throw new InvalidOperationException("Text search no longer respects its selected section");
            await VerifyHeaderSelectionAsync(search, library);

            query.Value = "https://soundcloud.com/artist/slow";
            var delayed = SearchAsync();
            await fixture.ResolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ShowSettingsAsync(SettingsSection.General);
            fixture.ResolveReady.SetResult(); await delayed;
            if (page.Value != Page.Settings) throw new InvalidOperationException("A stale URL response replaced the current page");
            await VerifyHeaderSelectionAsync(settingsButton, search);

            query.Value = "https://soundcloud.com/artist/missing";
            try { await SearchAsync(); throw new InvalidOperationException("Missing URL succeeded"); }
            catch (SoundCloudException error) when (error.StatusCode == 404)
            {
                if (searchLoading.Value || navigationHistory[navigationIndex].Pending)
                    throw new InvalidOperationException("A failed URL left search loading");
            }
            query.Value = "https://soundcloud.com/artist/song"; await SearchAsync();
            if (page.Value != Page.Track) throw new InvalidOperationException("Search did not recover after an unavailable link");
            await VerifyHeaderActivationMotionAsync();
            Console.WriteLine("SEARCH_LINKS_UI_OK: paste displays a chip, metadata resolves before Enter, direct navigation without a search frame or duplicate request, native editing/undo, profiles/playlists/albums/mixes, text search, history, stale isolation and error recovery");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            Console.Error.WriteLine($"SEARCH_LINKS_UI_FAILED: {error}");
        }
        finally
        {
            page.Changed -= ObservePage;
            api.Session = previousSession; api.BrowserTransport = previousTransport; me = previousMe;
            Window.Close();
        }
    }

    private async Task VerifyHeaderSelectionAsync(Button currentButton, Button? previous = null)
    {
        bool HasSelectionFill(Button button)
        {
            var pixels = CaptureSectionButton(button);
            var filled = 0;
            for (var i = 3; i < pixels.Length; i += 4)
                if (pixels[i] == 31) filled++; // The 12% selection fill covers the whole button.
            return filled > 500;
        }

        MoveActionPointer(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => topNavigation.Opacity == 1 && HasSelectionFill(currentButton) &&
            (previous == null || !HasSelectionFill(previous)));
    }

    private async Task OpenSearchFromHeaderAsync()
    {
        VerifyHeaderCardLayout();
        var nativeDpi = Window.Dpi;
        var setDpi = typeof(Window).GetMethod("SetDpi", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            foreach (uint dpi in new uint[] { 120, 144 })
            {
                setDpi.Invoke(Window, [dpi]); Window.PerformLayout();
                VerifyHeaderCardLayout();
            }
        }
        finally { setDpi.Invoke(Window, [nativeDpi]); Window.PerformLayout(); }
        var search = (Button)topNavigation.Children[^1];
        if (search.FindVisualChild<TransformDecorator>() != null)
            throw new InvalidOperationException("The search icon gained a section activation animation.");
        await VerifyHeaderInkTransitionAsync(settingsButton, Surface.Lerp(NavInk, .85), NavInk);
        await VerifyHeaderInkTransitionAsync(search, NavInk, NavHover);

        int Ink(Button button)
        {
            var pixels = CaptureSectionButton(button);
            var alpha = 0;
            for (var i = 3; i < pixels.Length; i += 4) alpha += pixels[i];
            return alpha;
        }

        MoveActionPointer(new Point(2, 2));
        await WaitForLoginFrameAsync();
        var settingsIdle = Ink(settingsButton);
        MoveActionPointer(SettingsCenter(settingsButton));
        await WaitForLikedLayoutAsync(() => settingsButton.IsMouseOver && Ink(settingsButton) > settingsIdle + 5000);
        await Task.Delay(150, lifetime.Token);
        CaptureUiPreview("header-settings-hover");

        var idle = Ink(search);
        var point = new Point(search.Bounds.X + search.ActualWidth / 2, search.Bounds.Y + search.ActualHeight / 2);
        MoveActionPointer(point);
        await WaitForLikedLayoutAsync(() => search.IsMouseOver && Ink(search) > idle + 15000);
        await Task.Delay(150, lifetime.Token);
        var hovered = Ink(search);
        CaptureUiPreview("header-navigation-hover");
        PressActionPointer(point, true);
        await WaitForLikedLayoutAsync(() => search.IsPressed && Ink(search) > hovered + 10000);
        PressActionPointer(point, false);
        await WaitForLikedLayoutAsync(() => searching.Value && topSearchBar.Opacity == 1 && topSearchInput.IsFocused &&
            Math.Abs(topSearchBar.ActualWidth - ShellHeader.NavigationWidth) < .1);
        var expectedAlpha = TrackButtons.StateColor(Color.Transparent, TrackButtons.OnSurface, HeaderButton.HoverOpacity).A;
        var searchPixels = CaptureSectionButton(topSearchBar);
        var flatPixels = 0;
        for (var i = 3; i < searchPixels.Length; i += 4)
            if (searchPixels[i] == expectedAlpha) flatPixels++;
        if (flatPixels < 1000)
            throw new InvalidOperationException("The header search field lost its flat state-layer fill.");
        var card = startupHeader.NavigationCard.Bounds;
        var surface = ShellHeader.StateLayerBounds(topSearchBar, Window.DpiScale);
        VerifyHeaderInset(card, surface, surface.X - card.X);
        VerifyHeaderInset(card, surface, card.Right - surface.Right);
        CaptureUiPreview("header-search-open");
    }

    private async Task VerifyHeaderInkTransitionAsync(Button button, Color idle, Color hover)
    {
        if (button.FindVisualChild<Image>() is not Image { Source: SimpleSvgSource source } glyph)
            throw new InvalidOperationException("The header button needs a retained tintable glyph.");
        var label = button.FindVisualChild<TextBlock>() as TextBlock;
        var outside = new Point(2, 2);
        var center = SettingsCenter(button);
        MoveActionPointer(outside);
        await WaitForLikedLayoutAsync(() => source.Tint == idle);
        var initial = CaptureSectionButton(glyph);
        var frames = new List<Color>();
        var inSync = true;
        void Sample()
        {
            if (source.Tint is not { } tint || glyph.Source != source || glyph.Opacity != 1)
            {
                inSync = false;
                return;
            }
            if (label != null)
            {
                var progress = (tint.R - idle.R) / (double)(hover.R - idle.R);
                var alpha = NavText.A + (NavTextHover.A - NavText.A) * progress;
                var foreground = label.Foreground;
                if (foreground is not { R: 255, G: 255, B: 255 } || Math.Abs(foreground.A - alpha) > 2)
                    inSync = false;
            }
            frames.Add(tint);
        }

        Window.FrameRendered += Sample;
        try
        {
            MoveActionPointer(center);
            await WaitForLikedLayoutAsync(() => source.Tint is { } tint && tint.R > idle.R && tint.R < hover.R);
            var interrupted = source.Tint;
            var middle = CaptureSectionButton(glyph);
            MoveActionPointer(outside);
            if (source.Tint != interrupted) throw new InvalidOperationException("Reversing hover snapped to a preset tint.");
            await WaitForLikedLayoutAsync(() => source.Tint == idle);
            MoveActionPointer(center);
            await WaitForLikedLayoutAsync(() => source.Tint == hover);
            var final = CaptureSectionButton(glyph);
            if (!inSync || frames.Distinct().Count(tint => tint.R > idle.R && tint.R < hover.R) < 2 ||
                frames.Any(tint => tint.R < idle.R || tint.R > hover.R) || initial.SequenceEqual(middle) || middle.SequenceEqual(final))
                throw new InvalidOperationException("The header glyph or label skipped colour frames or changed out of sync.");
            for (var i = 3; i < initial.Length; i += 4)
                if (initial[i] != middle[i] || initial[i] != final[i])
                    throw new InvalidOperationException("Tinting changed the glyph's visible coverage.");
        }
        finally
        {
            Window.FrameRendered -= Sample;
            MoveActionPointer(outside);
            await WaitForLikedLayoutAsync(() => source.Tint == idle);
        }
    }

    private async Task VerifyHeaderActivationMotionAsync()
    {
        var targets = new[] { Page.Home, Page.Feed, Page.Library, Page.Chats, Page.Settings };
        var buttons = topNavigation.Children.OfType<Button>().Take(4).Append(settingsButton).ToArray();
        for (var i = 0; i < targets.Length; i++)
        {
            var button = buttons[i];
            var target = targets[i];
            if (button.FindVisualChild<TransformDecorator>() is not TransformDecorator icon || !icon.Transform.IsIdentity)
                throw new InvalidOperationException($"The {target} icon is missing its resting transform.");
            var bounds = button.Bounds;
            var resting = CaptureSectionButton(icon);
            var poses = new HashSet<Matrix3x2>();
            void Sample() { if (!icon.Transform.IsIdentity) poses.Add(icon.Transform); }
            Window.FrameRendered += Sample;
            try
            {
                RouteWaveformClick(SettingsCenter(button));
                await WaitForLikedLayoutAsync(() => page.Value == target && poses.Count >= 2 && !icon.Transform.IsIdentity);
                var moving = CaptureSectionButton(icon);
                var coverageChanged = false;
                for (var pixel = 3; pixel < resting.Length; pixel += 4)
                    if (resting[pixel] != moving[pixel]) { coverageChanged = true; break; }
                await WaitForLikedLayoutAsync(() => icon.Transform.IsIdentity);
                if (!coverageChanged || poses.Count < 3 || button.Bounds != bounds)
                    throw new InvalidOperationException($"The {target} activation did not animate the rendered icon independently of its button.");
            }
            finally { Window.FrameRendered -= Sample; }
        }
        Console.WriteLine("HEADER_ACTIVATION_MOTION_OK: home, feed, library, chats and settings animate their rendered icons in place after activation");
    }

    private sealed class SearchLinksFixture : ISoundCloudBrowserTransport
    {
        internal const string TrackTitle = "Общество — песня с очень длинным названием, которое должно обрезаться внутри строки поиска";
        private static readonly string Track = JsonSerializer.Serialize(new { kind = "track", id = 91, title = TrackTitle, duration = 120000, user = new { id = 7, username = "Artist" } });
        private const string User = """{"kind":"user","id":7,"username":"Artist"}""";
        private const string Playlist = """{"kind":"playlist","id":94,"title":"List","tracks":[],"user":{"id":7,"username":"Artist"}}""";
        private const string Album = """{"kind":"playlist","id":95,"title":"Album","is_album":true,"tracks":[],"user":{"id":7,"username":"Artist"}}""";
        private const string Mix = """{"kind":"system-playlist","id":"soundcloud:system-playlists:mix","title":"Mix","tracks":[]}""";
        internal int Searches { get; private set; }
        internal int Resolutions { get; private set; }
        internal string? LastSearchPath { get; private set; }
        internal TaskCompletionSource ResolveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ResolveReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PreviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PreviewReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var path = uri.AbsolutePath;
            if (path == "/resolve")
            {
                Resolutions++;
                var url = Uri.UnescapeDataString(uri.Query[5..]);
                if (url.EndsWith("/missing", StringComparison.Ordinal)) throw new SoundCloudException("Не найдено", 404);
                if (url.EndsWith("/slow-preview", StringComparison.Ordinal))
                { PreviewStarted.SetResult(); await PreviewReady.Task; }
                if (url.EndsWith("/slow", StringComparison.Ordinal))
                {
                    ResolveStarted.SetResult();
                    // Deliberately emulate a transport that ignores cancellation.
                    await ResolveReady.Task;
                }
                return JsonDocument.Parse(url switch
                {
                    "https://www.soundcloud.com/artist" => User,
                    "https://soundcloud.com/artist/sets/list" => Playlist,
                    "https://soundcloud.com/artist/sets/album" => Album,
                    "https://soundcloud.com/discover/sets/mix" => Mix,
                    _ => Track
                });
            }
            if (path.StartsWith("/search", StringComparison.Ordinal)) { Searches++; LastSearchPath = path; }
            return JsonDocument.Parse(path switch
            {
                "/tracks/91" => Track,
                "/users/7" => User,
                "/playlists/94" => Playlist,
                "/playlists/95" => Album,
                _ when path.StartsWith("/system-playlists/", StringComparison.Ordinal) => Mix,
                _ when path.EndsWith("/web-profiles", StringComparison.Ordinal) => "[]",
                _ => """{"collection":[],"next_href":null}"""
            });
        }

        public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(JsonDocument.Parse(request.Kind == "sidebar" ? """{"data":{"playlists":[],"albums":[]}}"""
                : """{"data":{"trackComments":{"comments":[],"pageInfo":{"hasNextPage":false}}}}"""));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Unexpected write");
    }
}
