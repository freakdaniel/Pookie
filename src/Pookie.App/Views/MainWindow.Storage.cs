using Aprillz.MewUI;
using Pookie.App.Auth;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly AppDataPaths dataPaths;
    private readonly ConfigurationStore configuration;
    private readonly ImageDiskCache imageDiskCache;
    private readonly DispatcherTimer configurationTimer;
    private readonly SemaphoreSlim sessionWrites = new(1);

    private void RestoreConfiguration()
    {
        var saved = configuration.Load();
        volume.Value = saved.Volume; previousVolume = saved.PreviousVolume; muted.Value = saved.Volume == 0;
        discordEnabled.Value = saved.DiscordEnabled; shuffle.Value = saved.Shuffle; likesAsList.Value = saved.LikesAsList;
    }

    private void ObserveConfiguration()
    {
        void Changed() { configurationTimer.Stop(); configurationTimer.Start(); }
        volume.Changed += Changed; discordEnabled.Changed += Changed; shuffle.Changed += Changed; likesAsList.Changed += Changed;
    }

    private void SaveConfiguration()
    {
        configurationTimer.Stop();
        try { configuration.Save(new(volume.Value, previousVolume, discordEnabled.Value, shuffle.Value, likesAsList.Value)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!disposed) status.Value = "Не удалось сохранить настройки Pookie."; }
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
