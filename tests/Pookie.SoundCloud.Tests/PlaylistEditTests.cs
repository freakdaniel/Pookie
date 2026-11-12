using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PlaylistEditTests
{
    private static WebSession Session() => new("webid", "fixture-token", "Pookie-Test/1");
    private static HttpResponseMessage Json(string value = "{}") => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    [Fact]
    public async Task WebUpdatePreservesDuplicateAndUnavailableIdsInOriginalOrder()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/playlists/11", request.RequestUri!.AbsolutePath);
                Assert.Contains("representation=full", request.RequestUri.Query);
                return Json("""{"id":11,"user":{"id":42},"track_count":3,"tracks":[{"id":8},{"id":7},{"id":8}]}""");
            }
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/playlists/11", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(new long[] { 8, 7, 8, 90 }, body.RootElement.GetProperty("playlist").GetProperty("tracks").EnumerateArray().Select(t => t.GetInt64()));
            return Json();
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.AddToPlaylistAsync(42, 11, 90);
        Assert.Equal(2, calls);
    }
    [Theory]
    [InlineData("{\"id\":11,\"user\":{\"id\":8},\"track_count\":0,\"tracks\":[]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"is_album\":true,\"track_count\":0,\"tracks\":[]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"track_count\":2,\"tracks\":[{\"id\":8}]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"track_count\":1,\"tracks\":[{\"id\":0}]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"track_count\":null,\"tracks\":[]}")]
    public async Task UnconfirmedWebSnapshotCannotBeOverwritten(string playlist)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        { calls++; Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(Json(playlist)); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.AddToPlaylistAsync(42, 11, 90));
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task DuplicateDoesNotWriteAndLostPutIsNotReplayed()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Put) throw new HttpRequestException("Lost PUT response");
            return Task.FromResult(Json("""{"id":11,"user":{"id":42},"track_count":1,"tracks":[{"id":90}]}"""));
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.AddToPlaylistAsync(42, 11, 90); Assert.Equal(1, calls);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.AddToPlaylistAsync(42, 11, 91));
        Assert.Equal(3, calls);
    }
    [Fact]
    public async Task ConcurrentEditsReadAgainAfterThePreviousWrite()
    {
        var ids = new List<long> { 8 }; var reads = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                reads++;
                return Json(JsonSerializer.Serialize(new { id = 11, user = new { id = 42 }, track_count = ids.Count, tracks = ids.Select(id => new { id }).ToArray() }));
            }
            if (!started.Task.IsCompleted) { started.SetResult(); await release.Task; }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            ids = body.RootElement.GetProperty("playlist").GetProperty("tracks").EnumerateArray().Select(t => t.GetInt64()).ToList();
            return Json();
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        var first = transport.AddToPlaylistAsync(42, 11, 90); await started.Task;
        var second = transport.AddToPlaylistAsync(42, 11, 91);
        Assert.Equal(1, reads);
        release.SetResult(); await Task.WhenAll(first, second);
        Assert.Equal(new long[] { 8, 90, 91 }, ids); Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("remove")]
    [InlineData("artwork")]
    [InlineData("delete")]
    public async Task PlaylistEditorUsesNativeHttpAndConfirmsSuccessfulWrites(string kind)
    {
        var jpeg = new byte[] { 255, 216, 0, 255, 217 };
        var edit = kind switch
        {
            "update" => new PlaylistEditRequest(kind, 42, 11, new(42, 11, "New title", "Description", "private", [8, 8, 7], [8, 7, 8])),
            "remove" => new PlaylistEditRequest(kind, 42, 11, TrackId: 7),
            "artwork" => new PlaylistEditRequest(kind, 42, 11, Artwork: Convert.ToBase64String(jpeg)),
            _ => new PlaylistEditRequest(kind, 42, 11)
        };
        var calls = new List<HttpMethod>();
        using var http = new HttpClient(new Handler(async request =>
        {
            calls.Add(request.Method);
            Assert.Equal("/playlists/11", request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Contains("representation=full", request.RequestUri.Query);
                return calls.Count == 1
                    ? Json("""{"id":11,"user":{"id":42},"track_count":3,"tracks":[{"id":8},{"id":7},{"id":8}]}""")
                    : Json("""{"id":11,"user":{"id":42},"confirmed":true}""");
            }
            if (kind == "delete")
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Null(request.Content);
            }
            else if (kind == "artwork")
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
                var body = await request.Content.ReadAsByteArrayAsync();
                var text = System.Text.Encoding.ASCII.GetString(body);
                Assert.Contains("name=\"playlist[artwork_data]\"", text);
                Assert.Contains("filename=artwork.jpg", text);
                Assert.Contains("Content-Type: image/jpeg", text);
                Assert.True(body.AsSpan().IndexOf(jpeg) >= 0);
            }
            else
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var playlist = body.RootElement.GetProperty("playlist");
                Assert.Equal(kind == "update" ? new long[] { 8, 8, 7 } : [8, 8],
                    playlist.GetProperty("tracks").EnumerateArray().Select(item => item.GetInt64()));
                if (kind == "update")
                {
                    Assert.Equal("New title", playlist.GetProperty("title").GetString());
                    Assert.Equal("Description", playlist.GetProperty("description").GetString());
                    Assert.Equal("private", playlist.GetProperty("sharing").GetString());
                }
            }
            return new(HttpStatusCode.NoContent);
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        using var result = await transport.EditPlaylistAsync(edit);
        if (kind == "delete") Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Delete }, calls);
        else
        {
            Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Get }, calls);
            Assert.True(result.RootElement.GetProperty("confirmed").GetBoolean());
        }
    }

    [Fact]
    public async Task PlaylistEditorRejectsStaleReorderBeforeWriting()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json("""{"id":11,"user":{"id":42},"track_count":2,"tracks":[{"id":8},{"id":7}]}"""));
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        var edit = new PlaylistEditRequest("update", 42, 11, new(42, 11, "Title", "", "private", [8, 7], [7, 8]));
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.EditPlaylistAsync(edit));
        Assert.Equal(1, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    [Fact]
    public async Task ExtendedMetadataIsWrittenWithoutReplacingAnIncompleteTrackSnapshot()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get) return Json("""{"id":11,"user":{"id":42},"track_count":20,"tracks":[{"id":8}]}""");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var data = body.RootElement.GetProperty("playlist");
            Assert.False(data.TryGetProperty("tracks", out _));
            Assert.Equal("updated", data.GetProperty("permalink").GetString());
            Assert.Equal("Rock", data.GetProperty("genre").GetString());
            Assert.Equal("rock \"indie rock\"", data.GetProperty("tag_list").GetString());
            Assert.Equal("2026-11-13T00:00:00.000Z", data.GetProperty("release_date").GetString());
            Assert.Equal("ep", data.GetProperty("playlist_type").GetString());
            Assert.Equal("album", data.GetProperty("set_type").GetString());
            Assert.Equal("https://example.com/buy", data.GetProperty("purchase_url").GetString());
            Assert.Equal("Label", data.GetProperty("label_name").GetString());
            Assert.Equal("cc-by", data.GetProperty("license").GetString());
            return Json();
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        using var result = await transport.EditPlaylistAsync(new("update", 42, 11, new(42, 11, "Title", "", "private",
            Metadata: new("updated", "Rock", "rock \"indie rock\"", "2026-11-13", "ep", "https://example.com/buy", "Label", "cc-by"))));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ExtendedMetadataRejectsInvalidInputsAndCanClearOptionalValues()
    {
        Assert.True(new PlaylistMetadataUpdate(Genre: "", TagList: "", ReleaseDate: "", PurchaseUrl: "", LabelName: "").IsValid());
        foreach (var metadata in new[] { new PlaylistMetadataUpdate(Permalink: "../me"), new(Permalink: ""), new(Genre: "bad\nvalue"),
            new(ReleaseDate: "2026-02-30"), new(ReleaseDate: "13.11.2026"), new(PurchaseUrl: "javascript:alert(1)"),
            new(PurchaseUrl: "https://user:secret@example.com"), new(PlaylistType: "whatever"), new(License: "custom") })
            Assert.False(new PlaylistUpdateRequest(42, 11, "Title", "", "private", Metadata: metadata).IsValid());
    }
}
