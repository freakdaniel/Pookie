using System.Text.Json;
using Pookie.App.Playback;
using Pookie.SoundCloud;
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
    public int MaximumVolume { get; set; } = 100;
    public double PreviousVolume { get; set; } = 70;
    public bool DiscordEnabled { get; set; } = true;
    public bool Shuffle { get; set; }
    public bool LikesAsList { get; set; }
    public TrackViewMode? LikesView { get; set; }
    public TrackViewMode PlaylistView { get; set; } = TrackViewMode.List;
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
    public int SchemaVersion { get; set; } = 2;
    public bool SystemMediaEnabled { get; set; } = true;
    public string InterfaceLanguage { get; set; } = "";
    public string StartupPage { get; set; } = "home";
    public bool SmoothScrolling { get; set; } = true;
    public bool RememberSearchSection { get; set; }
    public int LastSearchSection { get; set; }
    public string CommentSort { get; set; } = "NEWEST";
    public bool ShowCommentMarkers { get; set; } = true;
    public bool RestoreLastTrack { get; set; } = true;
    public LastPlayerTrack? LastTrack { get; set; }
    public bool AutoContinue { get; set; } = true;
    public bool NormalizeVolume { get; set; } = true;
    public bool LyricsEnabled { get; set; } = true;
    public bool LyricsAutoScroll { get; set; } = true;
    public bool SendPlaybackHistory { get; set; } = true;
    public Dictionary<string, string> ShortcutOverrides { get; set; } = [];
    public bool ChatSendOnEnter { get; set; } = true;
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
        ShortcutOverrides = ShortcutOverrides ?? [],
        SchemaVersion = Math.Max(2, SchemaVersion),
        Repeat = Enum.IsDefined(Repeat) ? Repeat : RepeatMode.Off,
        LikesView = LikesView is { } view && Enum.IsDefined(view) ? view : LikesAsList ? TrackViewMode.Waveform : TrackViewMode.Cards,
        LikesAsList = LikesView is { } legacyView && Enum.IsDefined(legacyView) ? legacyView != TrackViewMode.Cards : LikesAsList,
        PlaylistView = Enum.IsDefined(PlaylistView) ? PlaylistView : TrackViewMode.List,
        MaximumVolume = MaximumVolume is 150 or 200 ? MaximumVolume : 100,
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, MaximumVolume is 150 or 200 ? MaximumVolume : 100) : 70,
        PreviousVolume = double.IsFinite(PreviousVolume) && PreviousVolume > 0 ? Math.Clamp(PreviousVolume, 1, MaximumVolume is 150 or 200 ? MaximumVolume : 100) : 70,
        InterfaceLanguage = Pookie.Localization.UiText.Choices.Any(choice => choice.Code == InterfaceLanguage) ? InterfaceLanguage : "",
        StartupPage = StartupPage is "home" or "library" ? StartupPage : "home",
        LastSearchSection = LastSearchSection is >= 0 and <= 4 ? LastSearchSection : 0,
        CommentSort = CommentSort is "NEWEST" or "OLDEST" or "TIMESTAMP" ? CommentSort : "NEWEST",
    };
}

// Only the metadata used to display the paused player belongs on disk.
internal sealed record LastPlayerTrack(long AccountId, long Id, string Title, double Duration, double FullDuration,
    string? ArtworkUrl, string? PermalinkUrl, long ArtistId, string Artist, string? ArtistUrl, string? AvatarUrl,
    string? Sharing, PublisherMetadata? PublisherMetadata);

internal sealed class ConfigurationStore(AppDataPaths paths)
{
    internal const string FileName = "settings.json";
    private readonly string file = Path.Combine(paths.Config, FileName);
    private SnapshotWriter<AppConfiguration>? writer;
    internal Task SaveAsync(AppConfiguration configuration) => (writer ??= new(Save)).SaveAsync(configuration.Normalize());
    internal Task FlushAsync() => writer?.FlushAsync() ?? Task.CompletedTask;
    public AppConfiguration Load() => Read(file);

    internal static AppConfiguration Read(string file)
    {
        try
        {
            if (!File.Exists(file)) return new AppConfiguration().Normalize();
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
