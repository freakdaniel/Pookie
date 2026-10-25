using Aprillz.MewUI;
using Aprillz.MewUI.Text;
using Pookie.App;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ApplicationEmojiTextTests
{
    [Fact]
    public void OrdinaryTextKeepsItsRequestAndEmojiKeepUnicodeOffsets()
    {
        var plain = new TextLayoutRequest { Text = "Ник — комментарий 123".AsMemory() };
        Assert.Same(plain, ApplicationEmojiText.Project(plain));
        const string value = "Ник 👨‍👩‍👧‍👦 😀 комментарий";
        var request = plain with { Text = value.AsMemory(), DefaultStyle = new("Inter", 14) };
        var projected = ApplicationEmojiText.Project(request);
        Assert.Equal(value, projected.Text.ToString());
        Assert.Equal(new[] { "👨‍👩‍👧‍👦", "😀" }, projected.Inlines.Select(i => value.Substring(i.Position, i.Length)));
        Assert.All(projected.Inlines, i => Assert.Equal(18.2, i.Object.Measure().Width, 4));
        Assert.Same(projected.Inlines[0].Object, ApplicationEmojiText.Project(request).Inlines[0].Object);
        Assert.Same(projected, ApplicationEmojiText.Project(projected));
    }

    [Fact]
    public void ExistingMusicInlinesAndStyledEmojiSizesArePreserved()
    {
        const string value = "😀 👋🏽 🇨🇦";
        var chip = new Chip();
        var request = new TextLayoutRequest
        {
            Text = value.AsMemory(), DefaultStyle = new("Inter", 14),
            Inlines = [new(0, 2, chip)], Runs = [new(3, 4, new("Inter", 24))]
        };
        var result = ApplicationEmojiText.Project(request);
        Assert.Equal(3, result.Inlines.Count); Assert.Same(chip, result.Inlines[0].Object);
        Assert.Equal(31.2, result.Inlines[1].Object.Measure().Width, 4);
        Assert.Equal(18.2, result.Inlines[2].Object.Measure().Width, 4);
        var sliced = request with { Text = value.AsMemory(3, 4), Inlines = [], Runs = [] };
        var inline = Assert.Single(ApplicationEmojiText.Project(sliced).Inlines);
        Assert.Equal(0, inline.Position); Assert.Equal(4, inline.Length);
    }
    private sealed class Chip : IInlineTextObject
    {
        public InlineMetrics Measure() => new(100, 20, 16);
        public void Draw(ITextRenderContext context, Point origin) { }
    }
}
