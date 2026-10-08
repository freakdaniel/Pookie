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
    private DispatcherTimer lyricsFollowTimer = null!;
    private static readonly TimeSpan LyricsFollowDelay = TimeSpan.FromSeconds(7);
    private readonly PaginationItems lyricsItems = new(LoadingRowStyle.Compact, item => ((LyricsDisplayRow)item).Index);
    private sealed record LyricsDisplayRow(int Index, string Text, double Spacer = 0);
    private sealed class LyricsRowVisual(TextBlock text, LyricsWaveDots dots)
    {
        internal TextBlock Text = text;
        internal LyricsWaveDots Dots = dots;
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
        lyricsService = new(new LrclibClient(lyricsHttp), Path.Combine(dataPaths.Cache, "Lyrics"));
        lyricsFollowTimer = new(LyricsFollowDelay);
        lyricsFollowTimer.Tick += ResumeLyricsFollowing;
    }

    private static LyricsIdentity IdentifyLyrics(SoundCloudTrack track)
    {
        var full = track.FullDuration > 0 ? track.FullDuration / 1000d : track.DurationSeconds;
        var timing = !(track.FullDuration > track.Duration + 2000 ||
            track.Media?.Transcodings is { Length: > 0 } transcodings && transcodings.All(item => item.Snipped));
        var artist = string.IsNullOrWhiteSpace(track.PublisherMetadata?.Artist) ? track.Author : track.PublisherMetadata.Artist;
        return new(track.Id, track.Title, artist, full, track.PublisherMetadata?.AlbumTitle, timing);
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
            var dots = new LyricsWaveDots().Width(90).Height(60).CenterHorizontal().CenterVertical();
            var content = new Grid().Columns("*").Rows("*").Children(text, dots);
            Button button = null!;
            button = new Button().Background(Color.Transparent).BorderThickness(0).Padding(12, 18).Content(content)
                .OnClick(() => { if (lyricsRows[button].Row is { Index: >= 0 } row) SeekLyricsLine(row.Index); })
                .OnMouseEnter(() => { lyricsRows[button].Hovered = true; RefreshLyricsRow(lyricsRows[button]); })
                .OnMouseLeave(() => { lyricsRows[button].Hovered = lyricsRows[button].Pressed = false; RefreshLyricsRow(lyricsRows[button]); });
            button.MouseDown += args => { if (args.Button == MouseButton.Left) { lyricsRows[button].Pressed = true; RefreshLyricsRow(lyricsRows[button]); } };
            button.MouseUp += _ => { lyricsRows[button].Pressed = false; RefreshLyricsRow(lyricsRows[button]); };
            text.Transitions = [Transition.Create(UIElement.OpacityProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
            dots.Transitions = [Transition.Create(UIElement.OpacityProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
            lyricsRows[button] = new(text, dots);
            context.Register("button", button);
            return button;
        }, (_, row, _, context) =>
        {
            var button = context.Get<Button>("button"); var view = lyricsRows[button];
            view.Hovered = button.IsMouseOver; view.Pressed = false; view.Row = row;
            view.Text.FontSize = row.Index == -3 ? 13 : lyricsFontSize;
            view.Text.FontWeight = row.Index == -3 ? FontWeight.Normal : FontWeight.Bold;
            view.Text.Text = row.Text;
            var gap = row.Index >= 0 && row.Text.Length == 0 && lyricsResult.Status == LyricsStatus.Synced;
            view.Text.IsVisible = !gap; view.Dots.IsVisible = gap;
            button.Height = row.Spacer > 0 ? row.Spacer : double.NaN;
            button.IsHitTestVisible = button.Focusable = row.Index >= 0 && lyricsResult.Status == LyricsStatus.Synced;
            button.Cursor = button.IsHitTestVisible ? CursorType.Hand : null;
            RefreshLyricsRow(view);
        }, (_, _, _, context) =>
        {
            var button = context.Get<Button>("button"); var view = lyricsRows[button];
            button.IsHitTestVisible = button.Focusable = false;
            view.Row = null; view.Hovered = view.Pressed = false; view.Dots.Reset();
        });
        lyricsScroll = (ScrollViewer)lyricsList.FindVisualChild<ScrollViewer>()!;
        ((IVisualTreeHost)lyricsScroll).VisitChildren(element =>
        { if (element is ScrollBar bar) { bar.Opacity = 0; bar.IsHitTestVisible = false; } return true; });
        smoothScrolls[lyricsScroll] = new SmoothScroll(lyricsScroll);
        lyricsScroll.MouseWheel += args => { if (args.Delta.Y != 0) PauseLyricsFollowing(); };
        lyricsScroll.KeyDown += args =>
        { if (args.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) PauseLyricsFollowing(); };
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

    private void BeginLyricsTrack(SoundCloudTrack track)
    {
        CancelLyricsRequest(); lyricsGeneration++;
        lyricsIdentity = IdentifyLyrics(track); lyricsResolvedFingerprint = null;
        lyricsResult = new(LyricsStatus.Loading); lyricsPosition = 0; lyricsActiveLine = -1; lyricsPlaying = false;
        ResumeLyricsFollowing(); RenderLyrics();
        if (expandedOpen && expandedPanelMode == PlayerPanel.Lyrics) StartLyricsLoad();
    }

    private void RefreshLyricsMetadata(SoundCloudTrack track)
    {
        if (lyricsIdentity?.Fingerprint != IdentifyLyrics(track).Fingerprint) BeginLyricsTrack(track);
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
        if (lyricsIdentity == null || !expandedOpen || expandedPanelMode != PlayerPanel.Lyrics) return;
        CancelLyricsRequest();
        var request = lyricsRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var identity = lyricsIdentity; var generation = lyricsGeneration;
        lyricsResolvedFingerprint = null; lyricsResult = new(LyricsStatus.Loading); RenderLyrics();
        Run(async () =>
        {
            try
            {
                await Task.Delay(450, request.Token);
                var result = await lyricsService.ResolveAsync(identity, request.Token);
                if (disposed || request.IsCancellationRequested || lyricsRequest != request || lyricsGeneration != generation) return;
                lyricsResolvedFingerprint = result.Status == LyricsStatus.Unavailable ? null : identity.Fingerprint;
                ApplyLyrics(result);
            }
            catch (OperationCanceledException) { }
            finally { if (lyricsRequest == request) lyricsRequest = null; request.Dispose(); }
        });
    }
    private void CancelLyricsRequest() { lyricsRequest?.Cancel(); lyricsRequest = null; }
    private void ApplyLyrics(LyricsResult result)
    {
        lyricsResult = result; lyricsActiveLine = result.Document?.ActiveLine(lyricsPosition) ?? -1;
        RenderLyrics();
    }

    private void RenderLyrics()
    {
        if (lyricsList == null) return;
        var document = lyricsResult.Document;
        var hasText = lyricsResult.Status is LyricsStatus.Synced or LyricsStatus.Plain;
        var loading = lyricsResult.Status == LyricsStatus.Loading;
        lyricsList.IsVisible = lyricsFade.IsVisible = hasText;
        lyricsMessage.IsVisible = !hasText && !loading;
        if (hasText && document != null)
        {
            var rows = document.Lines.Length > 0 ? document.Lines.Select((line, index) => new LyricsDisplayRow(index, line.Text)) :
                document.PlainText.Replace("\r", "").Split('\n').Select((line, index) => new LyricsDisplayRow(index, line));
            lyricsItems.SetData(new[] { new LyricsDisplayRow(-1, "", lyricsSpacer) }.Concat(rows)
                .Append(new LyricsDisplayRow(-4, "", 24))
                .Append(new LyricsDisplayRow(-3, "Текст предоставлен " + document.Source))
                .Append(new LyricsDisplayRow(-2, "", Math.Max(120, lyricsScroll.ActualHeight * .55))).Cast<object>());
        }
        else lyricsItems.SetData([]);
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
        if (view.Row is not { } row) { view.Dots.Reset(); return; }
        var opacity = row.Index == -3 ? .5 : row.Index < 0 ? 0 : view.Pressed ? .75 :
            view.Hovered || row.Index == lyricsActiveLine ? 1 : lyricsResult.Status == LyricsStatus.Plain ? .85 :
            Math.Abs(row.Index - lyricsActiveLine) == 1 ? .48 : .23;
        view.Text.Opacity = view.Dots.Opacity = opacity;
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
        if (!lyricsFollow || lyricsResult.Status != LyricsStatus.Synced) return;
        var index = Math.Max(0, lyricsActiveLine);
        var selected = lyricsRows.FirstOrDefault(pair => pair.Value.Row?.Index == index).Key;
        if (selected == null) { lyricsList.ScrollIntoView(index + 1); return; }
        var position = lyricsScroll.VerticalOffset + selected.Bounds.Y + selected.ActualHeight / 2 - lyricsScroll.Bounds.Y - lyricsScroll.ActualHeight * .4;
        smoothScrolls[lyricsScroll].ScrollTo(position);
    }

    private void PauseLyricsFollowing()
    {
        if (lyricsResult.Status != LyricsStatus.Synced) return;
        lyricsFollow = false;
        lyricsFollowTimer.Stop(); lyricsFollowTimer.Start();
    }
    private void ResumeLyricsFollowing()
    {
        lyricsFollowTimer.Stop(); lyricsFollow = true;
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
