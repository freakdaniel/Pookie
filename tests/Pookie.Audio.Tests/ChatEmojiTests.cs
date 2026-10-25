using System.Buffers.Binary;
using Pookie.App.Messages;
using Pookie.App.Storage;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatEmojiTests
{
    [Fact]
    public void OfflineCatalogHasArtworkAndRussianAndEnglishSearch()
    {
        Assert.Equal(3944, ChatEmojiCatalog.All.Count);
        Assert.Equal(3944, ChatEmojiCatalog.All.Select(e => e.Value).Distinct().Count());
        Assert.Contains(ChatEmojiCatalog.Search("сердце"), e => e.Value == "❤️");
        Assert.Contains(ChatEmojiCatalog.Search("heart"), e => e.Value == "❤️");
        Assert.Empty(ChatEmojiCatalog.Search("несуществующийпоисковыйзапрос"));
        foreach (var group in ChatEmojiCatalog.Groups)
            Assert.All(ChatEmojiCatalog.Search("", group.Group), e => Assert.Equal(group.Group, e.Group));
        Assert.Equal(new[] { "♀️", "♂️", "⚕️" }, ChatEmojiCatalog.All.Where(e => e.Asset.Length == 0).Select(e => e.Value));
        Assert.Equal(3941, ChatEmojiCatalog.Search("").Length);
        foreach (var emoji in ChatEmojiCatalog.All.Where(e => e.Asset.Length > 0))
        {
            using var image = typeof(ChatEmojiCatalog).Assembly.GetManifestResourceStream($"Pookie.App.Assets.Emoji.{emoji.Asset}.png");
            Assert.NotNull(image); var header = new byte[24]; image.ReadExactly(header);
            Assert.Equal(72, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)));
            Assert.Equal(72, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
            image.Position = 0; using var bytes = new MemoryStream(); image.CopyTo(bytes);
            var decoded = StbImageSharp.ImageResult.FromMemory(bytes.ToArray(), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            Assert.Equal(72 * 72 * 4, decoded.Data.Length);
        }
    }

    [Fact]
    public void InlineArtworkRetainsExactUnicodeRangesAndWholeSequences()
    {
        const string text = "Текст 😀 ❤ 👋🏽 👨‍👩‍👧‍👦 🇨🇦 1️⃣ 日本語";
        var spans = ChatEmojiCatalog.Spans(text);
        Assert.Equal(new[] { "😀", "❤", "👋🏽", "👨‍👩‍👧‍👦", "🇨🇦", "1️⃣" }, spans.Select(s => text.Substring(s.Start, s.Length)));
        Assert.All(spans, span => Assert.NotEmpty(ChatEmojiCatalog.Find(span.Value)!.Asset));
        Assert.Empty(ChatEmojiCatalog.Spans("abc 123 日本語 обычный текст"));
        // The font lacks these three standalone glyphs; they remain valid Unicode,
        // rather than being replaced by another vendor's artwork or dropped.
        Assert.True(ChatEmojiCatalog.IsEmoji("♀️"));
        Assert.Empty(ChatEmojiCatalog.Spans("♀️"));
    }

    [Fact]
    public void EmojiSequencesRemainWholeAtTheMessageLimit()
    {
        const string family = "👨‍👩‍👧‍👦";
        Assert.True(ChatEmojiCatalog.IsEmoji(family));
        Assert.True(ChatEmojiCatalog.IsEmoji("👋🏽"));
        Assert.True(ChatEmojiCatalog.IsEmoji("🇨🇦"));
        Assert.False(ChatEmojiCatalog.IsEmoji("❤️🔥"));
        Assert.False(ChatEmojiCatalog.IsEmoji("<script>"));
        Assert.True(ChatEmojiCatalog.Fits(new string('я', 19999), family.Length - 1, family, ChatRequest.MaxBodyLength));
        Assert.False(ChatEmojiCatalog.Fits(new string('я', 19999), 0, family, ChatRequest.MaxBodyLength));
        Assert.Equal("👍", ChatEmojiCatalog.Remember(["❤️", "👍", "invalid"], "👍")[0]);
        Assert.Equal(2, ChatEmojiCatalog.Remember(["❤️", "👍", "invalid"], "👍").Length);
    }

    [Fact]
    public async Task RecentEmojiAreDurableBoundedAndAccountIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-emoji-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(root); var first = new ChatWorkspaceStore(paths, 42);
            await first.SaveAsync(ChatWorkspaceStore.Empty with { RecentEmoji = [null!, "🔥", "❤️", "🔥", "invalid"] });
            Assert.Equal(new[] { "🔥", "❤️" }, new ChatWorkspaceStore(paths, 42).Load().RecentEmoji);
            Assert.Null(new ChatWorkspaceStore(paths, 7).Load().RecentEmoji);
            var recent = ChatEmojiCatalog.All.Take(100).Aggregate(Array.Empty<string>(), (list, e) => ChatEmojiCatalog.Remember(list, e.Value));
            Assert.Equal(40, recent.Length);
            await first.SaveAsync(ChatWorkspaceStore.Empty); Assert.Null(first.Load().RecentEmoji);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExtendedReactionAcceptsOneCatalogEmojiAndRejectsArbitraryPayloads()
    {
        ChatMessage Event(string emoji) => new("event", 42, ChatProtocol.React(42, 90, "target", emoji), DateTimeOffset.UtcNow);
        Assert.True(ChatProtocol.Project(Event("🦊"), 42, 90).IsReaction);
        Assert.True(ChatProtocol.Project(Event("👋🏽"), 42, 90).IsReaction);
        Assert.False(ChatProtocol.Project(Event("❤️🔥"), 42, 90).IsReaction);
        Assert.False(ChatProtocol.Project(Event("hello"), 42, 90).IsReaction);
        Assert.False(ChatProtocol.Project(Event("🦊"), 42, 91).IsReaction);
    }
}
