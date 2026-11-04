using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class TrackIdentityTests
{
    [Fact]
    public void DeduplicationNeedsCloseDurationAndKeepsDifferentCreditedArtists()
    {
        var seed = new SoundCloudTrack { Id = 1, Title = "Artist — SONG [Official Audio]", Duration = 120000 };
        Assert.True(TrackIdentity.Matches(seed, new() { Id = 2, Title = "song (перезалив)", Duration = 121500 }));
        Assert.False(TrackIdentity.Matches(seed, new() { Id = 3, Title = "song", Duration = 124000 }));
        Assert.False(TrackIdentity.Matches(seed, new() { Id = 4, Title = "Other Artist - song", Duration = 120000 }));
        Assert.False(TrackIdentity.Matches(seed, new() { Id = 5, Title = "song (speed up)", Duration = 120000 }));
    }
}
