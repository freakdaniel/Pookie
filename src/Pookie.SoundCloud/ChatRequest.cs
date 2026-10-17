namespace Pookie.SoundCloud;

public sealed record ChatRequest(string Kind, long UserId, long RecipientId = 0, string? Body = null, string? Cursor = null)
{
    public const int MaxBodyLength = 20000;
    public bool IsWrite => Kind is "send" or "read";
    public bool IsValid() => UserId is > 0 and <= 9007199254740991 &&
        (Kind == "list" ? RecipientId == 0 && Body == null :
         Kind is "thread" or "send" or "read" && RecipientId is > 0 and <= 9007199254740991 && RecipientId != UserId &&
         (Kind == "send" ? Body is { Length: > 0 and <= MaxBodyLength } && !string.IsNullOrWhiteSpace(Body) &&
             !Body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) : Body == null)) &&
        (IsWrite ? Cursor == null : Cursor == null || IsCursor(Cursor, UserId, RecipientId));

    public static bool IsCursor(string value, long userId, long recipientId) => value.Length <= 8192 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && SoundCloudWebClient.IsApiUri(uri) && uri.Fragment == "" &&
        uri.AbsolutePath == (recipientId == 0 ? $"/users/{userId}/conversations" : $"/users/{userId}/conversations/{recipientId}/messages");
    public override string ToString() => $"Chat request ({Kind}; content redacted)";
}
