using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using StbImageSharp;

namespace Pookie.App.Messages;

internal sealed record ChatMediaThumbnail(int Width, int Height, int ThumbWidth, int ThumbHeight, byte[] Pixels);
internal sealed record ChatMediaPreview(long SenderId, long RecipientId, string Caption, MediaFile[] Files,
    ChatMediaThumbnail?[] Images, string? GifUrl, byte[] PayloadHash);

internal static partial class ChatMediaCrypto
{
    internal const int MaxPreviewBytes = 128 * 1024;
    private const int PreviewHeaderSize = 36;
    // Separate nonces and domain-separated AAD authenticate the early preview.
    // The encrypted preview binds the entire original payload by its SHA-256 hash.
    internal static byte[] Encrypt(ChatMediaPackage package, byte[] key)
    {
        var payload = EncryptLegacy(package, key);
        byte[]? plain = null;
        try
        {
            var preview = new ChatMediaPreview(package.SenderId, package.RecipientId, package.Caption,
                package.Attachments.Select(a => new MediaFile(a.Name, a.MediaType, a.Data.Length, a.AsFile, a.Video)).ToArray(),
                package.Attachments.Select(Thumbnail).ToArray(), package.GifUrl, SHA256.HashData(payload));
            plain = JsonSerializer.SerializeToUtf8Bytes(preview, ChatPreviewJson.Default.ChatMediaPreview);
            if (plain.Length > MaxPreviewBytes) throw new InvalidDataException("Слишком большое описание вложения.");
            var result = new byte[PreviewHeaderSize + plain.Length + payload.Length];
            "PKM2"u8.CopyTo(result); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), plain.Length);
            RandomNumberGenerator.Fill(result.AsSpan(8, 12));
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(8, 12), plain, result.AsSpan(PreviewHeaderSize, plain.Length), result.AsSpan(20, 16), PreviewContext(package.SenderId, package.RecipientId));
            payload.CopyTo(result, PreviewHeaderSize + plain.Length);
            foreach (var image in preview.Images) if (image != null) CryptographicOperations.ZeroMemory(image.Pixels);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(payload); if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }

    internal static ChatMediaPreview? ReadPreview(ReadOnlySpan<byte> prefix, byte[] key, long sender, long recipient)
    {
        if (prefix.Length < 4 || !prefix[..4].SequenceEqual("PKM2"u8)) return null;
        if (prefix.Length < 8) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix[4..]);
        if (length is <= 0 or > MaxPreviewBytes) throw new CryptographicException("Повреждён заголовок вложения.");
        if (prefix.Length < PreviewHeaderSize + length) return null;
        var plain = new byte[length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(prefix.Slice(8, 12), prefix.Slice(PreviewHeaderSize, length), prefix.Slice(20, 16), plain, PreviewContext(sender, recipient));
            var preview = JsonSerializer.Deserialize(plain, ChatPreviewJson.Default.ChatMediaPreview) ?? throw new InvalidDataException("Нет описания вложения.");
            if (preview.SenderId != sender || preview.RecipientId != recipient || preview.Caption is not { Length: <= 20000 } ||
                preview.Files is not { Length: <= MaxAttachments } || preview.Images == null || preview.Images.Length != preview.Files.Length ||
                preview.PayloadHash is not { Length: 32 } || preview.Files.Any(f => f == null || f.Length is < 0 or > MaxFileBytes || f.Name is not { Length: <= 255 } || f.Type is not { Length: <= 128 } || f.Video != null && !f.Video.Valid) ||
                preview.GifUrl != null && !KlipyClient.IsMediaUrl(preview.GifUrl) ||
                preview.Images.Any(i => i != null && (i.Width is <= 0 or > 8192 || i.Height is <= 0 or > 8192 || (long)i.Width * i.Height > 16 * 1024 * 1024 ||
                    i.ThumbWidth is <= 0 or > 32 || i.ThumbHeight is <= 0 or > 32 || i.Pixels == null || i.Pixels.Length != i.ThumbWidth * i.ThumbHeight * 4)))
                throw new InvalidDataException("Некорректное описание вложения.");
            return preview;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static ChatMediaPackage Decrypt(byte[] encrypted, byte[] key, long senderId, long recipientId)
    {
        if (encrypted.Length > ChatMediaArchive.MaxEncryptedBytes) throw new InvalidDataException("Вложение слишком большое.");
        if (ReadPreview(encrypted, key, senderId, recipientId) is not { } preview)
            return DecryptLegacy(encrypted, key, senderId, recipientId);
        try
        {
            var offset = PreviewHeaderSize + BinaryPrimitives.ReadInt32LittleEndian(encrypted.AsSpan(4));
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(encrypted.AsSpan(offset)), preview.PayloadHash))
                throw new CryptographicException("Повреждено вложение.");
            var payload = encrypted.AsSpan(offset).ToArray();
            try { return DecryptLegacy(payload, key, senderId, recipientId); }
            finally { CryptographicOperations.ZeroMemory(payload); }
        }
        finally { foreach (var image in preview.Images) if (image != null) CryptographicOperations.ZeroMemory(image.Pixels); }
    }

    private static byte[] PreviewContext(long sender, long recipient) => System.Text.Encoding.UTF8.GetBytes($"pookie.media.v2.preview:{sender}:{recipient}");
    private static ChatMediaThumbnail? Thumbnail(ChatAttachment attachment)
    {
        if (!attachment.AsFile && attachment.Video is { Valid: true } video) return video.Thumbnail();
        if (attachment.AsFile || !attachment.MediaType.StartsWith("image/", StringComparison.Ordinal) || ChatImagePreview.ReadSize(attachment.Data) is not { } size) return null;
        var image = ImageResult.FromMemory(attachment.Data, ColorComponents.RedGreenBlueAlpha);
        try
        {
            var fitted = ChatImagePreview.Fit(size, 32, 32); var width = (int)fitted.Width; var height = (int)fitted.Height;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var source = (y * image.Height / height * image.Width + x * image.Width / width) * 4; var target = (y * width + x) * 4;
                pixels[target] = image.Data[source + 2]; pixels[target + 1] = image.Data[source + 1]; pixels[target + 2] = image.Data[source]; pixels[target + 3] = image.Data[source + 3];
            }
            return new((int)size.Width, (int)size.Height, width, height, pixels);
        }
        finally { CryptographicOperations.ZeroMemory(image.Data); }
    }
}
[JsonSerializable(typeof(ChatMediaPreview))]
internal partial class ChatPreviewJson : JsonSerializerContext;
