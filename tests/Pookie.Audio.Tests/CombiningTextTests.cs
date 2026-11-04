using Aprillz.MewUI.Text;
using Pookie.App;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class CombiningTextTests
{
    [Theory]
    [InlineData("e\u0301 a\u0302\u0301 й ё")]
    [InlineData("مُحَمَّد שָׁלוֹם नमस्ते เก้า བོད་")]
    [InlineData("👩🏽‍💻 ❤️‍🔥 1️⃣ 🇨🇦")]
    public void OrdinaryScriptsAndEmojiKeepEveryCharacter(string text)
    {
        Assert.Empty(CombiningText.HiddenRuns(text));
        var request = new TextLayoutRequest { Text = text.AsMemory(), DefaultStyle = new("Inter", 14) };
        var projected = ApplicationEmojiText.Project(request);
        Assert.Equal(text, projected.Text.ToString());
        Assert.All(projected.Inlines, inline => Assert.True(inline.Object.Measure().Width > 0));
    }

    [Fact]
    public void ReportedThaiPayloadKeepsSourceAndOffsetsButDoesNotShapeItsMarkStacks()
    {
        const string prefix = "зацените ласт трек там разъеб!! ";
        var text = prefix + "ฏ" + new string('\u0e4e', 480) + "ฏ" + new string('\u0e4e', 390) + " конец 😀";
        var request = new TextLayoutRequest { Text = text.AsMemory(), DefaultStyle = new("Inter", 14) };
        var projected = ApplicationEmojiText.Project(request);
        Assert.Equal(text, projected.Text.ToString());
        var hidden = projected.Inlines.Where(inline => inline.Object.Measure().Width == 0).ToArray();
        Assert.Equal(new[] { (prefix.Length, 481), (prefix.Length + 481, 391) }, hidden.Select(inline => (inline.Position, inline.Length)));
        Assert.Same(projected, ApplicationEmojiText.Project(projected));
        Assert.Contains(projected.Inlines, inline => text.Substring(inline.Position, inline.Length) == "😀");
    }

    [Theory]
    [InlineData("x\u0300\u0301\u0302\u0303\u0304\u0305\u0306\u0307\u0308z", 0, 10)]
    [InlineData("x\u0301\u200d\u0301\u200d\u0301z", 0, 6)]
    [InlineData("\u0301\u0301\u0301x", 0, 3)]
    [InlineData("x\U0001d165\U0001d165\U0001d165z", 0, 7)]
    public void DifferentMarksFormatsLeadingMarksAndSurrogatePairsAreBounded(string text, int start, int length)
    {
        Assert.Equal((start, length), Assert.Single(CombiningText.HiddenRuns(text)));
    }

    [Fact]
    public void SlicesUseLocalOffsetsAndExistingInlineObjectsKeepOwnership()
    {
        var source = "prefix x" + new string('\u0e4e', 500) + " end";
        var request = new TextLayoutRequest { Text = source.AsMemory(7, 501), DefaultStyle = new("Inter", 14) };
        var projected = ApplicationEmojiText.Project(request);
        var hidden = Assert.Single(projected.Inlines);
        Assert.Equal((0, 501), (hidden.Position, hidden.Length));
        var owned = request with { Inlines = [hidden] };
        Assert.Same(owned, ApplicationEmojiText.Project(owned));
    }

    [Fact]
    public void HugeStackProducesOneBoundedInlineAndPlainTextDoesNotAllocateARequest()
    {
        var text = "x" + new string('\u0e4e', 100_000) + " suffix";
        var projected = ApplicationEmojiText.Project(new() { Text = text.AsMemory() });
        Assert.Equal(100_001, Assert.Single(projected.Inlines).Length);
        var plain = new TextLayoutRequest { Text = "Обычный комментарий 123".AsMemory() };
        Assert.Same(plain, ApplicationEmojiText.Project(plain));
    }
}
