using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBlock? chatCacheUsageLabel;
    private FrameworkElement ChatCacheStatistics()
    {
        chatCacheUsageLabel = new TextBlock().Text("История: до 256 МиБ · медиа: до 512 МиБ на аккаунт").FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        Run(RefreshChatCacheStatisticsAsync);
        return chatCacheUsageLabel;
    }
    private async Task RefreshChatCacheStatisticsAsync()
    {
        EnsureChatAccount(); var account = chatAccountId; var label = chatCacheUsageLabel;
        var history = chatHistory == null ? 0 : await chatHistory.SizeAsync();
        var media = chatMedia.DiskCache == null ? 0 : await chatMedia.DiskCache.SizeAsync();
        if (account == chatAccountId && !disposed && label != null) label.Text = $"История и индекс: {FormatSettingsBytes(history)} / 256 МиБ\nМедиа: {FormatSettingsBytes(media)} / 512 МиБ\nИзбранное хранится отдельно и не вытесняется.";
    }
    private Task ClearChatDiskCacheAsync(bool media) => SettingsOperationAsync(async () =>
    {
        EnsureChatAccount(); var account = chatAccountId;
        if (media) { if (chatMedia.DiskCache != null) await chatMedia.DiskCache.ClearAsync(); ClearChatMemoryCache(); }
        else { if (chatHistory != null) await chatHistory.ClearAsync(); chatThreadCache.Clear(); chatThreadRecency.Clear(); }
        if (account != chatAccountId) return "Аккаунт изменился";
        await RefreshChatCacheStatisticsAsync();
        return media ? "Кеш вложений очищен. Папки, избранное и черновики сохранены." : "Кеш истории очищен. Папки, избранное и черновики сохранены.";
    });
}
