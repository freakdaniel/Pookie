namespace Pookie.SoundCloud;

// A successful creation POST followed by a failed read must not be presented
// as a rejected write, even when that read failed with a definite 4xx response.
public sealed class PlaylistCreationUnconfirmedException(long playlistId, Exception innerException)
    : Exception("Не удалось подтвердить создание. Проверь свои плейлисты перед повторной попыткой.", innerException)
{
    public long PlaylistId { get; } = playlistId;
}
