using Pookie.SoundCloud;

namespace Pookie.App.Playback;

internal enum RepeatMode { Off, Context, Track }
internal enum PlaybackContextKind { Track, Likes, Playlist, Album, Artist, Feed, Recommendations, History, Recent }
internal enum PlaybackEntryOrigin { Context, Manual }
internal enum PlaybackPreparation { Preparing, Ready, Failed, Stopped }
internal sealed record PlaybackContext(string Key, string Title, string Subtitle, string? NextHref = null, bool LibraryCursor = false, PlaybackContextKind Kind = PlaybackContextKind.Track);
internal sealed record PlaybackEntry(long EntryId, SoundCloudTrack Track, PlaybackEntryOrigin Origin, int SourceIndex = -1);
internal sealed record PlaybackQueueSnapshot(long SourceVersion, PlaybackContext? Context, PlaybackEntry? Current,
    IReadOnlyList<PlaybackEntry> Source, IReadOnlyList<PlaybackEntry> ManualUpcoming,
    IReadOnlyList<PlaybackEntry> ContextUpcoming, IReadOnlyList<PlaybackEntry> History,
    IReadOnlyList<PlaybackEntry> Forward, RepeatMode Repeat, bool Shuffle, PlaybackPreparation Preparation)
{
    internal IEnumerable<PlaybackEntry> Upcoming => ManualUpcoming.Concat(Forward).Concat(ContextUpcoming);
    internal bool CanNext => Upcoming.Any() || Context?.NextHref != null || Repeat == RepeatMode.Context && Source.Count > 0;
    internal bool CanPrevious => Current != null;
}

// Only this model decides the next entry. Pages and audio preparation consume its
// snapshot; a track ID is metadata, never the identity of a queue position.
internal sealed class PlaybackQueue
{
    private readonly Random random;
    private readonly List<PlaybackEntry> source = [], upcoming = [], manual = [], history = [], forward = [];
    private long entrySequence;
    private PlaybackQueueSnapshot? snapshot;
    internal long SourceVersion { get; private set; }
    internal PlaybackContext? Context { get; private set; }
    internal PlaybackEntry? Current { get; private set; }
    internal RepeatMode Repeat { get; private set; }
    internal bool Shuffle { get; private set; }
    internal PlaybackPreparation Preparation { get; private set; } = PlaybackPreparation.Stopped;

    internal PlaybackQueue(Random? random = null) => this.random = random ?? Random.Shared;
    internal PlaybackQueueSnapshot Snapshot => snapshot ??= new(SourceVersion, Context, Current,
        Copy(source), Copy(manual), Copy(upcoming), Copy(history), Copy(forward), Repeat, Shuffle, Preparation);

    internal PlaybackEntry StartContext(PlaybackContext context, IEnumerable<SoundCloudTrack> tracks, int selectedIndex)
    {
        var data = tracks.ToArray();
        if (selectedIndex < 0 || selectedIndex >= data.Length) throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        Clear(); Context = context;
        source.AddRange(data.Select((track, index) => NewEntry(track, PlaybackEntryOrigin.Context, index)));
        Current = source[selectedIndex];
        upcoming.AddRange(Shuffle ? source.Where(entry => entry != Current) : source.Skip(selectedIndex + 1));
        if (Shuffle) Mix(upcoming);
        Preparation = PlaybackPreparation.Preparing; Changed();
        return Current;
    }

    internal void Clear()
    {
        ++SourceVersion; Context = null; Current = null;
        source.Clear(); upcoming.Clear(); manual.Clear(); history.Clear(); forward.Clear();
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
        Repeat = mode; Changed();
    }
    internal PlaybackEntry Enqueue(SoundCloudTrack track, bool next = false)
    {
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
        if (next == null) { Preparation = PlaybackPreparation.Stopped; Changed(); return null; }
        if (Current != null) history.Add(Current);
        Current = next; Preparation = PlaybackPreparation.Preparing; Changed(); return next;
    }

    internal PlaybackEntry? Previous()
    {
        if (history.Count == 0) return Current;
        if (Current != null) forward.Insert(0, Current);
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
            if (Current != null) history.Add(Current);
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
            if (index < 0) return null;
            selected = upcoming[index];
            if (Shuffle) upcoming.RemoveAt(index); else upcoming.RemoveRange(0, index + 1);
        }
        if (Current != null) history.Add(Current);
        Current = selected; forward.Clear(); Preparation = PlaybackPreparation.Preparing; Changed(); return selected;
    }

    internal bool Append(long version, string cursor, TrackPage page)
    {
        if (version != SourceVersion || Context?.NextHref != cursor) return false;
        var added = page.Tracks.Select((track, index) => NewEntry(track, PlaybackEntryOrigin.Context, source.Count + index)).ToList();
        source.AddRange(added);
        if (Shuffle) Mix(added);
        upcoming.AddRange(added);
        Context = Context with { NextHref = page.NextHref == cursor ? null : page.NextHref };
        Changed(); return true;
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
