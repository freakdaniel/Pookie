using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class TrackFilterTests
{
    [Theory]
    [InlineData("тёмный принц", "темный принц", true)]
    [InlineData("темный принц", "тёмный принц", true)]
    [InlineData("те\u0308мный принц", "темный принц", true)]
    [InlineData("Café Déjà Vu", "deja cafe", true)]
    [InlineData("Пе\u0301сня", "песня", true)]
    [InlineData("ＮＩＧＨＴ", "night", true)]
    [InlineData("𝕹𝖎𝖌𝖍𝖙", "night", true)]
    [InlineData("Тёмный — принц", "  ПРИНЦ   тёмный ", true)]
    [InlineData("İstanbul", "istanbul", true)]
    [InlineData("!!!", "!!!", true)]
    [InlineData("Песня", "?", false)]
    [InlineData("май", "маи", false)]
    [InlineData("ўсё", "усе", false)]
    [InlineData("темный принц", "тёмный", true)]
    [InlineData("café", "cafe", true)]
    [InlineData("Новая песня", "песня отсутствует", false)]
    public void LanguageVariantsAndWords(string title, string query, bool expected)
    {
        var track = new SoundCloudTrack { Id = 1, Title = title };
        Assert.Equal(expected, TrackFilter.Matches(track, query));
    }

    [Fact]
    public void WordsCanMatchTitleAndArtistWithoutLosingQueueConsistency()
    {
        var match = new SoundCloudTrack { Id = 1, Title = "Догвилль", User = new() { Username = "тёмный принц" } };
        var other = match with { Id = 2, Title = "Другой трек" };
        const string query = "принц догвилль темный";
        var matcher = TrackFilter.CreateMatcher(query);
        Assert.True(matcher(match)); Assert.False(matcher(other));
        var queue = new PlaybackQueue();
        queue.StartContext(new("likes", "Лайки", "Фильтр", Filter: query), [match, other], 0);
        Assert.Single(queue.Snapshot.Source);
        Assert.Equal(1, queue.Current!.Track.Id);
    }
}
