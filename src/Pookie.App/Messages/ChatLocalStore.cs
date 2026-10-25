using System.Text.Json;
using System.Text.Json.Serialization;
using Pookie.App.Storage;

namespace Pookie.App.Messages;

internal sealed record ChatPending(long RecipientId, string Body, string Label, DateTimeOffset CreatedAt, bool Uncertain = false, string[]? BeforeIds = null, string? ClientContent = null)
{
    public override string ToString() => "Pending chat message (content redacted)";
}
internal sealed record ChatLocalState(Dictionary<long, string> Drafts, ChatPending[] Pending, Dictionary<long, ChatUnreadState>? Unread = null);

// Backward-compatible drafts and durable outbox. History and workspace data have separate stores.
internal sealed class ChatLocalStore(AppDataPaths paths, long accountId)
{
    private readonly string directory = Path.Combine(paths.Root, "Chats", accountId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private SnapshotWriter<ChatLocalState>? writer;
    private ChatLocalState? lastSnapshot;
    private Task lastSave = Task.CompletedTask;
    internal Task SaveAsync(ChatLocalState state, bool saveDrafts = true)
    {
        state = state with { Drafts = saveDrafts ? new(state.Drafts) : [], Pending = state.Pending.ToArray(), Unread = state.Unread == null ? null : new(state.Unread) };
        if (!lastSave.IsFaulted && lastSnapshot is { } previous && Same(previous.Drafts,state.Drafts) && Same(previous.Unread,state.Unread) && previous.Pending.SequenceEqual(state.Pending)) return lastSave;
        lastSnapshot = state;
        return lastSave = (writer ??= new(s => Save(s))).SaveAsync(state);
    }
    private static bool Same<T>(Dictionary<long,T>? left, Dictionary<long,T>? right) => left == null || right == null ? left == right :
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key,out var value) && EqualityComparer<T>.Default.Equals(value,pair.Value));
    internal Task FlushAsync() => writer?.FlushAsync() ?? Task.CompletedTask;
    internal ChatLocalState Load()
    {
        try
        {
            var path = Path.Combine(directory, "drafts.json");
            if (!File.Exists(path)) return new([], []);
            using var input = File.OpenRead(path);
            var state = JsonSerializer.Deserialize(input, ChatLocalJson.Default.ChatLocalState);
            if (state?.Drafts == null || state.Pending == null) return new([], []);
            return new(state.Drafts.Where(p => p.Key > 0 && p.Value is { Length: > 0 and <= 20000 }).ToDictionary(),
                state.Pending.Where(p => p != null && p.RecipientId > 0 && p.Body is { Length: > 0 and <= 20000 } && p.Label is { Length: <= 255 } &&
                    (p.BeforeIds == null || p.BeforeIds.Length <= 64 && p.BeforeIds.All(id => id is { Length: <= 512 })) &&
                    (p.ClientContent == null || p.ClientContent.Length <= ChatProtocol.MaxHostedContent)).ToArray(),
                state.Unread?.Where(p => p.Key > 0 && p.Value is { LastId.Length: <= 512, Count: >= 0 and <= 9999 }).Take(500).ToDictionary());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new([], []); }
    }
    internal void Save(ChatLocalState state, bool saveDrafts = true)
    {
        if (!saveDrafts) state = state with { Drafts = [] };
        AppDataPaths.CreatePrivateDirectory(Path.GetDirectoryName(directory)!);
        AppDataPaths.CreatePrivateDirectory(directory);
        var temporary = Path.Combine(directory, "drafts.tmp");
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var output = new FileStream(temporary, options))
        {
            JsonSerializer.Serialize(output, state, ChatLocalJson.Default.ChatLocalState);
            output.Flush(flushToDisk: true);
        }
        File.Move(temporary, Path.Combine(directory, "drafts.json"), overwrite: true);
    }
}
[JsonSerializable(typeof(ChatLocalState))]
internal partial class ChatLocalJson : JsonSerializerContext;
