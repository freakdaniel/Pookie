using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Storage;
using Pookie.Logging;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FrameworkElement GeneralSettingsView() => SettingsGroups(
        SettingsGroup(UiText.Get("settings.connections"),
            SettingsValue("SoundCloud", UiText.Get("settings.account.description"),
                () => demo ? UiText.Get("demo.mode") : signedIn.Value ? profile.Value : UiText.Get("auth.status.signedOut")),
            SettingsAction(UiText.Get("account.soundcloud"), UiText.Get("account.signOut.settingsDescription"), UiText.Get("account.signOut"),
                () => Run(() => SettingsOperationAsync(async () =>
                {
                    var account = me?.Id;
                    if (!await ConfirmAsync(UiText.Get("account.signOut.confirmation"), detail: UiText.Get("account.signOut.description"), owner: Window)) return "";
                    if (me?.Id != account) return UiText.Get("account.retryAfterChange");
                    await LogoutAsync(); return UiText.Get("account.status.sessionEnded");
                })), () => signedIn.Value && !demo),
            SettingsToggle("discord", UiText.Get("settings.discord.showMusic"), UiText.Get("settings.discord.description"),
                () => discordEnabled.Value, value => { discordEnabled.Value = value; if (presence != null) presence.Enabled = value; }),
            SettingsToggle("system_media", UiText.Get("settings.playback.systemControls"), UiText.Get("settings.playback.systemControlsDescription"),
                () => preferences.SystemMediaEnabled, value => UpdatePreferences(p => p with { SystemMediaEnabled = value }))),
        SettingsGroup(UiText.Get("settings.interface.title"),
            SettingsChoiceWithContent("language", UiText.Get("settings.language.label"), "",
                () => preferences.InterfaceLanguage, value => UpdatePreferences(p => p with { InterfaceLanguage = value }), LanguageOption,
                [(UiText.Get("settings.language.system"), ""), .. UiText.Choices])),
        SettingsGroup(UiText.Get("settings.startup.heading"),
            SettingsChoice("startup", UiText.Get("settings.startup.page"), UiText.Get("settings.startup.pageDescription"),
                () => preferences.StartupPage, value => UpdatePreferences(p => p with { StartupPage = value }),
                (UiText.Get("header.home"), "home"), (UiText.Get("header.library"), "library"))));

    private static FrameworkElement LanguageOption(string title, string language)
    {
        var country = language switch { "en-US" => "us", "sv" => "se", _ => language };
        FrameworkElement icon = language.Length == 0 ? Icons.View("globe", 18, TrackButtons.OnSurface).Center() :
            new Border().Width(22).Height(16).CornerRadius(3).ClipToBounds().BorderThickness(0)
                .Child(new Image { Source = Icons.CountryFlag(country), VectorCacheMode = ImageVectorCacheMode.Direct }
                    .StretchMode(Stretch.UniformToFill));
        return new Grid().Columns("22,*").Rows("*").Spacing(9).Children(icon.Center(),
            new TextBlock().Text(title).FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().Column(1));
    }

    private FrameworkElement InterfaceSettingsView() => SettingsGroups(
        SettingsGroup(UiText.Get("settings.libraryAndSearch"),
            SettingsChoice("likes", UiText.Get("settings.library.likedTrackLayout"), UiText.Get("settings.library.layoutDescription"),
                () => likesMode.Value, value => likesMode.Value = value, (UiText.Get("library.layout.square"), TrackViewMode.Cards),
                (UiText.Get("library.layout.waveform"), TrackViewMode.Waveform), (UiText.Get("library.layout.list"), TrackViewMode.List)),
            SettingsToggle("search_section", UiText.Get("settings.search.rememberCategory"), UiText.Get("settings.search.rememberCategoryDescription"),
                () => preferences.RememberSearchSection, value => UpdatePreferences(p => p with { RememberSearchSection = value })),
            SettingsToggle("smooth_scroll", UiText.Get("settings.interface.smoothScroll"), UiText.Get("settings.interface.smoothScrollDescription"),
                () => preferences.SmoothScrolling, value => UpdatePreferences(p => p with { SmoothScrolling = value }))),
        SettingsGroup(UiText.Get("settings.trackPage"),
            SettingsToggle("comment_markers", UiText.Get("settings.comments.waveformMarkers"), UiText.Get("settings.comments.markerDescription"),
                () => preferences.ShowCommentMarkers, value => UpdatePreferences(p => p with { ShowCommentMarkers = value })),
            SettingsChoice("comment_sort", UiText.Get("settings.comments.sortDefault"), UiText.Get("settings.comments.sortDescription"),
                () => preferences.CommentSort, value => UpdatePreferences(p => p with { CommentSort = value }),
                (UiText.Get("comments.sort.newest"), "NEWEST"), (UiText.Get("comments.sort.oldest"), "OLDEST"), (UiText.Get("comments.sort.trackTime"), "TIMESTAMP"))));

    private FrameworkElement PlaybackSettingsView() => SettingsGroups(
        SettingsGroup(UiText.Get("chats.media.music"),
            SettingsChoice("maximum_volume", UiText.Get("settings.volume.maximum"), UiText.Get("settings.volume.maximumDescription"),
                () => preferences.MaximumVolume, value => UpdatePreferences(p => p with { MaximumVolume = value }),
                ("100%", 100), ("150%", 150), ("200%", 200)),
            SettingsToggle("restore_last_track", UiText.Get("settings.playback.restoreLastTrack"), UiText.Get("settings.playback.restoreDescription"),
                () => preferences.RestoreLastTrack, value => UpdatePreferences(p => p with { RestoreLastTrack = value })),
            SettingsToggle("auto_continue", UiText.Get("settings.playback.autoContinue"), UiText.Get("settings.playback.autoContinueDescription"),
                () => preferences.AutoContinue, value => UpdatePreferences(p => p with { AutoContinue = value })),
            SettingsToggle("normalization", UiText.Get("settings.playback.normalizeVolume"), UiText.Get("settings.playback.normalizeDescription"),
                () => preferences.NormalizeVolume, value => UpdatePreferences(p => p with { NormalizeVolume = value }))),
        SettingsGroup(UiText.Get("lyrics.title"),
            SettingsToggle("lyrics", UiText.Get("settings.lyrics.find"), UiText.Get("settings.lyrics.providerDescription"),
                () => preferences.LyricsEnabled, value => UpdatePreferences(p => p with { LyricsEnabled = value })),
            SettingsToggle("lyrics_follow", UiText.Get("settings.lyrics.autoScroll"), UiText.Get("settings.lyrics.autoScrollDescription"),
                () => preferences.LyricsAutoScroll, value => UpdatePreferences(p => p with { LyricsAutoScroll = value }))));

    private FrameworkElement ChatSettingsView() => SettingsGroups(
        SettingsGroup(UiText.Get("header.messages"),
            SettingsAction(UiText.Get("settings.chats.sendShortcut"), UiText.Get("settings.chats.sendShortcutDescription"), UiText.Get("settings.shortcuts.title"),
                () => Run(() => ShowSettingsAsync(SettingsSection.Shortcuts))),
            SettingsToggle("chat_drafts", UiText.Get("settings.chats.saveDrafts"), UiText.Get("settings.chats.draftsDescription"),
                () => preferences.ChatSaveDrafts, value => UpdatePreferences(p => p with { ChatSaveDrafts = value })),
            SettingsToggle("chat_background", UiText.Get("settings.chats.backgroundRefresh"), UiText.Get("settings.chats.backgroundRefreshDescription"),
                () => preferences.ChatBackgroundRefresh, value => UpdatePreferences(p => p with { ChatBackgroundRefresh = value }))),
        SettingsGroup(UiText.Get("chats.attachments.label"),
            SettingsChoice("chat_download", UiText.Get("settings.chats.downloadIncoming"), UiText.Get("settings.chats.autoDownloadDescription"),
                () => preferences.ChatAutoLoad, value => UpdatePreferences(p => p with { ChatAutoLoad = value }), (UiText.Get("settings.choice.automatic"), true), (UiText.Get("settings.chats.onClick"), false)),
            SettingsToggle("chat_gifs", UiText.Get("settings.chats.animateGifs"), UiText.Get("settings.chats.gifAnimationDescription"),
                () => preferences.ChatAnimateGifs, value => UpdatePreferences(p => p with { ChatAnimateGifs = value })),
            SettingsToggle("chat_video", UiText.Get("settings.chats.autoplayVideos"), UiText.Get("settings.chats.videoPreviewDescription"),
                () => preferences.ChatAutoPlayVideo, value => UpdatePreferences(p => p with { ChatAutoPlayVideo = value })),
            SettingsToggle("klipy", UiText.Get("settings.chats.gifSearch"), UiText.Get("settings.chats.gifProviderDescription"),
                () => preferences.KlipyEnabled, value => UpdatePreferences(p => p with { KlipyEnabled = value })),
            new TextBlock().Text(UiText.Get("settings.chats.attachmentHostingDescription"))
                .FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Margin(0, 14)),
        SettingsGroup(UiText.Get("settings.chats.extensions"),
            new TextBlock().Text(UiText.Get("settings.chats.compatibilityDescription")).FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap)),
        SettingsGroup(UiText.Get("settings.chats.cache"),
            ChatCacheStatistics(),
            SettingsAction(UiText.Get("settings.history.title"), UiText.Get("settings.chats.clearHistoryDescription"), UiText.Get("history.clear"),()=>Run(()=>ClearChatDiskCacheAsync(false)),()=>me!=null&&!demo),
            SettingsAction(UiText.Get("chats.attachments.fileFilter"), UiText.Get("settings.chats.encryptedCopies"), UiText.Get("chats.cache.clearMedia"),()=>Run(()=>ClearChatDiskCacheAsync(true)),()=>me!=null&&!demo)),
        SettingsGroup(UiText.Get("settings.data.local"),
            SettingsAction(UiText.Get("settings.chats.deleteDrafts"), UiText.Get("settings.chats.clearDraftsDescription"), UiText.Get("settings.chats.deleteDrafts"),
                () => Run(() => ConfirmChatClearAsync(drafts: true)), () => me != null && !demo),
            SettingsAction(UiText.Get("settings.chats.clearTemporary"), UiText.Get("settings.chats.clearTemporaryDescription"), UiText.Get("common.clear"),
                () => Run(() => ConfirmChatClearAsync(drafts: false)))));

    private FrameworkElement DataSettingsView()
    {
        settingsCacheUsage = new CacheUsageView();
        return SettingsGroups(
            SettingsGroup("",
                settingsCacheUsage,
                SettingsAction(UiText.Get("cache.content.description"), UiText.Get("settings.cache.clearDescription"), UiText.Get("cache.clear"),
                    () => Run(ClearSettingsCacheAsync))),
            SettingsGroup(UiText.Get("history.listening.title"),
                SettingsToggle("soundcloud_history", UiText.Get("settings.history.sendActivity"), UiText.Get("settings.history.activityDescription"),
                    () => preferences.SendPlaybackHistory, value => UpdatePreferences(p => p with { SendPlaybackHistory = value }))),
            SettingsGroup(UiText.Get("settings.diagnostics.folders"),
                SettingsAction(UiText.Get("settings.data.appData"), UiText.Get("settings.data.description"), UiText.Get("files.openFolder"), () => OpenSettingsPath(dataPaths.Root)),
                SettingsAction(UiText.Get("settings.diagnostics.logs"), UiText.Get("settings.diagnostics.logsDescription"), UiText.Get("files.openFolder"), () => OpenSettingsPath(AppLog.DirectoryPath ?? Path.Combine(dataPaths.Root, "Logs"))),
                SettingsAction(UiText.Get("settings.diagnostics.reportDetails"), UiText.Get("settings.diagnostics.reportDescription"), UiText.Get("clipboard.action.copy"), CopySettingsDiagnostics)),
            SettingsGroup(UiText.Get("settings.preferences"),
                SettingsAction(UiText.Get("settings.reset.appAction"), UiText.Get("settings.reset.summary"), UiText.Get("settings.reset.action"),
                    () => Run(ResetSettingsAsync))));
    }

    private FrameworkElement AboutSettingsView() => SettingsGroups(
        SettingsGroup("Pookie",
            SettingsValue(UiText.Get("common.version"), UiText.Get("settings.about.tagline"), () => typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "—"),
            SettingsValue(UiText.Get("settings.about.platform"), "", () => System.Runtime.InteropServices.RuntimeInformation.OSDescription),
            SettingsValue(UiText.Get("common.audio"), UiText.Get("settings.playback.outputDeviceDescription"), () => audioStatus.Value),
            SettingsAction(UiText.Get("settings.about.sourceCode"), "github.com/freakdaniel/Pookie", UiText.Get("settings.about.openProject"), () => OpenSettingsUrl("https://github.com/freakdaniel/Pookie"), externalLink: true)),
        SettingsGroup(UiText.Get("settings.about.credits"),
            SettingsAction(UiText.Get("settings.about.illustrations"), "Icons8", UiText.Get("links.openWebsite"), OpenIllustrationCredits, externalLink: true),
            SettingsAction(UiText.Get("chats.emoji.label"), UiText.Get("credits.emoji.source"), UiText.Get("settings.about.openProject"), () => OpenSettingsUrl("https://github.com/samuelngs/apple-emoji-ttf"), externalLink: true),
            SettingsAction(UiText.Get("settings.about.componentLicenses"), UiText.Get("settings.about.licensesDescription"), UiText.Get("files.openFolder"),
                () => OpenSettingsPath(Path.Combine(AppContext.BaseDirectory, "licenses")))));
}
