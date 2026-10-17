using System.Text.Json;
using System.Text.Json.Serialization;
using Pookie.App.Storage;

namespace Pookie.App.Messages;

internal sealed record ChatPending(long RecipientId, string Body, string Label, DateTimeOffset CreatedAt, bool Uncertain = false, string[]? BeforeIds = null)
{
    public override string ToString() => "Pending chat message (content redacted)";
}
internal sealed record ChatLocalState(Dictionary<long, string> Drafts, ChatPending[] Pending);

// Only drafts and the small durable outbox. Received media stays in bounded memory.
internal sealed class ChatLocalStore(AppDataPaths paths, long accountId)
{
    private readonly string directory = Path.Combine(paths.Root, "Chats", accountId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal ChatLocalState Load()
    {
        try
        {
            var path = Path.Combine(directory, "drafts.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) return new([], []);
            var state = JsonSerializer.Deserialize(File.ReadAllText(path), ChatLocalJson.Default.ChatLocalState);
            if (state?.Drafts == null || state.Pending == null) return new([], []);
            return new(state.Drafts.Where(p => p.Key > 0 && p.Value is { Length: <= 20000 }).Take(50).ToDictionary(),
                state.Pending.Where(p => p != null && p.RecipientId > 0 && p.Body is { Length: > 0 and <= 20000 } && p.Label is { Length: <= 255 } &&
                    (p.BeforeIds == null || p.BeforeIds.Length <= 64 && p.BeforeIds.All(id => id is { Length: <= 512 }))).Take(50).ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new([], []); }
    }
    internal void Save(ChatLocalState state)
    {
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
