namespace Pookie.SoundCloud;

public sealed record ChatMessage(string Id, long SenderId, string Body, DateTimeOffset SentAt)
{
    // Locally authenticated extension payload. Never populated from SoundCloud JSON;
    // Body always retains the exact transport text for confirmation/reconciliation.
    public string? ClientContent { get; init; }
    public override string ToString() => "SoundCloud message (content redacted)";
}
public sealed record ChatConversation(string Id, SoundCloudUser Recipient, ChatMessage? LastMessage, bool Read);
public sealed record ChatConversationPage(ChatConversation[] Conversations, string? NextHref);
public sealed record ChatMessagePage(ChatMessage[] Messages, string? NextHref);
