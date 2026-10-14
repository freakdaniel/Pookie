using System.Net;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class PreviewDownloadBudgetTests
{
    [Fact]
    public async Task SequentialResponsesShareAHardBudgetForSyncAndAsyncReads()
    {
        var budget = new PreviewDownloadBudget(10);
        var network = new FixtureHandler();
        using var http = new HttpClient(new PreviewBudgetHandler(budget, network));
        using (var first = await http.GetAsync("https://fixture.test/first", HttpCompletionOption.ResponseHeadersRead))
        {
            using var body = await first.Content.ReadAsStreamAsync();
            var buffer = new byte[8];
            Assert.Equal(6, await body.ReadAsync(buffer)); Assert.Equal(0, await body.ReadAsync(buffer));
        }
        using (var second = await http.GetAsync("https://fixture.test/second", HttpCompletionOption.ResponseHeadersRead))
        {
            using var body = await second.Content.ReadAsStreamAsync();
            var buffer = new byte[8];
            Assert.Equal(4, body.Read(buffer));
            Assert.Throws<IOException>(() => body.Read(buffer));
        }
        Assert.Equal(10, budget.Used); Assert.Equal(0, budget.Remaining);
        await Assert.ThrowsAsync<IOException>(() => http.GetAsync("https://fixture.test/third", HttpCompletionOption.ResponseHeadersRead));
        Assert.Equal(2, network.Requests);
    }

    [Fact]
    public async Task CancelledReadsReturnUnusedReservationsAndContentDisposalClosesTheResponse()
    {
        var budget = new PreviewDownloadBudget(100);
        var source = new MemoryStream(new byte[6]);
        var network = new FixtureHandler(source);
        using var http = new HttpClient(new PreviewBudgetHandler(budget, network));
        using (var response = await http.GetAsync("https://fixture.test/", HttpCompletionOption.ResponseHeadersRead))
        {
            var body = await response.Content.ReadAsStreamAsync();
            using var stop = new CancellationTokenSource(); stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => Assert.Equal(0, await body.ReadAsync(new byte[8], stop.Token)));
            Assert.Equal(0, budget.Used);
            Assert.Equal(6, await body.ReadAsync(new byte[8])); Assert.Equal(6, budget.Used);
        }
        Assert.False(source.CanRead);
    }

    private sealed class FixtureHandler(Stream? stream = null) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = stream == null ? new ByteArrayContent(new byte[6]) : new StreamContent(stream) });
        }
    }
}
