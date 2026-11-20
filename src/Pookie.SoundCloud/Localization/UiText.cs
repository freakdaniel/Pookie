using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Pookie.Localization;

/// <summary>Resolves stable UI keys against the selected catalog, with US English as the fallback.</summary>
public static class UiText
{
    public const string DefaultLanguage = "en-US";
    public static IReadOnlyList<(string Title, string Code)> Choices { get; } = Array.AsReadOnly(new[]
    {
        ("Deutsch", "de"), ("English (US)", DefaultLanguage), ("Español", "es"), ("Français", "fr"),
        ("Italiano", "it"), ("Nederlands", "nl"), ("Polski", "pl"), ("Português", "pt"), ("Русский", "ru"), ("Svenska", "sv")
    });
    private static readonly FrozenSet<string> Languages = Choices.Select(choice => choice.Code.Split('-')[0])
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, FrozenDictionary<string, string>> Catalogs = new();

    // Catalog strings have distinct identities even when two keys have identical text.
    // Preserve that identity for live views. User content is never looked up by its spelling.
    private sealed record Message(string Language, string Key, object?[]? Arguments = null,
        long? Count = null, bool LiteralFormat = false, string? Separator = null);
    private static readonly ConditionalWeakTable<string, Message> Origins = new();
    private static CultureInfo? selectedLanguage;
    public static CultureInfo Language => Volatile.Read(ref selectedLanguage) ?? CultureInfo.CurrentUICulture;

    public static void SetLanguage(CultureInfo language) => Volatile.Write(ref selectedLanguage, language);

    public static CultureInfo ResolveLanguage(IEnumerable<string?> preferences)
    {
        foreach (var preference in preferences)
        {
            if (string.IsNullOrWhiteSpace(preference)) continue;
            var tag = preference.Trim().Split('.', '@')[0].Replace('_', '-');
            if (tag is "C" or "POSIX") return CultureInfo.GetCultureInfo(DefaultLanguage);
            var language = tag.Split('-')[0];
            if (!Languages.Contains(language)) continue;
            if (language.Equals("en", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo(DefaultLanguage);
            try { return CultureInfo.GetCultureInfo(tag); }
            catch (CultureNotFoundException) { continue; }
        }
        return CultureInfo.GetCultureInfo(DefaultLanguage);
    }

    public static string Get(string key)
    {
        var language = Language.TwoLetterISOLanguageName;
        if (Languages.Contains(language) && Catalog(language).TryGetValue(key, out var translation)) return translation;
        return Catalog("en")[key];
    }

    public static string Format(string key, params object?[] arguments) =>
        Render(new(Language.Name, key, arguments));

    // A composed label may contain verbatim artist names or user input alongside app messages.
    public static string Compose(FormattableString label) =>
        Render(new(Language.Name, label.Format, label.GetArguments(), LiteralFormat: true));

    public static string Join(string separator, IEnumerable<string?> parts)
    {
        var items = parts.ToArray();
        return items.Length switch
        {
            0 => "",
            1 => Refresh(items[0] ?? ""),
            _ => Render(new(Language.Name, "", items, Separator: separator))
        };
    }

    public static string TrackCount(long count) => Compose($"{count:N0} {Plural(count, "collection.track")}");
    public static string FollowerCount(long count) => Compose($"{count:N0} {Plural(count, "profile.follower")}");

    public static string Plural(long count, string key) => Render(new(Language.Name, key, Count: count));

    [return: NotNullIfNotNull(nameof(text))]
    public static string? Refresh(string? text)
    {
        if (string.IsNullOrEmpty(text) || !Origins.TryGetValue(text, out var source)) return text;
        if (source.Arguments == null && source.Count == null)
            return source.Language == Language.TwoLetterISOLanguageName ? text : Get(source.Key);
        return source.Language == Language.Name ? text : Render(source with { Language = Language.Name });
    }

    private static string Render(Message message)
    {
        string value;
        if (message.Count is long count)
        {
            // Keep the count so switching between languages can select a different plural form.
            value = new string(Get(message.Key + "." + PluralForm(count)).AsSpan());
        }
        else
        {
            var args = message.Arguments!;
            object?[]? refreshed = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is not string text) continue;
                var translation = Refresh(text);
                if (ReferenceEquals(translation, text)) continue;
                refreshed ??= (object?[])args.Clone();
                refreshed[i] = translation;
            }
            value = message.Separator is { } separator
                ? string.Join(Refresh(separator), refreshed ?? args)
                : string.Format(CultureInfo.CurrentCulture, message.LiteralFormat ? message.Key : Get(message.Key), refreshed ?? args);
        }
        if (value.Length != 0) Origins.GetValue(value, _ => message);
        return value;
    }

    private static string PluralForm(long count)
    {
        var language = Language.TwoLetterISOLanguageName;
        if (language == "ru") return count % 100 is >= 11 and <= 14 ? "many" :
            count % 10 == 1 ? "one" : count % 10 is >= 2 and <= 4 ? "few" : "many";
        if (language == "pl") return count == 1 ? "one" :
            count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) ? "few" : "many";
        if (count == 0 && (language == "fr" || language == "pt" && Language.Name != "pt-PT")) return "one";
        return count == 1 ? "one" : "many";
    }

    private static FrozenDictionary<string, string> Catalog(string language) => Catalogs.GetOrAdd(language, static code =>
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"Pookie.Localization.{code}.json")
            ?? throw new InvalidOperationException($"Missing application language catalog: {code}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToFrozenDictionary(
            entry => entry.Name, entry =>
            {
                var value = entry.Value.GetString()!;
                Origins.Add(value, new(code, entry.Name));
                return value;
            }, StringComparer.Ordinal);
    });
}
