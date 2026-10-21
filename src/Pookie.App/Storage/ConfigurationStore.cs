using System.Text.Json;
using Pookie.App.Playback;
using System.Text.Json.Serialization;

namespace Pookie.App.Storage;

internal sealed record AppConfiguration
{
    [JsonConstructor]
    public AppConfiguration() { }

    public AppConfiguration(double volume, double previousVolume, bool discordEnabled, bool shuffle, bool likesAsList, RepeatMode repeat = RepeatMode.Off)
    {
        Volume = volume; PreviousVolume = previousVolume; DiscordEnabled = discordEnabled;
        Shuffle = shuffle; LikesAsList = likesAsList; Repeat = repeat;
    }

    public double Volume { get; set; } = 70;
    public double PreviousVolume { get; set; } = 70;
    public bool DiscordEnabled { get; set; } = true;
    public bool Shuffle { get; set; }
    public bool LikesAsList { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
    public int SchemaVersion { get; set; } = 2;
    public bool SystemMediaEnabled { get; set; } = true;
    public string StartupPage { get; set; } = "home";
    public bool SmoothScrolling { get; set; } = true;
    public bool RememberSearchSection { get; set; }
    public int LastSearchSection { get; set; }
    public string CommentSort { get; set; } = "NEWEST";
    public bool ShowCommentMarkers { get; set; } = true;
    public bool AutoContinue { get; set; } = true;
    public bool NormalizeVolume { get; set; } = true;
    public bool LyricsEnabled { get; set; } = true;
    public bool LyricsAutoScroll { get; set; } = true;
    public bool WaveLearningEnabled { get; set; } = true;
    public bool WaveAnalysisEnabled { get; set; } = true;
    public int JournalRetentionDays { get; set; } = 90;
    public bool SendPlaybackHistory { get; set; } = true;
    public bool ChatSendOnEnter { get; set; }
    public bool ChatAutoLoad { get; set; } = true;
    public bool ChatAnimateGifs { get; set; } = true;
    public bool ChatAutoPlayVideo { get; set; } = true;
    public bool ChatSaveDrafts { get; set; } = true;
    public bool ChatBackgroundRefresh { get; set; } = true;
    public bool KlipyEnabled { get; set; } = true;
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    public AppConfiguration Normalize() => this with
    {
        SchemaVersion = Math.Max(2, SchemaVersion),
        Repeat = Enum.IsDefined(Repeat) ? Repeat : RepeatMode.Off,
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 100) : 70,
        PreviousVolume = double.IsFinite(PreviousVolume) && PreviousVolume > 0 ? Math.Clamp(PreviousVolume, 1, 100) : 70,
        StartupPage = StartupPage is "home" or "library" ? StartupPage : "home",
        LastSearchSection = LastSearchSection is >= 0 and <= 4 ? LastSearchSection : 0,
        CommentSort = CommentSort is "NEWEST" or "OLDEST" or "TIMESTAMP" ? CommentSort : "NEWEST",
        JournalRetentionDays = JournalRetentionDays is 30 or 90 or 180 ? JournalRetentionDays : 90
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
