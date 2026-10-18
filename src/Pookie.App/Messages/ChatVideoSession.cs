using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aprillz.MewUI;
using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace Pookie.App.Messages;

// One bounded video surface, no frame queue and no decrypted temporary file.
// LibVLC owns decoding/audio; only the UI thread touches the MewUI bitmap.
internal sealed unsafe class ChatVideoSession : IAsyncDisposable
{
    private readonly object nativeSync = new(), frameSync = new();
    private readonly byte[] bytes;
    private LibVLC? vlc;
    private MediaPlayer? player;
    private VlcMedia? media;
    private StreamMediaInput? input;
    private MemoryStream? stream;
    private void* plane;
    private int width, height, pitch, naturalWidth, naturalHeight;
    private long frameVersion;
    private bool disposed, preview;
    private static readonly object initialization = new();
    private readonly MediaPlayer.LibVLCVideoFormatCb setup;
    private readonly MediaPlayer.LibVLCVideoLockCb videoLock;
    private readonly MediaPlayer.LibVLCVideoUnlockCb videoUnlock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb display;
    private ChatVideoSession(byte[] bytes)
    {
        this.bytes = bytes;
        setup = Configure; videoLock = Lock; videoUnlock = Unlock; display = Display;
    }
    internal static Task<ChatVideoSession> StartAsync(byte[] data, bool silent = false, bool preview = false)
    {
        // Capture before yielding: the account-scoped original cache can evict
        // and zero its buffers while the decoder worker is being scheduled.
        var owned = data.ToArray();
        return Task.Run(() =>
        {
        var session = new ChatVideoSession(owned);
        try
        {
            lock (initialization)
            {
                var mac = "/Applications/VLC.app/Contents/MacOS/lib";
                if (OperatingSystem.IsMacOS() && Directory.Exists(mac)) Core.Initialize(mac); else Core.Initialize();
            }
            session.vlc = new LibVLC("--quiet", "--no-video-title-show", "--no-osd", "--no-spu", "--no-lua", "--no-metadata-network-access", "--avcodec-hw=none");
            session.preview = preview;
            session.player = new MediaPlayer(session.vlc);
            session.player.SetVideoCallbacks(session.videoLock, session.videoUnlock, session.display);
            session.player.SetVideoFormatCallbacks(session.setup, null);
            session.stream = new MemoryStream(session.bytes, writable: false);
            session.input = new StreamMediaInput(session.stream);
            session.media = new VlcMedia(session.vlc, session.input);
            session.media.AddOption(":demux=avformat");
            session.media.AddOption(":no-sub-autodetect-file");
            if (silent) session.media.AddOption(":no-audio");
            if (!session.player.Play(session.media)) throw new InvalidDataException("Видео не удалось открыть.");
            return session;
        }
        catch { session.DisposeCore(); throw; }
        });
    }
    private uint Configure(ref nint opaque, nint chroma, ref uint sourceWidth, ref uint sourceHeight, ref uint pitches, ref uint lines)
    {
        try
        {
            if (sourceWidth is 0 or > 32768 || sourceHeight is 0 or > 32768) return 0;
            var scale = Math.Min(1, Math.Min((preview ? 380d : 1920d) / sourceWidth, (preview ? 380d : 1080d) / sourceHeight));
            lock (frameSync)
            {
                // Stop can finish a pending format negotiation. Keep callbacks
                // valid until the native player has stopped and been released.
                if (plane != null) NativeMemory.AlignedFree(plane);
                naturalWidth = (int)sourceWidth; naturalHeight = (int)sourceHeight;
                width = Math.Max(1, (int)(sourceWidth * scale)); height = Math.Max(1, (int)(sourceHeight * scale));
                pitch = (width * 4 + 31) & ~31;
                var rows = (height + 31) & ~31;
                plane = NativeMemory.AlignedAlloc((nuint)(pitch * rows), 32);
                if (plane == null) return 0;
                new Span<byte>(plane, pitch * rows).Clear();
                Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
                sourceWidth = (uint)width; sourceHeight = (uint)height; pitches = (uint)pitch; lines = (uint)rows;
                return 1;
            }
        }
        catch { return 0; } // Never unwind a managed exception through a native decoder callback.
    }
    private nint Lock(nint opaque, nint planes)
    { Monitor.Enter(frameSync); Marshal.WriteIntPtr(planes, (nint)plane); return 0; }
    private void Unlock(nint opaque, nint picture, nint planes) => Monitor.Exit(frameSync);
    private void Display(nint opaque, nint picture) => Interlocked.Increment(ref frameVersion);
    internal bool CopyFrame(ref WriteableBitmap? bitmap, ref long version)
    {
        lock (frameSync)
        {
            var next = Interlocked.Read(ref frameVersion);
            if (disposed || plane == null || version == next || next == 0) return false;
            if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            { bitmap?.Dispose(); bitmap = new WriteableBitmap(width, height); }
            using var write = bitmap.LockForWrite();
            var pixels = write.PixelsBgra32;
            for (var row = 0; row < height; row++)
            {
                var target = pixels.Slice(row * width * 4, width * 4);
                new ReadOnlySpan<byte>((byte*)plane + row * pitch, width * 4).CopyTo(target);
                for (var i = 3; i < target.Length; i += 4) target[i] = 255;
            }
            version = next; return true;
        }
    }
    internal (long Time, long Length, bool Playing, bool Failed, bool Ended) Snapshot()
    {
        lock (nativeSync)
            return player == null || disposed ? default : (player.Time, player.Length, player.IsPlaying, player.State == VLCState.Error, player.State == VLCState.Ended);
    }
    internal ImageSource? Poster()
    {
        lock (frameSync)
        {
            if (disposed || plane == null || Interlocked.Read(ref frameVersion) == 0) return null;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) new ReadOnlySpan<byte>((byte*)plane + y * pitch, width * 4).CopyTo(pixels.AsSpan(y * width * 4, width * 4));
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return ImageSource.FromBgraPixels(width, height, pixels);
        }
    }
    internal ChatVideoInfo? PreviewInfo()
    {
        var duration = Snapshot().Length;
        lock (frameSync)
        {
            if (disposed || plane == null || Interlocked.Read(ref frameVersion) == 0) return null;
            var scale = Math.Min(32d / width, 32d / height);
            var tw = Math.Max(1, (int)(width * scale)); var th = Math.Max(1, (int)(height * scale));
            var pixels = new byte[tw * th * 4];
            for (var y = 0; y < th; y++) for (var x = 0; x < tw; x++)
            {
                new ReadOnlySpan<byte>((byte*)plane + (y * height / th) * pitch + (x * width / tw) * 4, 4).CopyTo(pixels.AsSpan((y * tw + x) * 4, 4));
                pixels[(y * tw + x) * 4 + 3] = 255;
            }
            return new(naturalWidth, naturalHeight, Math.Clamp(duration, 0, 86400000), tw, th, pixels);
        }
    }
    internal Task SetPausedAsync(bool paused) => Task.Run(() =>
    {
        lock (nativeSync)
        {
            if (disposed || player == null) return;
            if (!paused && player.State == VLCState.Ended) { player.Stop(); player.Play(media!); }
            else player.SetPause(paused);
        }
    });
    internal Task ToggleAsync() => Task.Run(() =>
    {
        lock (nativeSync)
        {
            if (disposed || player == null) return;
            if (player.State == VLCState.Ended) { player.Stop(); player.Play(media!); }
            else player.SetPause(player.IsPlaying);
        }
    });
    internal void Seek(double fraction) { lock (nativeSync) { if (!disposed && player?.IsSeekable == true) player.Position = (float)Math.Clamp(fraction, 0, 1); } }
    internal void Volume(int volume) { lock (nativeSync) { if (!disposed && player != null) player.Volume = Math.Clamp(volume, 0, 100); } }
    internal bool Rate(double rate) { lock (nativeSync) return !disposed && player != null && player.SetRate((float)Math.Clamp(rate, .25, 2)) == 0; }
    internal (int Volume, double Rate) PlaybackSettings() { lock (nativeSync) return player == null || disposed ? default : (player.Volume, player.Rate); }
    internal void Mute(bool muted) { lock (nativeSync) { if (!disposed && player != null) player.Mute = muted; } }
    public ValueTask DisposeAsync() => new(Task.Run(DisposeCore));
    private void DisposeCore()
    {
        lock (nativeSync)
        {
            if (disposed) return;
            lock (frameSync) disposed = true;
            try { player?.Stop(); }
            finally
            {
                player?.Dispose(); player = null; media?.Dispose(); input?.Dispose(); stream?.Dispose(); vlc?.Dispose();
                lock (frameSync) { if (plane != null) NativeMemory.AlignedFree(plane); plane = null; }
                CryptographicOperations.ZeroMemory(bytes);
                GC.KeepAlive(setup); GC.KeepAlive(videoLock); GC.KeepAlive(videoUnlock); GC.KeepAlive(display);
            }
        }
    }
}
