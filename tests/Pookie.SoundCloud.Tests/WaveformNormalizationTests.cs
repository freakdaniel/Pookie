using System.Text.Json;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class WaveformNormalizationTests
{
    private static double Gain(string json)
    { using var doc = JsonDocument.Parse(json); return WaveformNormalization.EstimateGainDb(doc.RootElement); }

    [Fact]
    public void WholeTrackEnvelopeDeterminesTheGainBeforeTheFirstPassage()
    {
        var quiet = Gain("""{"height":140,"samples":[35,35,35,35]}""");
        var loud = Gain("""{"height":140,"samples":[35,140,140,140]}""");
        Assert.Equal(0, quiet, 6);
        Assert.True(loud < -10);
        Assert.Equal(quiet, Gain("""{"height":280,"samples":[70,70,70,70]}"""), 6);
        Assert.Equal(-6.0206, Gain("""{"height":140,"samples":[70,70,70,70]}"""), 4);
    }

    [Fact]
    public void SilentIntrosDoNotMakeTheMusicLouderAndSilenceIsNotBoosted()
    {
        Assert.Equal(Gain("""{"height":140,"samples":[70,70]}"""),
            Gain("""{"height":140,"samples":[0,0,0,0,70,70]}"""));
        Assert.Equal(0, Gain("""{"height":140,"samples":[0,0]}"""));
        Assert.Equal(6, Gain("""{"height":140,"samples":[3,3]}"""));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"samples\":[10]}")]
    [InlineData("{\"height\":\"bad\",\"samples\":[10]}")]
    [InlineData("{\"height\":0,\"samples\":[10]}")]
    [InlineData("{\"height\":140,\"samples\":[]}")]
    [InlineData("{\"height\":140,\"samples\":[141]}")]
    [InlineData("{\"height\":140,\"samples\":[-1]}")]
    [InlineData("{\"height\":140,\"samples\":[false]}")]
    public void MalformedOrUncalibratedEnvelopesCannotSetTheGain(string json) => Assert.Throws<JsonException>(()=>Gain(json));
}
