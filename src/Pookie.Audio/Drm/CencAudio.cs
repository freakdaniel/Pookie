using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Pookie.Audio;

// Restricted ISO-BMFF reader for SoundCloud's AAC-LC/cenc audio. All offsets and
// subsample lengths are validated before any bytes are passed into the native CDM.
internal sealed record CencAudioInitialization(uint TrackId, uint DefaultSampleSize, byte[] KeyId, int IvSize,
    int FrequencyIndex, int Channels)
{
    public static CencAudioInitialization Parse(byte[] bytes)
    {
        var moov = Mp4.One(bytes, 0, bytes.Length, "moov");
        var tracks = Mp4.Boxes(bytes, moov.Payload, moov.End).Where(b => b.Type == "trak").ToArray();
        if (tracks.Length != 1) throw Mp4.Invalid();
        var trak = tracks[0];
        var tkhd = Mp4.One(bytes, trak.Payload, trak.End, "tkhd");
        var tkhdVersion = bytes[tkhd.Payload];
        var trackId = Mp4.U32(bytes, tkhd.Payload + (tkhdVersion == 0 ? 12 : tkhdVersion == 1 ? 20 : throw Mp4.Invalid()), tkhd.End);
        var mdia = Mp4.One(bytes, trak.Payload, trak.End, "mdia");
        var hdlr = Mp4.One(bytes, mdia.Payload, mdia.End, "hdlr");
        if (Mp4.Type(bytes, hdlr.Payload + 8, hdlr.End) != "soun") throw Mp4.Invalid();
        var minf = Mp4.One(bytes, mdia.Payload, mdia.End, "minf");
        var stbl = Mp4.One(bytes, minf.Payload, minf.End, "stbl");
        var stsd = Mp4.One(bytes, stbl.Payload, stbl.End, "stsd");
        if (Mp4.U32(bytes, stsd.Payload + 4, stsd.End) != 1) throw Mp4.Invalid();
        var entry = Mp4.One(bytes, stsd.Payload + 8, stsd.End, "enca");
        if (Mp4.U16(bytes, entry.Payload + 8, entry.End) != 0) throw Mp4.Invalid(); // sample entry version
        var channels = Mp4.U16(bytes, entry.Payload + 16, entry.End);
        var rate = (int)(Mp4.U32(bytes, entry.Payload + 24, entry.End) >> 16);
        var sinf = Mp4.One(bytes, entry.Payload + 28, entry.End, "sinf");
        var frma = Mp4.One(bytes, sinf.Payload, sinf.End, "frma");
        if (Mp4.Type(bytes, frma.Payload, frma.End) != "mp4a") throw Mp4.Invalid();
        var schm = Mp4.One(bytes, sinf.Payload, sinf.End, "schm");
        if (Mp4.Type(bytes, schm.Payload + 4, schm.End) != "cenc") throw new DrmPlaybackException("Поддерживается Widevine CENC; CBCS/FairPlay пока не подключён.");
        var schi = Mp4.One(bytes, sinf.Payload, sinf.End, "schi");
        var tenc = Mp4.One(bytes, schi.Payload, schi.End, "tenc");
        if (tenc.End - tenc.Payload != 24 || bytes[tenc.Payload] != 0 || bytes[tenc.Payload + 6] != 1 ||
            bytes[tenc.Payload + 7] is not (8 or 16)) throw Mp4.Invalid();
        var kid = bytes[(tenc.Payload + 8)..tenc.End];
        var esds = Mp4.One(bytes, entry.Payload + 28, entry.End, "esds");
        var asc = FindAsc(bytes, esds.Payload + 4, esds.End);
        if (asc.Length < 2) throw Mp4.Invalid();
        var objectType = asc[0] >> 3;
        var frequency = (asc[0] & 7) << 1 | asc[1] >> 7;
        var channelConfig = asc[1] >> 3 & 15;
        int[] rates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
        if (objectType != 2 || frequency >= rates.Length || rate != rates[frequency] || channels != channelConfig || channels is < 1 or > 6)
            throw new DrmPlaybackException("Этот вариант защищённого AAC не поддерживается; ожидается AAC-LC.");
        var mvex = Mp4.One(bytes, moov.Payload, moov.End, "mvex");
        var trex = Mp4.One(bytes, mvex.Payload, mvex.End, "trex");
        if (Mp4.U32(bytes, trex.Payload + 4, trex.End) != trackId) throw Mp4.Invalid();
        return new(trackId, Mp4.U32(bytes, trex.Payload + 16, trex.End), kid, bytes[tenc.Payload + 7], frequency, channels);
    }

    private static byte[] FindAsc(byte[] bytes, int position, int end)
    {
        while (position < end)
        {
            var tag = bytes[position++];
            int size = 0, count = 0;
            byte value;
            do
            {
                if (position >= end || ++count > 4) throw Mp4.Invalid();
                value = bytes[position++]; size = checked(size * 128 + (value & 127));
            } while ((value & 128) != 0);
            if (size > end - position) throw Mp4.Invalid();
            if (tag == 5) return bytes[position..(position + size)];
            if (tag == 3)
            {
                if (size < 3) throw Mp4.Invalid();
                var flags = bytes[position + 2];
                var nested = position + 3;
                if ((flags & 128) != 0) nested += 2;
                if ((flags & 64) != 0) { if (nested >= position + size) throw Mp4.Invalid(); nested += 1 + bytes[nested]; }
                if ((flags & 32) != 0) nested += 2;
                if (nested > position + size) throw Mp4.Invalid();
                var found = FindAsc(bytes, nested, position + size); if (found.Length > 0) return found;
            }
            if (tag == 4)
            {
                if (size < 13 || bytes[position] != 0x40) throw Mp4.Invalid();
                var found = FindAsc(bytes, position + 13, position + size); if (found.Length > 0) return found;
            }
            position += size;
        }
        return [];
    }
}

internal sealed record CencAudioSample(byte[] Data, byte[] Iv, CencSubsample[] Subsamples);

internal static class CencAudioFragment
{
    public static CencAudioSample[] Parse(byte[] bytes, CencAudioInitialization initialization)
    {
        var moof = Mp4.One(bytes, 0, bytes.Length, "moof");
        var mdat = Mp4.One(bytes, 0, bytes.Length, "mdat");
        var traf = Mp4.One(bytes, moof.Payload, moof.End, "traf");
        if (Mp4.Boxes(bytes, moof.Payload, moof.End).Count(b => b.Type == "traf") != 1) throw Mp4.Invalid();
        var boxes = Mp4.Boxes(bytes, traf.Payload, traf.End);
        if (boxes.Any(b => b.Type is "sgpd" or "sbgp")) throw new DrmPlaybackException("Смена DRM-параметров внутри MP4-фрагмента пока не поддерживается.");
        var tfhd = Mp4.One(bytes, traf.Payload, traf.End, "tfhd");
        var flags = Mp4.Flags(bytes, tfhd);
        if (Mp4.U32(bytes, tfhd.Payload + 4, tfhd.End) != initialization.TrackId) throw Mp4.Invalid();
        int cursor = tfhd.Payload + 8;
        long baseOffset = moof.Start;
        if ((flags & 1) != 0) { baseOffset = checked((long)Mp4.U64(bytes, cursor, tfhd.End)); cursor += 8; }
        else if ((flags & 0x20000) == 0) throw Mp4.Invalid();
        if ((flags & 2) != 0) cursor += 4;
        if ((flags & 8) != 0) cursor += 4;
        var defaultSize = initialization.DefaultSampleSize;
        if ((flags & 16) != 0) { defaultSize = Mp4.U32(bytes, cursor, tfhd.End); cursor += 4; }
        if ((flags & 32) != 0) cursor += 4;
        if (cursor > tfhd.End) throw Mp4.Invalid();
        var ranges = new List<(int Offset, int Size)>();
        long nextOffset = mdat.Payload;
        foreach (var trun in boxes.Where(b => b.Type == "trun"))
        {
            var runFlags = Mp4.Flags(bytes, trun);
            var count = Mp4.U32(bytes, trun.Payload + 4, trun.End);
            if (count == 0 || count > 20000 || ranges.Count + count > 20000) throw Mp4.Invalid();
            cursor = trun.Payload + 8;
            if ((runFlags & 1) != 0) { nextOffset = checked(baseOffset + Mp4.I32(bytes, cursor, trun.End)); cursor += 4; }
            if ((runFlags & 4) != 0) cursor += 4;
            for (var i = 0; i < count; i++)
            {
                if ((runFlags & 0x100) != 0) cursor += 4;
                var size = defaultSize;
                if ((runFlags & 0x200) != 0) { size = Mp4.U32(bytes, cursor, trun.End); cursor += 4; }
                if ((runFlags & 0x400) != 0) cursor += 4;
                if ((runFlags & 0x800) != 0) cursor += 4;
                if (cursor > trun.End || size is 0 or > 8184 || nextOffset < mdat.Payload || nextOffset > mdat.End - size) throw Mp4.Invalid();
                ranges.Add((checked((int)nextOffset), (int)size)); nextOffset += size;
            }
            if (cursor != trun.End) throw Mp4.Invalid();
        }
        if (ranges.Count == 0) throw Mp4.Invalid();
        var senc = Mp4.One(bytes, traf.Payload, traf.End, "senc");
        var sencFlags = Mp4.Flags(bytes, senc);
        if ((sencFlags & ~2) != 0 || Mp4.U32(bytes, senc.Payload + 4, senc.End) != ranges.Count) throw Mp4.Invalid();
        cursor = senc.Payload + 8;
        var samples = new List<CencAudioSample>();
        foreach (var range in ranges)
        {
            if (initialization.IvSize > senc.End - cursor) throw Mp4.Invalid();
            var iv = bytes[cursor..(cursor + initialization.IvSize)]; cursor += initialization.IvSize;
            CencSubsample[] subsamples;
            if ((sencFlags & 2) != 0)
            {
                var count = Mp4.U16(bytes, cursor, senc.End); cursor += 2;
                if (count == 0 || count > 4096) throw Mp4.Invalid();
                subsamples = new CencSubsample[count];
                long total = 0;
                for (var i = 0; i < count; i++)
                {
                    var clear = Mp4.U16(bytes, cursor, senc.End); var cipher = Mp4.U32(bytes, cursor + 2, senc.End); cursor += 6;
                    subsamples[i] = new(clear, cipher); total += clear + (long)cipher;
                }
                if (total != range.Size) throw Mp4.Invalid();
            }
            else subsamples = [new(0, (uint)range.Size)];
            samples.Add(new(bytes[range.Offset..(range.Offset + range.Size)], iv, subsamples));
        }
        if (cursor != senc.End) throw Mp4.Invalid();
        return samples.ToArray();
    }

    public static byte[] DecryptAdts(byte[] bytes, CencAudioInitialization initialization, WidevineSession session, CancellationToken token)
    {
        var samples = Parse(bytes, initialization);
        var size = samples.Sum(sample => sample.Data.Length + 7);
        var result = new byte[size];
        var position = 0;
        try
        {
            foreach (var sample in samples)
            {
                token.ThrowIfCancellationRequested();
                var raw = session.Decrypt(sample.Data, initialization.KeyId, sample.Iv, sample.Subsamples);
                try
                {
                    WriteAdtsHeader(result.AsSpan(position, 7), raw.Length, initialization.FrequencyIndex, initialization.Channels);
                    raw.CopyTo(result, position + 7); position += raw.Length + 7;
                }
                finally { CryptographicOperations.ZeroMemory(raw); }
            }
            return result;
        }
        catch { CryptographicOperations.ZeroMemory(result); throw; }
    }

    internal static void WriteAdtsHeader(Span<byte> header, int sampleSize, int frequency, int channels)
    {
        var length = sampleSize + 7;
        if (length > 8191 || sampleSize <= 0 || frequency is < 0 or > 12 || channels is < 1 or > 6 || header.Length < 7) throw Mp4.Invalid();
        header[0] = 0xff; header[1] = 0xf1;
        header[2] = (byte)(1 << 6 | frequency << 2 | channels >> 2);
        header[3] = (byte)((channels & 3) << 6 | length >> 11);
        header[4] = (byte)(length >> 3); header[5] = (byte)((length & 7) << 5 | 0x1f); header[6] = 0xfc;
    }
}

internal readonly record struct Mp4Box(string Type, int Start, int Payload, int End);
internal static class Mp4
{
    public static List<Mp4Box> Boxes(byte[] bytes, int start, int end)
    {
        if (start < 0 || end > bytes.Length || start > end) throw Invalid();
        var boxes = new List<Mp4Box>();
        while (start < end)
        {
            var size = (long)U32(bytes, start, end);
            var type = Type(bytes, start + 4, end);
            var header = 8;
            if (size == 1) { size = checked((long)U64(bytes, start + 8, end)); header = 16; }
            if (size == 0) size = end - start;
            if (size < header || size > end - start || boxes.Count >= 20000) throw Invalid();
            boxes.Add(new(type, start, start + header, checked(start + (int)size))); start += (int)size;
        }
        return boxes;
    }
    public static Mp4Box One(byte[] bytes, int start, int end, string type)
    {
        var matching = Boxes(bytes, start, end).Where(b => b.Type == type).ToArray();
        return matching.Length == 1 ? matching[0] : throw Invalid();
    }
    public static uint Flags(byte[] bytes, Mp4Box box) => U32(bytes, box.Payload, box.End) & 0xffffff;
    private static ReadOnlySpan<byte> Read(byte[] bytes, int position, int size, int end) =>
        position >= 0 && end <= bytes.Length && position <= end - size ? bytes.AsSpan(position, size) : throw Invalid();
    public static ushort U16(byte[] bytes, int position, int end) => BinaryPrimitives.ReadUInt16BigEndian(Read(bytes, position, 2, end));
    public static uint U32(byte[] bytes, int position, int end) => BinaryPrimitives.ReadUInt32BigEndian(Read(bytes, position, 4, end));
    public static int I32(byte[] bytes, int position, int end) => BinaryPrimitives.ReadInt32BigEndian(Read(bytes, position, 4, end));
    public static ulong U64(byte[] bytes, int position, int end) => BinaryPrimitives.ReadUInt64BigEndian(Read(bytes, position, 8, end));
    public static string Type(byte[] bytes, int position, int end) => System.Text.Encoding.ASCII.GetString(Read(bytes, position, 4, end));
    public static DrmPlaybackException Invalid() => new("Неподдерживаемый или повреждённый защищённый MP4-аудиопоток.");
}
