using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class ChatTests
{
    [Fact]
    public void ChatCommandsBindPaginationToAccountAndRecipient()
    {
        var request = new ChatRequest("thread", 42, 90, Cursor: "https://api-v2.soundcloud.com/users/42/conversations/90/messages?offset=30");
        Assert.True(request.IsValid());
        foreach (var url in new[] { "https://evil.test/users/42/conversations/90/messages", "https://api-v2.soundcloud.com/users/43/conversations/90/messages",
            "https://api-v2.soundcloud.com/users/42/conversations/91/messages", "https://evil@api-v2.soundcloud.com/users/42/conversations/90/messages", "https://api-v2.soundcloud.com/users/42/conversations/90/messages#evil" })
            Assert.False((request with { Cursor = url }).IsValid());
        Assert.False((request with { Kind = "send", Body = "Hi" }).IsValid());
        Assert.False(new ChatRequest("send", 42, 42, "Hi").IsValid());
        Assert.False(new ChatRequest("send", 42, 90, "\0").IsValid());
        Assert.False(new ChatRequest("send", 42, 90, new string('a', 20001)).IsValid());
        var command = new BrowserRequestCommand(new string('a', 32), "chat", Chat: new("send", 42, 90, "Привет 🎵\nТекст"));
        Assert.True(command.IsValid());
        Assert.False((command with { UserId = 99 }).IsValid());
        Assert.False((command with { Url = "https://api-v2.soundcloud.com/me" }).IsValid());
        Assert.False((command with { Operation = "me" }).IsValid());
        var serialized = JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand);
        Assert.Contains("\"recipient_id\":90", serialized);
        Assert.DoesNotContain("Привет", command.ToString());
        var largest = command with { Chat = new("send", 42, 90, new string('я', ChatRequest.MaxBodyLength)) };
        Assert.True(JsonSerializer.Serialize(largest, SoundCloudJson.Default.BrowserRequestCommand).Length < BrowserRequestCommand.MaxLength);
    }
    [Fact]
    public void ConversationsRejectOtherAccountsAndForeignCursors()
    {
        using var doc = JsonDocument.Parse("""
            {"collection":[{"id":"42:90","read":false,"users":[{"id":42,"username":"Me"},{"id":90,"username":"Other"}],
            "last_message":{"sender_urn":"soundcloud:users:90","content":"Привет","sent_at":"2026-10-17T10:00:00Z"}},
            {"id":"43:91","users":[{"id":43},{"id":91}]}],"next_href":"https://evil.test/"}
            """);
        var page = SoundCloudWebClient.ParseConversations(doc.RootElement, 42);
        Assert.Single(page.Conversations); Assert.Null(page.NextHref);
        Assert.Equal(90, page.Conversations[0].Recipient.Id);
        Assert.False(page.Conversations[0].Read);
        Assert.Equal("Привет", page.Conversations[0].LastMessage!.Body);
    }
    [Fact]
    public void MessagesHaveStableIdentityWithoutServerIdsAndIgnoreForeignSenders()
    {
        using var doc = JsonDocument.Parse("""
            {"collection":[
              {"sender_urn":"soundcloud:users:42","content":"Two","sent_at":"2026-10-17T10:01:00Z"},
              {"sender_urn":"soundcloud:users:90","content":"One","sent_at":"2026-10-17T10:00:00Z"},
              {"sender_urn":"soundcloud:users:90","content":"One","sent_at":"2026-10-17T10:00:00Z"},
              {"sender_urn":"soundcloud:users:91","content":"Foreign","sent_at":"2026-10-17T10:00:00Z"},
              {"content":"Malformed","sender":{"id":"wrong"},"sent_at":"invalid"}
            ]}
            """);
        var page = SoundCloudWebClient.ParseMessages(doc.RootElement, 42, 90);
        Assert.Equal(new[] { "One", "Two" }, page.Messages.Select(m => m.Body));
        Assert.Equal(page.Messages[0].Id, SoundCloudWebClient.ParseMessages(doc.RootElement, 42, 90).Messages[0].Id);
        Assert.DoesNotContain("One", page.Messages[0].ToString());
    }

    [Fact]
    public void FallbackIdentityUsesTheInstantRatherThanTimestampFormatting()
    {
        using var doc = JsonDocument.Parse("""
            {"collection":[
              {"sender_urn":"soundcloud:users:42","content":"Same","sent_at":"2026-10-17T10:01:00.123Z"},
              {"sender_urn":"soundcloud:users:42","content":"Same","sent_at":"2026-10-17T13:01:00.1230000+03:00"},
              {"sender_urn":"soundcloud:users:42","content":"Same","sent_at":"2026-10-17T10:01:00.124Z"},
              {"id":101,"sender_urn":"soundcloud:users:42","content":"Repeat","sent_at":"2026-10-17T10:02:00Z"},
              {"id":102,"sender_urn":"soundcloud:users:42","content":"Repeat","sent_at":"2026-10-17T10:02:00Z"}
            ]}
            """);
        var messages = SoundCloudWebClient.ParseMessages(doc.RootElement, 42, 90).Messages;
        Assert.Equal(4, messages.Length);
        Assert.Equal(2, messages.Count(m => m.Body == "Same"));
        Assert.Equal(2, messages.Count(m => m.Body == "Repeat"));
    }
}
