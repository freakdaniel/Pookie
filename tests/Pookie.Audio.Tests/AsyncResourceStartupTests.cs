using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class AsyncResourceStartupTests
{
    private sealed class Resource : IAsyncDisposable
    {
        internal int Disposals;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    [Fact] public async Task CanceledFactoryResultIsReleasedWithoutAnAwaitingUiContinuation()
    {
        using var cancel = new CancellationTokenSource();
        var native = new TaskCompletionSource<Resource>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startup = new AsyncResourceStartup<Resource>(() => native.Task, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup.Ready);
        Assert.False(startup.Completion.IsCompleted);
        var resource = new Resource(); native.SetResult(resource);
        await startup.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, resource.Disposals);
        Assert.False(startup.TryTake(out _));
    }

    [Fact] public async Task CancellationAfterReadyBeforeClaimStillReleasesTheNativeResource()
    {
        using var cancel = new CancellationTokenSource();
        var resource = new Resource();
        using var startup = new AsyncResourceStartup<Resource>(() => Task.FromResult(resource), cancel.Token);
        await startup.Ready;
        cancel.Cancel();
        await startup.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, resource.Disposals);
        Assert.False(startup.TryTake(out _));
    }

    [Fact] public async Task ClaimedResourceBelongsToItsUiOwnerAndIsNotDisposedTwice()
    {
        using var cancel = new CancellationTokenSource();
        var resource = new Resource();
        using var startup = new AsyncResourceStartup<Resource>(() => Task.FromResult(resource), cancel.Token);
        await startup.Ready;
        Assert.True(startup.TryTake(out var claimed)); Assert.Same(resource, claimed);
        cancel.Cancel(); await startup.Completion;
        Assert.Equal(0, resource.Disposals);
        await claimed.DisposeAsync(); Assert.Equal(1, resource.Disposals);
    }
}
