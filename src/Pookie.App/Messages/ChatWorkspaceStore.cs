using System.Text.Json;
using System.Text.Json.Serialization;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Messages;

internal sealed record ChatFolder(string Id, string Name, long[] Peers);
internal sealed record ChatFavorite(long PeerId, ChatMessage Message, DateTimeOffset SavedAt);
internal sealed record ChatReadPosition(string? MessageId, double Y, bool Bottom);
internal sealed record ChatVoiceDraft(string Path, ChatVoiceMetadata Metadata);
internal sealed record ChatDraft(string Text, ChatReplyReference? Reply = null, ChatVoiceDraft? Voice = null);
internal sealed record ChatWorkspaceState(ChatFolder[] Folders, long[] Pins, ChatFavorite[] Favorites,
    Dictionary<long, ChatReadPosition> Positions, long? LastPeer = null, Dictionary<long, ChatDraft>? Drafts = null, bool SavedOpen = false);

/// <summary>Durable account data. Never placed under Cache or evicted with history.</summary>
internal sealed class ChatWorkspaceStore(AppDataPaths paths, long account)
{
    private readonly string directory = Path.Combine(paths.Root, "Chats", account.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal ChatWorkspaceState Load()
    {
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            if (!File.Exists(path)) return Empty;
            using var input = File.OpenRead(path);
            var state = JsonSerializer.Deserialize(input, ChatWorkspaceJson.Default.ChatWorkspaceState);
            if (state?.Folders == null || state.Pins == null || state.Favorites == null || state.Positions == null) return Empty;
            return state with
            {
                Folders = state.Folders.Where(f => f is { Id.Length: > 0 and <= 64, Name.Length: > 0 and <= 40, Peers.Length: <= 2000 }).Take(30).ToArray(),
                Pins = state.Pins.Where(p => p > 0).Distinct().Take(100).ToArray(),
                Favorites = state.Favorites.Where(f => f.PeerId > 0 && f.Message is { Body.Length: <= 20000, Id.Length: > 0 and <= 512 }).DistinctBy(f => (f.PeerId, f.Message.Id)).ToArray(),
                Positions = state.Positions.Where(p => p.Key > 0 && p.Value != null && double.IsFinite(p.Value.Y) && (p.Value.MessageId == null || p.Value.MessageId.Length <= 512)).Take(2000).ToDictionary(),
                Drafts = state.Drafts?.Where(p => p.Key > 0 && p.Value is { Text.Length: <= 20000 } &&
                    (p.Value.Reply == null || p.Value.Reply is { MessageId.Length: > 0 and <= 512, Preview.Length: <= 160 } && p.Value.Reply.PeerId == p.Key) &&
                    (p.Value.Voice == null || p.Value.Voice is { Path.Length: > 0 and <= 4096, Metadata.Valid: true })).Take(50).ToDictionary()
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return Empty; }
    }
    internal static ChatWorkspaceState Empty => new([], [], [], []);
    internal void Save(ChatWorkspaceState state)
    {
        AppDataPaths.CreatePrivateDirectory(directory);
        var path = Path.Combine(directory, "workspace.tmp");
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path, options)) { JsonSerializer.Serialize(stream, state, ChatWorkspaceJson.Default.ChatWorkspaceState); stream.Flush(true); }
        File.Move(path, Path.Combine(directory, "workspace.json"), true);
    }
}
[JsonSerializable(typeof(ChatWorkspaceState))]
internal partial class ChatWorkspaceJson : JsonSerializerContext;
