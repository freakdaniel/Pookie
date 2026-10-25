using System.Text.Json;
using System.Text.Json.Serialization;
using WaveProfile = Pookie.Recommendations.WaveProfile;

namespace Pookie.App.Home;

internal sealed class WaveProfileStore(string directory)
{
    private readonly object gate = new();
    private readonly object pendingSync = new();
    private readonly Dictionary<long, Storage.SnapshotWriter<WaveProfile>> writers = new();
    private readonly Dictionary<long, WaveProfile> accepted = new();
    internal Task SaveAsync(long userId, WaveProfile profile)
    {
        lock (pendingSync)
        {
            if (!writers.TryGetValue(userId,out var writer)) writers[userId] = writer = new(snapshot => { lock(gate) SaveSnapshot(userId,snapshot); });
            accepted[userId] = profile;
            return CompleteSaveAsync(userId,profile,writer,writer.SaveAsync(profile));
        }
    }
    private async Task CompleteSaveAsync(long userId,WaveProfile profile,Storage.SnapshotWriter<WaveProfile> writer,Task saving)
    {
        await saving.ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        lock(pendingSync)
        {
            if (accepted.GetValueOrDefault(userId) != profile || !writer.FlushAsync().IsCompleted) return;
            accepted.Remove(userId); writers.Remove(userId);
        }
    }
    internal Task FlushAsync() { lock(pendingSync)return Task.WhenAll(writers.Values.Select(writer=>writer.FlushAsync())); }
    internal WaveTaste Load(long userId)
    {
        // A rapid account switch must see the accepted snapshot even while its
        // background write is pending; it must not wait for another disk writer.
        lock(pendingSync) if(accepted.TryGetValue(userId,out var snapshot))return new(snapshot);
        lock (gate) return LoadCore(userId);
    }
    private WaveTaste LoadCore(long userId)
    {
        try
        {
            var path = PathFor(userId);
            return new(File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), WaveJson.Default.WaveProfile) : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    internal void Save(long userId, WaveTaste taste)
    {
        lock (gate) SaveCore(userId, taste);
    }
    private void SaveCore(long userId, WaveTaste taste)
    {
        SaveSnapshot(userId,taste.Snapshot()); taste.Saved();
    }
    private void SaveSnapshot(long userId, WaveProfile snapshot)
    {
        var file = PathFor(userId); Directory.CreateDirectory(directory);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, WaveJson.Default.WaveProfile));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal WaveTaste ResetLearning(long userId)
    {
        lock (gate)
        {
            var reset = WithoutLearning(LoadCore(userId).Snapshot());
            SaveCore(userId, reset);
            return reset;
        }
    }
    internal static WaveTaste WithoutLearning(WaveProfile profile) => new(profile with {
        Tracks = profile.Tracks.Where(item => item.Liked || item.Blocked).Select(item => item with {
            Familiar = true, GoodPlays = 0, EarlySkips = 0, ListenedSeconds = 0, LastPlayed = null, Plays = 0
        }).ToArray()
    });
    private string PathFor(long userId) => userId > 0 ? Path.Combine(directory, userId + ".json") : throw new ArgumentOutOfRangeException(nameof(userId));
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WaveProfile))]
internal partial class WaveJson : JsonSerializerContext;
