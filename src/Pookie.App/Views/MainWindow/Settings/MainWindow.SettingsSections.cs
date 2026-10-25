using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Home;
using Pookie.App.Storage;
using Pookie.Logging;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FrameworkElement GeneralSettingsView() => SettingsGroups(
        SettingsGroup("Подключения",
            SettingsValue("SoundCloud", "Библиотека и чаты подключённого аккаунта",
                () => demo ? "Демонстрационный режим" : signedIn.Value ? profile.Value : "Вход не выполнен"),
            SettingsAction("Аккаунт SoundCloud", "Выход завершит сессию и остановит воспроизведение; настройки и кэш сохранятся", "Выйти",
                () => Run(() => SettingsOperationAsync(async () =>
                {
                    var account = me?.Id;
                    if (!await MessageBox.AskYesNoAsync("Выйти из SoundCloud?", detail: "Музыка остановится, очередь будет очищена", owner: Window)) return "";
                    if (me?.Id != account) return "Аккаунт изменился; повтори действие";
                    await LogoutAsync(); return "Сессия SoundCloud завершена";
                })), () => signedIn.Value && !demo),
            SettingsToggle("discord", "Показывать музыку в Discord", "Название, исполнитель и обложка в твоём статусе",
                () => discordEnabled.Value, value => { discordEnabled.Value = value; if (presence != null) presence.Enabled = value; }),
            SettingsToggle("system_media", "Управление воспроизведением из системы", "Медиакнопки и текущий трек в панели операционной системы",
                () => preferences.SystemMediaEnabled, value => UpdatePreferences(p => p with { SystemMediaEnabled = value }))),
        SettingsGroup("При запуске",
            SettingsChoice("startup", "Начальная страница", "Открывается после восстановления аккаунта",
                () => preferences.StartupPage, value => UpdatePreferences(p => p with { StartupPage = value }),
                ("Главная", "home"), ("Библиотека", "library"))));

    private FrameworkElement InterfaceSettingsView() => SettingsGroups(
        SettingsGroup("Библиотека и поиск",
            SettingsChoice("likes", "Вид понравившихся треков", "Тот же выбор доступен в библиотеке",
                () => likesAsList.Value, value => likesAsList.Value = value, ("Карточки", false), ("Подробный список", true)),
            SettingsToggle("search_section", "Запоминать категорию поиска", "Новый поиск открывает последнюю выбранную категорию вместо «Всё»",
                () => preferences.RememberSearchSection, value => UpdatePreferences(p => p with { RememberSearchSection = value })),
            SettingsToggle("smooth_scroll", "Плавная прокрутка", "Плавное перемещение страниц и списков при прокрутке",
                () => preferences.SmoothScrolling, value => UpdatePreferences(p => p with { SmoothScrolling = value }))),
        SettingsGroup("Страница трека",
            SettingsToggle("comment_markers", "Маркеры комментариев на звуковой волне", "Показывать комментарии с привязкой ко времени трека",
                () => preferences.ShowCommentMarkers, value => UpdatePreferences(p => p with { ShowCommentMarkers = value })),
            SettingsChoice("comment_sort", "Сортировка комментариев по умолчанию", "Применяется при открытии следующего трека",
                () => preferences.CommentSort, value => UpdatePreferences(p => p with { CommentSort = value }),
                ("Сначала новые", "NEWEST"), ("Сначала старые", "OLDEST"), ("По времени в треке", "TIMESTAMP"))));

    private FrameworkElement PlaybackSettingsView() => SettingsGroups(
        SettingsGroup("Музыка",
            SettingsToggle("auto_continue", "Продолжать похожими треками", "Подбирать музыку, когда закончится текущая очередь; текущий трек и добавленные вручную сохраняются",
                () => preferences.AutoContinue, value => UpdatePreferences(p => p with { AutoContinue = value })),
            SettingsToggle("normalization", "Выравнивать громкость треков", "Приблизительная поправка по звуковой волне трека; применяется со следующего трека",
                () => preferences.NormalizeVolume, value => UpdatePreferences(p => p with { NormalizeVolume = value }))),
        SettingsGroup("Текст песни",
            SettingsToggle("lyrics", "Искать текст через LRCLIB", "Запрашивать текст при открытии соответствующей панели плеера",
                () => preferences.LyricsEnabled, value => UpdatePreferences(p => p with { LyricsEnabled = value })),
            SettingsToggle("lyrics_follow", "Автопрокрутка текста", "Следовать за текущей строкой; после ручной прокрутки следование возобновляется",
                () => preferences.LyricsAutoScroll, value => UpdatePreferences(p => p with { LyricsAutoScroll = value }))));

    private FrameworkElement WavePreferencesView()
    {
        settingsBlockedList = new StackPanel().Vertical().Spacing(10);
        return SettingsGroups(
            SettingsGroup("Подбор музыки",
                SettingsChoice("wave_mode", "Характер Волны", "Тот же выбор доступен на главной; изменения применяются к следующим трекам",
                    () => waveTaste.Mode, SetWaveMode, ("Баланс", WaveMode.Balance), ("Любимое", WaveMode.Favorites), ("Открытия", WaveMode.Discoveries)),
                SettingsToggle("wave_learning", "Учитывать новые прослушивания и пропуски", "Обучать Волну на музыке из разных разделов Pookie; прежнее обучение и лайки сохраняются",
                    () => preferences.WaveLearningEnabled, value => UpdatePreferences(p => p with { WaveLearningEnabled = value })),
                SettingsToggle("wave_analysis", "Фоновый анализ звучания", "Загружать короткие фрагменты кандидатов для подбора музыки; выключение также прекращает новый анализ играющего аудио",
                    () => preferences.WaveAnalysisEnabled, value => UpdatePreferences(p => p with { WaveAnalysisEnabled = value }))),
            SettingsGroup("Исключённые треки",
                new TextBlock().Text("Восстанови отдельное произведение или сбрось все исключения; обучение и лайки сохранятся").FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap),
                settingsBlockedList,
                SettingsAction("Вернуть все исключённые треки", "Снова разрешить их подбор в Волне", "Сбросить исключения",
                    () => Run(() => SettingsOperationAsync(async () =>
                    {
                        var account = me?.Id;
                        if (!await MessageBox.AskYesNoAsync("Сбросить исключения?", detail: "Все исключённые произведения снова смогут попасть в Волну", owner: Window)) return "";
                        if (me?.Id != account) return "Аккаунт изменился; повтори действие";
                        ResetWaveBlocks(); RefreshSettingsData(); return "Исключения сброшены";
                    })), () => me != null && WaveBlockedTracks().Length > 0)),
            SettingsGroup("Накопленное обучение",
                SettingsAction("Удалить накопленное обучение", "Удалить локальные события, обученную модель и анализ звучания этого аккаунта; характер Волны, лайки и исключения сохранятся", "Удалить обучение",
                    () => Run(() => ConfirmWaveDataActionAsync(resetLearning: true)), () => me != null && !demo)));
    }

    private FrameworkElement ChatSettingsView() => SettingsGroups(
        SettingsGroup("Сообщения",
            SettingsChoice("chat_send", "Отправлять сообщение", "При отправке по Enter новая строка — Shift+Enter. Ctrl+Enter также отправляет сообщение",
                () => preferences.ChatSendOnEnter, value => UpdatePreferences(p => p with { ChatSendOnEnter = value }), ("Ctrl+Enter", false), ("Enter", true)),
            SettingsToggle("chat_drafts", "Сохранять черновики на устройстве", "Выключение удаляет сохранённые черновики; данные незавершённых отправок хранятся отдельно",
                () => preferences.ChatSaveDrafts, value => UpdatePreferences(p => p with { ChatSaveDrafts = value })),
            SettingsToggle("chat_background", "Обновлять чаты в фоне", "Получать новые сообщения и обновлять счётчик при переходе в другие разделы",
                () => preferences.ChatBackgroundRefresh, value => UpdatePreferences(p => p with { ChatBackgroundRefresh = value }))),
        SettingsGroup("Вложения",
            SettingsChoice("chat_download", "Загружать входящие вложения", "Вложение скачивается целым пакетом, включая файлы и видео",
                () => preferences.ChatAutoLoad, value => UpdatePreferences(p => p with { ChatAutoLoad = value }), ("Автоматически", true), ("По нажатию", false)),
            SettingsToggle("chat_gifs", "Анимировать GIF", "В видимой части открытого чата и в результатах поиска GIF",
                () => preferences.ChatAnimateGifs, value => UpdatePreferences(p => p with { ChatAnimateGifs = value })),
            SettingsToggle("chat_video", "Автовоспроизведение видео без звука", "Короткие превью в видимой части чата; полный просмотр — по нажатию",
                () => preferences.ChatAutoPlayVideo, value => UpdatePreferences(p => p with { ChatAutoPlayVideo = value })),
            SettingsToggle("klipy", "Поиск GIF через KLIPY", "Использует внешний сервис; если поиск недоступен в сборке, GIF можно прикрепить файлом",
                () => preferences.KlipyEnabled, value => UpdatePreferences(p => p with { KlipyEnabled = value })),
            new TextBlock().Text("Файлы шифруются на устройстве и размещаются на Pomf / qu.ax; очистка локальных данных не удаляет отправленные файлы с внешнего хостинга")
                .FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Margin(0, 14)),
        SettingsGroup("Расширения Pookie",
            new TextBlock().Text("Связанные ответы, реакции и голосовые полноценно отображаются между клиентами Pookie. В других клиентах ответы и реакции могут выглядеть как служебный текст, голосовые — как аудиофайл.").FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap)),
        SettingsGroup("Кеш чатов",
            ChatCacheStatistics(),
            SettingsAction("История и поиск", "Сообщения загрузятся из SoundCloud повторно; избранное, папки, позиции чтения и отправки сохранятся", "Очистить историю",()=>Run(()=>ClearChatDiskCacheAsync(false)),()=>me!=null&&!demo),
            SettingsAction("Файлы вложений", "Зашифрованные копии на этом устройстве", "Очистить медиа",()=>Run(()=>ClearChatDiskCacheAsync(true)),()=>me!=null&&!demo)),
        SettingsGroup("Локальные данные",
            SettingsAction("Удалить черновики", "Очистить текст и вложения черновиков этого аккаунта; незавершённые отправки сохранятся", "Удалить черновики",
                () => Run(() => ConfirmChatClearAsync(drafts: true)), () => me != null && !demo),
            SettingsAction("Освободить временные данные чатов", "Убрать загруженные вложения и просмотренные диалоги из памяти; черновики и незавершённые отправки сохранятся", "Очистить",
                () => Run(() => ConfirmChatClearAsync(drafts: false)))));

    private FrameworkElement DataSettingsView()
    {
        settingsCacheUsage = new CacheUsageView();
        return SettingsGroups(
            SettingsGroup("",
                settingsCacheUsage,
                SettingsAction("Обложки, звуковые волны и тексты", "Производные файлы можно загрузить повторно; сессия, чаты и обучение Волны сохранятся", "Очистить кэш",
                    () => Run(ClearSettingsCacheAsync))),
            SettingsGroup("История прослушиваний",
                SettingsToggle("soundcloud_history", "Отправлять прослушивания в SoundCloud", "Отдельно от локального обучения Волны; выключение действует сразу; после включения запись начнётся со следующего трека",
                    () => preferences.SendPlaybackHistory, value => UpdatePreferences(p => p with { SendPlaybackHistory = value })),
                SettingsChoice("journal_retention", "Хранить локальный журнал", "Срок сырых событий прослушивания; обученная модель хранится отдельно",
                    () => preferences.JournalRetentionDays, value => UpdatePreferences(p => p with { JournalRetentionDays = value }),
                    ("30 дней", 30), ("90 дней", 90), ("180 дней", 180)),
                SettingsAction("Очистить локальную историю Pookie", "Удалить события этого аккаунта на устройстве; история SoundCloud и обучение останутся", "Очистить историю",
                    () => Run(() => ConfirmWaveDataActionAsync(resetLearning: false)), () => me != null && !demo)),
            SettingsGroup("Папки и диагностика",
                SettingsAction("Данные приложения", "Настройки, локальные данные аккаунтов и кэш", "Открыть папку", () => OpenSettingsPath(dataPaths.Root)),
                SettingsAction("Диагностические журналы", "Технические события помогают разобраться с ошибкой", "Открыть папку", () => OpenSettingsPath(AppLog.DirectoryPath ?? Path.Combine(dataPaths.Root, "Logs"))),
                SettingsAction("Сведения для отчёта", "Версия приложения, ОС и аудиодвижок без токенов и текстов сообщений", "Скопировать", CopySettingsDiagnostics)),
            SettingsGroup("Предпочтения",
                SettingsAction("Сбросить настройки приложения", "Восстановить исходные предпочтения; аккаунт, очередь и накопленное обучение не удаляются", "Сбросить настройки",
                    () => Run(ResetSettingsAsync))));
    }

    private FrameworkElement AboutSettingsView() => SettingsGroups(
        SettingsGroup("Pookie",
            SettingsValue("Версия", "Нативный клиент SoundCloud", () => typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "—"),
            SettingsValue("Платформа", "", () => System.Runtime.InteropServices.RuntimeInformation.OSDescription),
            SettingsValue("Аудио", "Устройство вывода выбирается в операционной системе", () => audioStatus.Value),
            SettingsAction("Исходный код", "github.com/freakdaniel/Pookie", "Открыть проект", () => OpenSettingsUrl("https://github.com/freakdaniel/Pookie"), externalLink: true)),
        SettingsGroup("Авторы и лицензии",
            SettingsAction("Иллюстрации", "Icons8", "Открыть сайт", OpenIllustrationCredits, externalLink: true),
            SettingsAction("Эмодзи", "Twemoji · CC BY 4.0; данные Unicode / CLDR", "Открыть проект", () => OpenSettingsUrl("https://github.com/jdecked/twemoji"), externalLink: true),
            SettingsAction("Лицензии компонентов", "Условия использования библиотек, шрифтов и моделей", "Открыть папку",
                () => OpenSettingsPath(Path.Combine(AppContext.BaseDirectory, "licenses")))));
}
