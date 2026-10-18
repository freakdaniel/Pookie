using Pookie.App.Messages;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatUnreadTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
    private static ChatMessage Message(string id, int sender = 90, int seconds = 0) => new(id, sender, id, Now.AddSeconds(seconds));
    private static ChatConversation Conversation(ChatMessage last, bool read = false) => new("42:90", new() { Id = 90, Username = "Peer" }, last, read);

    [Fact]
    public void CountsAllIncomingMessagesBetweenPollsAndDoesNotCountOwnMessagesOrRepeatSnapshots()
    {
        var tracker = new ChatUnreadTracker(); var baseline = Message("baseline");
        tracker.Observe(Conversation(baseline, true), 42);
        var page = new[] { baseline, Message("first"), Message("own", 42), Message("second"), Message("third"), Message("fourth") };
        tracker.Observe(Conversation(page[^1]), 42, page);
        Assert.Equal("4", tracker.Badge(90));
        tracker.Observe(Conversation(page[^1]), 42, page);
        Assert.Equal("4", tracker.Badge(90));
        tracker.Clear(90); Assert.Equal("", tracker.Badge(90));
        var next = Message("next", seconds: 1);
        tracker.Observe(Conversation(next), 42, [page[^1], next]);
        Assert.Equal("1", tracker.Badge(90));
    }
    [Fact]
    public void UnknownHistoricReadPositionIsExplicitAndExternalReadClearsCounter()
    {
        var tracker = new ChatUnreadTracker(); var last = Message("last");
        tracker.Observe(Conversation(last), 42);
        Assert.Equal("1+", tracker.Badge(90));
        tracker.Observe(Conversation(last, true), 42);
        Assert.Equal("", tracker.Badge(90));
    }
    [Fact]
    public void RestoredWatermarkCountsMessagesReceivedWhileApplicationWasClosed()
    {
        var tracker = new ChatUnreadTracker(); var old = Message("old");
        tracker.States[90] = new(old.Id, old.SentAt, 0);
        var one = Message("one", seconds: 1); var two = Message("two", seconds: 2);
        tracker.Observe(Conversation(two), 42, [old, one, two]);
        Assert.Equal("2", tracker.Badge(90));
    }
}
