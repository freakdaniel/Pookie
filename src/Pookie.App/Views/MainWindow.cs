using System.Net;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.Audio;
using Pookie.Discord;
using Pookie.App.Diagnostics;
using Pookie.App.Auth;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow : IDisposable
{
    private readonly bool demo;
    private readonly bool smoke;
    private readonly bool uiSmoke;
    private readonly bool loginUiSmoke;
    private readonly bool skipSessionRestore;
    private bool syncingTrackList;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? loading;
    private CancellationTokenSource? login;
    private CancellationTokenSource? playLoading;
    private readonly HttpClient http;
    private readonly SoundCloudWebClient api;
    private readonly ObservableValue<string> query = new("");
    private readonly ObservableValue<string> profile = new("SoundCloud: вход не выполнен");
    private readonly ObservableValue<bool> signedIn = new(false);
    private readonly ObservableValue<string> status = new("Загрузка…");
    private readonly ObservableValue<string> heading = new("Поиск музыки");
    private readonly ObservableValue<string> title = new("Выбери трек");
    private readonly ObservableValue<string> artist = new("Музыка из SoundCloud");
    private readonly ObservableValue<string> currentTime = new("0:00");
    private readonly ObservableValue<string> totalTime = new("0:00");
    private readonly ObservableValue<string> audioStatus = new("");
    private readonly ObservableValue<string> listStatus = new("");
    private readonly ObservableValue<bool> hasMore = new(false);
    private readonly ObservableValue<bool> discordEnabled = new(true);
    private readonly List<SoundCloudTrack> tracks = [];
    private readonly List<string> demoFiles = [];
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer? closeTimer;
    private readonly ListBox list;
    private readonly Slider progress;
    private readonly Image artwork;
    private readonly Image profileAvatar;
    private readonly ListBox queueList;
    private IAudioPlayer? player;
    private PresenceService? presence;
    private LinuxSessionVault? vault;
    private NativeBrowserSession? browser;
    private readonly List<NativeBrowserSession> browserSessions = [];
    private SoundCloudUser? me;
    private SoundCloudTrack? current;
    private string? nextHref;
    private bool updatingProgress;
    private bool paused;
    private bool audioPreparing;
    private bool audioReady;
    private readonly ObservableValue<bool> playbackLoading = new(false);
    private bool disposed;
    private long playGeneration;
    public Window Window { get; }

    public MainWindow(string[] args, string brandFontFamily)
    {
        this.brandFontFamily = brandFontFamily;
        uiSmoke = args.Contains("--ui-smoke-test");
        loginUiSmoke = args.Contains("--login-ui-smoke-test");
        demo = args.Contains("--demo") || uiSmoke || loginUiSmoke;
        skipSessionRestore = args.Contains("--guest");
        smoke = args.Contains("--smoke-test");
        dataPaths = new AppDataPaths(demo || smoke ? Path.Combine(Path.GetTempPath(), "pookie-ui-" + Guid.NewGuid().ToString("N")) : null);
        configuration = new ConfigurationStore(dataPaths);
        imageDiskCache = new ImageDiskCache(dataPaths);
        imageDiskCache.Prune();
        configurationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300));
        configurationTimer.Tick += SaveConfiguration;
        RestoreConfiguration();
        ObserveConfiguration();
        SaveConfiguration();
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) handler.Proxy = new WebProxy(proxy);
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        api = new(http) { RequireBrowserTransport = true };
        if (!args.Contains("--no-audio"))
        {
            try { player = new SoundFlowPlayer(args.Contains("--silent-audio") || uiSmoke, SoundCloudWebClient.IsMediaUri); audioStatus.Value = "Аудио: SoundFlow"; }
            catch (Exception error) { audioStatus.Value = error.Message; }
        }
        else audioStatus.Value = "Аудио отключено для проверки интерфейса";
        if (!demo && !smoke)
        {
            try { presence = new PresenceService(); }
            catch (Exception error) when (error is InvalidOperationException or IOException) { }
        }
        if (presence != null) presence.Enabled = discordEnabled.Value;
        player?.Volume(volume.Value);

        list = CreateTrackList().OnSelectionChanged(item =>
        {
            if (!syncingTrackList && item is SoundCloudTrack track) { SetQueue(track); Run(() => PlayAsync(track)); }
        });
        queueList = CreateTrackList(true).OnSelectionChanged(item =>
        {
            if (!syncingQueue && item is SoundCloudTrack track) Run(() => PlayAsync(track));
        });
        progress = ThinSlider().Minimum(0).Maximum(1).Value(0).OnValueChanged(QueueSeek)
            .OnMouseDown(e => { if (e.Button == MouseButton.Left) BeginSeekDrag(); })
            .OnMouseUp(e => { if (e.Button == MouseButton.Left) EndSeekDrag(); });
        seekTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(160));
        seekTimer.Tick += FlushSeek;
        artwork = Icons.View("music-notes", 48).StretchMode(Stretch.UniformToFill);
        profileAvatar = new Image().Width(30).Height(30).StretchMode(Stretch.UniformToFill);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(350));
        timer.Tick += Poll;
        if (smoke)
        {
            closeTimer = new DispatcherTimer(TimeSpan.FromSeconds(5));
            closeTimer.Tick += () => Window?.Close();
        }

        Window = new Window().Title("Pookie").Resizable(DefaultWindowWidth, 840, minWidth: 1000, minHeight: 680).StartCenterScreen()
            .Padding(0).Background(Color.FromRgb(34, 34, 34))
            .Content(Layout())
            .OnLoaded(OnWindowLoaded)
            .OnClosed(Dispose);
        Window.FrameRendered += RestoreNavigationScroll;
        Window.ClientSizeChanged += size =>
        {
            UpdateContentFrameWidth(size.Width);
            UpdateLibraryCardSize(size.Width);
        };
        Window.PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape && searching.Value) { e.Handled = true; CloseTopSearch(clear: true); }
        };
        InitializeSearchInput();
    }

    private void OnWindowLoaded()
    {
        var size = Window!.ClientSize;
        UpdateContentFrameWidth(size.Width);
        UpdateLibraryCardSize(size.Width);
        if (uiSmoke) StartStartupLayoutProbe();
        if (loginUiSmoke) Window.FrameRendered += SampleLoginTransition;
        startupMinimumDisplay = HoldStartupSplashAsync();
        timer.Start(); closeTimer?.Start(); clipboardTimer?.Start();
        Run(async () =>
        {
            try { await InitializeAsync(); }
            catch (SoundCloudException error) when (error.StatusCode == 401)
            {
                await LogoutAsync();
                status.Value = "Сессия SoundCloud истекла. Войди снова, чтобы продолжить.";
            }
            finally
            {
                await HideStartupSplashAsync();
            }
            if (signedIn.Value) Run(LoadLikedIdsAsync);
            if (uiSmoke) await VerifyUiAsync();
            if (loginUiSmoke) await VerifyLoginUiAsync();
        });
    }

    private void UpdateContentFrameWidth(double availableWidth)
    {
        if (availableWidth > 0)
        {
            contentFrame.Width = Math.Min(1440, availableWidth);
            playerContentFrame.Width = Math.Min(1440, availableWidth);
            var sideWidth = (playerContentFrame.Width - 72 - PlayerControlsWidth - 24) / 2;
            playerTrackInfo.MaxWidth = Math.Clamp(sideWidth - 48 - 30 - 12 - 4, 60, 180);
        }
    }

    private async Task InitializeAsync()
    {
        status.Value = "Для продолжения войди в свой аккаунт SoundCloud.";
        if (loginUiSmoke || skipSessionRestore) return;
        if (demo)
        {
            heading.Value = "На твоей волне";
            ReplaceTracks(new TrackPage(Enumerable.Range(1, 3).Select(i => new SoundCloudTrack
            { Id = i, Title = $"Тестовый звук {i}", Duration = 12000, User = new() { Username = "Pookie audio test" } }).ToArray(), null));
            status.Value = "Демо-режим: реальные локальные WAV, без обращения к SoundCloud.";
            return;
        }
        try
        {
            vault = new LinuxSessionVault();
            var saved = await Task.Run(vault.Load, lifetime.Token);
            if (saved != null)
            {
                api.Session = saved;
                try
                {
                    var connected = new NativeBrowserSession(saved);
                    AttachBrowser(connected);
                    me = await connected.GetMeAsync(lifetime.Token);
                    api.Session = connected.Account;
                    profile.Value = me.Username;
                    signedIn.Value = true;
                    UpdateProfileAvatar(me);
                }
                catch (SoundCloudException error) when (error.StatusCode == 401)
                { browser?.Dispose(); browser = null; api.BrowserTransport = null; api.Session = null; }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { audioStatus.Value += " · Сессия: только в памяти"; }
        if (me != null)
        {
            await NavigateAsync(Page.Home);
        }
    }

    private CancellationToken BeginLoad()
    {
        loading?.Cancel(); loading?.Dispose();
        loading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        status.Value = "Загрузка…";
        return loading.Token;
    }

    private Task SearchAsync()
    {
        if (!CanUseWorkspace) return Task.CompletedTask;
        var search = query.Value.Trim();
        if (search.Length == 0) { status.Value = "Введи название, исполнителя или ссылку на трек."; return Task.CompletedTask; }
        if (demo) { status.Value = "Поиск SoundCloud отключён в демо-режиме."; return Task.CompletedTask; }
        return NavigateRouteAsync(new(Page.Home, Search: search), async _ =>
        {
            var token = BeginLoad();
            eyebrow.Value = "ПОИСК"; heading.Value = "Результаты поиска";
            ReplaceTracks(new([], null));
            var result = search.StartsWith("https://soundcloud.com/", StringComparison.OrdinalIgnoreCase)
                ? new TrackPage([await api.ResolveAsync(search, token)], null)
                : await api.SearchAsync(search, token);
            token.ThrowIfCancellationRequested();
            ReplaceTracks(result);
            status.Value = "Нажми на трек для воспроизведения.";
        });
    }

    private Task LikesAsync(bool showAll = false) => NavigateRouteAsync(new(showAll ? Page.LibraryTracks : Page.Library), async _ =>
    {
        eyebrow.Value = "БИБЛИОТЕКА"; heading.Value = "Мои лайки";
        if (demo) { RefreshLibraryCards(); status.Value = "В демо-режиме библиотека показывает локальные тестовые звуки."; return; }
        if (me == null) { BeginLoad(); ReplaceTracks(new([], null)); RefreshLibraryCards(); status.Value = "Войди в SoundCloud через профиль справа сверху, чтобы открыть свои лайки."; return; }
        var token = BeginLoad();
        if (libraryLikes != null) ReplaceTracks(libraryLikes);
        else ReplaceTracks(new([], null));
        var result = await api.GetLikesAsync(me.Id, token);
        token.ThrowIfCancellationRequested();
        libraryLikes = result;
        ReplaceTracks(result);
        likedIds.UnionWith(result.Tracks.Select(t => t.Id));
        UpdateLikeState();
        RefreshLibraryCards();
        status.Value = "Музыка, которую ты сохранил в SoundCloud.";
        RefreshOverviewSections();
        if (!showAll) Run(RefreshOverviewDataAsync);
    });

    private async Task MoreAsync()
    {
        if (!CanUseWorkspace) return;
        if (nextHref == null) return;
        likedActionError.Value = "";
        var target = nextHref;
        var token = BeginLoad();
        var page = await api.GetNextPageAsync(target, token);
        token.ThrowIfCancellationRequested();
        var ids = tracks.Select(t => t.Id).ToHashSet();
        tracks.AddRange(page.Tracks.Where(t => ids.Add(t.Id)));
        nextHref = page.NextHref;
        RefreshList();
        status.Value = "Следующая страница загружена.";
    }

    private void ReplaceTracks(TrackPage page) { tracks.Clear(); tracks.AddRange(page.Tracks); nextHref = page.NextHref; RefreshList(); }
    private void RefreshList()
    {
        syncingTrackList = true;
        try { list.Items(tracks.ToArray(), t => $"{t.Title}   ·   {t.Author}   ·   {FormatTime(t.DurationSeconds)}" + (t.Access is "preview" or "blocked" ? "   [ограничен]" : ""), t => t.Id); }
        finally { syncingTrackList = false; }
        hasMore.Value = nextHref != null;
        listStatus.Value = $"{tracks.Count} треков";
        if (page.Value is Page.Library or Page.LibraryTracks) libraryLikes = new(tracks.ToArray(), nextHref);
        RefreshLikedViews();
    }

    private async Task PlayAsync(SoundCloudTrack track, bool fromHistory = false)
    {
        if (!CanUseWorkspace) return;
        if (player == null) { status.Value = audioStatus.Value; return; }
        CancelSeek();
        var generation = ++playGeneration;
        playLoading?.Cancel(); playLoading?.Dispose();
        playLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = playLoading.Token;

        // Selection is immediate; resolving and preparing audio must not hold back the UI.
        if (current != null && current.Id != track.Id && !fromHistory && queueTracks.Any(t => t.Id == current.Id))
            playbackHistory.Push(current);
        current = track;
        audioPreparing = true; audioReady = false;
        paused = false; isPlaying.Value = true; playbackLoading.Value = true;
        advancing = false;
        title.Value = track.Title; artist.Value = track.Author;
        likedPlaybackPosition = 0;
        currentTime.Value = "0:00"; totalTime.Value = FormatTime(track.DurationSeconds);
        updatingProgress = true;
        try { progress.Value = 0; progress.Maximum = Math.Max(1, track.DurationSeconds); }
        finally { updatingProgress = false; }
        var cachedArtwork = libraryCoverCache.GetValueOrDefault(track.Id) ?? coverCache.GetValueOrDefault(track.Id);
        SetPlayerArtwork(cachedArtwork, generation);
        playerVisible.Value = true;
        if (queueTracks.Count == 0) SetQueue(track);
        RefreshLikedPlayback(); UpdateLikeState(); RefreshQueue();
        if (cachedArtwork == null) Run(() => LoadArtworkAsync(track, generation));
        if (!IsLibrary(page.Value)) status.Value = "Получаем аудиопоток…";

        try
        {
            presence?.Clear();
            player.Stop();
            AudioSource source;
            if (demo) source = new(DemoSource(track.Id), AudioTransport.File, track.DurationSeconds);
            else
            {
                var resolved = await new SoundCloudStreamResolver(api).ResolveAsync(track.Id, token, ["progressive", "hls", "ctr-encrypted-hls"]);
                track = resolved.Track;
                var stream = resolved.Stream;
                source = new(stream.Uri.AbsoluteUri, stream.Protected ? AudioTransport.WidevineHls : stream.Protocol == "hls" ? AudioTransport.Hls : AudioTransport.Progressive, stream.Duration)
                    { LicenseAuthToken = stream.LicenseAuthToken };
            }
            if (generation != playGeneration || disposed) return;
            await player.PlayAsync(source, token);
            if (generation != playGeneration || disposed) return;
            current = track;
            title.Value = track.Title; artist.Value = track.Author;
            localRecent.RemoveAll(item => item.Key == "track:" + track.Id);
            localRecent.Insert(0, LibraryItem.FromTrack(track));
            if (localRecent.Count > 30) localRecent.RemoveAt(30);
            RefreshOverviewSections();
            audioReady = true;
            // The user can pause the selected track while it is still loading.
            if (paused) player.Pause(true);
            if (!IsLibrary(page.Value))
                status.Value = demo ? "Играет локальный тестовый звук." : source.Transport == AudioTransport.WidevineHls
                    ? "Защищённое воспроизведение через Widevine и SoundFlow." : "Воспроизведение полного доступного потока.";
        }
        catch
        {
            // A superseded request must not reset the newly selected track or report its error.
            if (generation != playGeneration || disposed) return;
            audioReady = false; isPlaying.Value = false; RefreshLikedPlayback();
            throw;
        }
        finally
        {
            if (generation == playGeneration && !disposed)
            {
                audioPreparing = false;
                playbackLoading.Value = false;
            }
        }
    }

    private string DemoSource(long id)
    {
        var source = DemoAudio.Create(220 + id * 110);
        demoFiles.Add(source);
        return source;
    }
    private Task ToggleAsync()
    {
        if (current == null)
        {
            if (tracks.Count == 0) return Task.CompletedTask;
            SetQueue(tracks[0]); return PlayAsync(tracks[0]);
        }
        if (!audioPreparing && !audioReady) return PlayAsync(current);
        paused = !paused;
        if (audioReady) player?.Pause(paused);
        isPlaying.Value = !paused;
        RefreshLikedPlayback();
        if (paused) presence?.Clear();
        return Task.CompletedTask;
    }
    private async Task ConnectSoundCloudAsync()
    {
        if (demo) { status.Value = "Перезапусти Pookie без --demo для входа."; return; }
        login?.Cancel(); login?.Dispose();
        login = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        login.CancelAfter(TimeSpan.FromMinutes(10));
        var token = login.Token;
        var oldBrowser = browser;
        browser = null; api.BrowserTransport = null;
        if (oldBrowser != null) await oldBrowser.DisposeAsync();
        WebSession session;
        try
        {
            SetLoginButtonState(LoginButtonState.Waiting);
            session = await NativeWebLogin.ConnectAsync(token);
            token.ThrowIfCancellationRequested();
            SetLoginButtonState(LoginButtonState.Connecting);
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested && login?.Token == token)
        { status.Value = "Подключение отменено или истекло. Нажми «Войти в SoundCloud» ещё раз."; return; }
        var connectedBrowser = new NativeBrowserSession(session);
        SoundCloudUser connectedProfile;
        try { connectedProfile = await connectedBrowser.GetMeAsync(token); token.ThrowIfCancellationRequested(); }
        catch { await connectedBrowser.DisposeAsync(); throw; }
        AttachBrowser(connectedBrowser);
        likedLoading?.Cancel(); likedTask = null;
        likedIds.Clear(); likedIdsReady = false;
        api.Session = connectedBrowser.Account;
        me = connectedProfile;
        ClearLibraryData();
        ResetNavigationHistory();
        profile.Value = me.Username;
        signedIn.Value = true;
        UpdateProfileAvatar(me);
        profileOpen.Value = false;
        await ShowWorkspaceAsync();
        Run(LoadLikedIdsAsync);
        var saved = false;
        if (vault != null)
        {
            await sessionWrites.WaitAsync(token);
            try { token.ThrowIfCancellationRequested(); await Task.Run(() => vault.Save(connectedBrowser.Account), token); saved = true; }
            catch (InvalidOperationException) { }
            finally { sessionWrites.Release(); }
        }
        token.ThrowIfCancellationRequested();
        await LikesAsync();
        token.ThrowIfCancellationRequested();
        status.Value = saved ? "Сессия подключена и сохранена в хранилище паролей." : "Сессия подключена. Хранилище паролей недоступно: вход сохранён только до выхода.";
    }

    private async Task LogoutAsync()
    {
        workspace.IsEnabled = false;
        workspace.IsHitTestVisible = false;
        loginBusy.Value = true;
        profileOpen.Value = settingsOpen.Value = queueOpen.Value = false;
        CloseTopSearch(clear: true);
        try
        {
            CancelSeek();
            login?.Cancel(); loading?.Cancel(); playLoading?.Cancel(); likedLoading?.Cancel();
            likedTask = null;
            var closedBrowser = browser; browser = null; api.BrowserTransport = null;
            closedBrowser?.Dispose();
            ++playGeneration;
            api.Session = null; me = null;
            ClearLibraryData();
            ResetNavigationHistory();
            signedIn.Value = false;
            ResetProfileAvatar();
            profile.Value = "SoundCloud: вход не выполнен";
            current = null; paused = false; isPlaying.Value = false;
            audioPreparing = audioReady = false; playbackLoading.Value = false;
            RefreshLikedPlayback();
            playerVisible.Value = false;
            likedIds.Clear(); likedIdsReady = false; UpdateLikeState();
            queueTracks.Clear(); playbackHistory.Clear(); RefreshQueue();
            presence?.Clear();
            RunSync(() => player?.Stop());
            title.Value = "Выбери трек"; artist.Value = "Музыка из SoundCloud";
            currentTime.Value = "0:00"; totalTime.Value = "0:00";
            progress.Value = 0; progress.Maximum = 1;
            artwork.Source = Icons.Source("music-notes");
            playerBackdrop.Reset();
            page.Value = Page.Home; RefreshNavVisuals(); eyebrow.Value = "ГЛАВНАЯ"; heading.Value = "На твоей волне";
            ReplaceTracks(new TrackPage([], null));
            status.Value = "Ты вышел из SoundCloud. Войди снова, чтобы продолжить.";
            await ShowLoginScreenAsync();
            if (closedBrowser != null) await closedBrowser.DisposeAsync();
            await Task.Run(() => BrowserProfile.Clear(dataPaths), lifetime.Token);
            if (vault != null)
            {
                await sessionWrites.WaitAsync(lifetime.Token);
                try { await Task.Run(vault.Delete, lifetime.Token); }
                catch (InvalidOperationException)
                {
                    status.Value = "Выход выполнен в Pookie, но сохранённую сессию удалить не удалось: после перезапуска аккаунт может подключиться снова.";
                    return;
                }
                finally { sessionWrites.Release(); }
            }
            status.Value = "Ты вышел из SoundCloud. Войди снова, чтобы продолжить.";
        }
        finally { loginBusy.Value = false; }
    }

    private void Poll()
    {
        if (player == null || current == null || disposed || audioPreparing || !audioReady) return;
        RunSync(() =>
        {
            var state = player.Poll();
            playbackLoading.Value = state.Buffering;
            if (!seekDragging && pendingSeek == null && !seeking)
            {
                updatingProgress = true;
                try { progress.Maximum = Math.Max(1, state.Duration > 0 ? state.Duration : current.DurationSeconds); progress.Value = state.Position; }
                finally { updatingProgress = false; }
                currentTime.Value = FormatTime(state.Position);
                RefreshLikedRows(state.Position);
            }
            totalTime.Value = FormatTime(progress.Maximum);
            presence?.Update(new(current.Title, current.Author, current.ArtworkUrl ?? current.User?.AvatarUrl, current.PermalinkUrl,
                state.Position, progress.Maximum, state.Playing));
            if (state.Ended && !advancing)
            {
                advancing = true;
                Run(async () =>
                {
                    try { await SkipAsync(1); }
                    catch { isPlaying.Value = false; throw; }
                });
            }
        });
    }

    internal static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (SoundCloudException error) when (error.StatusCode == 401 && signedIn.Value)
        {
            try { await LogoutAsync(); }
            catch (Exception logoutError) when (!disposed) { status.Value = FriendlyError(logoutError); }
            if (!disposed) status.Value = "Сессия SoundCloud истекла. Войди снова, чтобы продолжить.";
        }
        catch (Exception error)
        {
            if (!disposed)
            {
                status.Value = FriendlyError(error);
                if (page.Value == Page.LibraryTracks) likedActionError.Value = status.Value;
                else if (page.Value > Page.LibraryTracks) librarySectionStatus.Value = status.Value;
            }
        }
    }
    private void RunSync(Action action) { try { action(); } catch (Exception error) { status.Value = FriendlyError(error); } }
    private static string FriendlyError(Exception error) => error is SoundCloudException or InvalidOperationException ? error.Message :
        error is HttpRequestException ? "Не удалось подключиться к SoundCloud. Проверь сеть и POOKIE_PROXY." : "Не удалось выполнить действие. Попробуй ещё раз.";

    public void Dispose()
    {
        if (disposed) return;
        Window.FrameRendered -= RestoreNavigationScroll;
        Window.FrameRendered -= SampleLoginTransition;
        loginSpinner.IsActive = false;
        CancelSeek(); seekTimer.Dispose();
        searchInput?.Dispose(); clipboardTimer?.Dispose();
        SaveConfiguration(); configurationTimer.Dispose();
        playbackLoading.Value = false;
        playerBackdrop.Reset();
        disposed = true; avatarLoading?.Cancel(); lifetime.Cancel(); loading?.Cancel(); login?.Cancel(); playLoading?.Cancel(); likedLoading?.Cancel();
        foreach (var session in browserSessions) session.Dispose();
        timer.Dispose(); closeTimer?.Dispose(); startupLayoutProbe?.Dispose(); player?.Dispose(); presence?.Dispose();
        // An outstanding libsecret operation may still be completing on its worker; process teardown releases it.
        http.Dispose();
    }

    internal async Task FinishShutdownAsync()
    {
        Dispose();
        foreach (var session in browserSessions) await session.DisposeAsync().ConfigureAwait(false);
        if (player != null) await player.DisposeAsync().ConfigureAwait(false);
        foreach (var file in demoFiles) try { File.Delete(file); } catch (IOException) { }
        if (demo || smoke)
            try { Directory.Delete(dataPaths.Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void AttachBrowser(NativeBrowserSession connected)
    {
        browser?.Dispose();
        browser = connected;
        api.BrowserTransport = connected;
        browserSessions.Add(connected);
        connected.Changed += message => Application.Current.Dispatcher?.BeginInvoke(() =>
        {
            if (disposed || browser != connected) return;
            if (message.Kind == "protection-session") Run(() => PersistBrowserSessionAsync(connected));
            if (message.Kind == "blocked") status.Value = "SoundCloud временно ограничил доступ. Капча не предложена; действие остановлено. Подробности — в окне сайта.";
            if (message.Kind == "checking") status.Value = message.ChallengeType == "hard_block"
                ? "SoundCloud заблокировал этот браузер. Подробности — в окне сайта."
                : message.Interactive
                ? "Пройди проверку в окне SoundCloud. Действие продолжится автоматически."
                : "SoundCloud проверяет браузер. Ожидаем завершения…";
            if (message.Kind == "passed") status.Value = likeBusy ? "Проверка пройдена. Выполняем действие…" : "Проверка SoundCloud пройдена.";
            if (message.Kind == "challenge-error") status.Value = "Не удалось показать проверку SoundCloud. Попробуй действие ещё раз.";
        });
    }
}
