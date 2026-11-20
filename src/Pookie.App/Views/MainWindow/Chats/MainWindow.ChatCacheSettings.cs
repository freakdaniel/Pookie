using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBlock? chatCacheUsageLabel;
    private FrameworkElement ChatCacheStatistics()
    {
        chatCacheUsageLabel = new TextBlock().Text(UiText.Get("settings.chats.cacheLimits")).FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        Run(RefreshChatCacheStatisticsAsync);
        return chatCacheUsageLabel;
    }
    private async Task RefreshChatCacheStatisticsAsync()
    {
        await EnsureChatAccountAsync(); var account = chatAccountId; var label = chatCacheUsageLabel;
        var history = chatHistory == null ? 0 : await chatHistory.SizeAsync();
        var media = chatMedia.DiskCache == null ? 0 : await chatMedia.DiskCache.SizeAsync();
        if (account == chatAccountId && !disposed && label != null) label.Text = UiText.Format("settings.chats.cacheUsage", FormatSettingsBytes(history), FormatSettingsBytes(media));
    }
    private Task ClearChatDiskCacheAsync(bool media) => SettingsOperationAsync(async () =>
    {
        await EnsureChatAccountAsync(); var account = chatAccountId;
        if (media) { if (chatMedia.DiskCache != null) await chatMedia.DiskCache.ClearAsync(); ClearChatMemoryCache(); }
        else { if (chatHistory != null) await chatHistory.ClearAsync(); chatThreadCache.Clear(); chatThreadRecency.Clear(); chatReplyLookups.Clear(); }
        if (account != chatAccountId) return UiText.Get("account.status.changed");
        await RefreshChatCacheStatisticsAsync();
        return media ? UiText.Get("chats.cache.attachmentsCleared") : UiText.Get("chats.cache.historyCleared");
    });
}
