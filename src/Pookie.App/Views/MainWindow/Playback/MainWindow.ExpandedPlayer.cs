using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum PlayerPanel { None, Lyrics, Queue }
    private readonly PlayerBackdrop expandedBackdrop = new();
    private readonly AnimationClock expandedAnimation = new(TimeSpan.FromMilliseconds(380), Easing.CubicBezier(.16, 1, .3, 1));
    private readonly AnimationClock panelAnimation = new(TimeSpan.FromMilliseconds(460), PanelSpring);
    private readonly ObservableValue<bool> expandedQueueActive = new(false), expandedLyricsActive = new(false);
    private readonly BufferedTrack expandedBuffer = new();
    private readonly LoadingTrack expandedLoading = new();
    private Grid expandedHost = null!;
    private ExpandedPlayerLayout expandedLayout = null!;
    private Border expandedCover = null!;
    private Image expandedArtwork = null!;
    private CoverGlassBackdrop expandedGlassBackdrop = null!;
    private Slider expandedProgress = null!;
    private TextBlock expandedPositionLabel = null!, expandedDurationLabel = null!;
    private ExpandedGlassSurface expandedVolume = null!;
    private Slider expandedVolumeSlider = null!;
    private ItemsControl expandedQueue = null!;
    private readonly Dictionary<Grid, CompactTrackRow> expandedQueueRows = [];
    private SoundCloudTrack[] expandedUpcoming = [];
    private TransitionContentControl expandedPanel = null!;
    private FrameworkElement expandedQueueContent = null!, expandedLyricsContent = null!;
    private bool expandedOpen, syncingExpandedProgress;
    private PlayerPanel expandedPanelMode;
    private double expandedReveal, expandedFrom, expandedTo, panelFrom, panelTo;
    private double panelReveal, panelRevealFrom;

    private static double PanelSpring(double time)
    {
        // Settle within a fraction of a device pixel before the endpoint. The
        // framework's spring normalizes to four decay constants, leaving a
        // visible endpoint jump for a panel that travels several hundred pixels.
        static double Position(double t) => 1 - Math.Exp(-9.1 * t) * (Math.Cos(10.639 * t) + 9.1 / 10.639 * Math.Sin(10.639 * t));
        return Position(Math.Clamp(time, 0, 1)) / Position(1);
    }

    private FrameworkElement ExpandedPlayer()
    {
        expandedArtwork = new Image().Source(artwork.Source).StretchMode(Stretch.UniformToFill);
        expandedGlassBackdrop = new CoverGlassBackdrop(expandedArtwork);
        var transport = new Grid().Columns("*").Rows("*");
        transport.Opacity = 0;
        transport.IsHitTestVisible = false;
        transport.Transitions = [Transition.Create(UIElement.OpacityProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
        var coverControls = new StackPanel().Horizontal().Spacing(22).CenterHorizontal().CenterVertical().Children(
            ExpandedIconButton("skip-back-solid", () => Run(() => SkipAsync(-1)), 42),
            ExpandedActionButton(new Grid().Columns("*").Rows("*").Children(
                    Icons.View("play-solid", 28, Color.White).Center().BindIsVisible(isPlaying, value => !value),
                    Icons.View("pause-solid", 28, Color.White).Center().BindIsVisible(isPlaying)),
                () => Run(ToggleAsync), 60),
            ExpandedIconButton("skip-forward-solid", () => Run(() => SkipAsync(1)), 42));
        var like = ExpandedActionButton(new Grid().Columns("*").Rows("*").Children(
                Icons.View("heart", 24, Color.White).Center().BindIsVisible(isLiked, value => !value),
                Icons.View("heart-filled", 24, TrackButtons.OnSelected).Center().BindIsVisible(isLiked)),
            () => Run(ToggleLikeAsync), 38, isLiked).BindIsEnabled(likeAvailable).ToolTip("Нравится");
        var dislike = ExpandedActionButton(new Grid().Columns("*").Rows("*").Children(
                Icons.View("thumbs-down", 24, Color.White).Center().BindIsVisible(isDisliked, value => !value),
                Icons.View("thumbs-down-filled", 24, TrackButtons.OnSelected).Center().BindIsVisible(isDisliked)),
            () => Run(RejectWaveTrackAsync), 38, isDisliked).BindIsEnabled(likeAvailable)
            .ToolTip("Не нравится — исключить из волны · Нажми ещё раз, чтобы отменить");
        ((ExpandedGlassButton)like).Connect(true); ((ExpandedGlassButton)dislike).Connect(false);
        transport.Children(
            new Border().Background(Color.FromArgb(100, 0, 0, 0)).IsHitTestVisible(false),
            coverControls,
            ExpandedVolumeControl().Left().Top().Margin(14),
            ExpandedActionButton(RepeatIcon(21), CycleRepeat, 38, repeatActive).CenterHorizontal().Top().Margin(14),
            ExpandedIconButton("queue", () => ToggleExpandedPanel(PlayerPanel.Queue), 38, expandedQueueActive)
                .Right().Top().Margin(14),
            new Grid().Columns("*,*,*").Rows("*").Height(42).Bottom().Margin(16).Children(
                ExpandedIconButton("shuffle", () => shuffle.Value = !shuffle.Value, 38, shuffle).Left().Column(0),
                ExpandedIconButton("text-align-left", () => ToggleExpandedPanel(PlayerPanel.Lyrics), 38, expandedLyricsActive).CenterHorizontal().Column(1),
                new StackPanel().Horizontal().Spacing(2).Right().Column(2).Children(like, dislike)));
        expandedCover = new Border().Background(Raised).CornerRadius(10).ClipToBounds()
            .Child(new Grid().Columns("*").Rows("*").Children(expandedArtwork, transport));
        hoverReveals.Add(new HoverReveal(expandedCover, visible =>
            { transport.IsHitTestVisible = visible; transport.Opacity = visible ? 1 : 0; },
            () => expandedVolumeSlider.IsMouseCaptured));

        expandedProgress = ThinSlider().Background(Color.Transparent).Minimum(0).Maximum(1)
            .OnMouseDown(e => { if (e.Button == MouseButton.Left) BeginSeekDrag(); })
            .OnMouseUp(e => { if (e.Button == MouseButton.Left) EndSeekDrag(); })
            .OnValueChanged(value =>
            {
                if (!syncingExpandedProgress) progress.Value = value;
            });
        var card = new StackPanel().Vertical().Spacing(0).Children(
            expandedCover,
            new StackPanel().Vertical().Spacing(5).Margin(0, 22, 0, 20).Children(
                new TextBlock().BindText(title).Foreground(Color.White).FontSize(20).Bold().CenterHorizontal()
                    .TextTrimming(TextTrimming.CharacterEllipsis),
                new TextBlock().BindText(artist).Foreground(PlayerSecondaryText).FontSize(14).CenterHorizontal()
                    .TextTrimming(TextTrimming.CharacterEllipsis)),
            new Grid().Columns("*").Rows("12,24").Children(
                new Grid().Columns("*").Rows("*").Row(0).Children(expandedBuffer, expandedProgress, expandedLoading),
                new Grid().Columns("*,*").Rows("*").Row(1).Children(
                    new TextBlock().BindText(currentTime).FontSize(11).Foreground(PlayerSecondaryText).Column(0).Ref(out expandedPositionLabel),
                    new TextBlock().BindText(totalTime).FontSize(11).Foreground(PlayerSecondaryText).Right().Column(1).Ref(out expandedDurationLabel))));
        foreach (var label in new[] { expandedPositionLabel, expandedDurationLabel })
        {
            label.Opacity = 0;
            label.IsHitTestVisible = false;
            label.Transitions = [Transition.Create(UIElement.OpacityProperty, 200, Easing.CubicBezier(.2, 0, 0, 1))];
        }
        hoverReveals.Add(new HoverReveal(expandedProgress, visible =>
            { expandedPositionLabel.Opacity = expandedDurationLabel.Opacity = visible ? 1 : 0; },
            () => expandedProgress.IsMouseCaptured));

        expandedQueueContent = ExpandedQueuePanel();
        expandedLyricsContent = LyricsPanel();
        expandedPanel = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Up, durationMs: 280)
        };
        var panelViewport = new Border().ClipToBounds().Child(expandedPanel);
        var cardLayer = new PlayerMotionLayer(card);
        var panelLayer = new PlayerMotionLayer(panelViewport);
        expandedLayout = new ExpandedPlayerLayout(cardLayer, panelLayer, expandedCover);
        expandedLayout.Add(cardLayer); expandedLayout.Add(panelLayer);
        expandedHost = new Grid().Columns("*").Rows("84,*").ClipToBounds().Children(
            expandedBackdrop.Row(0).RowSpan(2),
            new Grid().Columns("*,Auto").Rows("*").Padding(36, 20).Row(0).Children(
                Icons.LogoView(26, 25).Left().CenterVertical().Column(0),
                ExpandedActionButton(Icons.View("x", 24, Color.White), () => SetExpandedPlayer(false), 44, coverGlass: false).Right().Column(1)),
            expandedLayout.Row(1).Margin(24, 0, 24, 32));
        expandedHost.Opacity = 0;
        expandedHost.IsVisible = false;
        expandedHost.IsHitTestVisible = false;
        expandedAnimation.TickCallback = amount =>
        {
            expandedReveal = expandedFrom + (expandedTo - expandedFrom) * amount;
            expandedHost.Opacity = expandedReveal;
            expandedLayout.SetMotion(expandedReveal, expandedLayout.SideAmount, panelReveal);
        };
        expandedAnimation.CompletedCallback = () =>
        {
            expandedReveal = expandedTo;
            expandedHost.Opacity = expandedReveal;
            expandedLayout.SetMotion(expandedReveal, expandedLayout.SideAmount, panelReveal);
            if (expandedOpen) { workspace.IsVisible = false; return; }
            expandedHost.IsVisible = expandedHost.IsHitTestVisible = false;
            if (overviewRefreshPending) RefreshOverviewSections();
            workspace.IsHitTestVisible = workspace.IsEnabled;
            expandedLoading.SetLoading(false);
            playerArtworkButton.Focus();
            playerArtworkOverlay.Opacity = 0;
        };
        panelAnimation.TickCallback = amount =>
        {
            panelReveal = Math.Clamp(panelRevealFrom + (panelTo - panelRevealFrom) * amount, 0, 1);
            expandedLayout.SetMotion(expandedReveal, panelFrom + (panelTo - panelFrom) * amount, panelReveal);
        };
        panelAnimation.CompletedCallback = () =>
        {
            panelReveal = panelTo > 0 ? 1 : 0;
            expandedLayout.SetMotion(expandedReveal, panelTo, panelReveal);
        };
        progress.OnValueChanged(_ => SyncExpandedTimeline());
        playerVisible.Changed += () => { if (!playerVisible.Value) ResetExpandedPlayer(); };
        return expandedHost;
    }

    private ExpandedGlassSurface ExpandedVolumeControl()
    {
        expandedVolumeSlider = ThinSlider().Minimum(0).Maximum(100).BindValue(volume).Width(100).CenterVertical();
        expandedVolumeSlider.Opacity = 0;
        expandedVolumeSlider.IsEnabled = false;
        expandedVolumeSlider.Transitions = [.. expandedVolumeSlider.Transitions ?? [],
            Transition.Create(UIElement.OpacityProperty, 170, Easing.CubicBezier(.2, 0, 0, 1))];
        expandedVolume = new ExpandedGlassSurface(expandedGlassBackdrop).Background(Color.Transparent).BorderThickness(0).CornerRadius(19)
            .Width(38).Height(38).Content(new Grid().Columns("38,*").Rows("*").Children(
                ExpandedActionButton(new Grid().Columns("*").Rows("*").Children(
                    Icons.View("speaker-high", 20, Color.White).Center().BindIsVisible(muted, value => !value),
                    Icons.View("speaker-slash", 20, Color.White).Center().BindIsVisible(muted)), ToggleMute, 38, paintSurface: false).Column(0),
                expandedVolumeSlider.Margin(8, 0, 14, 0).Column(1)));
        expandedVolume.Transitions = [Transition.Create(FrameworkElement.WidthProperty, 250, Easing.CubicBezier(.2, 0, 0, 1))];
        hoverReveals.Add(new HoverReveal(expandedVolume, SetExpandedVolumeOpen, () => expandedVolumeSlider.IsMouseCaptured));
        return expandedVolume;
    }

    private void SetExpandedVolumeOpen(bool open)
    {
        expandedVolume.Width = open ? 160 : 38;
        expandedVolumeSlider.Opacity = open ? 1 : 0;
        expandedVolumeSlider.IsEnabled = open;
    }

    private Button ExpandedIconButton(string icon, Action action, double size, ObservableValue<bool>? active = null)
        => ExpandedActionButton(Icons.View(icon, size > 30 ? 24 : 18, Color.White), action, size, active);

    private Button ExpandedActionButton(FrameworkElement glyph, Action action, double size, ObservableValue<bool>? active = null,
        bool paintSurface = true, bool coverGlass = true)
    {
        var button = new ExpandedGlassButton(coverGlass ? expandedGlassBackdrop : null, paintSurface).Initialize(glyph)
            .Width(size).Height(size).CornerRadius(size / 2).CenterVertical().OnClick(action);
        void Refresh() => button.SetActive(active?.Value == true);
        if (active != null) active.Changed += Refresh;
        Refresh();
        return button;
    }

    private void SetExpandedPlayer(bool open)
    {
        if (disposed || expandedOpen == open || open && (!playerVisible.Value || current == null)) return;
        expandedAnimation.Stop();
        expandedOpen = open;
        RefreshWaveMotion();
        SetLyricsPanelActive(open && expandedPanelMode == PlayerPanel.Lyrics);
        if (open)
        {
            waveSettingsOpen.Value = false;
            if (expandedPanelMode != PlayerPanel.None) SetExpandedPanelContent(expandedPanelMode, animate: false);
            if (!expandedHost.IsVisible)
            {
                expandedPositionLabel.Opacity = expandedDurationLabel.Opacity = 0;
            }
            expandedHost.IsVisible = expandedHost.IsHitTestVisible = true;
            workspace.IsHitTestVisible = false;
            queueOpen.Value = profileOpen.Value = false;
            playerArtworkOverlay.Opacity = 0;
            SyncExpandedTimeline(); RefreshExpandedQueue();
        }
        else { workspace.IsVisible = true; CancelSeek(); SetExpandedVolumeOpen(false); }
        expandedFrom = expandedReveal;
        expandedTo = open ? 1 : 0;
        expandedAnimation.Start();
    }

    private void ToggleExpandedPanel(PlayerPanel mode)
    {
        var next = expandedPanelMode == mode ? PlayerPanel.None : mode;
        if (next != PlayerPanel.None && next != expandedPanelMode)
            SetExpandedPanelContent(next, animate: expandedOpen && expandedPanelMode != PlayerPanel.None);
        expandedPanelMode = next;
        expandedQueueActive.Value = next == PlayerPanel.Queue;
        expandedLyricsActive.Value = next == PlayerPanel.Lyrics;
        panelAnimation.Stop();
        BeginExpandedPanelMotion(next != PlayerPanel.None);
        if (next == PlayerPanel.Queue) RefreshExpandedQueue();
        SetLyricsPanelActive(next == PlayerPanel.Lyrics && expandedOpen);
    }

    private void SetExpandedPanelContent(PlayerPanel mode, bool animate)
    {
        // A hidden panel has no visible outgoing page. Its reveal belongs to the
        // outer player motion, not a slide from the last retained panel content.
        if (!animate)
        {
            expandedPanel.Transition = ContentTransition.CreateNone();
            expandedPanel.Content = null;
        }
        expandedPanel.Content = mode == PlayerPanel.Queue ? expandedQueueContent : expandedLyricsContent;
        expandedPanel.Transition = ContentTransition.CreateSlide(SlideDirection.Up, durationMs: 280);
    }

    private void BeginExpandedPanelMotion(bool open)
    {
        panelFrom = expandedLayout.SideAmount;
        panelTo = open ? 1 : 0;
        panelRevealFrom = panelReveal;
        expandedLayout.SetSideTarget(panelTo);
        panelAnimation.Duration = TimeSpan.FromMilliseconds(460);
        panelAnimation.Start();
    }

    private void SyncExpandedTimeline()
    {
        if (expandedProgress == null || !expandedHost.IsVisible || syncingExpandedProgress) return;
        syncingExpandedProgress = true;
        try { expandedProgress.Maximum = progress.Maximum; expandedProgress.Value = progress.Value; }
        finally { syncingExpandedProgress = false; }
        expandedLoading.IsVisible = loadingTrack.IsVisible;
        expandedLoading.SetLoading(loadingTrack.IsVisible);
        expandedProgress.IsVisible = expandedBuffer.IsVisible = !loadingTrack.IsVisible;
    }

    private void ResetExpandedPlayer()
    {
        SetLyricsPanelActive(false);
        expandedAnimation.Stop(); panelAnimation.Stop();
        if (expandedHost.IsVisible) workspace.IsVisible = true;
        expandedOpen = false; expandedReveal = expandedFrom = expandedTo = panelFrom = panelTo = 0;
        expandedPanelMode = PlayerPanel.None;
        expandedQueueActive.Value = expandedLyricsActive.Value = false;
        expandedPositionLabel.Opacity = expandedDurationLabel.Opacity = 0;
        SetExpandedVolumeOpen(false);
        panelReveal = panelRevealFrom = 0;
        expandedLayout.SetSideTarget(0);
        expandedLayout.SetMotion(0, 0, 0);
        expandedHost.Opacity = 0;
        expandedHost.IsVisible = expandedHost.IsHitTestVisible = false;
        if (!disposed && overviewRefreshPending) RefreshOverviewSections();
        expandedLoading.SetLoading(false); expandedBuffer.Reset(); expandedBackdrop.Reset();
        expandedGlassBackdrop?.Reset();
        workspace.IsHitTestVisible = workspace.IsEnabled;
    }
}
