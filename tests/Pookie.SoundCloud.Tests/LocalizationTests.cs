using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pookie.Localization;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("de_CH.UTF-8", "de-CH")]
    [InlineData("pt_BR@latin", "pt-BR")]
    [InlineData("sv-FI", "sv-FI")]
    [InlineData("en-AU", "en-US")]
    [InlineData("C.UTF-8", "en-US")]
    [InlineData("POSIX", "en-US")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", "en-US")]
    public void ResolvesSystemLanguageTags(string preference, string expected) =>
        Assert.Equal(expected, UiText.ResolveLanguage([preference]).Name);

    [Fact]
    public void UsesFirstSupportedPreference() =>
        Assert.Equal("fr-CA", UiText.ResolveLanguage([null, "ja", "fr_CA.UTF-8", "de"]).Name);

    [Theory]
    [InlineData("en-US", "Library", "1 track", "21 tracks")]
    [InlineData("de-DE", "Bibliothek", "1 Track", "21 Tracks")]
    [InlineData("fr-FR", "Bibliothèque", "1 titre", "21 titres")]
    [InlineData("pl-PL", "Biblioteka", "1 utwór", "21 utworów")]
    [InlineData("ru-RU", "Библиотека", "1 трек", "21 трек")]
    [InlineData("ja-JP", "Library", "1 track", "21 tracks")]
    public void LocalizesMessagesAndCounts(string culture, string library, string single, string multiple)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(library, UiText.Get("header.library"));
        Assert.Equal(single, UiText.TrackCount(1));
        Assert.Equal(multiple, UiText.TrackCount(21));
        const string title = "Плейлист {0} · Café";
        Assert.Contains(title, UiText.Format("playlist.delete.confirmation", title));
    }

    [Fact]
    public void RefreshesNestedMessagesAndPluralFormsWhilePreservingUserText()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var original = new string(UiText.Get("header.library").AsSpan());
        var summary = UiText.Join(" · ", [original, UiText.Get("header.library"), UiText.TrackCount(21)]);
        var message = UiText.Format("playlist.delete.confirmation", original);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        Assert.Equal("Library · Библиотека · 21 трек", UiText.Refresh(summary));
        Assert.Equal(UiText.Format("playlist.delete.confirmation", original), UiText.Refresh(message));
        Assert.Same(original, UiText.Refresh(original));
    }

    [Fact]
    public void EveryCatalogCoversTheEnglishMessagesAndPreservesFormatArguments()
    {
        static Dictionary<string, string> Read(string code)
        {
            using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"Pookie.Localization.{code}.json");
            Assert.NotNull(stream);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!);
        }
        static string[] Arguments(string message) =>
            Regex.Matches(message, @"\{\d+(?:,\s*-?\d+)?(?::[^}]*)?\}").Select(match => match.Value).Order().ToArray();

        var english = Read("en");
        foreach (var (_, code) in UiText.Choices)
        {
            var messages = code == UiText.DefaultLanguage ? english : Read(code);
            Assert.Equal(english.Keys.Order(), messages.Keys.Order());
            foreach (var (key, value) in messages)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{code}: {key}");
                Assert.Matches(@"^[a-z][a-zA-Z0-9]*(?:\.[a-z][a-zA-Z0-9]*)+$", key);
                Assert.Equal(Arguments(english[key]), Arguments(value));
                if (english[key].Contains('{')) _ = CompositeFormat.Parse(value);
            }
        }
    }
}
