using System.IO.Compression;

namespace Pookie.App.Messages;

// qu.ax accepts ZIPs, but not opaque .bin files. The archive contains only ciphertext.
internal static class ChatMediaArchive
{
    internal const int MaxEncryptedBytes = ChatMediaCrypto.MaxPayloadBytes + 32;
    internal const int MaxArchiveBytes = MaxEncryptedBytes + 4096;
    private const string EntryName = "payload.bin";

    internal static byte[] Wrap(byte[] encrypted)
    {
        if (encrypted.Length > MaxEncryptedBytes) throw new InvalidDataException("Вложение слишком большое.");
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry(EntryName, CompressionLevel.NoCompression).Open();
            entry.Write(encrypted);
        }
        return output.ToArray();
    }

    internal static byte[] Unwrap(byte[] bytes)
    {
        if (bytes.Length > MaxArchiveBytes) throw new InvalidDataException("Вложение слишком большое.");
        using var input = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count != 1 || archive.Entries[0] is not { FullName: EntryName, Length: >= 36 and <= MaxEncryptedBytes } entry)
            throw new InvalidDataException("Некорректный архив вложения.");
        // Never extract paths to disk, and bound the decompressed size before allocating.
        using var stream = entry.Open();
        var encrypted = new byte[(int)entry.Length];
        stream.ReadExactly(encrypted);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Некорректный размер вложения.");
        return encrypted;
    }
}
