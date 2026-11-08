using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.Lyrics;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private HttpClient lyricsHttp = null!;
    private LyricsService lyricsService = null!;
    private CancellationTokenSource? lyricsRequest;
    private LyricsIdentity? lyricsIdentity;
    private LyricsResult lyricsResult = new(LyricsStatus.Loading);
    private string? lyricsResolvedFingerprint;
    private long lyricsGeneration;
    private double lyricsPosition;
    private int lyricsActiveLine = -1;
    private bool lyricsFollow = true, lyricsPlaying;
    private double lyricsFontSize = 40, lyricsSpacer;
    private double? lyricsFollowTarget;
    private DispatcherTimer lyricsFollowTimer = null!;
    private static readonly TimeSpan LyricsFollowDelay = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan NearbyLyricsFollowDelay = TimeSpan.FromSeconds(2.5);
    private readonly PaginationItems lyricsItems = new(LoadingRowStyle.Compact, item => ((LyricsDisplayRow)item).Index);
    private sealed record LyricsDisplayRow(int Index, string Text, double Spacer = 0);
    private sealed class LyricsRowVisual(Grid root, TextBlock text, LyricsWaveDots dots)
    {
        internal Grid Root = root;
        internal TextBlock Text = text;
        internal LyricsWaveDots Dots = dots;
        internal Button? Credit;
        internal string? CreditProvider;
        internal LyricsDisplayRow? Row;
        internal bool Hovered, Pressed;
    }
    private readonly Dictionary<Button, LyricsRowVisual> lyricsRows = [];
    private ItemsControl lyricsList = null!;
    private ScrollViewer lyricsScroll = null!;
    private QueueEdgeFade lyricsFade = null!;
    private LyricsSkeleton lyricsSkeleton = null!;
    private TextBlock lyricsMessage = null!;
    private Grid lyricsContent = null!;
    private bool lyricsWasLoading = true;
    private readonly AnimationClock lyricsReveal = new(TimeSpan.FromMilliseconds(360), Easing.CubicBezier(.2, 0, 0, 1));

    private void InitializeLyrics()
    {
        lyricsHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        lyricsService = new(new LrclibClient(lyricsHttp), Path.Combine(dataPaths.Cache, "Lyrics"), lyricSearch.FindLyricsAsync);
        lyricsFollowTimer = new(NearbyLyricsFollowDelay);
        lyricsFollowTimer.Tick += CheckLyricsFollowDelay;
    }

    private static LyricsIdentity IdentifyLyrics(SoundCloudTrack track)
    {
        var full = track.FullDuration > 0 ? track.FullDuration / 1000d : track.DurationSeconds;
        var timing = !(track.FullDuration > track.Duration + 2000 ||
            track.Media?.Transcodings is { Length: > 0 } transcodings && transcodings.All(item => item.Snipped));
        var artist = string.IsNullOrWhiteSpace(track.PublisherMetadata?.Artist) ? track.Author : track.PublisherMetadata.Artist;
        return new(track.Id, track.Title, artist, full, track.PublisherMetadata?.AlbumTitle, timing)
        { Isrc = LrcRedSearch.NormalizeIsrc(track.PublisherMetadata?.Isrc) };
    }

    private FrameworkElement LyricsPanel()
    {
        lyricsList = new ItemsControl().ItemHeight(84).VariableHeightPresenter().Background(Color.Transparent)
            .BorderThickness(0).Padding(0).ItemPadding(new Thickness(0));
        lyricsList.ItemsSource = lyricsItems.View;
        lyricsList.ItemTemplate = new DelegateTemplate<LyricsDisplayRow>(context =>
        {
            var text = new TextBlock().FontSize(lyricsFontSize).Bold().Foreground(Color.White)
                .TextAlignment(TextAlignment.Center).TextWrapping(TextWrapping.Wrap);
            text.LineSpacing = 6;
            var dots = new LyricsWaveDots().Foreground(Color.White).Width(90).Height(60).CenterHorizontal().CenterVertical();
            var content = new Grid().Columns("*").Rows("*").Children(text, dots);
            Button button = null!;
            button = new Button().Background(Color.Transparent).BorderThickness(0).Padding(12, 18).Content(content)
                .OnClick(() => { if (lyricsRows[button].Row is { Index: >= 0 } row) SeekLyricsLine(row.Index); })
                .OnMouseEnter(() => { lyricsRows[button].Hovered = true; RefreshLyricsRow(lyricsRows[button]); })
                .OnMouseLeave(() => { lyricsRows[button].Hovered = lyricsRows[button].Pressed = false; RefreshLyricsRow(lyricsRows[button]); });
            button.MouseDown += args => { if (args.Button == MouseButton.Left) { lyricsRows[button].Pressed = true; RefreshLyricsRow(lyricsRows[button]); } };
            button.MouseUp += _ => { lyricsRows[button].Pressed = false; RefreshLyricsRow(lyricsRows[button]); };
            // Fade the ink, not a composited subtree. On MewVG an Opacity group
            // allocates and paints another render surface for every visible line.
            text.Transitions = [Transition.Create(Control.ForegroundProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
            dots.Transitions = [Transition.Create(Control.ForegroundProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
            var root = new Grid().Columns("*").Rows("Auto").Children(button);
            lyricsRows[button] = new(root, text, dots);
            context.Register("button", button);
            return root;
        }, (_, row, _, context) =>
        {
            var button = context.Get<Button>("button"); var view = lyricsRows[button];
            view.Hovered = button.IsMouseOver; view.Pressed = false; view.Row = row;
            var branded = row.Index == -3 && row.Text is LrclibClient.Name or LrcRedSearch.Name;
            if (branded && view.CreditProvider != row.Text) SetLyricsCredit(view, row.Text);
            if (view.Credit is { } credit) credit.IsVisible = branded;
            button.IsVisible = !branded;
            view.Text.FontSize = row.Index == -3 ? 13 : lyricsFontSize;
            view.Text.FontWeight = row.Index == -3 ? FontWeight.Normal : FontWeight.Bold;
            view.Text.Text = row.Index == -3 ? "Текст предоставлен " + row.Text : row.Text;
            var gap = row.Index >= 0 && row.Text.Length == 0 && lyricsResult.Status == LyricsStatus.Synced;
            view.Text.IsVisible = !gap; view.Dots.IsVisible = gap;
            button.Height = row.Spacer > 0 ? row.Spacer : double.NaN;
            button.IsHitTestVisible = button.Focusable = row.Index >= 0 && lyricsResult.Status == LyricsStatus.Synced;
            view.Root.IsHitTestVisible = branded || button.IsHitTestVisible;
            button.Cursor = button.IsHitTestVisible ? CursorType.Hand : null;
            RefreshLyricsRow(view);
        }, (_, _, _, context) =>
        {
            var button = context.Get<Button>("button"); var view = lyricsRows[button];
            button.IsHitTestVisible = button.Focusable = false;
            view.Root.IsHitTestVisible = false;
            if (view.Credit is { } credit) credit.IsVisible = false;
            view.Row = null; view.Hovered = view.Pressed = false; view.Dots.Reset();
        });
        lyricsScroll = (ScrollViewer)lyricsList.FindVisualChild<ScrollViewer>()!;
        ((IVisualTreeHost)lyricsScroll).VisitChildren(element =>
        { if (element is ScrollBar bar) { bar.Opacity = 0; bar.IsHitTestVisible = false; } return true; });
        smoothScrolls[lyricsScroll] = new SmoothScroll(lyricsScroll)
        {
            RestoringAnchor = () =>
            {
                // During manual scrolling of plain lyrics, a measured row can
                // shift the offset without moving the text on screen.
                if (lyricsResult.Status == LyricsStatus.Plain) return true;
                if (!lyricsFollow || !preferences.LyricsAutoScroll || lyricsResult.Status != LyricsStatus.Synced) return false;
                // Measuring a wrapped line can shift the virtualized viewport mid-scroll.
                // Preserve momentum and recheck the active row against the corrected layout.
                lyricsFollowTarget = null;
                return true;
            }
        };
        lyricsScroll.MouseWheel += args => { if (args.Delta.Y != 0) PauseLyricsFollowing(); };
        lyricsScroll.KeyDown += args =>
        {
            if (args.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)) return;
            smoothScrolls[lyricsScroll].Stop();
            PauseLyricsFollowing();
        };
        lyricsFade = new(expandedBackdrop) { IsHitTestVisible = false };
        lyricsSkeleton = new LyricsSkeleton();
        lyricsMessage = new TextBlock().Text("Для текущего трека\nотсутствует текст").FontSize(40).Bold()
            .Foreground(Color.White).TextAlignment(TextAlignment.Center).TextWrapping(TextWrapping.Wrap)
            .CenterVertical().CenterHorizontal().Margin(12, 0);
        lyricsMessage.LineSpacing = 6;
        lyricsContent = new Grid().Columns("*").Rows("*").Children(lyricsList, lyricsFade, lyricsMessage);
        lyricsContent.Opacity = 0;
        lyricsReveal.TickCallback = amount => { lyricsContent.Opacity = amount; lyricsSkeleton.Opacity = 1 - amount; };
        lyricsReveal.CompletedCallback = () => { lyricsSkeleton.IsVisible = false; lyricsSkeleton.SetActive(false); };
        return new Grid().Columns("*").Rows("*").Children(lyricsContent, lyricsSkeleton);
    }

    private void SetLyricsCredit(LyricsRowVisual view, string provider)
    {
        if (view.Credit is { } previous)
        {
            view.Root.Remove(previous);
            previous.Dispose();
        }
        var url = provider == LrcRedSearch.Name ? LrcRedSearch.Source : LrclibClient.Source;
        var mark = provider == LrcRedSearch.Name ? (FrameworkElement)LrcRedProviderMark() :
            new Image().Source(ImageSource.FromResource<MainWindow>("Pookie.App.Assets.Branding.lrclib.png"))
                .Width(18).Height(18).StretchMode(Stretch.Uniform);
        var credit = LyricsProviderCredit("Текст предоставлен", provider, mark,
            () => OpenExternalUrl(url, () => status.Value = "Не удалось открыть сайт " + provider)).CenterHorizontal();
        view.CreditProvider = provider;
        view.Credit = credit;
        view.Root.Add(credit);
    }

    private static Border LrcRedProviderMark() => new Border().Width(8).Height(8).Background(Color.FromRgb(227, 6, 19));

    private static Button LyricsProviderCredit(string label, string provider, FrameworkElement mark, Action open) =>
        TrackButtons.Action(new StackPanel().Horizontal().Spacing(6).Children(
                new TextBlock().Text(label).FontSize(12).Foreground(Muted).CenterVertical(),
                new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(mark.CenterVertical(),
                    new TextBlock().Text(provider).FontSize(12).Bold().Foreground(TrackButtons.OnSurface).CenterVertical())),
            open, TrackButtons.Text, 26).Hint("Открыть " + provider);

    private void BeginLyricsTrack(SoundCloudTrack track)
    {
        CancelLyricsRequest(); lyricsGeneration++;
        lyricsIdentity = IdentifyLyrics(track); lyricsResolvedFingerprint = null;
        lyricsResult = new(preferences.LyricsEnabled ? LyricsStatus.Loading : LyricsStatus.NotFound); lyricsPosition = 0; lyricsActiveLine = -1; lyricsPlaying = false;
        ResumeLyricsFollowing(); RenderLyrics();
        if (expandedOpen && expandedPanelMode == PlayerPanel.Lyrics) StartLyricsLoad();
    }

    private void RefreshLyricsMetadata(SoundCloudTrack track)
    {
        var updated = IdentifyLyrics(track);
        if (lyricsIdentity is not { } previous || previous.Fingerprint != updated.Fingerprint ||
            previous.Isrc != updated.Isrc && lyricsResult.Status is not (LyricsStatus.Synced or LyricsStatus.Instrumental))
            BeginLyricsTrack(track);
        else lyricsIdentity = updated;
    }

    private void SetLyricsPanelActive(bool active)
    {
        lyricsSkeleton.SetActive(active && lyricsSkeleton.IsVisible);
        if (active && lyricsIdentity != null && lyricsResolvedFingerprint != lyricsIdentity.Fingerprint) StartLyricsLoad();
        if (!active) { CancelLyricsRequest(); lyricsFollowTimer.Stop(); smoothScrolls[lyricsScroll].Stop(); }
        else ResumeLyricsFollowing();
        foreach (var row in lyricsRows.Values) RefreshLyricsRow(row);
    }

    private void StartLyricsLoad()
    {
        if (!preferences.LyricsEnabled || lyricsIdentity == null || !expandedOpen || expandedPanelMode != PlayerPanel.Lyrics) return;
        CancelLyricsRequest();
        var request = lyricsRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var identity = lyricsIdentity; var generation = lyricsGeneration;
        lyricsResolvedFingerprint = null; lyricsResult = new(LyricsStatus.Loading); RenderLyrics();
        Run(async () =>
        {
            try
            {
                await Task.Delay(450, request.Token);
                if (!preferences.LyricsEnabled) return;
                var result = await lyricsService.ResolveAsync(identity, request.Token);
                if (disposed || !preferences.LyricsEnabled || request.IsCancellationRequested || lyricsRequest != request || lyricsGeneration != generation) return;
                lyricsResolvedFingerprint = result.Status == LyricsStatus.Unavailable ? null : identity.Fingerprint;
                ApplyLyrics(result);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { return; }
            finally { if (lyricsRequest == request) lyricsRequest = null; request.Dispose(); }
        });
    }
    private void CancelLyricsRequest() { lyricsRequest?.Cancel(); lyricsRequest = null; }
    private void ApplyLyrics(LyricsResult result)
    {
        lyricsResult = result; lyricsActiveLine = result.Document?.ActiveLine(lyricsPosition) ?? -1;
        RenderLyrics(resetScroll: true);
    }

    private void RenderLyrics(bool resetScroll = false)
    {
        if (lyricsList == null) return;
        lyricsFollowTarget = null;
        var document = lyricsResult.Document;
        lyricsMessage.Text = preferences.LyricsEnabled ? "Для текущего трека\nотсутствует текст" : "Тексты песен отключены\nв настройках";
        var hasText = lyricsResult.Status is LyricsStatus.Synced or LyricsStatus.Plain;
        var loading = lyricsResult.Status == LyricsStatus.Loading;
        resetScroll |= !hasText;
        if (resetScroll) smoothScrolls[lyricsScroll].Stop();
        lyricsList.IsVisible = lyricsFade.IsVisible = hasText;
        lyricsMessage.IsVisible = !hasText && !loading;
        if (hasText && document != null)
        {
            var rows = document.Lines.Length > 0 ? document.Lines.Select((line, index) => new LyricsDisplayRow(index, line.Text)) :
                document.PlainText.Replace("\r", "").Split('\n').Select((line, index) => new LyricsDisplayRow(index, line));
            lyricsItems.SetData(new[] { new LyricsDisplayRow(-1, "", lyricsSpacer) }.Concat(rows)
                .Append(new LyricsDisplayRow(-4, "", 24))
                .Append(new LyricsDisplayRow(-3, document.Source))
                .Append(new LyricsDisplayRow(-2, "", Math.Max(120, lyricsScroll.ActualHeight * .55))).Cast<object>());
        }
        else lyricsItems.SetData([]);
        if (resetScroll)
        {
            // Inserting into an empty virtual list can shift its anchor to the
            // end. Reset after publishing the new rows and replace any old seek.
            lyricsScroll.SetScrollOffsets(0, 0);
            if (hasText) lyricsList.ScrollIntoView(0);
        }
        foreach (var view in lyricsRows.Values) RefreshLyricsRow(view);
        if (loading)
        {
            lyricsReveal.Stop(); lyricsContent.Opacity = 0;
            lyricsSkeleton.Opacity = 1; lyricsSkeleton.IsVisible = true;
        }
        else if (lyricsWasLoading) lyricsReveal.Start();
        lyricsWasLoading = loading;
        lyricsSkeleton.SetActive(expandedOpen && expandedPanelMode == PlayerPanel.Lyrics && lyricsSkeleton.IsVisible);
    }

    private void RefreshLyricsRow(LyricsRowVisual view)
    {
        if (view.Row is not { } row || view.Credit?.IsVisible == true) { view.Dots.Reset(); return; }
        var opacity = row.Index == -3 ? .5 : row.Index < 0 ? 0 : view.Pressed ? .75 :
            view.Hovered || row.Index == lyricsActiveLine ? 1 : lyricsResult.Status == LyricsStatus.Plain ? .85 :
            Math.Abs(row.Index - lyricsActiveLine) == 1 ? .48 : .23;
        view.Text.Foreground = view.Dots.Foreground = Color.FromArgb((byte)Math.Round(opacity * 255), 255, 255, 255);
        view.Dots.SetActive(view.Dots.IsVisible && row.Index == lyricsActiveLine && expandedOpen && expandedPanelMode == PlayerPanel.Lyrics &&
            lyricsPlaying && isPlaying.Value && !playbackLoading.Value);
    }

    private void TickLyrics(double position, bool? playing = null)
    {
        lyricsPosition = position;
        if (playing.HasValue) lyricsPlaying = playing.Value;
        lyricsActiveLine = lyricsResult.Document?.ActiveLine(position) ?? -1;
        foreach (var view in lyricsRows.Values) RefreshLyricsRow(view);
    }

    private void UpdateLyricsFrame()
    {
        if (!expandedOpen || expandedPanelMode != PlayerPanel.Lyrics || lyricsScroll.ActualHeight <= 0) return;
        var font = Math.Clamp(lyricsScroll.ActualWidth / 15, 28, 42);
        var spacer = Math.Max(80, lyricsScroll.ActualHeight * .35);
        if (Math.Abs(font - lyricsFontSize) > .5 || Math.Abs(spacer - lyricsSpacer) > 1)
        {
            lyricsFontSize = font; lyricsSpacer = spacer; lyricsMessage.FontSize = font; RenderLyrics();
            foreach (var view in lyricsRows.Values) if (view.Row?.Index != -3) { view.Text.FontSize = font; view.Text.InvalidateMeasure(); }
            lyricsList.InvalidateMeasure(); return;
        }
        if (!preferences.LyricsAutoScroll || !lyricsFollow || lyricsResult.Status != LyricsStatus.Synced) return;
        var index = Math.Max(0, lyricsActiveLine);
        var selected = lyricsRows.FirstOrDefault(pair => pair.Value.Row?.Index == index).Key;
        var scroll = smoothScrolls[lyricsScroll];
        if (selected == null)
        {
            scroll.Stop();
            lyricsFollowTarget = null;
            lyricsList.ScrollIntoView(index + 1);
            return;
        }
        var scale = Window.DpiScale;
        // Row bounds already include the presenter's pixel-snapped scroll offset.
        // Adding the fractional offset back makes the target move as the spring advances.
        var renderedOffset = LayoutRounding.RoundToPixel(lyricsScroll.VerticalOffset, scale);
        var position = renderedOffset + (selected.Bounds.Y - lyricsScroll.Bounds.Y) + selected.ActualHeight / 2 - lyricsScroll.ActualHeight * .4;
        position = Math.Clamp(LayoutRounding.RoundToPixel(position, scale), 0, SmoothScroll.Maximum(lyricsScroll));
        if (lyricsFollowTarget is { } previous && Math.Abs(previous - position) * scale < 1.5 &&
            (scroll.IsRunning || Math.Abs(lyricsScroll.VerticalOffset - position) * scale < 1.5)) return;
        lyricsFollowTarget = position;
        scroll.ScrollTo(position);
    }

    private void PauseLyricsFollowing()
    {
        if (!preferences.LyricsAutoScroll || lyricsResult.Status != LyricsStatus.Synced) return;
        lyricsFollow = false;
        lyricsFollowTarget = null;
        lyricsFollowTimer.Stop();
        lyricsFollowTimer.Interval = NearbyLyricsFollowDelay;
        lyricsFollowTimer.Start();
    }

    private void CheckLyricsFollowDelay()
    {
        // Check the arranged viewport after the wheel motion has had time to settle.
        if (lyricsFollowTimer.Interval == NearbyLyricsFollowDelay && !IsActiveLyricVisible())
        {
            lyricsFollowTimer.Interval = LyricsFollowDelay - NearbyLyricsFollowDelay;
            return;
        }
        ResumeLyricsFollowing();
    }

    private bool IsActiveLyricVisible()
    {
        if (lyricsActiveLine < 0) return false;
        foreach (var (button, view) in lyricsRows)
        {
            if (view.Row?.Index != lyricsActiveLine) continue;
            var ink = view.Dots.IsVisible ? view.Dots.Bounds : view.Text.Bounds;
            var visible = lyricsScroll.Bounds.Intersect(ink);
            // A recycled viewport can retain the focused row at its old bounds.
            // Hit testing excludes such rows, even if their bounds still overlap.
            if (!visible.IsEmpty && lyricsScroll.HitTest(visible.Center) is { } hit &&
                (ReferenceEquals(hit, button) || button.IsAncestorOf(hit))) return true;
        }
        return false;
    }

    private void ResumeLyricsFollowing()
    {
        lyricsFollowTimer.Stop(); lyricsFollow = preferences.LyricsEnabled && preferences.LyricsAutoScroll;
        lyricsFollowTarget = null;
        // Paused playback can leave the render loop idle when the timer expires.
        // Request the frame that starts centering, even with no moving controls.
        if (expandedOpen && expandedPanelMode == PlayerPanel.Lyrics) Window.InvalidateVisual();
    }
    private void SeekLyricsLine(int index)
    {
        if (!audioReady || lyricsResult.Status != LyricsStatus.Synced || lyricsResult.Document is not { } document || index < 0 || index >= document.Lines.Length) return;
        ResumeLyricsFollowing();
        progress.Value = Math.Clamp(document.Lines[index].Start, 0, progress.Maximum); FlushSeek();
    }

    private void DisposeLyrics()
    {
        CancelLyricsRequest(); lyricsFollowTimer.Dispose();
        lyricsReveal.Stop();
        Window.FrameRendered -= UpdateLyricsFrame;
        lyricsSkeleton.SetActive(false); lyricsFade.Dispose();
        foreach (var row in lyricsRows.Values) row.Dots.Reset();
        lyricsService.Dispose(); lyricsHttp.Dispose();
    }
}
