using Pookie.Localization;
using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task SettingsOperationAsync(Func<Task<string>> operation)
    {
        if (settingsBusy || disposed) return;
        settingsBusy = true; settingsMessage.Value = ""; RefreshSettingsControls();
        try { var result = await operation(); if (!disposed) settingsMessage.Value = result; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!disposed) settingsMessage.Value = FriendlyError(error).TrimEnd('.');
            Pookie.Logging.AppLog.Failure("Pookie.Settings", "Не удалось выполнить действие настроек", error);
        }
        finally { settingsBusy = false; if (!disposed) RefreshSettingsControls(); }
    }

    private void RefreshSettingsData()
    {
        var generation = ++settingsDataGeneration;
        if (settingsSection.Value == SettingsSection.Data && settingsCacheUsage != null)
            Run(async () =>
            {
                var size = await new CacheMaintenance(dataPaths).InspectAsync(lifetime.Token);
                if (disposed || generation != settingsDataGeneration) return;
                settingsCacheUsage.Update(size);
            });
    }

    private Task ClearSettingsCacheAsync() => SettingsOperationAsync(async () =>
    {
        if (!await ConfirmAsync(UiText.Get("cache.confirmClear"), detail: UiText.Get("settings.cache.clearConfirmation"), owner: Window)) return "";
        var result = await new CacheMaintenance(dataPaths).ClearAsync(lifetime.Token);
        RefreshSettingsData();
        return result.RemainingBytes == 0 ? UiText.Format("cache.freed", FormatSettingsBytes(result.DeletedBytes)):
            UiText.Format("cache.freedPartial", FormatSettingsBytes(result.DeletedBytes));
    });

    private Task ConfirmChatClearAsync(bool drafts) => SettingsOperationAsync(async () =>
    {
        var account = me?.Id;
        if (!await ConfirmAsync(drafts ? UiText.Get("settings.chats.confirmDeleteDrafts") : UiText.Get("settings.chats.confirmClearTemporary"),
            detail: drafts ? UiText.Get("settings.chats.deleteDraftsConfirmation") :
                UiText.Get("settings.chats.clearTemporaryConfirmation"), owner: Window)) return "";
        if (me?.Id != account) return UiText.Get("account.retryAfterChange");
        if (drafts) await ClearChatDraftsAsync(); else ClearChatMemoryCache();
        return drafts ? UiText.Get("settings.chats.draftsDeleted") : UiText.Get("chats.cache.cleared");
    });

    private Task ResetSettingsAsync() => SettingsOperationAsync(async () =>
    {
        if (!await ConfirmAsync(UiText.Get("settings.reset.confirmation"), detail: UiText.Get("settings.reset.description"), owner: Window)) return "";
        UpdatePreferences(_ => new AppConfiguration());
        discordEnabled.Value = true; if (presence != null) presence.Enabled = true;
        likesMode.Value = TrackViewMode.Cards; collectionMode.Value = TrackViewMode.List;
        shuffle.Value = false; volume.Value = previousVolume = 70;
        playbackQueue.SetRepeat(Pookie.App.Playback.RepeatMode.Off); RefreshRepeatState(); RefreshQueue();
        SaveConfiguration(); RefreshSettingsControls();
        return UiText.Get("settings.reset.success");
    });

    private static string FormatSettingsBytes(long bytes) => CacheUsageView.FormatBytes(bytes);

    private void OpenSettingsPath(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) { settingsMessage.Value = UiText.Get("files.folder.notCreated"); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { settingsMessage.Value = UiText.Get("files.folder.openFailed"); }
    }

    private void OpenSettingsUrl(string url) =>
        OpenExternalUrl(url, () => settingsMessage.Value = UiText.Get("links.website.openFailed"));

    private void CopySettingsDiagnostics()
    {
        CopyText($"Pookie {typeof(MainWindow).Assembly.GetName().Version}\n{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n{audioStatus.Value}");
        settingsMessage.Value = UiText.Get("settings.diagnostics.copied");
    }
}
