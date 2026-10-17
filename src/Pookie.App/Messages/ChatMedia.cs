using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.App.Messages;

internal sealed record ChatAttachment(string Name, string MediaType, byte[] Data);
internal sealed record ChatMediaPackage(long SenderId, long RecipientId, string Caption, ChatAttachment[] Attachments, string? GifUrl = null)
{
    public override string ToString() => "Chat media package (content redacted)";
}

// The key travels only in the SoundCloud DM, never in a hosting request.
internal sealed class ChatMediaReference(char provider, string objectId, byte[] key)
{
    public char Provider { get; } = provider;
    public string ObjectId { get; } = objectId;
    public byte[] Key { get; } = key;
    public string Code => $"p1:{Provider}:{ObjectId}:{Convert.ToBase64String(Key).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    public Uri Uri => new(Provider switch
    {
        'p' => $"https://pomf2.lain.la/f/{ObjectId}.bin",
        'q' => $"https://qu.ax/x/{ObjectId}.zip",
        'c' => $"https://files.catbox.moe/{ObjectId}.bin",
        'n' => $"https://0x0.st/{ObjectId}.bin",
        'z' => $"https://0x0.st/{ObjectId}",
        _ => throw new InvalidDataException("Неизвестный хостинг вложения.")
    });
    public override string ToString() => "Chat media reference (key redacted)";

    public static ChatMediaReference? Parse(string? code)
    {
        if (code is not { Length: <= 36 }) return null;
        var parts = code.Split(':');
        if (parts.Length != 4 || parts[0] != "p1" || parts[1] is not ("p" or "q" or "c" or "n" or "z") ||
            parts[2].Length is < 1 or > 8 || !parts[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
            parts[3].Length != 22 || !parts[3].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        try
        {
            var result = new ChatMediaReference(parts[1][0], parts[2], Convert.FromBase64String(parts[3].Replace('-', '+').Replace('_', '/') + "=="));
            return result.Key.Length == 16 && result.Code == code ? result : null;
        }
        catch (FormatException) { return null; }
    }
}

internal static class ChatMediaCrypto
{
    internal const int MaxFileBytes = 16 * 1024 * 1024;
    internal const int MaxPayloadBytes = 32 * 1024 * 1024;
    internal const int MaxAttachments = 4;
    private const int HeaderSize = 32; // magic + nonce + full authentication tag

    internal static byte[] Encrypt(ChatMediaPackage package, byte[] key)
    {
        Validate(package);
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new MediaHeader(package.SenderId, package.RecipientId, package.Caption,
            package.Attachments.Select(a => new MediaFile(a.Name, a.MediaType, a.Data.Length)).ToArray(), package.GifUrl), ChatMediaJson.Default.MediaHeader);
        var length = checked(4 + metadata.Length + package.Attachments.Sum(a => a.Data.Length));
        if (metadata.Length > 32768 || length > MaxPayloadBytes) throw new InvalidOperationException("Вложения слишком большие: максимум 32 МБ на сообщение.");
        var plain = new byte[length];
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(plain, metadata.Length);
            metadata.CopyTo(plain, 4);
            var offset = 4 + metadata.Length;
            foreach (var attachment in package.Attachments) { attachment.Data.CopyTo(plain, offset); offset += attachment.Data.Length; }
            var encrypted = new byte[HeaderSize + length];
            "PKM1"u8.CopyTo(encrypted);
            RandomNumberGenerator.Fill(encrypted.AsSpan(4, 12));
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(encrypted.AsSpan(4, 12), plain, encrypted.AsSpan(HeaderSize), encrypted.AsSpan(16, 16), Context(package.SenderId, package.RecipientId));
            return encrypted;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static ChatMediaPackage Decrypt(byte[] encrypted, byte[] key, long senderId, long recipientId)
    {
        if (encrypted.Length is < HeaderSize + 4 or > MaxPayloadBytes + HeaderSize || !encrypted.AsSpan(0, 4).SequenceEqual("PKM1"u8))
            throw new InvalidDataException("Некорректный формат вложения.");
        var plain = new byte[encrypted.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(encrypted.AsSpan(4, 12), encrypted.AsSpan(HeaderSize), encrypted.AsSpan(16, 16), plain, Context(senderId, recipientId));
            var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(plain);
            if (metadataLength is <= 0 or > 32768 || metadataLength > plain.Length - 4) throw new InvalidDataException("Повреждены данные вложения.");
            var metadata = JsonSerializer.Deserialize(plain.AsSpan(4, metadataLength), ChatMediaJson.Default.MediaHeader)
                ?? throw new InvalidDataException("Нет описания вложения.");
            if (metadata.SenderId != senderId || metadata.RecipientId != recipientId || metadata.Files is not { Length: <= MaxAttachments } ||
                metadata.Files.Any(f => f == null || f.Length is < 0 or > MaxFileBytes) || metadata.Files.Sum(f => (long)f.Length) != plain.Length - 4 - metadataLength)
                throw new InvalidDataException("Вложение не соответствует переписке.");
            var offset = 4 + metadataLength;
            var files = metadata.Files.Select(f => { var bytes = plain.AsSpan(offset, f.Length).ToArray(); offset += f.Length; return new ChatAttachment(f.Name, f.Type, bytes); }).ToArray();
            var package = new ChatMediaPackage(senderId, recipientId, metadata.Caption, files, metadata.GifUrl);
            Validate(package);
            return package;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static bool IsRasterImage(byte[] bytes) =>
        bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
        bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ||
        bytes.Length >= 6 && (bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8)) ||
        bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    private static byte[] Context(long sender, long recipient) => Encoding.UTF8.GetBytes($"pookie.media.v1:{sender}:{recipient}");
    private static void Validate(ChatMediaPackage package)
    {
        if (package.SenderId <= 0 || package.RecipientId <= 0 || package.SenderId == package.RecipientId || package.Caption == null ||
            package.Caption.Length > 20000 || package.Attachments is not { Length: <= MaxAttachments } || package.Attachments.Length == 0 && package.GifUrl == null ||
            package.Attachments.Any(a => a == null || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 255 || a.Name.Any(char.IsControl) || a.Name.IndexOfAny(['/', '\\']) >= 0 ||
                a.MediaType == null || a.MediaType.Length > 128 || a.Data.Length > MaxFileBytes) || package.GifUrl != null && !KlipyClient.IsMediaUrl(package.GifUrl))
            throw new InvalidDataException("Некорректные данные сообщения с медиа.");
    }
}

internal sealed record MediaFile(string Name, string Type, int Length);
internal sealed record MediaHeader(long SenderId, long RecipientId, string Caption, MediaFile[] Files, string? GifUrl);
[JsonSerializable(typeof(MediaHeader))]
internal partial class ChatMediaJson : JsonSerializerContext;
