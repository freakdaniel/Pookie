using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Pookie.App.Messages;

namespace Pookie.App.Platform;

internal static class ClipboardBitmap
{
    // Windows screenshots commonly expose only CF_DIB/CF_DIBV5, without a PNG.
    // Convert supported true-colour bitmaps on a worker, one scanline at a time.
    internal static byte[] PngFromDib(byte[] dib)
    {
        if (dib.Length < 40) throw new InvalidDataException("Некорректное изображение в буфере.");
        int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(offset));
        var header = Int(0); var width = Int(4); var signedHeight = Int(8);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)); var compression = Int(16);
        if (header is not (40 or 108 or 124) || width <= 0 || signedHeight is 0 or int.MinValue ||
            bits is not (24 or 32) || compression is not (0 or 3) || dib.Length < header ||
            (long)width * Math.Abs(signedHeight) > 16 * 1024 * 1024)
            throw new InvalidDataException("Этот формат изображения в буфере не поддерживается.");
        var height = Math.Abs(signedHeight);
        var offset = header + (header == 40 && compression == 3 ? 12 : 0);
        var stride = checked((int)(((long)width * bits + 31) / 32 * 4));
        if (offset + (long)stride * height > dib.Length) throw new InvalidDataException("Изображение в буфере повреждено.");
        if (compression == 3 && (Int(40) != 0x00ff0000 || Int(44) != 0x0000ff00 || Int(48) != 0x000000ff))
            throw new InvalidDataException("Этот формат цвета в буфере не поддерживается.");
        var alpha = bits == 32 && header >= 108 && Int(52) == unchecked((int)0xff000000);
        if (bits == 32 && !alpha)
            for (var y = 0; y < height && !alpha; y++) for (var x = 0; x < width && !alpha; x++) alpha = dib[offset + y * stride + x * 4 + 3] != 0;
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 4 + 1)];
            for (var y = 0; y < height; y++)
            {
                var sourceRow = offset + (signedHeight > 0 ? height - y - 1 : y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var source = sourceRow + x * (bits / 8); var target = 1 + x * 4;
                    row[target] = dib[source + 2]; row[target + 1] = dib[source + 1]; row[target + 2] = dib[source];
                    row[target + 3] = bits == 32 && alpha ? dib[source + 3] : (byte)255;
                }
                zip.Write(row);
                if (compressed.Length > ChatMediaCrypto.MaxFileBytes) throw new InvalidOperationException("Изображение превышает 16 МБ.");
            }
        }
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var info = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(info, width); BinaryPrimitives.WriteInt32BigEndian(info.AsSpan(4), height);
        info[8] = 8; info[9] = 6;
        Chunk(output, "IHDR", info); Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
        if (output.Length > ChatMediaCrypto.MaxFileBytes) throw new InvalidOperationException("Изображение превышает 16 МБ.");
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] bytes)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); output.Write(number);
        var name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(bytes);
        var crc = uint.MaxValue;
        void Add(byte value) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
        foreach (var value in name) Add(value); foreach (var value in bytes) Add(value);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
