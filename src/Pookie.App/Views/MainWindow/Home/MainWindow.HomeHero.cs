using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Border homeHero = null!;
    private Button wavePlay = null!, waveReject = null!, waveSettingsButton = null!;
    private TextBlock wavePlayLabel = null!;
    private Image wavePlayGlyph = null!;
    private WaveBackdrop waveBackdrop = null!;
    private bool homeHeroPlaying;
    private long homeHeroPlayRequest;

    private FrameworkElement HomeHeroView()
    {
        waveBackdrop = new WaveBackdrop();
        wavePlayGlyph = Icons.View("play-solid", 24, TrackButtons.OnPrimary).CenterVertical();
        wavePlayLabel = new TextBlock().Text("Слушать").FontSize(16).SemiBold().Foreground(TrackButtons.OnPrimary).CenterVertical();
        wavePlay = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(wavePlayGlyph, wavePlayLabel),
            () => Run(PlayHomeHeroAsync), TrackButtons.Filled, 56).Padding(24, 0);
        waveReject = TrackButtons.Action(new TextBlock().Text("Не моё").FontSize(13).SemiBold(),
            () => Run(RejectWaveTrackAsync), TrackButtons.Glass, 36);
        waveSettingsButton = TrackButtons.Icon(Icons.View("gear", 24),
            () => { EnsureWaveTaste(); RefreshWaveSettings(); waveSettingsOpen.Value = !waveSettingsOpen.Value; }, TrackButtons.Glass, 56)
            .ToolTip("Настроить волну");
        homeHero = new Border().Height(320).CornerRadius(28).ClipToBounds().Margin(0, 0, 18, 0).Child(
            new Grid().Columns("*").Rows("*").Children(waveBackdrop,
                new StackPanel().Vertical().Spacing(14).Center().Children(
                    new TextBlock().Text("Твоя волна").FontSize(38).Bold().CenterHorizontal(),
                    TrackButtons.Connected(wavePlay, waveSettingsButton).CenterHorizontal(),
                    waveReject.CenterHorizontal())));
        return homeHero;
    }
    private void RenderHomeHero()
    {
        if (homeHero == null) return;
        SeedWaveFromHome();
        homeHero.IsVisible = true;
        RefreshHomeHeroPlayback();
    }
    private async Task PlayHomeHeroAsync()
    {
        if (homeHeroPlaying) return;
        if (playbackQueue.Context?.Key == WaveQueueKey && current != null) { await ToggleAsync(); return; }
        SeedWaveFromHome();
        var request = ++homeHeroPlayRequest;
        var session = api.Session; var generation = playGeneration; var sourceVersion = playbackQueue.SourceVersion;
        homeHeroPlaying = true; RefreshHomeHeroPlayback();
        try
        {
            if (waveTaste.Rank(waveCandidates.Values, [], 20, DateTimeOffset.UtcNow).Length < 3)
                await FetchWaveCandidatesAsync(lifetime.Token);
            if (disposed || request != homeHeroPlayRequest || api.Session != session || generation != playGeneration || sourceVersion != playbackQueue.SourceVersion) return;
            var ranked = waveTaste.Rank(waveCandidates.Values, [], 20, DateTimeOffset.UtcNow);
            if (ranked.Length == 0)
            {
                status.Value = waveTaste.Mode == WaveMode.Favorites ? "Поставь лайки любимым трекам, чтобы запустить эту волну." :
                    "Пока не удалось подобрать музыку. Попробуй режим «Баланс» или послушай несколько треков.";
                return;
            }
            StartQueueContext(new(WaveQueueKey, "Твоя волна", "Сейчас играет твоя волна", "pookie:wave:" + ++waveCursor,
                Kind: PlaybackContextKind.Wave), ranked, ranked[0]);
            await PlayAsync(ranked[0]);
            Run(async () =>
            {
                var waveVersion = playbackQueue.SourceVersion;
                await FetchWaveCandidatesAsync(lifetime.Token);
                if (!disposed && request == homeHeroPlayRequest && api.Session == session && playbackQueue.SourceVersion == waveVersion)
                    RerankWaveQueue();
            });
        }
        finally { if (request == homeHeroPlayRequest) { homeHeroPlaying = false; if (!disposed) RefreshHomeHeroPlayback(); } }
    }
    private void RefreshHomeHeroPlayback()
    {
        if (homeHero == null) return;
        var selected = playbackQueue.Context?.Key == WaveQueueKey && current != null;
        var playing = selected && isPlaying.Value;
        wavePlayLabel.Text = homeHeroPlaying ? "Подбираем музыку…" : playing ? "Пауза" : "Слушать";
        wavePlayGlyph.Source = Icons.Source(playing ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
        TrackButtons.SetAvailability(wavePlay, !homeHeroPlaying && (selected || waveCandidates.Count > 0 || HomeItems("mixes").Length > 0), homeHeroPlaying);
        waveReject.IsVisible = selected;
        RefreshWaveMotion();
    }
    private void RefreshWaveMotion()
    {
        if (waveBackdrop == null) return;
        var visible = !disposed && !expandedOpen && page.Value == Page.Home;
        waveBackdrop.SetActive(visible && playbackQueue.Context?.Key == WaveQueueKey && isPlaying.Value && !playbackLoading.Value, waveTaste.Mode, visible);
    }
    private void ResetHomeHero()
    {
        ++homeHeroPlayRequest; homeHeroPlaying = false;
        waveBackdrop?.SetActive(false, waveTaste.Mode, visible: false);
    }
}
