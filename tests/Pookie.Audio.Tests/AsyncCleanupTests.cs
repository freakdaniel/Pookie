using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class AsyncCleanupTests
{
    [Fact] public async Task DrainWaitsForResourcesRetiredByAnOutstandingStartup()
    {
        var cleanup = new AsyncCleanup();
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Start()
        {
            await startup.Task;
            cleanup.Add(disposed.Task);
            admitted.SetResult();
        }
        cleanup.Add(Start(), propagateFailure: false);
        var draining = cleanup.DrainAsync();
        Assert.False(draining.IsCompleted);
        startup.SetResult(); await admitted.Task;
        Assert.False(draining.IsCompleted);
        disposed.SetResult(); await draining;
        Assert.True(cleanup.DrainAsync().IsCompletedSuccessfully);
    }

    [Fact] public async Task FailureIsReportedAfterAllResourcesFinishAndDoesNotPoisonLaterDrains()
    {
        var cleanup = new AsyncCleanup();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("Native cleanup failed");
        var failed = Task.FromException(failure);
        cleanup.Add(failed); cleanup.Add(failed); cleanup.Add(pending.Task);
        var draining = cleanup.DrainAsync();
        Assert.False(draining.IsCompleted);
        pending.SetResult();
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => draining));
        await cleanup.DrainAsync();
        cleanup.Add(Task.CompletedTask); await cleanup.DrainAsync();
    }

    [Fact] public async Task CallerHandledStartupCancellationDoesNotFailTeardown()
    {
        var cleanup = new AsyncCleanup();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        cleanup.Add(Task.FromCanceled(canceled.Token), propagateFailure: false);
        await cleanup.DrainAsync();
    }

    [Fact] public async Task FailureStormRetainsOnlyABoundedSampleAndReportsOverflow()
    {
        var cleanup = new AsyncCleanup();
        var first = new IOException("First failure");
        cleanup.Add(Task.FromException(first)); cleanup.Add(Task.FromException(first));
        for (var i = 1; i < 100; i++) cleanup.Add(Task.FromException(new IOException("Failure " + i)));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => cleanup.DrainAsync());
        Assert.Equal(17, failure.InnerExceptions.Count);
        Assert.Same(first, failure.InnerExceptions[0]);
        Assert.Contains("84 additional cleanup failure reports", failure.InnerExceptions[^1].Message);
        await cleanup.DrainAsync();
    }
}
