using Pookie.Localization;
using System.Globalization;

namespace Pookie.App.Messages;

internal static class ChatPresentation
{
    internal static string Time(DateTimeOffset date) => date.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    internal static string Day(DateTimeOffset date) => date.ToLocalTime().ToString("M", CultureInfo.CurrentCulture);
    internal static string InboxTime(DateTimeOffset date, DateTimeOffset now)
    {
        var local = date.ToLocalTime(); var today = now.ToLocalTime().Date;
        if (local.Date == today) return Time(date);
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weekStart = today.AddDays(-((7 + (int)today.DayOfWeek - (int)firstDay) % 7));
        if (local.Date >= weekStart && local.Date < today) return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(local.ToString("ddd", CultureInfo.CurrentCulture));
        return local.ToString("d", CultureInfo.CurrentCulture);
    }
    internal static string Preview(string body) => string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    internal static string MediaLabel(ChatMediaPackage package)
    {
        if(package.Attachments.FirstOrDefault()?.Voice!=null)return UiText.Get("chats.voice.label");
        if (package.Attachments.Length > 1) return UiText.Format("chats.attachments.count", package.Attachments.Length);
        if (package.GifUrl != null) return "GIF";
        if (package.Attachments.FirstOrDefault() is { AsFile: true } document) return document.Name;
        return package.Attachments.FirstOrDefault()?.MediaType switch
        {
            "image/gif" => "GIF", var mime when mime?.StartsWith("image/", StringComparison.Ordinal) == true => UiText.Get("chats.media.photo"),
            var mime when mime?.StartsWith("video/", StringComparison.Ordinal) == true => UiText.Get("chats.media.video"),
            var mime when mime?.StartsWith("audio/", StringComparison.Ordinal) == true => UiText.Get("common.audio"),
            _ => package.Attachments.FirstOrDefault()?.Name ?? UiText.Get("chats.attachment.label")
        };
    }
}
