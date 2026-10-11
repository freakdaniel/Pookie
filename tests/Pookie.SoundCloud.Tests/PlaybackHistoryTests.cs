using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PlaybackHistoryTests
{
    [Fact]
    public async Task PlaybackHistoryUsesAuthenticatedBrowserWriteAndPropagatesFailure()
    {
        using var http = new HttpClient(new Handler());
        var browser = new Transport();
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test"), BrowserTransport = browser };
        await api.RecordPlaybackAsync(90, "soundcloud:playlists:11");
        Assert.Equal((90L, "soundcloud:playlists:11"), Assert.Single(browser.Plays));
        browser.Fail = true;
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => api.RecordPlaybackAsync(91));
        Assert.Equal(403, error.StatusCode);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.RecordPlaybackAsync(0));
        await Assert.ThrowsAsync<ArgumentException>(() => api.RecordPlaybackAsync(90, "https://evil.test"));
        api.Session = null;
        await Assert.ThrowsAsync<SoundCloudException>(() => api.RecordPlaybackAsync(90));
        Assert.Equal(2, browser.Plays.Count);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("soundcloud:playlists:11", true)]
    [InlineData("soundcloud:users:42", true)]
    [InlineData("soundcloud:system-playlists:your-moods:42:1", true)]
    [InlineData("", false)]
    [InlineData("soundcloud:playlists:0", false)]
    [InlineData("soundcloud:playlists:9007199254740992", false)]
    [InlineData("soundcloud:tracks:90", false)]
    [InlineData("soundcloud:playlists:11\n", false)]
    [InlineData("https://evil.test", false)]
    public void HistoryCommandsCannotChooseArbitraryWrites(string? context, bool valid)
    {
        var command = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "play-history", TrackId: 90, ContextUrn: context);
        Assert.Equal(valid, command.IsValid());
        Assert.False((command with { Url = "https://api-v2.soundcloud.com/me/play-history" }).IsValid());
        Assert.False((command with { UserId = 42 }).IsValid());
        Assert.False((command with { TrackId = 0 }).IsValid());
        Assert.False((command with { TrackId = long.MaxValue }).IsValid());
        if (context != null) Assert.False((command with { Operation = "me" }).IsValid());
        Assert.False(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/me/play-history"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Playback history must not bypass the browser session.");
    }

    private sealed class Transport : ISoundCloudBrowserTransport
    {
        internal readonly List<(long Track, string? Context)> Plays = [];
        internal bool Fail;
        public Task RecordPlaybackAsync(long trackId, string? contextUrn = null, CancellationToken cancellationToken = default)
        {
            Plays.Add((trackId, contextUrn));
            return Fail ? Task.FromException(new SoundCloudException("Fixture block", 403)) : Task.CompletedTask;
        }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
