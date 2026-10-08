using System.Net;
using Pookie.Lyrics;
using Xunit;

namespace Pookie.Lyrics.Tests;

public sealed class LyricsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pookie-lyrics-test-" + Guid.NewGuid().ToString("N"));
    private readonly LyricsIdentity track = new(17, "Песня", "Артист", 180);
    private static LyricsCandidate Candidate(long id = 1) => new()
    { Id = id, TrackName = "Песня", ArtistName = "Артист", Duration = 180, SyncedLyrics = "[00:01.00]Первая\n[00:04.00]Вторая", PlainLyrics = "Первая\nВторая" };
    private LyricsService Service(ILyricsProvider provider, TimeProvider? clock = null) => new(provider, Path.Combine(root, "Cache"), clock ?? new Clock());

    [Fact] public void LrcGroupsSimultaneousLinesPreservesChorusAndAppliesFileOffset()
    {
        var document = LrcParser.Parse("[ar:Artist]\n[offset:+500]\n[00:01.250][00:09.00]Припев\n[00:01.250]Другая строка\n[00:04.00]\n[00:70.00]bad", null, "test", null, "", "");
        Assert.Equal(3, document.Lines.Length); Assert.Equal(.75, document.Lines[0].Start);
        Assert.Equal("Припев\nДругая строка", document.Lines[0].Text);
        Assert.Equal("", document.Lines[1].Text); Assert.Equal("Припев", document.Lines[2].Text);
        Assert.Equal(-1, document.ActiveLine(.7)); Assert.Equal(0, document.ActiveLine(1));
        Assert.Equal(2, document.ActiveLine(10)); Assert.Equal(0, document.ActiveLine(2));
    }

    [Fact] public void PlainTextAndMalformedLrcDoNotInventTimestamps()
    {
        var document = LrcParser.Parse("[ti:test]\nПросто текст", null, "test", null, "", "");
        Assert.Empty(document.Lines); Assert.Equal("Просто текст", document.PlainText);
        Assert.Equal(LyricsStatus.Plain, document.Status);
    }

    [Fact] public void MatchingAllowsSmallDurationDifferencesButRejectsOtherVersionsAndUnrelatedArtists()
    {
        Assert.True(LyricsMatcher.Matches(track with { Title = "Артист — Песня [prod. Person]", Artist = "Uploader" }, Candidate()));
        Assert.True(LyricsMatcher.Matches(track with { Duration = 185 }, Candidate()));
        Assert.False(LyricsMatcher.Matches(track with { Duration = 200 }, Candidate()));
        Assert.False(LyricsMatcher.Matches(track with { Title = "Песня (Sped Up)", Duration = 145 }, Candidate()));
        Assert.False(LyricsMatcher.Matches(track with { Artist = "Uploader" }, Candidate()));
        Assert.True(LyricsMatcher.Matches(track, Candidate() with { ArtistName = "Артист & Другой" }));
        Assert.False(LyricsMatcher.Matches(track, Candidate() with { Duration = double.NaN }));
    }

    [Fact] public void RankingPrefersSyncedThenClosestDurationAndBreaksTiesDeterministically()
    {
        var plain = Candidate() with { SyncedLyrics = null };
        var timed = Candidate(2) with { Duration = 184 };
        var nearest = Candidate(3) with { Duration = 180.5 };
        Assert.Equal(2, LyricsMatcher.Choose(track, [plain, timed])!.Id);
        Assert.Equal(3, LyricsMatcher.Choose(track, [plain, timed, nearest])!.Id);
        Assert.Equal(1, LyricsMatcher.Choose(track, [Candidate(2) with { SyncedLyrics = "[00:06]Другая" }, Candidate()])!.Id);
        Assert.Equal(1, LyricsMatcher.Choose(track, [Candidate(), Candidate(2)])!.Id);
    }

    [Fact] public void SmallSpellingDifferencesAreScoredWithoutAcceptingUnrelatedSongs()
    {
        var identity = track with { Title = "Ночная история", Artist = "Исполнитель" };
        Assert.True(LyricsMatcher.Matches(identity, Candidate() with { TrackName = "Ночная исторя", ArtistName = "Исполнитель" }));
        Assert.False(LyricsMatcher.Matches(identity, Candidate() with { TrackName = "Другая песня", ArtistName = "Исполнитель" }));
    }

    [Fact] public void BrokenSyncedDataDoesNotOutrankValidTimestamps()
    {
        Assert.Equal(2, LyricsMatcher.Choose(track, [Candidate() with { SyncedLyrics = "Без меток" }, Candidate(2) with { Duration = 181 }])!.Id);
    }

    [Fact] public async Task ExactSyncedMatchIsCachedAndDuplicatesMakeNoNewRequest()
    {
        var provider = new Provider { Exact = Candidate() }; using var service = Service(provider);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        await service.ResolveAsync(track, default); Assert.Equal(1, provider.Calls);
    }

    [Fact] public async Task PlainExactLookupStillSearchesForTimedLyrics()
    {
        var provider = new Provider { Exact = Candidate() with { SyncedLyrics = null }, Search = [Candidate(2) with { Duration = 181 }] };
        using var service = Service(provider);
        var result = await service.ResolveAsync(track, default);
        Assert.Equal(LyricsStatus.Synced, result.Status); Assert.Equal(2, result.Document!.ProviderId); Assert.Equal(2, provider.Calls);
    }

    [Fact] public async Task PlainOnlyRecordIsDisplayedWhenTimedSearchFindsNothing()
    {
        using var service = Service(new Provider { Exact = Candidate() with { SyncedLyrics = null } });
        var result = await service.ResolveAsync(track, default);
        Assert.Equal(LyricsStatus.Plain, result.Status); Assert.NotNull(result.Document);
        Assert.Equal("Первая\nВторая", result.Document.PlainText); Assert.Empty(result.Document.Lines);
    }

    [Fact] public async Task ParenthesizedUploaderSuffixRetriesCleanTitleAndReturnsPlainLyrics()
    {
        var identity = new LyricsIdentity(18, "whitek3d — White Buttowski (tg: whitek3d)", "whitek3d", 85);
        var record = new LyricsCandidate { Id = 23269745, TrackName = "White Buttowski", ArtistName = "whitek3d",
            AlbumName = "WHITEK3D FIRST FAME", Duration = 85, PlainLyrics = "Первая строка\nВторая строка" };
        var provider = new Provider { SearchResult = (title, _) => title == "White Buttowski" ? [record] : [] };
        using var service = Service(provider);
        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(LyricsStatus.Plain, result.Status); Assert.Equal(record.Id, result.Document!.ProviderId);
        Assert.Empty(result.Document.Lines); Assert.Equal(record.PlainLyrics, result.Document.PlainText);
        Assert.Equal("White Buttowski (tg: whitek3d)", Assert.Single(provider.Gets).Title);
        Assert.Equal(new[] { "White Buttowski (tg: whitek3d)", identity.Title, "White Buttowski", "whitek3d — White Buttowski" },
            provider.Searches.Select(query => query.Title));
        Assert.Equal(5, provider.Calls);
        await service.ResolveAsync(identity, default); Assert.Equal(5, provider.Calls);
    }

    [Fact] public async Task CleanTitleCanUpgradePlainResultToSyncedWithoutDiscardingPlainFallback()
    {
        var identity = track with { Title = "Песня (ссылка на канал)" };
        var provider = new Provider { Exact = Candidate() with { SyncedLyrics = null },
            SearchResult = (title, _) => title == "Песня" ? [Candidate(2)] : [] };
        using var service = Service(provider);
        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(LyricsStatus.Synced, result.Status); Assert.Equal(2, result.Document!.ProviderId);
        Assert.Equal(new[] { identity.Title, "Песня" }, provider.Searches.Select(query => query.Title));
    }

    [Fact] public async Task ExactOriginalSyncedRecordDoesNotTriggerCleanTitleSearch()
    {
        var identity = track with { Title = "Песня (название главы)" };
        var provider = new Provider { Exact = Candidate() with { TrackName = identity.Title } };
        using var service = Service(provider);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(identity, default)).Status);
        Assert.Equal(1, provider.Calls); Assert.Empty(provider.Searches);
    }

    [Fact] public void ParenthesisFallbackHandlesMultipleNestedGroupsWithoutAcceptingWrongVersionOrDuration()
    {
        Assert.Equal("Артист — Песня", LyricsMatcher.WithoutParentheses("Артист — (канал (ссылка)) Песня (tg: artist)"));
        var identity = track with { Title = "Артист — Песня (ссылка на канал)" };
        Assert.True(LyricsMatcher.Matches(identity, Candidate() with { SyncedLyrics = null }));
        Assert.False(LyricsMatcher.Matches(identity, Candidate() with { Duration = 240 }));
        Assert.False(LyricsMatcher.Matches(identity, Candidate() with { ArtistName = "Другой артист" }));
        Assert.False(LyricsMatcher.Matches(track with { Title = "Песня (Sped Up) (tg: artist)" }, Candidate()));
    }

    [Fact] public async Task OlderNegativeCacheCannotSuppressNewTitleFallback()
    {
        var identity = track with { Title = "Песня (tg: artist)" };
        var provider = new Provider { SearchResult = (title, _) => title == "Песня" ? [Candidate() with { SyncedLyrics = null }] : [] };
        using var service = Service(provider);
        var oldFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"v3-title-fallback\n{identity.Title}\n{identity.Artist}\n{identity.Duration:R}\n{identity.Album}\n{identity.TimingAvailable}"))));
        var payload = System.Text.Json.JsonSerializer.Serialize(new { fingerprint = oldFingerprint, at = DateTimeOffset.UtcNow,
            result = new LyricsResult(LyricsStatus.NotFound), complete = true }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(Path.Combine(root, "Cache", $"{identity.TrackId}-{oldFingerprint}.json"), payload);
        Assert.NotEqual(oldFingerprint, identity.Fingerprint);
        Assert.Equal(LyricsStatus.Plain, (await service.ResolveAsync(identity, default)).Status);
        Assert.Equal(3, provider.Calls);
    }

    [Fact] public async Task PartialPlainResultDoesNotCacheAwayAFailedTimedLookup()
    {
        var provider = new Provider { Exact = Candidate() with { SyncedLyrics = null }, SearchError = new HttpRequestException("no", null, HttpStatusCode.Forbidden) };
        using var service = Service(provider);
        Assert.Equal(LyricsStatus.Plain, (await service.ResolveAsync(track, default)).Status);
        provider.SearchError = null; provider.Search = [Candidate(2)];
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        Assert.Equal(4, provider.Calls);
    }

    [Theory]
    [InlineData("spedup")]
    [InlineData("sped up")]
    [InlineData("speed up")]
    [InlineData("speedup")]
    [InlineData("𝙨𝙥𝙚𝙚𝙙 𝙪𝙥")]
    [InlineData("𝙨𝙥𝙚𝙙𝙪𝙥")]
    [InlineData("𝘴𝘱𝘦𝘥 𝘶𝘱")]
    [InlineData("ＳＰＥＥＤＵＰ")]
    [InlineData("𝓼𝓹𝓮𝓮𝓭 𝓾𝓹")]
    [InlineData("𝚜𝚙𝚎𝚎𝚍 𝚞𝚙")]
    [InlineData("ⓢⓟⓔⓔⓓ ⓤⓟ")]
    [InlineData("ˢᵖᵉᵉᵈ ᵘᵖ")]
    [InlineData("ꜱᴘᴇᴇᴅ ᴜᴘ")]
    [InlineData("s̲p̲e̲e̲d̲ u̲p̲")]
    [InlineData("śpêéd úp")]
    [InlineData("s\u200Bpe\u2060ed\u200Dup")]
    [InlineData("speed \u200B up")]
    [InlineData("ѕрееԁ υр")]
    [InlineData("ꜱᴘᴇᴇᴅ ♡")]
    [InlineData("speed♡")]
    [InlineData("speed ♥️")]
    [InlineData("speed 💕")]
    [InlineData("speed")]
    [InlineData("speed up + reverb")]
    [InlineData("spedup+reverb")]
    [InlineData("speedup & reverb")]
    [InlineData("sped up and reverb")]
    [InlineData("speedup/reverb")]
    [InlineData("speed up x reverb")]
    [InlineData("speed up with reverb")]
    [InlineData("speed up reverb")]
    [InlineData("speed up + reverbed version")]
    [InlineData("speedup + echo")]
    [InlineData("speedup + bass boosted + reverb")]
    [InlineData("speedup + 8d audio")]
    [InlineData("reverb + speed up")]
    [InlineData("echo & reverb + spedup")]
    [InlineData("bass boosted + reverb + speedup")]
    [InlineData("speed up by Kannekii")]
    [InlineData("spedup - by @Kannekii")]
    [InlineData("speed up + reverb by Live Remix")]
    [InlineData("speed up by DJ Speed")]
    [InlineData("speed up by DJ - Speed")]
    [InlineData("reverb + speed up by DJ Speedup")]
    [InlineData("𝙨𝙥𝙚𝙚𝙙 𝙪𝙥 + reverb by Kannekii")]
    public void SpedUpAliasesDetectTrailingVersionAndMatchNativeLyrics(string tag)
    {
        var identity = track with { Title = $"Артист — Песня ({tag.ToUpperInvariant()}) (tg: artist)", Duration = 150 };
        Assert.Equal("Артист — Песня (tg: artist)", LyricsMatcher.SpedUpOriginal(identity)!.Title);
        Assert.True(LyricsMatcher.Matches(track with { Title = $"Песня ({tag})", Duration = 150 },
            Candidate() with { TrackName = "Песня (Sped Up)", Duration = 150 }));
        Assert.True(LyricsMatcher.IsExact(track with { Title = "Песня (Sped Up)", Duration = 150 },
            Candidate() with { TrackName = $"Песня ({tag})", Duration = 150 }));
        Assert.False(LyricsMatcher.Matches(track with { Title = $"Песня ({tag})" }, Candidate()));
        Assert.Null(LyricsMatcher.SpedUpOriginal(track with { Title = "Speed Up My Heart" }));
        Assert.Null(LyricsMatcher.SpedUpOriginal(track with { Title = "𝙨𝙥𝙚𝙚𝙙 𝙪𝙥 My Heart" }));
        Assert.Null(LyricsMatcher.SpedUpOriginal(track with { Title = "speedup" }));
        Assert.Null(LyricsMatcher.SpedUpOriginal(track with { Title = "Песня" }));
    }

    [Theory]
    [InlineData("Speed")]
    [InlineData("spedup")]
    [InlineData("Артист - Speed")]
    [InlineData("Артист - speedup")]
    [InlineData("Speed Up My Heart")]
    [InlineData("The Speed of Sound")]
    [InlineData("Песня Speed Racer")]
    [InlineData("Песня superspeed")]
    [InlineData("Песня speed2")]
    [InlineData("Песня speed_up")]
    [InlineData("Песня (tg: speed)")]
    [InlineData("Песня (I love speed up)")]
    [InlineData("Песня (prod: speed)")]
    [InlineData("Песня ꜱᴘᴇᴇᴅ Racer")]
    [InlineData("Песня speed up + unknown effect")]
    [InlineData("Песня speed up + reverbish")]
    [InlineData("Песня speed up by")]
    [InlineData("Песня speed up by ♡")]
    [InlineData("Песня (tg: speed up by User)")]
    [InlineData("Speed Up by the River")]
    [InlineData("Песня reverb")]
    public void SpeedWordsInSongNamesAndMetadataDoNotStartAdaptation(string title)
    {
        var identity = track with { Title = title };
        Assert.Null(LyricsMatcher.SpedUpOriginal(identity));
        var query = identity.Queries()[0];
        var candidate = Candidate() with { TrackName = query.Title, ArtistName = query.Artist };
        Assert.True(LyricsMatcher.IsExact(identity, candidate));
    }

    [Theory]
    [InlineData("ЗНАК - Toxi$,Дора speed up + reverb", "ЗНАК - Toxi$,Дора")]
    [InlineData("дети немой страны (speed up by Kannekii)", "дети немой страны")]
    [InlineData("Песня speedup & reverb by Someone (tg: channel)", "Песня (tg: channel)")]
    [InlineData("Песня [reverb + speedup by DJ Speed]", "Песня")]
    [InlineData("Песня speed up + reverb (Live)", "Песня (Live)")]
    public void CompoundSpeedEditsRemoveOnlyEffectsAndEditorCredits(string title, string expected)
    {
        Assert.Equal(expected, LyricsMatcher.SpedUpOriginal(track with { Title = title })!.Title);
    }

    [Theory]
    [InlineData("Песня (speedup - by @Kannekii)")]
    [InlineData("Песня speedup by DJ - Speed")]
    [InlineData("Песня (tg: editor - nickname)")]
    public void EditorAndMetadataSeparatorsDoNotReplaceTheArtist(string title)
    {
        var identity = track with { Title = title };
        Assert.Equal("Артист", identity.Queries()[0].Artist);
        Assert.Equal(LyricsMatcher.SearchTitle(title), identity.Queries()[0].Title);
        var embedded = identity with { Title = "Настоящий артист — " + title };
        Assert.Equal("Настоящий артист", embedded.Queries()[0].Artist);
        Assert.Equal(LyricsMatcher.SearchTitle(title), embedded.Queries()[0].Title);
    }

    [Theory]
    [InlineData("speed up + reverb by Kannekii", true)]
    [InlineData("reverb + speed up by Kannekii", true)]
    [InlineData("speed up + reverb", false)]
    [InlineData("speed up by DJ Speed", false)]
    public async Task CompoundEditsResolveOriginalScaleOrKeepPlainAndRefreshPreviousCache(string tag, bool synced)
    {
        var identity = track with { Title = $"Артист — Песня ({tag})", Duration = 150 };
        var original = Candidate() with { SyncedLyrics = synced ? "[01:00]Первая\n[01:30]Вторая" : null };
        var provider = new Provider { SearchResult = (title, artist) => title == "Песня" && artist == "Артист" ? [original] : [] };
        using var service = Service(provider);
        var oldFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"v6-speed-tags\n{identity.Title}\n{identity.Artist}\n{identity.Duration:R}\n{identity.Album}\n{identity.TimingAvailable}"))));
        var payload = System.Text.Json.JsonSerializer.Serialize(new { fingerprint = oldFingerprint, at = DateTimeOffset.UtcNow,
            result = new LyricsResult(LyricsStatus.NotFound), complete = true }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(Path.Combine(root, "Cache", $"{identity.TrackId}-{oldFingerprint}.json"), payload);

        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(synced ? LyricsStatus.Synced : LyricsStatus.Plain, result.Status);
        Assert.Equal(synced ? new[] { 50d, 75d } : [], result.Document!.Lines.Select(line => line.Start));
        Assert.Contains(("Песня", "Артист"), provider.Searches);
        Assert.DoesNotContain(provider.Searches, query => query.Artist.Contains("Kannekii", StringComparison.Ordinal));
        var calls = provider.Calls;
        Assert.Equal(result.Document.Lines, (await service.ResolveAsync(identity, default)).Document!.Lines);
        Assert.Equal(calls, provider.Calls);
    }

    [Fact] public async Task CompoundNativeLyricsRemainUnscaledAndPreserveSeparateRecordingVersions()
    {
        var identity = track with { Title = "Песня (speed up + reverb by Live Remix)", Duration = 150 };
        var native = Candidate() with { TrackName = "Песня (spedup)", Duration = 150, SyncedLyrics = "[01:00]Первая" };
        using var service = Service(new Provider { Exact = native });
        Assert.Equal(60, Assert.Single((await service.ResolveAsync(identity, default)).Document!.Lines).Start);
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity with { Title = "Песня (Live) speedup + reverb by Remix" }, [Candidate()]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity with { Title = "Песня (Remix) speedup by Live" }, [Candidate()]));
    }

    [Fact] public void SpeedTagFoldingPreservesArtistAndSongLettersAndOtherVersionTags()
    {
        const string title = "ѕрееԁ — Café (ꜱᴘᴇᴇᴅ ᴜᴘ ♡) (tg: uploader)";
        Assert.Equal("ѕрееԁ — Café (speed up) (tg: uploader)", LyricsMatcher.SearchTitle(title));
        var identity = track with { Title = title, Duration = 150 };
        Assert.Equal("ѕрееԁ — Café (tg: uploader)", LyricsMatcher.SpedUpOriginal(identity)!.Title);
        Assert.Equal("ѕрееԁ", identity.Queries()[0].Artist);
        var remix = track with { Title = "Песня (Remix) speed ♡", Duration = 150 };
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(remix, [Candidate()]));
        Assert.Equal(1, LyricsMatcher.ChooseSpedUpOriginal(remix, [Candidate() with { TrackName = "Песня (Remix)" }])!.Id);
        Assert.Equal("Песня", LyricsMatcher.SpedUpOriginal(track with { Title = "Песня (spedup) (speed up ♡)" })!.Title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BareSpeedWithHeartFindsEmbeddedArtistOriginalAndRefreshesPreviousCache(bool synced)
    {
        var identity = new LyricsIdentity(19, "кис кис - мальчик speed ♡", "даша", 150);
        var original = Candidate() with { TrackName = "мальчик", ArtistName = "КИС-КИС",
            SyncedLyrics = synced ? "[01:00]Первая\n[01:30]Вторая" : null };
        var provider = new Provider { SearchResult = (title, artist) => title == "мальчик" && artist == "кис кис" ? [original] : [] };
        using var service = Service(provider);
        var oldFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"v5-unicode\n{identity.Title}\n{identity.Artist}\n{identity.Duration:R}\n{identity.Album}\n{identity.TimingAvailable}"))));
        var payload = System.Text.Json.JsonSerializer.Serialize(new { fingerprint = oldFingerprint, at = DateTimeOffset.UtcNow,
            result = new LyricsResult(LyricsStatus.NotFound), complete = true }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(Path.Combine(root, "Cache", $"{identity.TrackId}-{oldFingerprint}.json"), payload);

        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(synced ? LyricsStatus.Synced : LyricsStatus.Plain, result.Status);
        Assert.Equal(synced ? new[] { 50d, 75d } : [], result.Document!.Lines.Select(line => line.Start));
        Assert.Contains(("мальчик", "кис кис"), provider.Searches);
        Assert.Contains(provider.Gets, query => query.Title == "мальчик speed" && query.Artist == "кис кис");
        var calls = provider.Calls;
        Assert.Equal(result.Status, (await service.ResolveAsync(identity, default)).Status);
        Assert.Equal(calls, provider.Calls);
    }

    [Fact] public async Task DecoratedNativeSpeedUpLyricsKeepTheirOwnTimestamps()
    {
        var identity = track with { Title = "Песня (ѕрееԁ υр ♡)", Duration = 150 };
        var provider = new Provider { Exact = Candidate() with { TrackName = "Песня (spedup)", Duration = 150,
            SyncedLyrics = "[01:00]Первая\n[01:30]Вторая" } };
        using var service = Service(provider);
        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(LyricsStatus.Synced, result.Status);
        Assert.Equal(new[] { 60d, 90d }, result.Document!.Lines.Select(line => line.Start));
        Assert.Empty(provider.Searches);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StyledSpeedUpSearchFindsOriginalAndIgnoresOlderNegativeCache(bool synced)
    {
        var identity = new LyricsIdentity(18, "Дипинс - Этажи (𝙨𝙥𝙚𝙚𝙙 𝙪𝙥)", "wqombo", 150);
        var original = Candidate() with { TrackName = "Этажи", ArtistName = "Дипинс",
            SyncedLyrics = synced ? "[01:00]Первая\n[01:30]Вторая" : null };
        var provider = new Provider { SearchResult = (title, artist) => title == "Этажи" && artist == "Дипинс" ? [original] : [] };
        using var service = Service(provider);
        var oldFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"v4-spedup\n{identity.Title}\n{identity.Artist}\n{identity.Duration:R}\n{identity.Album}\n{identity.TimingAvailable}"))));
        var payload = System.Text.Json.JsonSerializer.Serialize(new { fingerprint = oldFingerprint, at = DateTimeOffset.UtcNow,
            result = new LyricsResult(LyricsStatus.NotFound), complete = true }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(Path.Combine(root, "Cache", $"{identity.TrackId}-{oldFingerprint}.json"), payload);

        Assert.Equal("Дипинс - Этажи", LyricsMatcher.SpedUpOriginal(identity)!.Title);
        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(synced ? LyricsStatus.Synced : LyricsStatus.Plain, result.Status);
        Assert.Equal(synced ? new[] { 50d, 75d } : [], result.Document!.Lines.Select(line => line.Start));
        Assert.Equal("Первая\nВторая", result.Document.PlainText);
        Assert.Contains(provider.Gets, query => query.Title == "Этажи (speed up)" && query.Artist == "Дипинс");
        Assert.DoesNotContain(provider.Searches, query => query.Title.Contains("𝙨", StringComparison.Ordinal));
        Assert.Contains(("Этажи", "Дипинс"), provider.Searches);
    }

    [Fact] public async Task SpedUpOriginalScalesAllGroupsAndIsNotScaledAgainFromCache()
    {
        var identity = track with { Title = "Песня (spedup)", Duration = 150 };
        var original = Candidate() with { SyncedLyrics = "[offset:+1000]\n[00:01]Начало\n[01:01]\n[02:01]Финал", PlainLyrics = null };
        var provider = new Provider { SearchResult = (title, _) => title == "Песня" ? [original] : [] };
        using var service = Service(provider);
        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(LyricsStatus.Synced, result.Status);
        Assert.Equal(new[] { 0d, 50d, 100d }, result.Document!.Lines.Select(line => line.Start));
        Assert.Equal("", result.Document.Lines[1].Text); Assert.Equal(1, result.Document.ActiveLine(50));
        var calls = provider.Calls;
        var cached = await service.ResolveAsync(identity, default);
        Assert.Equal(result.Document.Lines, cached.Document!.Lines); Assert.Equal(calls, provider.Calls);
        Assert.Equal(1, provider.Searches.Count(query => query.Title == "Песня"));
    }

    [Fact] public async Task SpedUpSearchWithoutBracketsFallsBackToOriginalPlainText()
    {
        var provider = new Provider { SearchResult = (title, _) => title == "Песня" ? [Candidate() with { SyncedLyrics = null }] : [] };
        using var service = Service(provider);
        var result = await service.ResolveAsync(track with { Title = "Песня #speedup", Duration = 150 }, default);
        Assert.Equal(LyricsStatus.Plain, result.Status); Assert.Empty(result.Document!.Lines);
        Assert.Equal("Первая\nВторая", result.Document.PlainText);
        Assert.Equal(new[] { "Песня #speedup", "Песня" }, provider.Searches.Select(query => query.Title));
    }

    [Fact] public async Task NativeSpedUpTimestampsRemainUnscaledAndAvoidOriginalSearch()
    {
        var identity = track with { Title = "Песня (speedup)", Duration = 150 };
        var native = Candidate() with { TrackName = "Песня (sped up)", Duration = 150, SyncedLyrics = "[01:00]Строка" };
        var provider = new Provider { Exact = native };
        using var service = Service(provider);
        var result = await service.ResolveAsync(identity, default);
        Assert.Equal(LyricsStatus.Synced, result.Status); Assert.Equal(60, Assert.Single(result.Document!.Lines).Start);
        Assert.Equal(1, provider.Calls);
    }

    [Fact] public async Task AdaptedOriginalCanUpgradeNativePlainButCannotReplaceItWithAnotherPlain()
    {
        var identity = track with { Title = "Песня (sped up)", Duration = 150 };
        var native = Candidate(2) with { TrackName = identity.Title, Duration = 150, SyncedLyrics = null, PlainLyrics = "Текст ускоренной версии" };
        var provider = new Provider { Exact = native, SearchResult = (title, _) => title == "Песня" ? [Candidate()] : [] };
        using var service = Service(provider);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(identity, default)).Status);
        provider.SearchResult = (title, _) => title == "Песня" ? [Candidate() with { SyncedLyrics = null }] : [];
        var plain = await service.ResolveAsync(identity, default, force: true);
        Assert.Equal(LyricsStatus.Plain, plain.Status); Assert.Equal(2, plain.Document!.ProviderId);
    }

    [Fact] public void OriginalSpeedEstimateRejectsDifferentArtistsVersionsAndImplausibleRates()
    {
        var identity = track with { Title = "Песня (Sped Up)", Duration = 150 };
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { Duration = 120 }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { Duration = 301 }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { Duration = double.NaN }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { ArtistName = "Другой артист" }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { TrackName = "Песня (Remix)" }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { TrackName = "Песня (Live)" }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate() with { TrackName = "Песенка" }]));
        Assert.Null(LyricsMatcher.ChooseSpedUpOriginal(track, [Candidate()]));
    }

    [Fact] public async Task UnknownDurationPreviewAndInvalidOriginalTimestampsFallBackToPlain()
    {
        foreach (var identity in new[] { track with { Title = "Песня spedup", Duration = 0 },
            track with { Title = "Песня spedup", Duration = 150, TimingAvailable = false } })
        {
            using var service = Service(new Provider { SearchResult = (title, _) => title == "Песня" ? [Candidate()] : [] });
            var result = await service.ResolveAsync(identity, default, force: true);
            Assert.Equal(LyricsStatus.Plain, result.Status); Assert.Empty(result.Document!.Lines);
        }
        var broken = Candidate() with { SyncedLyrics = "[10:00]Строка" };
        Assert.Empty(LyricsMatcher.SpedUpDocument(track with { Title = "Песня spedup", Duration = 150 }, broken).Lines);
        Assert.Equal(1, LyricsMatcher.SpedUpDocument(track with { Duration = 150 }, Candidate()).Lines[0].Start);
    }

    [Fact] public async Task FailedOriginalLookupIsUnavailableAndCanRecoverWithoutNegativeCache()
    {
        var identity = track with { Title = "Песня spedup", Duration = 150 };
        var provider = new Provider { SearchResult = (title, _) => title == "Песня"
            ? throw new HttpRequestException("no", null, HttpStatusCode.Forbidden) : [] };
        using var service = Service(provider);
        Assert.Equal(LyricsStatus.Unavailable, (await service.ResolveAsync(identity, default)).Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Cache")));
        provider.SearchResult = (title, _) => title == "Песня" ? [Candidate()] : [];
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(identity, default)).Status);
    }

    [Fact] public void PlausibleOriginalRankingPrefersUsableTimestampsThenStableMetadataMatch()
    {
        var identity = track with { Title = "Песня spedup", Duration = 150 };
        var plain = Candidate() with { SyncedLyrics = null };
        var timed = Candidate(2) with { Duration = 190 };
        Assert.Equal(2, LyricsMatcher.ChooseSpedUpOriginal(identity, [plain, timed])!.Id);
        Assert.Equal(1, LyricsMatcher.ChooseSpedUpOriginal(identity, [Candidate(2), Candidate()])!.Id);
    }

    [Fact] public async Task AutomaticSelectionUsesDurationAndNeverRequiresUserChoice()
    {
        var provider = new Provider { Search = [Candidate(), Candidate(2) with { Duration = 179.5, SyncedLyrics = "[00:06]Другая" }] };
        using var service = Service(provider);
        var result = await service.ResolveAsync(track with { Duration = 179 }, default);
        Assert.Equal(LyricsStatus.Synced, result.Status); Assert.Equal(2, result.Document!.ProviderId);
        Assert.InRange(provider.Calls, 2, 3);
    }

    [Fact] public async Task SignatureChangeInvalidatesOldAutomaticSelection()
    {
        var provider = new Provider { Exact = Candidate() }; using var service = Service(provider);
        await service.ResolveAsync(track, default); provider.Exact = null;
        Assert.Equal(LyricsStatus.NotFound, (await service.ResolveAsync(track with { Duration = 145 }, default)).Status);
        Assert.Equal(3, provider.Calls);
    }

    [Fact] public async Task PreviewGetsPlainTextEvenWhenSyncedRecordExists()
    {
        using var service = Service(new Provider { Exact = Candidate() });
        var result = await service.ResolveAsync(track with { TimingAvailable = false }, default);
        Assert.Equal(LyricsStatus.Plain, result.Status); Assert.Empty(result.Document!.Lines);
    }

    [Fact] public async Task TransientFailuresRetryAutomaticallyWithIncreasingDelay()
    {
        var provider = new Provider { Exact = Candidate(), Failures = 3 }; var clock = new Clock();
        using var service = Service(provider, clock);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        Assert.Equal(4, provider.Calls);
        Assert.Equal(new[] { 750d, 2000d, 4000d }, clock.Delays.Select(delay => delay.TotalMilliseconds));
    }

    [Fact] public async Task TimeoutRetriesButCallerCancellationStopsRequests()
    {
        var provider = new Provider { Exact = Candidate(), Failures = 1, Error = new TaskCanceledException() };
        using var service = Service(provider);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        Assert.Equal(2, provider.Calls);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveAsync(track, cancel.Token));
        Assert.Equal(2, provider.Calls);
    }

    [Fact] public async Task FailedExactRequestCanRecoverThroughSearchWithoutAButton()
    {
        var provider = new Provider { GetError = new HttpRequestException("no", null, HttpStatusCode.Forbidden), Search = [Candidate()] };
        using var service = Service(provider);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status); Assert.Equal(2, provider.Calls);
    }

    [Fact] public async Task NetworkFailuresNeverCreateNegativeCache()
    {
        var provider = new Provider { Fail = true }; using var service = Service(provider);
        Assert.Equal(LyricsStatus.Unavailable, (await service.ResolveAsync(track, default)).Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Cache")));
        provider.Fail = false; provider.Exact = Candidate();
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
    }

    [Fact] public async Task NotFoundIsCachedButFreshResolutionCanCheckAgain()
    {
        var provider = new Provider(); using var service = Service(provider);
        Assert.Equal(LyricsStatus.NotFound, (await service.ResolveAsync(track, default)).Status);
        await service.ResolveAsync(track, default); Assert.Equal(2, provider.Calls);
        provider.Exact = Candidate(); Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default, true)).Status);
    }

    [Fact] public async Task CancelledNonCooperativeReplyCannotWriteCacheOrBlockNextRequest()
    {
        var completion = new TaskCompletionSource<LyricsCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Pending = completion.Task };
        using var service = Service(provider); using var cancel = new CancellationTokenSource();
        var first = service.ResolveAsync(track, cancel.Token); var second = service.ResolveAsync(track, cancel.Token);
        Assert.Equal(1, provider.Calls); cancel.Cancel(); completion.SetResult(Candidate());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Task.Delay(50); Assert.Empty(Directory.GetFiles(Path.Combine(root, "Cache")));
        provider.Pending = null; provider.Exact = Candidate();
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
    }

    [Fact] public async Task ClientUsesIndependentCredentialsAndEscapesMetadata()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("lrclib.net", request.RequestUri!.Host); Assert.Contains("%26", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            Assert.Contains("Pookie", request.Headers.UserAgent.ToString());
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }));
        using var client = new LrclibClient(http); Assert.Empty(await client.SearchAsync("Title & Other", "Artist", default));
    }

    [Fact] public async Task ClientHonors429CooldownAndRejectsLargeResponses()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++; var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60)); return response;
        }));
        using var client = new LrclibClient(http);
        await Assert.ThrowsAsync<LyricsRateLimitException>(() => client.SearchAsync("a", "b", default));
        await Assert.ThrowsAsync<LyricsRateLimitException>(() => client.SearchAsync("a", "b", default)); Assert.Equal(1, calls);
        using var bigHttp = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[LrclibClient.MaxResponseBytes + 1]) }));
        using var bigClient = new LrclibClient(bigHttp);
        await Assert.ThrowsAsync<InvalidDataException>(() => bigClient.SearchAsync("a", "b", default));
    }

    [Fact] public async Task BackendWaitsForRateLimitBeforeRetrying()
    {
        var clock = new Clock();
        var provider = new Provider { Exact = Candidate(), Failures = 1, Error = new LyricsRateLimitException(TimeSpan.FromSeconds(4)) };
        using var service = Service(provider, clock);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(clock.Delays));
    }

    [Fact] public async Task CorruptedCacheAndFailedWritesDoNotPreventShowingText()
    {
        using var service = Service(new Provider { Exact = Candidate() });
        var file = Path.Combine(root, "Cache", $"{track.TrackId}-{track.Fingerprint}.json");
        await File.WriteAllTextAsync(file, "broken");
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        File.Delete(file); Directory.CreateDirectory(file);
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Cache"), "*.tmp"));
    }

    [Fact] public void FingerprintChangesMatchingRulesAndDoesNotDependOnSystemLanguage()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var fingerprint = (track with { Duration = 180.25 }).Fingerprint;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal(fingerprint, (track with { Duration = 180.25 }).Fingerprint);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact] public async Task ExpiredTextRemainsReadableOfflineWithoutOverwritingCache()
    {
        var clock = new Clock(); var provider = new Provider { Exact = Candidate() }; using var service = Service(provider, clock);
        await service.ResolveAsync(track, default);
        var cacheFile = Directory.GetFiles(Path.Combine(root, "Cache")).Single(); var original = await File.ReadAllTextAsync(cacheFile);
        clock.At += TimeSpan.FromDays(31); provider.Fail = true;
        Assert.Equal(LyricsStatus.Synced, (await service.ResolveAsync(track, default)).Status);
        Assert.Equal(original, await File.ReadAllTextAsync(cacheFile));
    }

    [Fact] public async Task InstrumentalMatchDoesNotBecomePlainTextOrNeedSearch()
    {
        var provider = new Provider { Exact = Candidate() with { Instrumental = true, SyncedLyrics = null, PlainLyrics = null } };
        using var service = Service(provider); var result = await service.ResolveAsync(track, default);
        Assert.Equal(LyricsStatus.Instrumental, result.Status); Assert.Empty(result.Document!.Lines); Assert.Equal(1, provider.Calls);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset At = DateTimeOffset.UtcNow;
        public List<TimeSpan> Delays = [];
        public override DateTimeOffset GetUtcNow() => At;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { Delays.Add(dueTime); At += dueTime; return new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan); }
    }
    private sealed class Provider : ILyricsProvider
    {
        public int Calls, Failures;
        public LyricsCandidate? Exact; public LyricsCandidate[] Search = []; public bool Fail;
        public Exception Error = new HttpRequestException(); public Exception? GetError, SearchError;
        public Task<LyricsCandidate?>? Pending;
        public Func<string, string, LyricsCandidate[]>? SearchResult;
        public List<LyricsQuery> Gets = [];
        public List<(string Title, string Artist)> Searches = [];
        private void Send() { Calls++; if (Fail || Failures-- > 0) throw Error; }
        public Task<LyricsCandidate?> GetAsync(LyricsQuery query, CancellationToken token)
        { Send(); Gets.Add(query); if (GetError != null) throw GetError; return Pending ?? Task.FromResult(Exact); }
        public Task<LyricsCandidate[]> SearchAsync(string title, string artist, CancellationToken token)
        { Send(); Searches.Add((title, artist)); if (SearchError != null) throw SearchError; return Task.FromResult(SearchResult?.Invoke(title, artist) ?? Search); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request)); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
