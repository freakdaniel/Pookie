using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.App.Storage;

internal sealed record AppConfiguration(double Volume = 70, double PreviousVolume = 70,
    bool DiscordEnabled = true, bool Shuffle = false, bool LikesAsList = false)
{
    public AppConfiguration Normalize() => this with
    {
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 100) : 70,
        PreviousVolume = double.IsFinite(PreviousVolume) && PreviousVolume > 0 ? Math.Clamp(PreviousVolume, 1, 100) : 70
    };
}

internal sealed class ConfigurationStore(AppDataPaths paths)
{
    private readonly string file = Path.Combine(paths.Config, "settings.json");
    public AppConfiguration Load()
    {
        try
        {
            if (!File.Exists(file)) return new();
            return (JsonSerializer.Deserialize(File.ReadAllText(file), StorageJson.Default.AppConfiguration) ?? new()).Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(AppConfiguration configuration)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(configuration.Normalize(), StorageJson.Default.AppConfiguration));
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(AppConfiguration))]
internal partial class StorageJson : JsonSerializerContext;
