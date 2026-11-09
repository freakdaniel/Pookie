#if WINDOWS
using System.Buffers;
using System.Runtime.InteropServices.Marshalling;
using Pookie.Logging;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio;

[GeneratedComClass]
internal sealed unsafe partial class PlayReadyMediaStream(PlayReadyHlsStream media, CancellationToken stop, long position = 0)
    : IEncryptedMediaStream, IAgileObject
{
    private const int BlockSize = 65536;
    private readonly object gate = new();
    private long offset = position;

    public int Read(void* destination, uint count, uint* read)
    {
        if (read != null) *read = 0;
        if (count == 0) return 0;
        if (destination == null) return InvalidArgument;
        lock (gate)
        {
            var remaining = Math.Min(count, Math.Max(0, media.Length - offset));
            if (remaining == 0) return 1; // S_FALSE: a short read at EOF.
            var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(remaining, BlockSize));
            var next = (byte*)destination;
            try
            {
                while (remaining > 0)
                {
                    // IStream.Read is synchronous. Media Foundation calls it on
                    // its worker threads; HTTP and cache I/O remain asynchronous.
                    var size = (int)Math.Min(remaining, BlockSize);
                    var received = media.ReadAsync(offset, buffer.AsMemory(0, size), stop).GetAwaiter().GetResult();
                    if (received == 0) return StreamReadFault;
                    buffer.AsSpan(0, received).CopyTo(new Span<byte>(next, received));
                    offset += received;
                    next += received;
                    remaining -= received;
                    if (read != null) *read += (uint)received;
                }
                return next - (byte*)destination == count ? 0 : 1;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return StreamReadFault; }
            catch (Exception error)
            {
                AppLog.Failure("Pookie.Audio.PlayReady", "Чтение защищённого HLS", error);
                return StreamReadFault;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
    }

    public int Seek(long move, uint origin, long* result)
    {
        lock (gate)
        {
            var start = origin switch { 0 => 0L, 1 => offset, 2 => media.Length, _ => -1 };
            if (start < 0 || move < -start || move > long.MaxValue - start) return InvalidStreamOperation;
            offset = start + move;
            if (result != null) *result = offset;
            return 0;
        }
    }

    public int Stat(StreamStat* stat, uint flags)
    {
        *stat = new() { Type = 2, Size = media.Length }; // STGTY_STREAM, STGM_READ
        return 0;
    }

    public int Clone(nint* stream)
    {
        lock (gate)
        {
            using var copy = Export<IEncryptedMediaStream>(new PlayReadyMediaStream(media, stop, offset));
            *stream = copy.Detach();
            return 0;
        }
    }

    public int Write(void* buffer, uint count, uint* written) => StreamAccessDenied;
    public int SetSize(ulong size) => StreamAccessDenied;
    public int CopyTo(nint stream, ulong count, ulong* read, ulong* written) => NotImplemented;
    public int Commit(uint flags) => StreamAccessDenied;
    public int Revert() => StreamAccessDenied;
    public int LockRegion(ulong offset, ulong count, uint flags) => InvalidStreamOperation;
    public int UnlockRegion(ulong offset, ulong count, uint flags) => InvalidStreamOperation;
}
#endif
