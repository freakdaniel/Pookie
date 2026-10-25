using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.App.Messages;

internal sealed record ChatEmoji(string Value, string Asset, string Group, string Name, string Keywords)
{
    internal string SearchText { get; } = (Keywords + " " + Value).ToLowerInvariant().Replace('ё', 'е');
}

// Metadata is small and immutable; artwork is opened only for realized picker rows.
internal static class ChatEmojiCatalog
{
    private static readonly Lazy<ChatEmoji[]> data = new(Load);
    internal static IReadOnlyList<ChatEmoji> All => data.Value;
    private static readonly Lazy<Dictionary<string, ChatEmoji>> byValue = new(() => data.Value.ToDictionary(e => e.Value, StringComparer.Ordinal));
    internal static ChatEmoji? Find(string value) => byValue.Value.GetValueOrDefault(value);
    internal static string? LoadedAsset(string value) => data.IsValueCreated ? Find(value)?.Asset : null;
    internal static void Warm() { _ = data.Value.Length; _ = byValue.Value.Count; }
    internal static readonly (string Group, string Title, string Icon)[] Groups =
    [
        ("Smileys & Emotion", "Смайлы и эмоции", "smiley"), ("People & Body", "Люди и жесты", "hand-waving"),
        ("Animals & Nature", "Животные и природа", "leaf"), ("Food & Drink", "Еда и напитки", "coffee"),
        ("Travel & Places", "Места и путешествия", "airplane"), ("Activities", "Занятия и спорт", "soccer-ball"),
        ("Objects", "Предметы", "lightbulb"), ("Symbols", "Символы", "heart"), ("Flags", "Флаги", "flag")
    ];
    internal static string Title(string group) => Groups.FirstOrDefault(g => g.Group == group).Title ?? group;
    internal static string Normalize(string text) => text.ToLowerInvariant().Replace('ё', 'е');
    internal static ChatEmoji[] Search(string query, string? group = null)
    {
        var terms = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return data.Value.Where(e => (group == null || e.Group == group) && Matches(e, terms)).ToArray();
    }
    private static bool Matches(ChatEmoji emoji, string[] terms)
    { foreach (var term in terms) if (!emoji.SearchText.Contains(term, StringComparison.Ordinal)) return false; return true; }
    internal static bool IsEmoji(string? value) => value is { Length: > 0 and <= 32 } && Find(value) != null;
    internal static string[] Remember(IEnumerable<string>? recent, string value) =>
        new[] { value }.Concat(recent ?? []).Where(IsEmoji).Distinct(StringComparer.Ordinal).Take(40).ToArray();
    // Insert complete Unicode sequences; never cut a surrogate pair/ZWJ at the body limit.
    internal static bool Fits(string text, int selectionLength, string emoji, int maximum) =>
        IsEmoji(emoji) && text.Length - selectionLength + emoji.Length <= maximum;
    private static ChatEmoji[] Load()
    {
        using var stream = typeof(ChatEmojiCatalog).Assembly.GetManifestResourceStream("Pookie.App.Assets.Emoji.catalog.json")!;
        return JsonSerializer.Deserialize(stream, ChatEmojiJson.Default.ChatEmojiArray) ?? [];
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChatEmoji[]))]
internal partial class ChatEmojiJson : JsonSerializerContext;
