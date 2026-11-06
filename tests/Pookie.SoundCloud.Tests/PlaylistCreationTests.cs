using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PlaylistCreationTests
{
    private static WebSession Session() => new("webid", "fixture-token", "Pookie-Test/1");
    private static string Response(string sharing) => $$"""
        {"id":17,"title":"Новый 🎵","sharing":"{{sharing}}","user":{"id":42},"track_count":1,"tracks":[{"id":123}]}
        """;
    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    public async Task CreationSeedsTrackInSinglePost(string sharing)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api-v2.soundcloud.com/playlists", request.RequestUri!.GetLeftPart(UriPartial.Path));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var playlist = body.RootElement.GetProperty("playlist");
            Assert.Equal("Новый 🎵", playlist.GetProperty("title").GetString());
            Assert.Equal(sharing, playlist.GetProperty("sharing").GetString());
            Assert.Equal(123, Assert.Single(playlist.GetProperty("tracks").EnumerateArray()).GetInt64());
            return new(HttpStatusCode.Created) { Content = new StringContent(Response(sharing)) };
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        using var result = await transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", sharing));
        Assert.Equal(17, result.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task LostResponseIsNeverReplayed()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; throw new HttpRequestException("Lost response"); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", "private")));
        Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"wrong\"}")]
    [InlineData("{\"id\":17,\"user\":null}")]
    [InlineData("{\"id\":17,\"user\":{\"id\":8}}")]
    [InlineData("{\"id\":17,\"user\":{\"id\":42},\"title\":[],\"sharing\":\"private\"}")]
    public void MalformedConfirmationIsRejected(string body)
    {
        using var document = JsonDocument.Parse(body);
        Assert.False(new PlaylistCreateRequest(42, 123, "Новый 🎵", "private").Confirms(document.RootElement));
    }
    [Fact]
    public void CommandsAreBoundedAndCannotMixOperations()
    {
        var request = new PlaylistCreateRequest(42, 123, "Название", "private");
        var command = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "playlist-create", Playlist: request);
        Assert.True(command.IsValid());
        Assert.False((command with { Operation = "me" }).IsValid());
        Assert.False((command with { Url = "https://api-v2.soundcloud.com/me" }).IsValid());
        Assert.False((command with { TrackId = 999 }).IsValid());
        foreach (var title in new[] { "", " ", "line\nbreak", new string('x', 256) })
            Assert.False((command with { Playlist = request with { Title = title } }).IsValid());
        Assert.True((request with { Title = new string('x', 255) }).IsValid());
        Assert.False((request with { Sharing = "anything" }).IsValid());
        var json = JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand);
        Assert.Contains("\"user_id\":42", json); Assert.Contains("\"track_id\":123", json);
        Assert.DoesNotContain("Название", command.ToString());
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
