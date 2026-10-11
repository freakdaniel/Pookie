using System.Diagnostics;
using Aprillz.MewUI;
using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const string WaveQueueKey = "home:wave";
    private WaveTaste waveTaste = new();
    private WaveProfileStore waveStore = null!;
    private readonly WaveListening waveListening = new();
    private readonly Stopwatch waveTime = Stopwatch.StartNew();
    private DispatcherTimer waveSaveTimer = null!;
    private long waveAccount, waveGeneration, waveCursor;
    private readonly Dictionary<long, SoundCloudTrack> waveCandidates = [];
    private readonly Dictionary<long, (string? Cursor, DateTimeOffset Fetched)> waveRelated = [];
    private readonly HashSet<string> waveMixesLoaded = [];
    private Task? waveCandidateFetch;
    private string? waveLikesCursor;

    private void InitializeWave()
    {
        var directory = Path.Combine(dataPaths.Root, "Wave");
        Storage.AppDataPaths.CreatePrivateDirectory(directory);
        waveStore = new(directory);
        waveSaveTimer = new DispatcherTimer(TimeSpan.FromSeconds(2));
        waveSaveTimer.Tick += SaveWaveProfile;
        page.Changed += RefreshWaveMotion;
    }
    private void EnsureWaveTaste()
    {
        var account = me?.Id ?? 0;
        if (account == waveAccount) return;
        ClearWaveData(); waveAccount = account;
        if (account > 0 && !demo) waveTaste = waveStore.Load(account);
        AddWaveCandidates(waveTaste.KnownTracks);
    }
    private void SaveWaveProfile()
    {
        waveSaveTimer.Stop();
        if (waveAccount <= 0 || demo || !waveTaste.Dirty) return;
        try { waveStore.Save(waveAccount, waveTaste); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Pookie.Logging.AppLog.For("Pookie.Wave").Debug(error, "Не удалось сохранить профиль волны"); }
    }
    private void ScheduleWaveSave() { waveSaveTimer.Stop(); waveSaveTimer.Start(); }
    private void FinishWaveListening(bool skipped = false)
    {
        if (waveListening.Track == null) return;
        waveListening.Finish(waveTaste, skipped, DateTimeOffset.UtcNow); ScheduleWaveSave();
        RerankWaveQueue();
    }
    private void ClearWaveData()
    {
        FinishWaveListening(); SaveWaveProfile();
        ++waveGeneration; waveAccount = 0; waveTaste = new(); waveCandidates.Clear(); waveRelated.Clear();
        waveMixesLoaded.Clear(); waveCandidateFetch = null; waveLikesCursor = null;
        homeLikesCount = null;
        if (waveSettingsOpen != null) waveSettingsOpen.Value = false;
    }
    private void AddWaveCandidates(IEnumerable<SoundCloudTrack> tracks)
    {
        foreach (var track in tracks.Where(HomeRecommendations.Playable)) waveCandidates[track.Id] = track;
        if (waveCandidates.Count > 800)
            foreach (var id in waveCandidates.Keys.Take(waveCandidates.Count - 800).ToArray()) waveCandidates.Remove(id);
    }
    private void ImportWaveHomeData(SoundCloudTrack[] likes, IReadOnlySet<long> knownLikes, LibraryItem[] history,
        IEnumerable<SoundCloudTrack> candidates)
    {
        EnsureWaveTaste();
        foreach (var track in likes) waveTaste.Import(track, true, likedIdsReady ? likedIds.Contains(track.Id) : true);
        foreach (var track in history.Select(item => item.Track).OfType<SoundCloudTrack>())
            waveTaste.Import(track, true, knownLikes.Contains(track.Id) ? true : null);
        AddWaveCandidates(likes.Concat(history.Select(item => item.Track).OfType<SoundCloudTrack>()).Concat(candidates));
        ScheduleWaveSave();
    }
    private void SeedWaveFromHome()
    {
        EnsureWaveTaste();
        AddWaveCandidates(HomeItems("recommendations").Concat(HomeItems("releases")).Concat(HomeItems("recent"))
            .Select(item => item.Track).OfType<SoundCloudTrack>());
        foreach (var track in libraryLikes?.Tracks ?? []) { waveTaste.Import(track, true, likedIdsReady ? likedIds.Contains(track.Id) : true); AddWaveCandidates([track]); }
        foreach (var track in waveCandidates.Values) if (likedIds.Contains(track.Id)) waveTaste.Import(track, true, true);
        if (likedIdsReady) waveTaste.SyncLikes(likedIds);
    }
    private IEnumerable<SoundCloudTrack> WavePlayed() => playbackQueue.Context?.Key != WaveQueueKey ? [] :
        playbackQueue.Snapshot.History.Select(entry => entry.Track).Concat(playbackQueue.Current != null ? [playbackQueue.Current.Track] : [])
            .Concat(playbackQueue.Snapshot.Forward.Select(entry => entry.Track));
    private IEnumerable<SoundCloudTrack> WaveReserved() => playbackQueue.Snapshot.Forward.Concat(playbackQueue.Snapshot.ManualUpcoming)
        .Select(entry => entry.Track).Concat(playbackQueue.Current != null ? [playbackQueue.Current.Track] : []);

    private Task FetchWaveCandidatesAsync(CancellationToken token)
    {
        if (waveCandidateFetch is not { IsCompleted: false }) waveCandidateFetch = FetchWaveSourcesAsync(lifetime.Token);
        return waveCandidateFetch.WaitAsync(token);
    }
    private async Task FetchWaveSourcesAsync(CancellationToken token)
    {
        if (demo || api.Session is not { } session || me == null) return;
        var generation = waveGeneration;
        bool Current() => generation == waveGeneration && api.Session == session && !token.IsCancellationRequested;
        if (waveLikesCursor is { } likesCursor)
        {
            try
            {
                var likes = await api.GetNextPageAsync(likesCursor, token);
                if (!Current()) return;
                foreach (var track in likes.Tracks) waveTaste.Import(track, true, likedIdsReady ? likedIds.Contains(track.Id) : true);
                AddWaveCandidates(likes.Tracks); waveLikesCursor = likes.NextHref == likesCursor ? null : likes.NextHref;
                ScheduleWaveSave();
            }
            catch (Exception error) when (error is SoundCloudException { StatusCode: not 401 } or HttpRequestException or System.Text.Json.JsonException)
            { Pookie.Logging.AppLog.For("Pookie.Wave").Debug(error, "Не удалось дополнить лайки для волны"); }
        }
        if (waveTaste.Mode == WaveMode.Favorites) return;
        var seeds = waveTaste.Seeds(waveCandidates.Values, 24)
            .OrderBy(seed => waveRelated.GetValueOrDefault(seed.Id).Fetched).Take(3).ToArray();
        async Task Related(SoundCloudTrack seed)
        {
            token.ThrowIfCancellationRequested();
            if (generation != waveGeneration || api.Session != session) return;
            var known = waveRelated.GetValueOrDefault(seed.Id);
            if (known.Fetched != default && known.Cursor == null && DateTimeOffset.UtcNow - known.Fetched < TimeSpan.FromMinutes(10)) return;
            try
            {
                var result = known.Cursor != null ? await api.GetNextPageAsync(known.Cursor, token) : await api.GetRelatedTracksAsync(seed.Id, token);
                if (generation != waveGeneration || api.Session != session || token.IsCancellationRequested) return;
                AddWaveCandidates(result.Tracks); waveTaste.Relate(seed, result.Tracks);
                waveRelated[seed.Id] = (result.NextHref == known.Cursor ? null : result.NextHref, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (SoundCloudException error) when (error.StatusCode == 401) { throw; }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
            { Pookie.Logging.AppLog.For("Pookie.Wave").Debug(error, "Не удалось получить кандидатов волны"); }
        }
        await Task.WhenAll(seeds.Select(Related));
        if (!Current()) return;
        foreach (var mix in HomeItems("mixes").Where(item => item.Playlist != null && !waveMixesLoaded.Contains(item.Key)).Take(1))
        {
            try
            {
                var detail = collectionDetails.GetValueOrDefault(mix.Key) ?? await api.GetCollectionDetailAsync(mix, token);
                if (!Current()) return;
                collectionDetails[mix.Key] = detail; AddWaveCandidates(detail.Tracks.Tracks); waveMixesLoaded.Add(mix.Key);
            }
            catch (Exception error) when (error is SoundCloudException { StatusCode: not 401 } or HttpRequestException or System.Text.Json.JsonException)
            { Pookie.Logging.AppLog.For("Pookie.Wave").Debug(error, "Не удалось загрузить микс для волны"); }
        }
    }
    private async Task<TrackPage> GetWavePageAsync(CancellationToken token)
    {
        var generation = waveGeneration; var session = api.Session;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await FetchWaveCandidatesAsync(token);
            if (generation != waveGeneration || api.Session != session || token.IsCancellationRequested) return new([], null);
            var pending = playbackQueue.Snapshot.Upcoming.Select(entry => entry.Track);
            var ranked = waveTaste.Rank(waveCandidates.Values, WavePlayed().Concat(pending), 20, DateTimeOffset.UtcNow,
                reserved: WaveReserved().Concat(pending));
            if (ranked.Length > 0) return new(ranked, "pookie:wave:" + ++waveCursor);
            if (waveTaste.Mode == WaveMode.Favorites && waveLikesCursor == null) break;
        }
        return new([], null);
    }
    private void RerankWaveQueue()
    {
        if (playbackQueue.Context?.Key != WaveQueueKey) return;
        var reserved = WaveReserved();
        var ranked = waveTaste.Rank(waveCandidates.Values, WavePlayed(), 20, DateTimeOffset.UtcNow, reserved: reserved);
        playbackQueue.ReplaceRecommendations(playbackQueue.SourceVersion, ranked);
        RefreshQueue();
    }
    private void SetWaveMode(WaveMode mode)
    {
        EnsureWaveTaste(); waveTaste.SetMode(mode); ScheduleWaveSave(); RerankWaveQueue();
        RefreshWaveSettings(); RefreshHomeHeroPlayback();
        if (playbackQueue.Context?.Key == WaveQueueKey) Run(() => queueLoader.EnsureAheadAsync(force: true));
    }
    private async Task RejectWaveTrackAsync()
    {
        if (current is not { } track || playbackQueue.Context?.Key != WaveQueueKey) return;
        FinishWaveListening(); waveTaste.Block(track); ScheduleWaveSave(); RerankWaveQueue();
        await SkipAsync(1);
    }
    private void WaveLikeChanged(SoundCloudTrack track, bool liked)
    {
        EnsureWaveTaste(); waveTaste.Import(track, true, liked); AddWaveCandidates([track]);
        ScheduleWaveSave(); RerankWaveQueue();
        RefreshHomeShortcuts();
    }
}
