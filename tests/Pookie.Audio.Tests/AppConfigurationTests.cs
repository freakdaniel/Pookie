using System.Text.Json;
using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class AppConfigurationTests
{
    [Fact]
    public void Legacy_configuration_keeps_playback_preferences_and_defaults_new_settings()
    {
        var root = Directory.CreateTempSubdirectory("pookie-settings-legacy-").FullName;
        try
        {
            var paths = new AppDataPaths(root);
            File.WriteAllText(Path.Combine(paths.Config, "settings.json"), """
                {"volume":34,"previous_volume":86,"discord_enabled":false,"shuffle":true,"likes_as_list":true,"repeat":2}
                """);
            var saved = new ConfigurationStore(paths).Load();
            Assert.Equal(34, saved.Volume); Assert.False(saved.DiscordEnabled); Assert.True(saved.LikesAsList);
            Assert.True(saved.AutoContinue); Assert.True(saved.NormalizeVolume); Assert.True(saved.ChatSaveDrafts);
            Assert.True(saved.WaveLearningEnabled); Assert.Equal(90, saved.JournalRetentionDays);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Settings_roundtrip_preserves_new_preferences_and_unknown_future_fields()
    {
        var root = Directory.CreateTempSubdirectory("pookie-settings-roundtrip-").FullName;
        try
        {
            var paths = new AppDataPaths(root);
            File.WriteAllText(Path.Combine(paths.Config, "settings.json"), """
                {"volume":34,"future_preference":{"mode":"keep"}}
                """);
            var store = new ConfigurationStore(paths);
            store.Save(store.Load() with { StartupPage = "library", AutoContinue = false, LyricsEnabled = false,
                ShowCommentMarkers = false, CommentSort = "TIMESTAMP", ChatSendOnEnter = true, ChatAutoLoad = false,
                ChatAnimateGifs = false, ChatAutoPlayVideo = false, WaveLearningEnabled = false, JournalRetentionDays = 30,
                RememberSearchSection = true, LastSearchSection = 4, SendPlaybackHistory = false, SystemMediaEnabled = false });
            var saved = new ConfigurationStore(paths).Load();
            Assert.Equal("library", saved.StartupPage); Assert.False(saved.AutoContinue); Assert.False(saved.LyricsEnabled);
            Assert.True(saved.ChatSendOnEnter); Assert.False(saved.ChatAutoLoad); Assert.False(saved.ChatAnimateGifs);
            Assert.False(saved.ChatAutoPlayVideo); Assert.False(saved.WaveLearningEnabled); Assert.Equal(30, saved.JournalRetentionDays);
            Assert.Equal("TIMESTAMP", saved.CommentSort); Assert.False(saved.ShowCommentMarkers); Assert.Equal(4, saved.LastSearchSection);
            Assert.False(saved.SendPlaybackHistory); Assert.False(saved.SystemMediaEnabled);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Config, "settings.json")));
            Assert.Equal("keep", json.RootElement.GetProperty("future_preference").GetProperty("mode").GetString());
            Assert.Empty(Directory.GetFiles(paths.Config, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Invalid_selection_values_normalize_to_supported_choices()
    {
        var saved = new AppConfiguration() with { SchemaVersion = -1, StartupPage = "invalid", LastSearchSection = 999,
            CommentSort = "invalid", JournalRetentionDays = -1 };
        var normalized = saved.Normalize();
        Assert.Equal(2, normalized.SchemaVersion); Assert.Equal("home", normalized.StartupPage);
        Assert.Equal(0, normalized.LastSearchSection); Assert.Equal("NEWEST", normalized.CommentSort);
        Assert.Equal(90, normalized.JournalRetentionDays);
    }
}
