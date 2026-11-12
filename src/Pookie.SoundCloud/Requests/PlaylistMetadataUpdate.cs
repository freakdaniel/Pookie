using System.Globalization;

namespace Pookie.SoundCloud;

// Null leaves the server value intact; an empty string clears an optional field.
public sealed record PlaylistMetadataUpdate(string? Permalink = null, string? Genre = null, string? TagList = null,
    string? ReleaseDate = null, string? PlaylistType = null, string? PurchaseUrl = null, string? LabelName = null, string? License = null)
{
    public bool HasChanges() => Permalink != null || Genre != null || TagList != null || ReleaseDate != null ||
        PlaylistType != null || PurchaseUrl != null || LabelName != null || License != null;
    public bool IsValid() =>
        (Permalink == null || Permalink.Length is > 0 and <= 255 && Permalink.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) &&
        Text(Genre, 255) && Text(TagList, 4000) && Text(LabelName, 255) &&
        (ReleaseDate == null || ReleaseDate == "" || DateOnly.TryParseExact(ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) &&
        (PlaylistType == null || PlaylistType is "playlist" or "album" or "ep" or "single" or "compilation") &&
        (PurchaseUrl == null || PurchaseUrl == "" || PurchaseUrl.Length <= 2000 && Uri.TryCreate(PurchaseUrl, UriKind.Absolute, out var url) &&
            url.Scheme is "http" or "https" && url.UserInfo.Length == 0) &&
        (License == null || License is "all-rights-reserved" or "no-rights-reserved" or "cc-by" or "cc-by-sa" or "cc-by-nd" or "cc-by-nc" or "cc-by-nc-sa" or "cc-by-nc-nd" or "cc0");
    private static bool Text(string? value, int max) => value == null || value.Length <= max && !value.Any(char.IsControl);
}
