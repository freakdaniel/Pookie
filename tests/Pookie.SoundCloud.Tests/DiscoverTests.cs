using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class DiscoverTests
{
    [Fact]
    public void OnlyPersonalSystemMixesSurviveEditorialModulesPromotionsAndDuplicates()
    {
        using var document = JsonDocument.Parse("""
            {"collection":[{"items":{"collection":[
              {"kind":"system-playlist","id":"soundcloud:system-playlists:your-moods:42:1","title":"Your Mix 1","tracks":[]},
              {"kind":"system-playlist","id":"soundcloud:system-playlists:weekly::test","title":"Weekly Wave","made_for":{"id":42},"tracks":[]},
              {"kind":"system-playlist","id":"soundcloud:system-playlists:charts-top:all","title":"Charts","tracks":[]},
              {"kind":"playlist","id":1,"title":"Editorial","tracks":[]},
              {"kind":"system-playlist","id":"soundcloud:system-playlists:new-for-you::other","title":"Other","made_for":{"id":7},"tracks":[]},
              {"ad_urn":"ad:1","origin":{"kind":"system-playlist","id":"soundcloud:system-playlists:weekly::ad","title":"Promotion","tracks":[]}}
            ]}},{"items":[{"kind":"system-playlist","id":"soundcloud:system-playlists:your-moods:42:1","title":"Your Mix 1","tracks":[]}]}]}
            """);
        var items = DiscoverData.PersonalMixes(document.RootElement, 42);
        Assert.Equal(new[] { "Your Mix 1", "Weekly Wave" }, items.Select(item => item.Title));
        Assert.All(items, item => Assert.StartsWith("soundcloud:system-playlists:", item.Playlist!.Urn));
    }

    [Theory]
    [InlineData("mixed-selections", true)]
    [InlineData("mixed-selections?limit=6", true)]
    [InlineData("mixed-selections/delete", false)]
    public void DiscoverAddsOnlyTheReadEndpoint(string path, bool allowed) =>
        Assert.Equal(allowed, BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/" + path));
}
