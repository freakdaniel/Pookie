using System.Globalization;

namespace Pookie.App.Messages;

internal static class ChatPresentation
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    internal static string Time(DateTimeOffset date) => date.ToLocalTime().ToString("HH:mm", Russian);
    internal static string Day(DateTimeOffset date) => date.ToLocalTime().ToString("d MMMM", Russian);
    internal static string InboxTime(DateTimeOffset date, DateTimeOffset now)
    {
        var local = date.ToLocalTime(); var today = now.ToLocalTime().Date;
        if (local.Date == today) return Time(date);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (local.Date >= monday && local.Date < today) return Russian.TextInfo.ToTitleCase(local.ToString("ddd", Russian));
        return local.ToString("dd.MM.yy", Russian);
    }
    internal static string Preview(string body) => string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    internal static string MediaLabel(ChatMediaPackage package)
    {
        if (package.Attachments.Length > 1) return $"Файлы · {package.Attachments.Length}";
        if (package.GifUrl != null) return "GIF";
        if (package.Attachments.FirstOrDefault() is { AsFile: true } document) return document.Name;
        return package.Attachments.FirstOrDefault()?.MediaType switch
        {
            "image/gif" => "GIF", var mime when mime?.StartsWith("image/", StringComparison.Ordinal) == true => "Фотография",
            var mime when mime?.StartsWith("video/", StringComparison.Ordinal) == true => "Видео",
            var mime when mime?.StartsWith("audio/", StringComparison.Ordinal) == true => "Аудио",
            _ => package.Attachments.FirstOrDefault()?.Name ?? "Вложение"
        };
    }
}
