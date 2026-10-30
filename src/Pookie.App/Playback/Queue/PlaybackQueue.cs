using Pookie.SoundCloud;

namespace Pookie.App.Playback;

internal enum RepeatMode { Off, Context, Track }
internal enum PlaybackContextKind { Track, Likes, Playlist, Album, Artist, Feed, Recommendations, History, Recent, Wave }
internal enum PlaybackEntryOrigin { Context, Manual, Recommendation }
internal enum PlaybackPreparation { Preparing, Ready, Failed, Stopped }
internal sealed record PlaybackContext(string Key, string Title, string Subtitle, string? NextHref = null, bool LibraryCursor = false, PlaybackContextKind Kind = PlaybackContextKind.Track, string? ResourceUrn = null);
internal sealed record PlaybackEntry(long EntryId, SoundCloudTrack Track, PlaybackEntryOrigin Origin, int SourceIndex = -1);
internal sealed record PlaybackQueueSnapshot(long SourceVersion, PlaybackContext? Context, PlaybackEntry? Current,
    IReadOnlyList<PlaybackEntry> Source, IReadOnlyList<PlaybackEntry> ManualUpcoming,
    IReadOnlyList<PlaybackEntry> ContextUpcoming, IReadOnlyList<PlaybackEntry> History,
    IReadOnlyList<PlaybackEntry> Forward, RepeatMode Repeat, bool Shuffle, PlaybackPreparation Preparation,
    IReadOnlyList<PlaybackEntry> Recommendations, bool RecommendationsExhausted, bool AutoContinue = true)
{
    internal IEnumerable<PlaybackEntry> Upcoming => ManualUpcoming.Concat(Forward).Concat(ContextUpcoming).Concat(Recommendations);
    internal bool CanLoadRecommendations => AutoContinue && Context is { Kind: not PlaybackContextKind.Wave, NextHref: null } &&
        Repeat == RepeatMode.Off && !RecommendationsExhausted && ContextUpcoming.Count <= 5 && Recommendations.Count < PlaybackQueue.WaveUpcomingLimit;
    internal bool CanNext => ManualUpcoming.Count + Forward.Count + ContextUpcoming.Count > 0 ||
        AutoContinue && Repeat == RepeatMode.Off && Recommendations.Count > 0 ||
        Context?.NextHref != null || Repeat == RepeatMode.Context && Source.Count > 0;
    internal bool CanPrevious => Current != null;
}

// Only this model decides the next entry. Pages and audio preparation consume its
// snapshot; a track ID is metadata, never the identity of a queue position.
internal sealed class PlaybackQueue
{
    internal const int WaveUpcomingLimit = 3;
    private readonly Random random;
    private readonly List<PlaybackEntry> source = [], upcoming = [], manual = [], history = [], forward = [], recommendations = [];
    private bool recommendationsExhausted;
    private long entrySequence;
    private long waveRevision;
    private PlaybackQueueSnapshot? snapshot;
    internal long SourceVersion { get; private set; }
    internal PlaybackContext? Context { get; private set; }
    internal PlaybackEntry? Current { get; private set; }
    internal RepeatMode Repeat { get; private set; }
    internal bool Shuffle { get; private set; }
    internal bool AutoContinue { get; private set; } = true;
    internal PlaybackPreparation Preparation { get; private set; } = PlaybackPreparation.Stopped;

    internal PlaybackQueue(Random? random = null) => this.random = random ?? Random.Shared;
    internal PlaybackQueueSnapshot Snapshot => snapshot ??= new(SourceVersion, Context, Current,
        Copy(source), Copy(manual), Copy(upcoming), Copy(history), Copy(forward), Repeat, Shuffle, Preparation,
        Copy(recommendations), recommendationsExhausted, AutoContinue);

    internal PlaybackEntry StartContext(PlaybackContext context, IEnumerable<SoundCloudTrack> tracks, int selectedIndex)
    {
        var data = tracks.ToArray();
        if (selectedIndex < 0 || selectedIndex >= data.Length) throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        Clear(); Context = context;
        source.AddRange(data.Select((track, index) => NewEntry(track, PlaybackEntryOrigin.Context, index)));
        Current = source[selectedIndex];
        upcoming.AddRange(Shuffle ? source.Where(entry => entry != Current) : source.Skip(selectedIndex + 1));
        if (Shuffle) Mix(upcoming);
        if (context.Kind == PlaybackContextKind.Wave)
        {
            upcoming.RemoveRange(Math.Min(WaveUpcomingLimit, upcoming.Count), Math.Max(0, upcoming.Count - WaveUpcomingLimit));
            var retained = upcoming.Select(entry => entry.EntryId).Append(Current.EntryId).ToHashSet();
            source.RemoveAll(entry => !retained.Contains(entry.EntryId));
        }
        Preparation = PlaybackPreparation.Preparing; Changed();
        return Current;
    }

    internal void Clear()
    {
        ++SourceVersion; Context = null; Current = null;
        source.Clear(); upcoming.Clear(); manual.Clear(); history.Clear(); forward.Clear(); recommendations.Clear(); recommendationsExhausted = false;
        Preparation = PlaybackPreparation.Stopped; Changed();
    }

    internal void SetShuffle(bool enabled)
    {
        if (Shuffle == enabled) return;
        Shuffle = enabled;
        if (enabled) Mix(upcoming);
        else upcoming.Sort((a, b) => a.SourceIndex.CompareTo(b.SourceIndex));
        Changed();
    }
    internal void SetRepeat(RepeatMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Repeat == mode) return;
        // Repeat changes advancement policy, not the retained visible queue.
        Repeat = mode; Changed();
    }
    internal PlaybackEntry Enqueue(SoundCloudTrack track, bool next = false)
    {
        recommendations.RemoveAll(entry => entry.Track.Id == track.Id);
        var entry = NewEntry(track, PlaybackEntryOrigin.Manual);
        if (next) manual.Insert(0, entry); else manual.Add(entry);
        Changed(); return entry;
    }
    internal bool RemoveManual(long entryId)
    {
        var removed = manual.RemoveAll(entry => entry.EntryId == entryId) > 0;
        if (removed) Changed(); return removed;
    }
    internal bool MoveManual(long entryId, int destination)
    {
        var index = manual.FindIndex(entry => entry.EntryId == entryId);
        if (index < 0 || destination < 0 || destination >= manual.Count) return false;
        var entry = manual[index]; manual.RemoveAt(index); manual.Insert(destination, entry);
        Changed(); return true;
    }
    internal void ClearManual() { manual.Clear(); Changed(); }
    internal void SetAutoContinue(bool enabled)
    {
        if (AutoContinue == enabled) return;
        AutoContinue = enabled;
        recommendations.Clear(); recommendationsExhausted = false; Changed();
    }
    internal void PruneRecommendations(Func<SoundCloudTrack, bool> keep)
    {
        if (recommendations.RemoveAll(entry => !keep(entry.Track)) > 0) Changed();
    }
    internal void RemoveUnavailableRecommendation(long trackId)
    {
        var removed = recommendations.RemoveAll(entry => entry.Track.Id == trackId) > 0;
        if (Context?.Kind != PlaybackContextKind.Wave) { if (removed) Changed(); return; }
        removed |= upcoming.RemoveAll(entry => entry.Track.Id == trackId) > 0;
        // Current is retained independently. Remove it from the repeat source as
        // well, otherwise Repeat Context would resurrect a failed recommendation.
        removed |= source.RemoveAll(entry => entry.Track.Id == trackId) > 0;
        if (removed) Changed();
    }

    internal PlaybackEntry? Next(bool naturalEnd = false)
    {
        if (naturalEnd && Repeat == RepeatMode.Track && Current != null)
        { Preparation = PlaybackPreparation.Preparing; Changed(); return Current; }
        PlaybackEntry? next = TakeFirst(manual) ?? TakeFirst(forward) ?? TakeFirst(upcoming);
        if (next == null && Context?.NextHref == null && Repeat == RepeatMode.Context && source.Count > 0)
        {
            // A new cycle creates fresh occurrences so played rows and repeated
            // tracks never share a retained key in the UI.
            var cycle = source.Select(entry => NewEntry(entry.Track, PlaybackEntryOrigin.Context, entry.SourceIndex)).ToList();
            if (Shuffle) Mix(cycle);
            upcoming.AddRange(cycle); next = TakeFirst(upcoming);
        }
        if (next == null && AutoContinue && Context?.NextHref == null && Repeat == RepeatMode.Off) next = TakeFirst(recommendations);
        if (next == null) { Preparation = PlaybackPreparation.Stopped; Changed(); return null; }
        if (Current != null && Preparation != PlaybackPreparation.Failed) history.Add(Current);
        Current = next; recommendationsExhausted = false; Preparation = PlaybackPreparation.Preparing; Changed(); return next;
    }

    internal PlaybackEntry? Previous()
    {
        if (history.Count == 0) return Current;
        if (Current != null && Preparation != PlaybackPreparation.Failed) forward.Insert(0, Current);
        Current = history[^1]; history.RemoveAt(history.Count - 1);
        Preparation = PlaybackPreparation.Preparing; Changed(); return Current;
    }

    internal PlaybackEntry? Select(long entryId)
    {
        if (Current?.EntryId == entryId) return Current;
        var past = history.FindIndex(entry => entry.EntryId == entryId);
        if (past >= 0)
        {
            while (history.Count > past) Previous();
            return Current;
        }
        var ahead = forward.FindIndex(entry => entry.EntryId == entryId);
        if (ahead >= 0)
        {
            if (Current != null && Preparation != PlaybackPreparation.Failed) history.Add(Current);
            history.AddRange(forward.Take(ahead));
            Current = forward[ahead]; forward.RemoveRange(0, ahead + 1);
            Preparation = PlaybackPreparation.Preparing; Changed(); return Current;
        }
        var index = manual.FindIndex(entry => entry.EntryId == entryId);
        PlaybackEntry? selected;
        if (index >= 0) { selected = manual[index]; manual.RemoveAt(index); }
        else
        {
            index = upcoming.FindIndex(entry => entry.EntryId == entryId);
            if (index >= 0)
            {
                selected = upcoming[index];
                if (Shuffle) upcoming.RemoveAt(index); else upcoming.RemoveRange(0, index + 1);
            }
            else
            {
                index = recommendations.FindIndex(entry => entry.EntryId == entryId);
                if (index < 0) return null;
                selected = recommendations[index]; recommendations.RemoveRange(0, index + 1); upcoming.Clear();
            }
        }
        if (Current != null && Preparation != PlaybackPreparation.Failed) history.Add(Current);
        Current = selected; forward.Clear(); recommendationsExhausted = false; Preparation = PlaybackPreparation.Preparing; Changed(); return selected;
    }

    internal bool AppendRecommendations(long version, IEnumerable<SoundCloudTrack> tracks)
    {
        if (!AutoContinue || version != SourceVersion || Context is not { Kind: not PlaybackContextKind.Wave, NextHref: null } || Repeat != RepeatMode.Off) return false;
        var data = tracks.ToArray();
        var reserved = source.Concat(manual).Concat(history).Concat(forward).Concat(recommendations)
            .Select(entry => entry.Track.Id).ToHashSet();
        if (Current != null) reserved.Add(Current.Track.Id);
        foreach (var track in data)
        {
            if (recommendations.Count >= WaveUpcomingLimit) break;
            if (track.Id > 0 && reserved.Add(track.Id)) recommendations.Add(NewEntry(track, PlaybackEntryOrigin.Recommendation));
        }
        recommendationsExhausted = data.Length == 0;
        Changed(); return true;
    }

    internal bool Append(long version, string cursor, TrackPage page)
    {
        if (version != SourceVersion || Context?.NextHref != cursor) return false;
        if (Context.Kind == PlaybackContextKind.Wave)
        {
            UpdateWaveUpcoming(page.Tracks, rebuild: false);
            Context = Context with { NextHref = page.NextHref == cursor ? null : page.NextHref };
            Changed(); return true;
        }
        var added = page.Tracks.Select((track, index) => NewEntry(track, PlaybackEntryOrigin.Context, source.Count + index)).ToList();
        source.AddRange(added);
        if (Shuffle) Mix(added);
        upcoming.AddRange(added);
        Context = Context with { NextHref = page.NextHref == cursor ? null : page.NextHref };
        Changed(); return true;
    }
    internal bool ReplaceRecommendations(long version, IEnumerable<SoundCloudTrack> tracks, bool rebuild = false)
    {
        if (version != SourceVersion || Context?.Kind != PlaybackContextKind.Wave) return false;
        var changed = UpdateWaveUpcoming(tracks, rebuild);
        // Ordinary model updates leave both displayed entries and an in-flight refill alone.
        // Explicit mode/dislike changes invalidate old fetches, even if their retained rows match.
        if (rebuild || Context.NextHref == null)
        {
            Context = Context with { NextHref = "pookie:wave:rerank:" + ++waveRevision };
            changed = true;
        }
        if (changed) Changed();
        return true;
    }

    private bool UpdateWaveUpcoming(IEnumerable<SoundCloudTrack> tracks, bool rebuild)
    {
        var previous = upcoming.ToArray();
        var next = rebuild ? new List<PlaybackEntry>() : previous.ToList();
        var reserved = manual.Concat(forward).Concat(next).Select(entry => entry.Track.Id).ToHashSet();
        if (Current != null) reserved.Add(Current.Track.Id);
        foreach (var track in tracks)
        {
            if (next.Count >= WaveUpcomingLimit) break;
            if (!reserved.Add(track.Id)) continue;
            var retained = previous.FirstOrDefault(entry => entry.Track.Id == track.Id);
            next.Add(retained == null ? NewEntry(track, PlaybackEntryOrigin.Context, source.Count + next.Count)
                : retained with { Track = track });
        }
        if (previous.SequenceEqual(next)) return false;
        var removed = previous.Select(entry => entry.EntryId).ToHashSet();
        source.RemoveAll(entry => removed.Contains(entry.EntryId));
        source.AddRange(next); upcoming.Clear(); upcoming.AddRange(next);
        return true;
    }
    internal void SetPreparation(long entryId, PlaybackPreparation state, SoundCloudTrack? resolved = null)
    {
        if (Current?.EntryId != entryId) return;
        if (resolved != null) Current = Current with { Track = resolved };
        Preparation = state; Changed();
    }
    private PlaybackEntry NewEntry(SoundCloudTrack track, PlaybackEntryOrigin origin, int index = -1) => new(++entrySequence, track, origin, index);
    private static IReadOnlyList<PlaybackEntry> Copy(List<PlaybackEntry> entries) => Array.AsReadOnly(entries.ToArray());
    private void Changed() => snapshot = null;
    private void Mix(List<PlaybackEntry> entries) => random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(entries));
    private static PlaybackEntry? TakeFirst(List<PlaybackEntry> entries)
    {
        if (entries.Count == 0) return null;
        var entry = entries[0]; entries.RemoveAt(0); return entry;
    }
}
