using Pookie.App.Browser;
using Aprillz.MewUI;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly AppDataPaths dataPaths;
    private readonly ConfigurationStore configuration;
    private readonly ImageDiskCache imageDiskCache;
    private readonly DispatcherTimer configurationTimer;
    private readonly SemaphoreSlim sessionWrites = new(1);
    private AppConfiguration preferences = new();

    private void RestoreConfiguration()
    {
        var saved = configuration.Load();
        preferences = saved;
        volume.Value = saved.Volume; previousVolume = saved.PreviousVolume; muted.Value = saved.Volume == 0;
        playbackQueue.SetRepeat(saved.Repeat);
        discordEnabled.Value = saved.DiscordEnabled; shuffle.Value = saved.Shuffle; likesAsList.Value = saved.LikesAsList;
    }

    private void ObserveConfiguration()
    {
        void Changed() { configurationTimer.Stop(); configurationTimer.Start(); RefreshSettingsControls(); }
        volume.Changed += Changed; discordEnabled.Changed += Changed; shuffle.Changed += Changed; likesAsList.Changed += Changed;
    }

    private void SaveConfiguration()
    {
        configurationTimer.Stop();
        try
        {
            preferences = preferences with { Volume = volume.Value, PreviousVolume = previousVolume, DiscordEnabled = discordEnabled.Value,
                Shuffle = shuffle.Value, LikesAsList = likesAsList.Value, Repeat = playbackQueue.Repeat };
            var save = configuration.SaveAsync(preferences);
            Run(async () =>
            {
                try { await save; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { if (!disposed) settingsMessage.Value = status.Value = "Не удалось сохранить настройки Pookie."; }
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!disposed) settingsMessage.Value = status.Value = "Не удалось сохранить настройки Pookie."; }
    }

    private void UpdatePreferences(Func<AppConfiguration, AppConfiguration> update)
    {
        var before = preferences;
        preferences = update(before).Normalize();
        if (preferences == before) return;
        ApplyChatPreferences(before);
        ApplyPlaybackPreferences(before);
        ApplyWavePreferences(before);
        if (before.ShowCommentMarkers != preferences.ShowCommentMarkers && trackDetailWaveformLayer != null) RenderTrackCommentMarkers();
        foreach (var scroll in smoothScrolls.Values) scroll.Enabled = preferences.SmoothScrolling;
        configurationTimer.Stop(); configurationTimer.Start();
        settingsMessage.Value = "";
        RefreshSettingsControls();
    }

    private async Task PersistBrowserSessionAsync(NativeBrowserSession connected)
    {
        await sessionWrites.WaitAsync(lifetime.Token);
        try
        {
            if (disposed || browser != connected) return;
            api.Session = connected.Account;
            if (vault != null)
                try { await Task.Run(() => vault.Save(connected.Account), lifetime.Token); }
                catch (InvalidOperationException) { }
        }
        finally { sessionWrites.Release(); }
    }
}
