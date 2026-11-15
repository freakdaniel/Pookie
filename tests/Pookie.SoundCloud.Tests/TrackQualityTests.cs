using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class TrackQualityTests
{
    [Theory]
    [InlineData("pcm_s24le", true)]
    [InlineData("PCM_S16LE", true)]
    [InlineData("flac", true)]
    [InlineData("alac", true)]
    [InlineData("aiff", true)]
    [InlineData("wav", true)]
    [InlineData("aac", false)]
    [InlineData("mp3", false)]
    [InlineData("opus", false)]
    [InlineData("vorbis", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ClassifiesTheOriginalCodec(string? codec, bool expected) =>
        Assert.Equal(expected, new TrackUpload(codec, null, null, null, null).IsLossless);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsTheOriginalUploadThroughBothNativePaths(bool transport)
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("graph.soundcloud.com", request.RequestUri!.Host);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Contains("mediaUpload { codec container bitRate sampleRate channels }", body.RootElement.GetProperty("query").GetString());
            var variables = body.RootElement.GetProperty("variables");
            Assert.Equal("soundcloud:tracks:42", variables.GetProperty("trackUrn").GetString());
            Assert.False(variables.TryGetProperty("options", out _));
            return """{"data":{"tracks":[null,{"urn":"soundcloud:tracks:99","mediaUpload":{"codec":"mp3"}},{"urn":"soundcloud:tracks:42","mediaUpload":{"codec":"pcm_s24le","container":"wav","bitRate":2116800,"sampleRate":44100,"channels":2}}]}}""";
        }));
        var session = new WebSession("fixture", "token", "Test");
        using var native = new SoundCloudHttpTransport(http, () => session);
        var api = new SoundCloudWebClient(http) { Session = session, Transport = transport ? native : null };
        Assert.Equal(new("pcm_s24le", "wav", 2116800, 44100, 2), await api.GetTrackUploadAsync(42));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{\"urn\":\"soundcloud:tracks:42\",\"mediaUpload\":null}]")]
    [InlineData("[{\"urn\":\"soundcloud:tracks:99\",\"mediaUpload\":{\"codec\":\"flac\"}}]")]
    public async Task MissingOrUnrelatedMetadataStaysUnknown(string tracks)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult("{\"data\":{\"tracks\":" + tracks + "}}")));
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "token", "Test") };
        Assert.Null(await api.GetTrackUploadAsync(42));
    }

    [Fact]
    public void QualityReadsRejectCommentParameters()
    {
        Assert.True(new TrackReadRequest("quality", 42).IsValid());
        Assert.False(new TrackReadRequest("quality", 42, Cursor: "next").IsValid());
        Assert.False(new TrackReadRequest("quality", 42, CommentId: 1).IsValid());
        Assert.False(new TrackReadRequest("quality", 42, Sort: "OLDEST").IsValid());
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<string>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK) { Content = new StringContent(await response(request)) };
    }
}
