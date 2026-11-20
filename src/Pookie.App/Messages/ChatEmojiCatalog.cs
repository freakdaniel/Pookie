using Pookie.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Pookie.App.Messages;

internal sealed record ChatEmoji(string Value, string Asset, string Group, string Name, string Keywords)
{
    internal string SearchText { get; } = (Keywords + " " + Value).ToLowerInvariant().Replace('ё', 'е');
}

// Metadata is small and immutable; artwork is opened only for realized picker rows.
internal static class ChatEmojiCatalog
{
    private sealed class Index
    {
        internal readonly ChatEmoji[] Data;
        internal Dictionary<string, ChatEmoji> ByValue { get; } = new(StringComparer.Ordinal);
        internal HashSet<char> Initials { get; } = [];

        internal Index(string language)
        {
            Data = Load(language);
            foreach (var emoji in Data) ByValue.Add(emoji.Value, emoji);
            foreach (var emoji in Data)
            {
                ByValue.TryAdd(emoji.Value.Replace("\ufe0f", ""), emoji);
                Initials.Add(emoji.Value[0]);
            }
        }
    }

    // Keep only the active language's search index. Loading happens on first use.
    private static readonly object gate = new();
    private static (string? Language, Index? Data) cached;
    private static Index Current
    {
        get
        {
            var language = UiText.Language.TwoLetterISOLanguageName;
            lock (gate)
            {
                if (cached.Language != language)
                    cached = (language, new Index(UiText.ResolveLanguage([language]).TwoLetterISOLanguageName));
                return cached.Data!;
            }
        }
    }

    internal static IReadOnlyList<ChatEmoji> All => Current.Data;
    internal static ChatEmoji? Find(string value) => Current.ByValue.GetValueOrDefault(value);
    internal static void Warm() => _ = Current;
    internal static (string Group, string Title, string Icon)[] Groups =>
    [
        ("Smileys & Emotion", UiText.Get("emoji.category.emotions"), "smiley"), ("People & Body", UiText.Get("emoji.category.people"), "hand-waving"),
        ("Animals & Nature", UiText.Get("emoji.category.nature"), "leaf"), ("Food & Drink", UiText.Get("emoji.category.food"), "coffee"),
        ("Travel & Places", UiText.Get("emoji.category.travel"), "airplane"), ("Activities", UiText.Get("emoji.category.activities"), "soccer-ball"),
        ("Objects", UiText.Get("emoji.category.objects"), "lightbulb"), ("Symbols", UiText.Get("emoji.category.symbols"), "heart"), ("Flags", UiText.Get("emoji.category.flags"), "flag")
    ];
    internal static string Title(string group) => Groups.FirstOrDefault(g => g.Group == group).Title ?? group;
    internal static string Normalize(string text) => text.ToLowerInvariant().Replace('ё', 'е');
    internal static ChatEmoji[] Search(string query, string? group = null)
    {
        var terms = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Current.Data.Where(e => e.Asset.Length > 0 && (group == null || e.Group == group) && Matches(e, terms)).ToArray();
    }
    private static bool Matches(ChatEmoji emoji, string[] terms)
    { foreach (var term in terms) if (!emoji.SearchText.Contains(term, StringComparison.Ordinal)) return false; return true; }
    internal static bool IsEmoji(string? value) => value is { Length: > 0 and <= 32 } && Find(value) != null;
    internal static string[] Remember(IEnumerable<string>? recent, string value) =>
        new[] { value }.Concat(recent ?? []).Where(IsEmoji).Distinct(StringComparer.Ordinal).Take(40).ToArray();
    // Insert complete Unicode sequences; never cut a surrogate pair/ZWJ at the body limit.
    internal static bool Fits(string text, int selectionLength, string emoji, int maximum) =>
        IsEmoji(emoji) && text.Length - selectionLength + emoji.Length <= maximum;
    internal static IReadOnlyList<(int Start, int Length, string Value)> Spans(string text) => Spans(text.AsSpan());
    internal static IReadOnlyList<(int Start, int Length, string Value)> Spans(ReadOnlySpan<char> text)
    {
        if (text.Length == 0) return [];
        var index = Current;
        List<(int, int, string)>? result = null;
        for (var offset = 0; offset < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text[offset..]);
            if (index.Initials.Contains(text[offset]) && index.ByValue.GetValueOrDefault(text.Slice(offset, length).ToString()) is { Asset.Length: > 0 } emoji)
                (result ??= []).Add((offset, length, emoji.Value));
            offset += length;
        }
        return result ?? (IReadOnlyList<(int, int, string)>)Array.Empty<(int, int, string)>();
    }
    private static ChatEmoji[] Load(string language)
    {
        using var stream = typeof(ChatEmojiCatalog).Assembly.GetManifestResourceStream("Pookie.App.Assets.Emoji.catalog.json")!;
        var emoji = JsonSerializer.Deserialize(stream, ChatEmojiJson.Default.ChatEmojiArray) ?? [];
        if (language == "ru") return emoji;
        using var resource = typeof(ChatEmojiCatalog).Assembly.GetManifestResourceStream($"Pookie.App.Emoji.{language}.json.gz")!;
        using var decompressed = new System.IO.Compression.GZipStream(resource, System.IO.Compression.CompressionMode.Decompress);
        var annotations = JsonSerializer.Deserialize(decompressed, ChatEmojiJson.Default.DictionaryStringString)!;
        for (var i = 0; i < emoji.Length; i++)
        {
            var item = emoji[i];
            if (annotations.TryGetValue(item.Value, out var annotation))
                emoji[i] = new(item.Value, item.Asset, item.Group, annotation.Split('|', 2)[0], item.Keywords + " " + annotation);
        }
        return emoji;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChatEmoji[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class ChatEmojiJson : JsonSerializerContext;
