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
        if (settingsSection.Value == SettingsSection.Wave && settingsBlockedList != null)
        {
            settingsBlockedList.Clear();
            var blocked = me == null ? [] : WaveBlockedTracks();
            if (blocked.Length == 0) settingsBlockedList.Add(new TextBlock().Text("Исключённых треков пока нет").FontSize(13).Foreground(Muted).Margin(0, 12));
            foreach (var track in blocked)
            {
                var account = me?.Id;
                settingsBlockedList.Add(SettingsRow(track.Title, track.Author,
                    TrackButtons.Action(new TextBlock().Text("Восстановить").FontSize(14).SemiBold(), () =>
                    {
                        if (me?.Id != account) return;
                        UnblockWaveTrack(track); RefreshSettingsData(); RefreshSettingsControls();
                    }, TrackButtons.Tonal)));
            }
        }
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
        if (!await MessageBox.AskYesNoAsync("Очистить кэш?", detail: "Обложки, звуковые волны и тексты песен будут загружены повторно; аккаунт и персональные данные сохранятся", owner: Window)) return "";
        var result = await new CacheMaintenance(dataPaths).ClearAsync(lifetime.Token);
        RefreshSettingsData();
        return result.RemainingBytes == 0 ? $"Освобождено {FormatSettingsBytes(result.DeletedBytes)}" :
            $"Освобождено {FormatSettingsBytes(result.DeletedBytes)}; часть файлов используется или уже загружена повторно";
    });

    private Task ConfirmWaveDataActionAsync(bool resetLearning) => SettingsOperationAsync(async () =>
    {
        if (me is not { } account || demo) return "Войди в SoundCloud, чтобы управлять данными аккаунта";
        var title = resetLearning ? "Удалить накопленное обучение?" : "Очистить локальную историю Pookie?";
        var detail = resetLearning ? "Будут удалены локальные события, обученная модель и анализ звучания этого аккаунта; режим Волны, лайки и исключения сохранятся; история SoundCloud не изменится" :
            "Будут удалены локальные события прослушивания этого аккаунта; обученная модель, исключения и история SoundCloud сохранятся; серверная история может снова отображаться в библиотеке";
        if (!await MessageBox.AskYesNoAsync(title, detail: detail, owner: Window)) return "";
        if (me?.Id != account.Id) return "Аккаунт изменился; повтори действие";
        var cleared = resetLearning ? await ResetWaveLearningAsync(account.Id) : await ClearLocalWaveHistoryAsync(account.Id);
        RefreshSettingsData();
        return !cleared ? "Аккаунт изменился или операция уже выполняется; повтори действие" :
            resetLearning ? "Накопленное обучение удалено; явные предпочтения сохранены" : "Локальная история очищена; история SoundCloud не изменена";
    });

    private Task ConfirmChatClearAsync(bool drafts) => SettingsOperationAsync(async () =>
    {
        var account = me?.Id;
        if (!await MessageBox.AskYesNoAsync(drafts ? "Удалить черновики?" : "Освободить временные данные чатов?",
            detail: drafts ? "Текст и вложения черновиков этого аккаунта будут удалены; незавершённые отправки сохранятся" :
                "Просмотренные диалоги и вложения будут убраны из памяти; черновики и незавершённые отправки сохранятся", owner: Window)) return "";
        if (me?.Id != account) return "Аккаунт изменился; повтори действие";
        if (drafts) ClearChatDrafts(); else ClearChatMemoryCache();
        return drafts ? "Черновики удалены" : "Временные данные чатов очищены";
    });

    private Task ResetSettingsAsync() => SettingsOperationAsync(async () =>
    {
        if (!await MessageBox.AskYesNoAsync("Сбросить настройки приложения?", detail: "Восстановятся исходные предпочтения интерфейса, воспроизведения и чатов; аккаунт, очередь и обучение сохранятся; сохранённые черновики не удаляются", owner: Window)) return "";
        UpdatePreferences(_ => new AppConfiguration());
        discordEnabled.Value = true; if (presence != null) presence.Enabled = true;
        likesAsList.Value = false; shuffle.Value = false; volume.Value = previousVolume = 70;
        playbackQueue.SetRepeat(Pookie.App.Playback.RepeatMode.Off); RefreshRepeatState(); RefreshQueue();
        SaveConfiguration(); RefreshSettingsControls();
        return "Исходные настройки восстановлены";
    });

    private static string FormatSettingsBytes(long bytes) => CacheUsageView.FormatBytes(bytes);

    private void OpenSettingsPath(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) { settingsMessage.Value = "Папка пока не создана"; return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { settingsMessage.Value = "Не удалось открыть папку"; }
    }

    private void OpenSettingsUrl(string url) =>
        OpenExternalUrl(url, () => settingsMessage.Value = "Не удалось открыть сайт");

    private void CopySettingsDiagnostics()
    {
        CopyText($"Pookie {typeof(MainWindow).Assembly.GetName().Version}\n{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n{audioStatus.Value}");
        settingsMessage.Value = "Сведения скопированы";
    }
}
