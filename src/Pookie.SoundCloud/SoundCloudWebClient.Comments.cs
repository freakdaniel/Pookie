using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<SoundCloudComment> CreateTrackCommentAsync(long trackId, string body, double? timestamp = null, CancellationToken token = default)
    {
        var request = new CommentWriteRequest("create", trackId, body.Trim(), timestamp);
        using var document = await WriteCommentDocumentAsync(request, token);
        var result = GraphObject(GraphData(document), "createTrackComment");
        if (!result.TryGetProperty("comment", out var row) || row.ValueKind != JsonValueKind.Object)
            throw new SoundCloudException("SoundCloud не подтвердил отправку комментария.");
        var comment = ParseGraphComment(row);
        if (comment.Id <= 0 || string.IsNullOrWhiteSpace(comment.Body))
            throw new SoundCloudException("SoundCloud не подтвердил отправку комментария.");
        return comment;
    }

    public async Task DeleteTrackCommentAsync(long trackId, SoundCloudComment comment, long viewerId, CancellationToken token = default)
    {
        if (viewerId <= 0 || comment.User?.Id != viewerId)
            throw new SoundCloudException("Можно удалить только свой комментарий.");
        using var document = await WriteCommentDocumentAsync(new("delete", trackId, CommentId: comment.Id), token);
        if (!GraphData(document).TryGetProperty("deleteTrackComment", out var deleted) || deleted.ValueKind != JsonValueKind.True)
            throw new SoundCloudException("SoundCloud не подтвердил удаление комментария.");
    }

    private async Task<JsonDocument> WriteCommentDocumentAsync(CommentWriteRequest request, CancellationToken token)
    {
        if (!request.IsValid()) throw new ArgumentException("Некорректный комментарий.");
        if (Session?.IsValid() != true) throw new SoundCloudException("Войди в SoundCloud, чтобы оставить комментарий.");
        if (BrowserTransport is not { } browser) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        var document = await browser.WriteCommentAsync(request, token);
        if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            document.Dispose();
            throw new SoundCloudException("SoundCloud отклонил действие с комментарием.");
        }
        return document;
    }
}
