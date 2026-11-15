using System.Text.RegularExpressions;
using Pookie.App.Browser;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class BrowserTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionBrowserTemplateSuppliesEveryInjectedValue(bool audio)
    {
        var script = BrowserRequestScript.Read(new("fixture", "fixture-token", "Fixture"), "https://soundcloud.com", audio);
        Assert.DoesNotMatch(new Regex("__POOKIE_[A-Z_]+__"), script);
        Assert.Contains("PookieTrackQuality", script);
        Assert.Contains("playlist-edit", script);
    }

    [Fact]
    public void MacWebsiteProfilesHaveStableDistinctIdentifiersWithoutLoadingWebKit()
    {
        var profile = Path.Combine(Path.GetTempPath(), "pookie-profile-identity", "A");
        Assert.Equal(MacEmbeddedBrowser.ProfileIdentifier(profile), MacEmbeddedBrowser.ProfileIdentifier(profile + Path.DirectorySeparatorChar));
        Assert.NotEqual(MacEmbeddedBrowser.ProfileIdentifier(profile), MacEmbeddedBrowser.ProfileIdentifier(profile + "-other"));
        Assert.True(Guid.TryParse(MacEmbeddedBrowser.ProfileIdentifier(profile), out _));
    }
}
