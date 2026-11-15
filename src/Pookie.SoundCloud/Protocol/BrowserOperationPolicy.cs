namespace Pookie.SoundCloud;

// Shared by the process host, embedded host and injected JavaScript. A write
// canceled during verification must tear down its worker, never become a read.
public static class BrowserOperationPolicy
{
    public const string WriteOperations = "like repost follow playlist-add playlist-create playlist-edit playlist-like playlist-repost comment-write play-history";
    private static readonly HashSet<string> writes = WriteOperations.Split(' ').ToHashSet(StringComparer.Ordinal);
    public static bool IsWrite(BrowserRequestCommand command) => command.Chat?.IsWrite == true ||
        writes.Contains(command.Operation);
}
