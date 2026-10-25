// SPDX-License-Identifier: BSD-3-Clause
using Pookie.App.Storage;
using Pookie.App.Messages;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class MemoryOwnershipTests
{
    [Fact] public async Task CanceledHistoryQueriesFinishWhileTheWorkerIsStillBusy()
    {
        var root=Path.Combine(Path.GetTempPath(),"pookie-history-cancel-"+Guid.NewGuid().ToString("N"));
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        try
        {
            await using var history=new ChatHistoryStore(new(root),42);
            var run=typeof(ChatHistoryStore).GetMethod("Run",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.MakeGenericMethod(typeof(bool));
            var blocking=(Task<bool>)run.Invoke(history,[new Func<Microsoft.Data.Sqlite.SqliteConnection,bool>(_=>{entered.Set();release.Wait();return true;}),CancellationToken.None])!;
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                using var canceled=new CancellationTokenSource();
                var queries=Enumerable.Range(0,10000).Select(_=>history.SearchAsync("query",token:canceled.Token)).ToArray();
                canceled.Cancel();
                await Task.WhenAll(queries.Select(async query=>await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>query))).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(blocking.IsCompleted);
            }
            finally { release.Set();await blocking; }
            Assert.Empty(await history.SearchAsync("query"));
        }
        finally{release.Set();Directory.Delete(root,true);}
    }
    [Fact] public void OrphanCleanupPreservesActiveCachesAndUnknownDirectories()
    {
        var root=Path.Combine(Path.GetTempPath(),"pookie-audio-orphan-test-"+Guid.NewGuid().ToString("N"));
        var stale=Path.Combine(root,Guid.NewGuid().ToString("N"));var active=Path.Combine(root,Guid.NewGuid().ToString("N"));var legacy=Path.Combine(root,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);Directory.CreateDirectory(active);Directory.CreateDirectory(legacy);
        try
        {
            File.WriteAllText(Path.Combine(stale,".active"),"");
            using var lease=new FileStream(Path.Combine(active,".active"),FileMode.Create,FileAccess.ReadWrite,FileShare.None);
            foreach(var path in new[]{stale,active,legacy})Directory.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-10));
            EncodedTrackCache.PruneOrphans(root,DateTime.UtcNow.AddDays(-7));
            Assert.False(Directory.Exists(stale));Assert.True(Directory.Exists(active));Assert.True(Directory.Exists(legacy));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public void ArtworkDecodeNormalizesLargeIntrinsicImagesBeforeCaching()
    {
        const int side=2048;
        var dib=new byte[40+side*side*4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dib,40);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4),side);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8),side);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12),1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14),32);
        var png=Pookie.App.Platform.ClipboardBitmap.PngFromDib(dib);
        var source=ArtworkImage.Prepare(png);
        Assert.Equal(1024,source.PixelWidth);Assert.Equal(1024,source.PixelHeight);
        var pixels=new byte[1024*1024*4];source.CopyPixels(pixels,1024*4);
        Assert.Equal(255,pixels[3]);
        // Reject an oversized upload from metadata before allocating its pixels.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16),32768);
        Assert.Throws<ArgumentException>(()=>ArtworkImage.Prepare(png));
    }
    [Fact] public void SharedPixelBudgetEvictsAcrossCachesWithoutDisposingVisibleSources()
    {
        var budget = new CacheBudget(100);
        var first = new ResourceCache<int,byte[]>(10,budget,value=>value.Length);
        var second = new ResourceCache<int,byte[]>(10,budget,value=>value.Length);
        var visible = new byte[60]; first[1]=visible; second[2]=new byte[60];
        Assert.Empty(first); Assert.Single(second); Assert.Equal(60,budget.Bytes);
        Assert.Equal(60,visible.Length);
        second.Clear(); Assert.Equal(0,budget.Bytes);
    }
    [Fact] public async Task RequestBurstHasBoundedOwnershipAndRetriesFailures()
    {
        var cache = new AsyncResourceCache<int,byte[]>(64,new CacheBudget(256),b=>b.Length);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active=0;var maximum=0;
        async Task<byte[]?> Fetch()
        {
            var count=Interlocked.Increment(ref active);InterlockedExtensionsMax(ref maximum,count);
            await gate.Task;Interlocked.Decrement(ref active);return new byte[8];
        }
        var tasks=Enumerable.Range(0,800).Select(i=>cache.GetAsync(i,Fetch,default)).ToArray();
        Assert.InRange(cache.PendingCount,1,32);gate.SetResult();await Task.WhenAll(tasks);
        Assert.InRange(maximum,1,32);Assert.Equal(0,cache.PendingCount);Assert.InRange(cache.Count,1,32);
        Assert.Null(await cache.GetAsync(900,()=>Task.FromResult<byte[]?>(null),default));
        Assert.NotNull(await cache.GetAsync(900,()=>Task.FromResult<byte[]?>([1]),default));
    }
    private static void InterlockedExtensionsMax(ref int target,int value)
    { int previous;do {previous=Volatile.Read(ref target);if(previous>=value)return;}while(Interlocked.CompareExchange(ref target,value,previous)!=previous); }
    [Fact] public async Task DuplicateRequestsShareWorkAndCanceledWaiterDoesNotCancelOtherConsumers()
    {
        var cache = new AsyncResourceCache<int,byte[]>(4,new CacheBudget(100),b=>b.Length);
        var source=new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        Task<byte[]?> Fetch(){Interlocked.Increment(ref calls);return source.Task;}
        var first=cache.GetAsync(1,Fetch,default);using var cancel=new CancellationTokenSource();
        Assert.Same(first,cache.GetAsync(1,Fetch,default));
        var second=cache.GetAsync(1,Fetch,cancel.Token);cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>second);
        source.SetResult([1]);Assert.Equal(new byte[]{1},await first);Assert.Equal(1,calls);
        Assert.Same(first,cache.GetAsync(1,Fetch,default));
    }
    [Fact] public async Task WriterCoalescesPendingSnapshotsAndFlushWaitsForDurability()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var writes=new List<int>();var writer=new SnapshotWriter<int>(value=>{if(value==0){entered.Set();release.Wait();}writes.Add(value);});
        var first=writer.SaveAsync(0);Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var saves=Enumerable.Range(1,1000).Select(writer.SaveAsync).ToArray();var flush=writer.FlushAsync();Assert.False(flush.IsCompleted);
        release.Set();await Task.WhenAll(saves.Append(first).Append(flush));Assert.Equal(new[]{0,1000},writes);
    }
    [Fact] public async Task DurableDraftsAndPendingSendsAreNotSilentlyTruncated()
    {
        var root=Path.Combine(Path.GetTempPath(),"pookie-draft-retention-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppDataPaths(root);var workspace=new ChatWorkspaceStore(paths,42);
            var drafts=Enumerable.Range(1,80).ToDictionary(i=>(long)i,i=>new ChatDraft(""));
            drafts[81]=new("",Voice:new(Path.Combine(root,"voice.wav"),new(1,[1,2,3])));
            drafts[82]=new("last draft");
            await workspace.SaveAsync(ChatWorkspaceStore.Empty with{Drafts=drafts});
            var result=workspace.Load();Assert.Equal(2,result.Drafts!.Count);Assert.NotNull(result.Drafts[81].Voice);
            var local=new ChatLocalStore(paths,42);
            var pending=Enumerable.Range(1,80).Select(i=>new ChatPending(i,"message","label",DateTimeOffset.UtcNow,true)).ToArray();
            await local.SaveAsync(new(Enumerable.Range(1,80).ToDictionary(i=>(long)i,i=>"draft"),pending));
            Assert.Equal(80,local.Load().Drafts.Count);Assert.Equal(80,local.Load().Pending.Length);
        }
        finally{Directory.Delete(root,true);}
    }
}
