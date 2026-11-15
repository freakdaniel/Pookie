using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Run this flag in an isolated compositor at the desired refresh rate. Frame
    // timing is diagnostic; functional bounds above do not depend on GPU speed.
    private async Task ProbePooledPagePerformanceAsync(SoundCloudUser user, SoundCloudTrack[] tracks, LibraryItem[] publications)
    {
        var trackItems = tracks.Take(1000).Select(LibraryItem.FromTrack).ToArray();
        var raster = typeof(TrackWaveform).GetField("raster", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var gaps = new List<double>(); var layouts = new List<double>(); var renders = new List<double>();
        long previousFrame = 0;
        void Sample()
        {
            var now = Stopwatch.GetTimestamp();
            if (previousFrame != 0) gaps.Add(Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds);
            previousFrame = now;
            var frame = Window.LastFramePerformanceStats;
            layouts.Add(frame.LayoutMs); renders.Add(frame.RenderBodyMs);
        }
        for (var cycle = -3; cycle < 20; cycle++)
        {
            var allocated = GC.GetTotalAllocatedBytes(false);
            foreach (var name in new[] { "search", "profile-tracks", "profile-publications", "track-related" })
            {
                gaps.Clear(); layouts.Clear(); renders.Clear(); previousFrame = 0;
                Window.FrameRendered += Sample;
                try
                {
                    if (name == "search")
                    {
                        BeginNavigation(new(Page.Search, Search: "pool", Section: SearchSection.Tracks));
                        searchSection.Value = SearchSection.Tracks; SetSearchResults(new(trackItems, null));
                    }
                    else if (name.StartsWith("profile", StringComparison.Ordinal))
                    {
                        var section = name == "profile-tracks" ? UserSection.Tracks : UserSection.Overview;
                        BeginNavigation(new(Page.UserProfile, Item: UserItem(user), UserSection: section));
                        userSection.Value = section;
                        userProfileState = new(user, new() { [section] = new(section == UserSection.Tracks ? trackItems : publications, null) }, [], [], [], false);
                        RenderUserProfile();
                    }
                    else
                    {
                        BeginNavigation(new(Page.Track, Item: trackItems[0]));
                        trackDetailState = new(tracks[0], new([], null), new([], null), false, false)
                        { MetadataLoading = false, SidebarLoading = false, Sections = new Dictionary<TrackSection, LibraryPage>
                            { [TrackSection.Related] = new(trackItems, null) } };
                        trackDetailSection.Value = TrackSection.Related; RenderTrackSection();
                    }
                    shellScroll.SetScrollOffsets(0, 0);
                    await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 20000);
                    var maximum = Math.Min(2400, SmoothScroll.Maximum(shellScroll));
                    for (var step = 0; step < 12; step++)
                    {
                        shellScroll.SetScrollOffsets(0, maximum * (step < 6 ? step : 11 - step) / 5);
                        await WaitForLoginFrameAsync();
                    }
                }
                finally { Window.FrameRendered -= Sample; }
                Console.WriteLine("UI_POOL_PERF_PAGE " + JsonSerializer.Serialize(new
                {
                    phase = cycle < 0 ? "warmup" : "measured", cycle, page = name, frames = renders.Count,
                    gap95Ms = Percentile(gaps, .95), gapMaxMs = Percentile(gaps, 1),
                    layout95Ms = Percentile(layouts, .95), render95Ms = Percentile(renders, .95)
                }));
            }
            var rows = likedRows.Values.Concat(userPublicationRows).Concat(trackSectionRows)
                .Concat(searchViews.Values.Select(view => view.LibraryTrack).OfType<LikedTrackRow>()).Distinct().ToArray();
            var buffers = rows.Select(row => raster.GetValue(row.Waveform)).OfType<WriteableBitmap>().Distinct().ToArray();
            if (userPublicationViews.Count > 30 || trackSectionRowPool.Count > 20 || searchViews.Count > 100 || rows.Length > 128)
                throw new InvalidOperationException("Repeated pooled-page navigation exceeded its bounded control budget.");
            using var process = Process.GetCurrentProcess();
            Console.WriteLine("UI_POOL_PERF_CYCLE " + JsonSerializer.Serialize(new
            {
                phase = cycle < 0 ? "warmup" : "measured", cycle,
                allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                managedBytes = GC.GetTotalMemory(false), workingSetBytes = process.WorkingSet64,
                rows = rows.Length, buffers = buffers.Length,
                rasterBytes = buffers.Sum(bitmap => (long)bitmap.PixelWidth * bitmap.PixelHeight * 4),
                searchSlots = searchViews.Count, publicationSlots = userPublicationViews.Count, relatedSlots = trackSectionRowPool.Count
            }));
        }
        Console.WriteLine("UI_POOL_PERF_OK: 3 warmup and 20 measured navigation/rebind/scroll cycles; timing diagnostics and bounded resource snapshots");

        static double Percentile(List<double> samples, double quantile)
        {
            if (samples.Count == 0) return 0;
            samples.Sort(); return samples[Math.Min(samples.Count - 1, (int)(samples.Count * quantile))];
        }
    }
}
