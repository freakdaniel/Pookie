using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.App.Messages;
using Pookie.Audio;
using Pookie.App.Storage;
using Pookie.Lyrics;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySettingsUiAsync()
    {
        var originalPlayer = player;
        var audio = new SettingsVerificationPlayer();
        try
        {
            timer.Stop(); chatTimer.Stop();
            UpdatePreferences(p => p with { ChatBackgroundRefresh = false });
            player = audio;
            api.Session = new("settingsfixture", "fixture-token", "Settings fixture");
            api.BrowserTransport = new ContinuationFixtureTransport();
            me = new() { Id = 42, Username = "Settings fixture" };
            profile.Value = me.Username; signedIn.Value = true; likedIdsReady = true;
            EnsureWaveTaste(); await ShowWorkspaceAsync();
            await VerifySettingsDraftCleanupBeforeOpeningChatsAsync();
            if (settingsButton.ToolTip != null || settingsToggles.Values.Any(control => control.ToolTip != null) ||
                settingsChoices.Values.Any(control => control.ToolTip != null))
                throw new InvalidOperationException("Settings gear, switches or selects retained tooltips.");
            Window.Activate();
            Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
            await WaitForSettingsLayoutAsync(() => Window.IsActive && Math.Abs(Window.ClientSize.Width - 1000) < 1 &&
                Math.Abs(Window.ClientSize.Height - 680) < 1 && workspace.IsHitTestVisible && settingsButton.ActualWidth > 0);

            // Merely hovering the gear must keep the page and menus unchanged.
            var initialPage = page.Value;
            RouteWaveformPointer(SettingsCenter(settingsButton));
            await Task.Delay(350, lifetime.Token);
            if (page.Value != initialPage || shellMenus.Any(menu => menu.Layer.IsOpen))
                throw new InvalidOperationException("Hovering the settings gear opened a page or popup.");
            RouteWaveformClick(SettingsCenter(settingsButton));
            await WaitForSettingsLayoutAsync(() => page.Value == Page.Settings && settingsSection.Value == SettingsSection.General);
            if (shellMenus.Any(menu => menu.Layer.IsOpen))
                throw new InvalidOperationException("Settings opened with a shell popup covering its page.");

            var sections = Enum.GetValues<SettingsSection>();
            if (sections.Length != 7 || settingsTabs.Count != sections.Length)
                throw new InvalidOperationException("The settings page does not expose all seven sections.");
            foreach (var section in sections)
            {
                await SelectSettingsTabAsync(section);
                CheckSettingsTabBounds();
                CheckSettingsCategoryStyles();
                if (settingsTabs[section].IsEnabled || settingsTabs[section].StyleName != "section-button")
                    throw new InvalidOperationException("The selected settings tab lost the library section style or active state.");
                CaptureUiPreview("settings-" + section.ToString().ToLowerInvariant() + "-1000x680");
                var maximum = SmoothScroll.Maximum(shellScroll);
                if (maximum > 50)
                {
                    shellScroll.SetScrollOffsets(0, maximum);
                    await WaitForSettingsLayoutAsync(() => Math.Abs(shellScroll.VerticalOffset - maximum) < 1);
                    await SettleSettingsPresentationAsync();
                    CaptureUiPreview("settings-" + section.ToString().ToLowerInvariant() + "-bottom-1000x680");
                }
            }
            AssertExcludedAnimationPreferenceAbsent();
            await VerifySettingsSelectAsync();
            await VerifyMaterialSwitchAsync();
            await VerifySettingsCacheChartAsync();

            await SelectSettingsTabAsync(SettingsSection.Chats);
            await WaitForSettingsLayoutAsync(() => SmoothScroll.Maximum(shellScroll) > 100);
            var savedScroll = Math.Min(180, SmoothScroll.Maximum(shellScroll));
            shellScroll.SetScrollOffsets(0, savedScroll);
            await WaitForSettingsLayoutAsync(() => Math.Abs(shellScroll.VerticalOffset - savedScroll) < 1);
            await SelectSettingsTabAsync(SettingsSection.About);
            RouteWaveformClick(SettingsCenter(backButton));
            await WaitForSettingsLayoutAsync(() => settingsSection.Value == SettingsSection.Chats &&
                pendingNavigationScroll == null && Math.Abs(shellScroll.VerticalOffset - savedScroll) < 1);
            RouteWaveformClick(SettingsCenter(forwardButton));
            await WaitForSettingsLayoutAsync(() => settingsSection.Value == SettingsSection.About && shellScroll.VerticalOffset < 1);

            await SelectSettingsTabAsync(SettingsSection.Interface);
            await SetSettingsChoiceAsync("likes", 1);
            if (!likesAsList.Value) throw new InvalidOperationException("The likes choice did not update the shared library preference.");
            RouteWaveformClick(SettingsCenter(backButton));
            await WaitForSettingsLayoutAsync(() => settingsSection.Value == SettingsSection.About);
            if (!likesAsList.Value) throw new InvalidOperationException("Back navigation restored a stale likes view preference.");
            RouteWaveformClick(SettingsCenter(forwardButton));
            await WaitForSettingsLayoutAsync(() => settingsSection.Value == SettingsSection.Interface);
            if (!likesAsList.Value || settingsChoices["likes"].SelectedIndex != 1)
                throw new InvalidOperationException("Forward navigation restored a stale likes view preference.");

            var source = new[] { SettingsFixtureTrack(101), SettingsFixtureTrack(102) };
            StartQueueContext(new("settings:playlist", "Проверка настроек", "Текущая очередь", Kind: PlaybackContextKind.Playlist), source, source[0]);
            var manual = playbackQueue.Enqueue(SettingsFixtureTrack(800));
            await PlayAsync(source[0]); await queueLoader.EnsureAheadAsync(); RefreshQueue();
            var currentEntry = playbackQueue.Current!.EntryId; var sourceVersion = playbackQueue.SourceVersion;
            if (playbackQueue.Snapshot.Recommendations.Count == 0)
                throw new InvalidOperationException("The settings fixture did not prepare recommendations to test their cancellation.");

            await SelectSettingsTabAsync(SettingsSection.Playback);
            await SetSettingsToggleAsync("auto_continue", false);
            if (preferences.AutoContinue || playbackQueue.AutoContinue || playbackQueue.Snapshot.Recommendations.Count != 0)
                throw new InvalidOperationException("Disabling automatic continuation did not update the actual queue policy.");
            CheckSettingsQueue();
            await SetSettingsToggleAsync("auto_continue", true);
            await queueLoader.EnsureAheadAsync();
            if (!playbackQueue.AutoContinue || playbackQueue.Snapshot.Recommendations.Count == 0)
                throw new InvalidOperationException("Enabling automatic continuation did not resume recommendation prefetch.");
            CheckSettingsQueue();
            await SetSettingsToggleAsync("lyrics", false);
            if (preferences.LyricsEnabled || lyricsRequest != null || lyricsResult.Status != LyricsStatus.NotFound)
                throw new InvalidOperationException("Disabling lyrics did not cancel the actual lyrics request/state.");
            await SetSettingsToggleAsync("lyrics", true);
            await SetSettingsToggleAsync("lyrics_follow", false);
            if (preferences.LyricsAutoScroll || lyricsFollow)
                throw new InvalidOperationException("Disabling lyrics following left automatic scroll enabled.");
            await SetSettingsToggleAsync("lyrics_follow", true);
            if (!preferences.LyricsAutoScroll || !lyricsFollow)
                throw new InvalidOperationException("Enabling lyrics following did not resume its actual policy.");

            await SelectSettingsTabAsync(SettingsSection.Wave);
            var analysisCancellation = waveAudioCancellation.Token;
            await SetSettingsToggleAsync("wave_analysis", false);
            if (preferences.WaveAnalysisEnabled || CanAnalyzeWave || audio.AnalysisEnabled || !analysisCancellation.IsCancellationRequested)
                throw new InvalidOperationException("Disabling Wave analysis did not cancel work and release audio capture.");
            await SetSettingsToggleAsync("wave_analysis", true);
            if (!CanAnalyzeWave || !audio.AnalysisEnabled)
                throw new InvalidOperationException("Enabling Wave analysis did not resume audio capture.");
            var learningCancellation = waveObservationCancellation.Token;
            await SetSettingsToggleAsync("wave_learning", false);
            if (preferences.WaveLearningEnabled || CanLearnWave || CanAnalyzeWave || audio.AnalysisEnabled || !learningCancellation.IsCancellationRequested)
                throw new InvalidOperationException("Disabling Wave learning left observations or analysis active.");
            await SetSettingsToggleAsync("wave_learning", true);
            if (!CanLearnWave || !CanAnalyzeWave || !audio.AnalysisEnabled)
                throw new InvalidOperationException("Enabling Wave learning did not restore the active capture policy.");
            CheckSettingsQueue();

            await SelectSettingsTabAsync(SettingsSection.Data);
            BeginPlaybackHistory(playbackQueue.Current!);
            if (playbackHistorySubmitted) throw new InvalidOperationException("The fixture did not begin a fresh history occurrence.");
            await SetSettingsToggleAsync("soundcloud_history", false);
            if (preferences.SendPlaybackHistory || !playbackHistorySubmitted)
                throw new InvalidOperationException("Disabling SoundCloud history did not seal the current occurrence.");
            await SetSettingsToggleAsync("soundcloud_history", true);
            if (!preferences.SendPlaybackHistory || !playbackHistorySubmitted)
                throw new InvalidOperationException("Re-enabling history reopened an occurrence that had already been suppressed.");
            CheckSettingsQueue();

            // Persist nondefault values through controls, then reload from disk.
            await SetSettingsToggleAsync("soundcloud_history", false);
            await SelectSettingsTabAsync(SettingsSection.Playback);
            await SetSettingsToggleAsync("auto_continue", false);
            await SetSettingsToggleAsync("lyrics", false);
            await SelectSettingsTabAsync(SettingsSection.Wave);
            await SetSettingsToggleAsync("wave_analysis", false);
            await SetSettingsToggleAsync("wave_learning", false);
            await SelectSettingsTabAsync(SettingsSection.Chats);
            await SetSettingsChoiceAsync("chat_send", 1);
            await SetSettingsChoiceAsync("chat_download", 1);
            await SetSettingsToggleAsync("chat_background", false);
            await SetSettingsToggleAsync("klipy", false);
            await SelectSettingsTabAsync(SettingsSection.General);
            await SetSettingsChoiceAsync("startup", 1);
            SaveConfiguration();
            var saved = configuration.Load();
            if (saved.AutoContinue || saved.LyricsEnabled || saved.SendPlaybackHistory || saved.WaveLearningEnabled || saved.WaveAnalysisEnabled ||
                !saved.LikesAsList || saved.StartupPage != "library" || !saved.ChatSendOnEnter || saved.ChatAutoLoad || saved.ChatBackgroundRefresh || saved.KlipyEnabled)
                throw new InvalidOperationException("The settings controls did not survive configuration save/reload.");
            CheckSettingsQueue();

            await VerifySettingsWaveMaintenanceAsync();

            Window.WindowSize = WindowSize.Resizable(1280, 840, minWidth: 1000, minHeight: 680);
            await WaitForSettingsLayoutAsync(() => Math.Abs(Window.ClientSize.Width - 1280) < 1 && Math.Abs(Window.ClientSize.Height - 840) < 1);
            await SettleSettingsPresentationAsync();
            CheckSettingsTabBounds(); CaptureUiPreview("settings-general-1280x840");
            Console.WriteLine("UI_SETTINGS_OK: native gear/tab/toggle/select clicks, animated borderless menu, keyboard selection/dismissal, category dividers, external-link icons, seven sections at 1000x680, history section/scroll restoration, persistent likes view, real playback/lyrics/history/Wave policies, preserved current/manual entries, configuration round trip and no reduced-animation preference");

            void CheckSettingsQueue()
            {
                var state = playbackQueue.Snapshot;
                if (state.Current?.EntryId != currentEntry || state.SourceVersion != sourceVersion ||
                    !state.Source.Select(entry => entry.Track.Id).SequenceEqual(source.Select(track => track.Id)) ||
                    state.ManualUpcoming.Count != 1 || state.ManualUpcoming[0].EntryId != manual.EntryId || current?.Id != source[0].Id || !audioReady)
                    throw new InvalidOperationException("Changing a preference replaced the current occurrence, source or manual queue.");
            }
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            Console.Error.WriteLine("UI_SETTINGS_FAILED: " + error);
        }
        finally { player = originalPlayer; Window.Close(); }
    }

    private async Task VerifySettingsWaveMaintenanceAsync()
    {
        var source = SettingsFixtureTrack(501);
        var candidates = Enumerable.Range(600, 15).Select(id => SettingsFixtureTrack(id)).ToArray();
        AddWaveCandidates(candidates);
        StartQueueContext(new(WaveQueueKey, "Волна", "Волна", "pookie:wave:maintenance", Kind: PlaybackContextKind.Wave), [source], source);
        var manual = playbackQueue.Enqueue(SettingsFixtureTrack(900));
        var currentEntry = playbackQueue.Current!.EntryId;
        var sourceVersion = playbackQueue.SourceVersion;
        var generation = waveGeneration;
        var epoch = waveLearningEpoch;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveCandidateFetch = held.Task;
        var refill = queueLoader.EnsureAheadAsync(force: true);
        await Task.Delay(50, lifetime.Token);
        if (refill.IsCompleted) throw new InvalidOperationException("Maintenance fixture did not hold the source refill.");
        try
        {
            if (!await ResetWaveLearningAsync(me!.Id)) throw new InvalidOperationException("Account maintenance was not accepted.");
            if (waveGeneration != generation || waveLearningEpoch == epoch)
                throw new InvalidOperationException("Learning reset invalidated the source refill generation.");
        }
        finally { held.TrySetResult(); }
        await refill;
        if (playbackQueue.SourceVersion != sourceVersion || playbackQueue.Current?.EntryId != currentEntry ||
            !playbackQueue.Snapshot.ManualUpcoming.Contains(manual) || playbackQueue.Context?.NextHref == null ||
            playbackQueue.Snapshot.Source.Count <= 1)
            throw new InvalidOperationException("Reset during a pending source refill discarded its cursor, current track or manual queue.");
        var stored = waveStore.Load(me!.Id).Snapshot();
        if (stored.Tracks.Any(item => item.Plays > 0 || item.GoodPlays > 0 || item.ListenedSeconds > 0))
            throw new InvalidOperationException("Completed maintenance retained implicit statistics on disk.");
        Console.WriteLine("UI_SETTINGS_WAVE_MAINTENANCE_OK: pending source refill survives learning reset; durable JSON has no prior listening statistics.");
    }

    private async Task VerifySettingsDraftCleanupBeforeOpeningChatsAsync()
    {
        SaveConfiguration();
        if (page.Value == Page.Chats || chatAccountId != 0 || configuration.Load().ChatBackgroundRefresh)
            throw new InvalidOperationException("Draft-cleanup fixture must start before opening Chats, with persisted background polling disabled.");
        var store = new ChatLocalStore(dataPaths, me!.Id);
        var created = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
        var pending = new ChatPending(91, "Unconfirmed fixture message", "Fixture", created, true, ["before-fixture"]);
        var unread = new ChatUnreadState("unread-fixture", created, 3);
        var state = new ChatLocalState(new() { [90] = "Persisted draft before opening Chats" }, [pending], new() { [92] = unread });

        store.Save(state);
        await ClearChatDraftsAsync();
        AssertPreserved("Clear drafts");
        ResetChats(); // Recreate the uninitialized-account condition for the switch.
        store.Save(state);
        UpdatePreferences(p => p with { ChatSaveDrafts = false });
        AssertPreserved("Disable draft persistence");
        SaveConfiguration();
        if (configuration.Load().ChatSaveDrafts)
            throw new InvalidOperationException("Disabled draft persistence was not saved.");

        UpdatePreferences(p => p with { ChatSaveDrafts = true });
        ResetChats(); store.Save(new([], []));
        Console.WriteLine("UI_SETTINGS_DRAFT_CLEANUP_OK: clear/disable before first Chats visit removes persisted drafts while preserving durable outbox/read positions with background polling disabled");

        void AssertPreserved(string action)
        {
            var saved = store.Load();
            if (page.Value == Page.Chats || saved.Drafts.Count != 0 || chatDrafts.Count != 0 ||
                saved.Pending.Length != 1 || saved.Pending[0].Body != pending.Body || !saved.Pending[0].Uncertain ||
                saved.Pending[0].BeforeIds?.SingleOrDefault() != "before-fixture" ||
                saved.Unread?.GetValueOrDefault(92) != unread || !chatPending.ContainsKey(91) ||
                !chatOutgoing.ContainsKey(91) || chatUnread.States.GetValueOrDefault(92) != unread)
                throw new InvalidOperationException(action + " before opening Chats erased outbox/read positions or left saved drafts behind.");
        }
    }

    private async Task SelectSettingsTabAsync(SettingsSection section)
    {
        if (settingsSection.Value != section || page.Value != Page.Settings)
        {
            if (settingsTabs[section].Bounds.Y >= 64) RouteWaveformClick(SettingsCenter(settingsTabs[section]));
            else await ShowSettingsAsync(section);
        }
        else shellScroll.SetScrollOffsets(0, 0);
        await WaitForSettingsLayoutAsync(() => page.Value == Page.Settings && settingsSection.Value == section &&
            !navigationHistory[navigationIndex].Pending && settingsScroll.ActualHeight > 50);
        await SettleSettingsPresentationAsync();
    }

    private async Task SetSettingsToggleAsync(string key, bool value)
    {
        var control = settingsToggles[key];
        await RevealSettingsControlAsync(control);
        if (control.IsChecked != value) RouteWaveformClick(SettingsCenter(control));
        await WaitForSettingsLayoutAsync(() => control.IsChecked == value);
    }

    private async Task SetSettingsChoiceAsync(string key, int value)
    {
        Window.Activate();await WaitForSettingsLayoutAsync(()=>Window.IsActive);
        var control = settingsChoices[key];
        await RevealSettingsControlAsync(control);
        RouteWaveformClick(SettingsCenter(control));
        await WaitForSettingsLayoutAsync(() => control.IsDropDownOpen && control.MenuReveal == 1);
        RouteSettingsOptionClick(control.Menu!.Options[value]);
        await WaitForSettingsLayoutAsync(() => control.SelectedIndex == value && !control.IsDropDownOpen);
    }

    private async Task VerifySettingsSelectAsync()
    {
        await SelectSettingsTabAsync(SettingsSection.Interface);
        var select = settingsChoices["likes"];
        await RevealSettingsControlAsync(select);
        if (select.BorderThickness != 0 || select.Height != 48)
            throw new InvalidOperationException("Settings select lost its filled, borderless 48px trigger.");
        var original = select.SelectedIndex;
        RouteWaveformClick(SettingsCenter(select));
        await Task.Delay(35, lifetime.Token);
        if (!select.IsDropDownOpen || select.MenuReveal <= 0 || select.MenuReveal >= 1)
            throw new InvalidOperationException("Select opening skipped its intermediate animation frames.");
        CaptureUiPreview("settings-select-opening", select.Menu);
        await WaitForSettingsLayoutAsync(() => select.MenuReveal == 1 && select.Menu!.ActualHeight > 0);
        var menu = select.Menu!;
        if (menu.Card.BorderThickness != 0 || menu.Options.Any(option => option.Height != 48) ||
            menu.Bounds.Right > Window.ClientSize.Width + 1 || menu.Bounds.Bottom > Window.ClientSize.Height + 1)
            throw new InvalidOperationException("Settings menu is outlined, cramped or outside the window.");
        CaptureUiPreview("settings-select-open", menu);
        RouteWaveformPointer(new Point(10, 10));
        RouteSettingsKey(Key.Escape);
        await WaitForSettingsLayoutAsync(() => !select.IsDropDownOpen);
        if (select.SelectedIndex != original)
            throw new InvalidOperationException("Dismissing the menu changed its selection.");
        await WaitForSettingsLayoutAsync(() => select.Background == TrackButtons.TonalSurface);
        if (!select.IsFocused || KeyboardFocusFeedback.IsVisible(select))
            throw new InvalidOperationException("Pointer-opened select retained a focus tint after Escape.");
        select.Focus(); RouteSettingsKey(Key.Down);
        await WaitForSettingsLayoutAsync(() => select.IsDropDownOpen && select.MenuReveal == 1);
        RouteSettingsKey(Key.End); RouteSettingsKey(Key.Enter);
        await WaitForSettingsLayoutAsync(() => !select.IsDropDownOpen && select.SelectedIndex == menu.Options.Count - 1);
        if (!KeyboardFocusFeedback.IsVisible(select))
            throw new InvalidOperationException("Keyboard selection lost its visible focus cue.");
        await SetSettingsChoiceAsync("likes", original);
        await WaitForSettingsLayoutAsync(() => select.Background == TrackButtons.TonalSurface);
        if (KeyboardFocusFeedback.IsVisible(select))
            throw new InvalidOperationException("Mouse selection retained the select's keyboard focus tint.");
        CaptureUiPreview("settings-select-after-pointer-selection");
        RouteWaveformClick(SettingsCenter(select));
        await WaitForSettingsLayoutAsync(() => select.IsDropDownOpen && select.MenuReveal == 1);
        RouteWaveformClick(new Point(10, 80));
        await WaitForSettingsLayoutAsync(() => !select.IsDropDownOpen);
        RouteWaveformPointer(new Point(10, 10));
        await WaitForSettingsLayoutAsync(() => select.Background == TrackButtons.TonalSurface);
        foreach (var keyboard in new[] { false, true })
        {
            if (keyboard) { select.Focus(); RouteSettingsKey(Key.Down); }
            else RouteWaveformClick(SettingsCenter(select));
            await WaitForSettingsLayoutAsync(() => select.IsDropDownOpen && select.MenuReveal == 1);
            RouteWaveformClick(SettingsCenter(select));
            RouteWaveformPointer(new Point(10, 10));
            await WaitForSettingsLayoutAsync(() => !select.IsDropDownOpen && select.Background == TrackButtons.TonalSurface);
            if (!select.IsFocused || KeyboardFocusFeedback.IsVisible(select))
                throw new InvalidOperationException("Clicking the select trigger to dismiss its popup retained the focus tint.");
        }
    }

    private void RouteSettingsKey(Key key)
    {
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter")!;
        foreach (var method in new[] { "KeyDown", "KeyUp" })
            router.GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [Window, new KeyEventArgs(key, 0)]);
    }

    private async Task VerifyMaterialSwitchAsync()
    {
        await SelectSettingsTabAsync(SettingsSection.Interface);
        var control = settingsToggles["smooth_scroll"];
        await RevealSettingsControlAsync(control);
        var visual = VisualTree.Find(control, element => element is MaterialSwitchVisual) as MaterialSwitchVisual ??
            throw new InvalidOperationException("The native switch does not use its Material visual template.");
        var original = control.IsChecked;
        await WaitForSettingsLayoutAsync(() => !visual.Animating);
        if (control.Height != 40 || visual.TrackBounds.Width != 40 || visual.TrackBounds.Height != 24 ||
            visual.HandleSize != (original ? 18 : 12))
            throw new InvalidOperationException("Material switch lost its touch target, track or handle geometry.");

        var samples = new List<double>();
        void Sample() => samples.Add(visual.SelectionPosition);
        Window.FrameRendered += Sample;
        try
        {
            RouteWaveformClick(SettingsCenter(control));
            await Task.Delay(65, lifetime.Token);
            if (control.IsChecked == original || visual.SelectionPosition <= 0 || visual.SelectionPosition >= 1)
                throw new InvalidOperationException("The switch did not animate its selection travel.");
            CaptureUiPreview("settings-switch-motion");
            await WaitForSettingsLayoutAsync(() => !visual.Animating);
            if (samples.Count(value => value > 0 && value < 1) < 3 || visual.HandleSize != (control.IsChecked ? 18 : 12))
                throw new InvalidOperationException("Switch motion skipped intermediate frames or its handle size.");
            RouteWaveformPointer(new Point(10, 10));
            await WaitForSettingsLayoutAsync(() => !visual.Animating && visual.StateOpacity == 0);
            if (!control.IsFocused || KeyboardFocusFeedback.IsVisible(control))
                throw new InvalidOperationException("A pointer-clicked switch retained its focus halo after the pointer left.");
            CaptureUiPreview("settings-switch-compact-idle");
            RouteWaveformPointer(SettingsCenter(control));
            await WaitForSettingsLayoutAsync(() => !visual.Animating && visual.StateOpacity == .08);
            RouteWaveformPointer(new Point(10, 10));
            await WaitForSettingsLayoutAsync(() => !visual.Animating && visual.StateOpacity == 0);

            var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter")!;
            var click = router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Single(method => method.Name == "MouseButton" && method.GetParameters().Length == 10);
            click.Invoke(null, [Window, SettingsCenter(control), new Point(0, 0), MouseButton.Left, true, true, false, false, 1, ModifierKeys.None]);
            await WaitForSettingsLayoutAsync(() => visual.HandleSize == 21);
            CaptureUiPreview("settings-switch-pressed");
            RouteWaveformPointer(new Point(10, 10));
            click.Invoke(null, [Window, new Point(10, 10), new Point(0, 0), MouseButton.Left, false, false, false, false, 1, ModifierKeys.None]);
            await WaitForSettingsLayoutAsync(() => !visual.Animating);
            if (control.IsChecked == original) throw new InvalidOperationException("Releasing outside the switch committed a cancelled press.");

            control.Focus(); RouteSettingsKey(Key.Space);
            await WaitForSettingsLayoutAsync(() => !visual.Animating && control.IsChecked == original);
            if (visual.StateOpacity != .12 || !KeyboardFocusFeedback.IsVisible(control))
                throw new InvalidOperationException("Keyboard toggling lost its visible switch focus cue.");
            control.IsEnabled = false;
            RouteSettingsKey(Key.Space); RouteWaveformClick(SettingsCenter(control));
            if (control.IsChecked != original || visual.Animating)
                throw new InvalidOperationException("The disabled switch accepted input or kept animating.");
            control.IsEnabled = true;
            await WaitForSettingsLayoutAsync(() => !visual.Animating);
            Console.WriteLine("UI_SETTINGS_MATERIAL_SWITCH_OK: native template, compact 40x24 track, 40px target, 12/18/21px handle, intermediate frames, cancelled press, pointer/keyboard focus distinction and disabled input");
        }
        finally { Window.FrameRendered -= Sample; control.IsEnabled = true; control.IsChecked = original; }
    }

    private async Task VerifySettingsCacheChartAsync()
    {
        await SelectSettingsTabAsync(SettingsSection.Data);
        var directories = new[] { dataPaths.Images, dataPaths.Waveforms, Path.Combine(dataPaths.Cache, "Lyrics") };
        var sizes = new[] { 6 * 1024 * 1024, 3 * 1024 * 1024, 1024 * 1024 };
        for (var index = 0; index < directories.Length; index++)
        {
            Directory.CreateDirectory(directories[index]);
            using var file = File.Create(Path.Combine(directories[index], "settings-chart-fixture.bin"));
            file.SetLength(sizes[index]);
        }
        var expected = await new CacheMaintenance(dataPaths).InspectAsync(lifetime.Token);
        RefreshSettingsData();
        var view = settingsCacheUsage; var chart = view.Chart;
        await WaitForSettingsLayoutAsync(() => view.Usage == expected && !chart.Animating);
        var actual = new[] { expected.ImagesBytes, expected.WaveformsBytes, expected.LyricsBytes };
        if (view.TotalText != FormatSettingsBytes(expected.TotalBytes) ||
            chart.Fractions.Where((value, index) => Math.Abs(value - actual[index] / (double)expected.TotalBytes) > .00001).Any() ||
            view.Amounts.Where((value, index) => value.Text != FormatSettingsBytes(actual[index])).Any())
            throw new InvalidOperationException("The cache chart does not represent real per-category disk usage.");
        if (Math.Abs(view.Title.Bounds.X - view.Legends[0].Bounds.X) > .5 ||
            Math.Abs(view.Legends[0].Label.Bounds.X - view.Title.Bounds.X - 14) > .5 ||
            view.Legends.Where((legend, index) => Math.Abs(legend.Label.Bounds.X - view.Amounts[index].Bounds.X) > .5).Any())
            throw new InvalidOperationException("Cache legend dots do not align with the heading, or values lost their label alignment.");
        if (VisualTree.Find(settingsScroll.Content, element => element is TextBlock { Text: "Кэш" }) != null)
            throw new InvalidOperationException("The cache card retained its duplicate section heading.");
        CaptureUiPreview("settings-data-chart");
        var angle = -Math.PI / 2 + chart.Fractions[0] * Math.PI;
        var samples = new List<double>();
        void Sample() => samples.Add(chart.Highlights[0]);
        Window.FrameRendered += Sample;
        try
        {
            RouteWaveformPointer(new Point(chart.Bounds.X + 84 + Math.Cos(angle) * 70, chart.Bounds.Y + 84 + Math.Sin(angle) * 70));
            await Task.Delay(45, lifetime.Token);
            if (chart.Highlights[0] <= 0 || chart.Highlights[0] >= 1 || view.Legends[0].Highlight != chart.Highlights[0])
                throw new InvalidOperationException("The cache chart/legend hover snapped instead of animating together.");
            CaptureUiPreview("settings-data-chart-hover-motion");
            await WaitForSettingsLayoutAsync(() => chart.HoveredCategory == 0 && !chart.Animating &&
                view.TotalText == FormatSettingsBytes(expected.ImagesBytes));
            if (samples.Count(value => value > 0 && value < 1) < 3)
                throw new InvalidOperationException("The cache hover skipped its intermediate frames.");
            CaptureUiPreview("settings-data-chart-hover");

            var previous = chart.Highlights[0];
            RouteWaveformPointer(SettingsCenter(view.Legends[1]));
            if (chart.Highlights[0] != previous)
                throw new InvalidOperationException("Switching cache categories reset an in-progress hover transition.");
            await Task.Delay(45, lifetime.Token);
            if (chart.Highlights[0] <= 0 || chart.Highlights[0] >= 1 || chart.Highlights[1] <= 0 || chart.Highlights[1] >= 1)
                throw new InvalidOperationException("Cache categories did not crossfade during a pointer transition.");
            await WaitForSettingsLayoutAsync(() => chart.HoveredCategory == 1 && !chart.Animating &&
                view.TotalText == FormatSettingsBytes(expected.WaveformsBytes));
            CaptureUiPreview("settings-data-legend-hover");
            RouteWaveformPointer(new Point(10, 10));
            await Task.Delay(45, lifetime.Token);
            if (chart.Highlights[1] <= 0 || chart.Highlights[1] >= 1)
                throw new InvalidOperationException("Cache highlight removal snapped instead of fading.");
            await WaitForSettingsLayoutAsync(() => !chart.Animating && chart.Highlights.All(value => value == 0) &&
                view.TotalText == FormatSettingsBytes(expected.TotalBytes));
        }
        finally { Window.FrameRendered -= Sample; }
        await new CacheMaintenance(dataPaths).ClearAsync(lifetime.Token);
        RefreshSettingsData();
        await WaitForSettingsLayoutAsync(() => view.Usage?.TotalBytes == 0 && !chart.Animating);
        if (chart.Fractions.Any(value => value != 0) || view.TotalText != "0 Б")
            throw new InvalidOperationException("The empty cache retained nonzero chart data after clearing.");
        CaptureUiPreview("settings-data-chart-empty");
        Console.WriteLine("UI_SETTINGS_CACHE_CHART_OK: real file sizes, aligned title/legend/values, single heading, animated chart and legend hover/crossfade/dismissal, empty state after clearing");
    }

    private void RouteSettingsOptionClick(Button option)
    {
        // Native dropdowns live on a separate OS surface while retaining owner-relative bounds.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var surface = Window;
        for (var ancestor = option.Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.GetType().GetProperty("HostSurface", flags)?.GetValue(ancestor) is Window native)
            { surface = native; break; }
        var map = typeof(Window).GetMethod("SurfacePointToVisualTree", flags)!;
        var origin = (Point)map.Invoke(surface, [new Point(0, 0)])!;
        var unit = (Point)map.Invoke(surface, [new Point(1, 1)])!;
        var center = SettingsCenter(option);
        var point = new Point((center.X - origin.X) / (unit.X - origin.X), (center.Y - origin.Y) / (unit.Y - origin.Y));
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter")!;
        var click = router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "MouseButton" && method.GetParameters().Length == 10);
        click.Invoke(null, [surface, point, new Point(0, 0), MouseButton.Left, true, true, false, false, 1, ModifierKeys.None]);
        click.Invoke(null, [surface, point, new Point(0, 0), MouseButton.Left, false, false, false, false, 1, ModifierKeys.None]);
    }

    private void CheckSettingsCategoryStyles()
    {
        var body = (Grid)settingsScroll.Content!;
        foreach (var pageContent in body.Children.OfType<StackPanel>().Where(content => content.IsVisible))
            foreach (var group in pageContent.Children.OfType<StackPanel>())
            {
                var rows = ((StackPanel)group.Children[1]).Children.OfType<Border>().ToArray();
                if (rows.Length == 0) continue;
                if (rows[^1].BorderThickness != 0 || rows[^1].NonUniformBorderThickness != Thickness.Zero ||
                    rows.Take(rows.Length - 1).Any(row => row.NonUniformBorderThickness.Bottom != 1))
                    throw new InvalidOperationException("Settings category lost its internal dividers or retained a trailing divider.");
            }
        if (settingsSection.Value != SettingsSection.About) return;
        var links = settingsActions.Select(action => action.Button).Where(button => button.Content is StackPanel).ToArray();
        if (links.Length != 2 || links.Any(button => button.StyleName != TrackButtons.Text || button.Height != 40 || button.CornerRadius != 20 ||
            ((StackPanel)button.Content!).Children.OfType<Image>().Single().ActualWidth != 18))
            throw new InvalidOperationException("About links lost their external-link glyph or Material text-button styling.");
    }

    private async Task RevealSettingsControlAsync(FrameworkElement control)
    {
        await WaitForSettingsLayoutAsync(() => control.ActualHeight > 0);
        var offset = Math.Clamp(shellScroll.VerticalOffset + control.Bounds.Y - 88, 0, SmoothScroll.Maximum(shellScroll));
        shellScroll.SetScrollOffsets(0, offset);
        await WaitForSettingsLayoutAsync(() => control.Bounds.Y >= 64 - 1 &&
            control.Bounds.Bottom <= settingsScroll.Bounds.Bottom + 1);
    }

    private void CheckSettingsTabBounds()
    {
        var tabs = settingsTabs.Values.OrderBy(tab => tab.Bounds.X).ToArray();
        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index];
            var active = ReferenceEquals(tab, settingsTabs[settingsSection.Value]);
            var content = (StackPanel)tab.Content!;
            var underline = content.Children.OfType<Border>().Single();
            var label = ((Grid)content.Children[0]).Children.OfType<TextBlock>().First();
            if (Math.Abs(underline.Opacity - (active ? 1 : 0)) > .001 || active && label.Foreground != Color.White)
                throw new InvalidOperationException("Settings underline/label did not settle on the selected tab.");
            if (!tab.IsVisible || tab.ActualWidth <= 0 || tab.ActualHeight <= 0 || tab.Bounds.X < 0 ||
                tab.Bounds.Right > Window.ClientSize.Width + 1 || tab.Bounds.Bottom > settingsScroll.Bounds.Y + 1 ||
                index > 0 && (tab.Bounds.X < tabs[index - 1].Bounds.Right || Math.Abs(tab.Bounds.Y - tabs[0].Bounds.Y) > 1))
                throw new InvalidOperationException("A settings tab is clipped, overlapping or inaccessible at the current window size.");
        }
    }

    private void AssertExcludedAnimationPreferenceAbsent()
    {
        if (typeof(AppConfiguration).GetProperties().Any(property => property.Name is "ReduceMotion" or "ReducedMotion" or "ReduceAnimations") ||
            settingsToggles.Keys.Any(key => key is "reduce_motion" or "reduced_motion" or "reduce_animations"))
            throw new InvalidOperationException("The excluded reduced-animation preference was added.");
        var forbidden = new[] { "Уменьшение анимаций", "Уменьшать анимации", "Уменьшить анимации", "Reduced motion" };
        if (VisualTree.Find(Window.Content, element => element is TextBlock text &&
            forbidden.Any(value => text.Text.Contains(value, StringComparison.OrdinalIgnoreCase))) != null)
            throw new InvalidOperationException("The settings page exposes the excluded reduced-animation option.");
    }

    private async Task SettleSettingsPresentationAsync()
    {
        await Task.Delay(250, lifetime.Token);
        await WaitForSettingsLayoutAsync(() => !contentFrame.IsArrangeDirty &&
            Math.Abs(contentFrame.ActualWidth - Math.Min(1440, Window.ClientSize.Width)) < 1 &&
            settingsTabs.All(pair => Math.Abs(((StackPanel)pair.Value.Content!).Children.OfType<Border>().Single().Opacity -
                (pair.Key == settingsSection.Value ? 1 : 0)) < .001));
    }

    private async Task WaitForSettingsLayoutAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Window.PerformLayout();
            if (ready() && (page.Value != Page.Settings || !settingsScroll.IsArrangeDirty) && !workspace.IsArrangeDirty)
            { await WaitForLoginFrameAsync(); return; }
            await Task.Delay(50, lifetime.Token);
        }
        throw new InvalidOperationException($"Settings layout did not settle: page={page.Value}, section={settingsSection.Value}, scroll={shellScroll.VerticalOffset}, viewport={settingsScroll.Bounds}, window={Window.ClientSize}, active={Window.IsActive}.");
    }

    private static Point SettingsCenter(FrameworkElement element) => new(element.Bounds.X + element.ActualWidth / 2, element.Bounds.Y + element.ActualHeight / 2);
    private static SoundCloudTrack SettingsFixtureTrack(long id) => new() { Id = id, Title = "Настройки · трек " + id,
        Duration = 12000, User = new() { Id = id, Username = "Fixture artist" } };

    private sealed class SettingsVerificationPlayer : IAudioPlayer
    {
        internal bool AnalysisEnabled { get; private set; } = true;
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SetAnalysisCaptureEnabled(bool enabled) => AnalysisEnabled = enabled;
        public AudioState Poll() => new(0, 12, true, false, false);
        public void Pause(bool paused) { }
        public void Volume(double percent) { }
        public void Stop() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
