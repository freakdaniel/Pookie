using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private LibrarySkeleton ArtworkSkeleton(Image image) => artworkLoading.TryGetValue(image, out var skeleton)
        ? skeleton : throw new InvalidOperationException("Artwork skeleton is not registered.");

    private async Task VerifyArtworkLifetimeAsync()
    {
        timer.Stop();
        try
        {
            await Task.Yield();
            for (var batch = 0; batch < 3; batch++)
            {
                var removed = CreateRemovedArtworkCards(pending: batch == 1);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var retained = removed.Count(reference => reference.IsAlive);
                using var process = Process.GetCurrentProcess();
                Console.WriteLine($"ARTWORK_LIFETIME: batch={batch + 1}; retained={retained}/100; rss_mib={process.WorkingSet64 / 1048576d:F1}; managed_mib={GC.GetTotalMemory(false) / 1048576d:F1}");
                if (retained != 0) throw new InvalidOperationException("Removed collection artwork retained its card and pixels.");
                await Task.Yield();
            }
            // Live controls must keep their loading registration across collection.
            var image = new Image(); var layer = ArtworkLayer(image);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if (!artworkLoading.TryGetValue(image, out _)) throw new InvalidOperationException("Live artwork lost its skeleton.");
            var skeleton = ArtworkSkeleton(image);
            skeleton.SetActive(true);
            if (skeleton.Running) throw new InvalidOperationException("Detached artwork started an infinite clock.");
            trackDetailPlaylists.Add(layer);
            if (!skeleton.Running) throw new InvalidOperationException("Attached loading artwork did not start its shimmer.");
            trackDetailPlaylists.Clear();
            if (skeleton.Running) throw new InvalidOperationException("Removed artwork retained its shimmer.");
            trackDetailPlaylists.Add(layer);
            if (!skeleton.Running) throw new InvalidOperationException("Reattached loading artwork did not resume its shimmer.");
            trackDetailPlaylists.Clear();
            StopCardArtwork(image); layer.Dispose();
            Console.WriteLine("ARTWORK_LIFETIME_OK: removed sidebar cards/pixels collect; live skeletons remain registered");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("ARTWORK_LIFETIME_FAILED: " + error); }
        finally { Window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference[] CreateRemovedArtworkCards(bool pending)
    {
        var removed = new WeakReference[100];
        for (var i = 0; i < removed.Length; i++)
        {
            // Same creation path as the sidebar, bypassing download/caches and optionally removing mid-shimmer.
            var card = CreateCollectionCardView(responsive: true);
            card.Cover.Source = ImageSource.FromBgraPixels(500, 500, new byte[500 * 500 * 4]);
            if (pending)
            {
                trackDetailPlaylists.Add(card.Root);
                ArtworkSkeleton(card.Cover).SetActive(true);
                if (!ArtworkSkeleton(card.Cover).Running) throw new InvalidOperationException("Pending-card fixture did not start its shimmer.");
            }
            removed[i] = new WeakReference(card.Cover);
        }
        if (pending) trackDetailPlaylists.Clear();
        return removed;
    }
}
