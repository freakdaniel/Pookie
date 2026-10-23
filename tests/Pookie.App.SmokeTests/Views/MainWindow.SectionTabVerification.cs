using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySectionTabsAsync()
    {
        Button Tab(string title) => (Button)VisualTree.Find(Window.Content, element => element is Button button &&
            button.StyleName == "section-button" && button.FindVisualChild<TextBlock>() is TextBlock label && label.Text == title)!;
        async Task StableActive(Button button, string name)
        {
            await WaitForLikedLayoutAsync(() => button.ActualWidth > 20 && shellPageScroll.IsReady &&
                pendingNavigationScroll == null && !navigationHistory[navigationIndex].Pending);
            RouteWaveformPointer(new Point(2, 2));
            await WaitForLoginFrameAsync();
            bool Settled()
            {
                var settled = button.FindVisualChild<TextBlock>() is TextBlock { Foreground: var color } && color == Color.White;
                VisualTree.Visit(button, element =>
                {
                    if (element is Image or TextBlock or Border && element is UIElement visual && visual.Opacity is > 0 and < 1)
                        settled = false;
                });
                return settled;
            }
            await WaitForLikedLayoutAsync(Settled);
            if (button.IsEnabled || button.Focus() || button.FindVisualChild<TextBlock>() is not TextBlock { Foreground: var foreground } || foreground != Color.White)
                throw new InvalidOperationException($"Active {name} is clickable, focusable or lost its white label");
            var before = CaptureSectionButton(button);
            var generation = navigationGeneration; var historyCount = navigationHistory.Count;
            var offset = shellScroll.VerticalOffset;
            var center = new Point(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2);
            async Task ObserveUnchanged(string input)
            {
                // Nav icons/labels transition over 250 ms. Inspect every
                // rendered frame throughout that interval, including geometry.
                var started = Stopwatch.GetTimestamp();
                do
                {
                    await WaitForLoginFrameAsync();
                    if (!before.SequenceEqual(CaptureSectionButton(button)) || generation != navigationGeneration ||
                        historyCount != navigationHistory.Count || offset != shellScroll.VerticalOffset)
                        throw new InvalidOperationException($"{input} changed active {name}: generation={generation}/{navigationGeneration}, history={historyCount}/{navigationHistory.Count}, offset={offset}/{shellScroll.VerticalOffset}.");
                }
                while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 250);
            }
            RouteWaveformPointer(center);
            await ObserveUnchanged("Hover");
            for (var click = 0; click < 3; click++) RouteWaveformClick(center);
            await ObserveUnchanged("Repeated clicks");
        }

        await NavigateAsync(Page.Home);
        await StableActive(Tab("Главная"), "top-bar home");
        await NavigateAsync(Page.Feed);
        await StableActive(Tab("Лента"), "top-bar feed");
        await NavigateAsync(Page.Library);
        await StableActive(Tab("Библиотека"), "top-bar library overview");
        await StableActive(Tab("Обзор"), "library overview tab");
        ShowLibraryTracks(); likedFilter.Text = ""; likesAsList.Value = true;
        ReplaceTracks(new(Enumerable.Range(90000, 30).Select(id => new SoundCloudTrack { Id = id, Title = "Трек " + id }).ToArray(), null));
        await WaitForLikedLayoutAsync(() => NavigationScroll() is { } scroll && SmoothScroll.Maximum(scroll) > 300);
        NavigationScroll()!.SetScrollOffsets(0, 20);
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && Math.Abs(NavigationScroll()!.VerticalOffset - 20) < 1);
        await StableActive(Tab("Лайки"), "scrolled likes tab");
        var library = Tab("Библиотека");
        if (!library.IsEnabled) throw new InvalidOperationException("Top-bar library overview is unavailable from a child page");
        RouteWaveformClick(new Point(library.Bounds.X + library.ActualWidth / 2, library.Bounds.Y + library.ActualHeight / 2));
        await WaitForLikedLayoutAsync(() => page.Value == Page.Library && !library.IsEnabled);
        await StableActive(library, "library overview after child-page navigation");

        await NavigateRouteAsync(new(Page.Search, Search: "fixture", Section: SearchSection.Tracks), _ =>
        {
            query.Value = "fixture"; searchSection.Value = SearchSection.Tracks;
            searchHeading.Value = "Результаты для «fixture»";
            SetSearchResults(new(tracks.Select(LibraryItem.FromTrack).ToArray(), null));
            return Task.CompletedTask;
        });
        await WaitForLikedLayoutAsync(() => NavigationScroll() is { } scroll && SmoothScroll.Maximum(scroll) > 300);
        NavigationScroll()!.SetScrollOffsets(0, 20);
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && Math.Abs(NavigationScroll()!.VerticalOffset - 20) < 1);
        await StableActive(Tab("Треки"), "scrolled search tracks tab");
        CaptureUiPreview("active-search-tab");
        var all = Tab("Всё");
        var bright = ((Grid)((StackPanel)all.Content!).Children[0]).Children.OfType<TextBlock>().Last();
        RouteWaveformPointer(new Point(all.Bounds.X + all.ActualWidth / 2, all.Bounds.Y + all.ActualHeight / 2));
        await WaitForLikedLayoutAsync(() => bright.Opacity > .999);
        if (!all.IsEnabled || all.Background != Color.Transparent)
            throw new InvalidOperationException("Inactive search tab lost its interaction or acquired a hover background");
        RouteWaveformPointer(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => bright.Opacity < .001);
        Console.WriteLine("UI_SECTION_TABS_OK: active tabs remain white without hover/click changes, reject mouse and keyboard activation, preserve scroll and navigation generation, and allow library overview from child pages; inactive hover remains available");
    }

    private byte[] CaptureSectionButton(Button button)
    {
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(button.ActualWidth * scale), (int)Math.Ceiling(button.ActualHeight * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.Translate(-button.Bounds.X, -button.Bounds.Y);
        button.Render(context);
        context.EndFrame();
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }
}
