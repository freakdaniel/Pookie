namespace Pookie.SoundCloud;

public sealed record FeedEntry(string Key, LibraryItem Item, SoundCloudUser? Actor, string Type, string? CreatedAt)
{
    public bool IsRepost => Type.Contains("repost", StringComparison.OrdinalIgnoreCase);
}
public sealed record FeedPage(FeedEntry[] Entries, string? NextHref);
