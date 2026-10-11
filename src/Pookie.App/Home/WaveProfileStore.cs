using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.App.Home;

internal sealed class WaveProfileStore(string directory)
{
    internal WaveTaste Load(long userId)
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
        var file = PathFor(userId); Directory.CreateDirectory(directory);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(taste.Snapshot(), WaveJson.Default.WaveProfile));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, file, overwrite: true); taste.Saved();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private string PathFor(long userId) => userId > 0 ? Path.Combine(directory, userId + ".json") : throw new ArgumentOutOfRangeException(nameof(userId));
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WaveProfile))]
internal partial class WaveJson : JsonSerializerContext;
