using System.Buffers.Binary;
using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using StbImageSharp;

namespace Pookie.App;

// Decode one frame at a time. No unbounded frame cache; hidden GIFs stop decoding.
internal sealed class ChatGifAnimation : IDisposable
{
    private readonly Image image;
    private readonly Func<bool> visible;
    private readonly DispatcherTimer timer = new(TimeSpan.FromMilliseconds(30));
    private readonly object gate = new();
    private readonly MemoryStream input;
    private IEnumerator<AnimatedFrameResult>? frames;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long nextFrame;
    private bool busy;
    private volatile bool disposed;
    internal ChatGifAnimation(Image image, byte[] bytes, Func<bool> visible)
    {
        if (!CanAnimate(bytes)) throw new ArgumentException("GIF exceeds animation bounds.");
        this.image = image; this.visible = visible;
        input = new MemoryStream(bytes.ToArray(), writable: false);
        timer.Tick += Tick; timer.Start();
    }
    internal static bool CanAnimate(byte[] bytes)
    {
        if (bytes.Length is < 13 or > 8 * 1024 * 1024 || !bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) && !bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8)) return false;
        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        return width > 0 && height > 0 && (long)width * height <= 1024 * 1024;
    }
    private async void Tick()
    {
        if (disposed || busy || clock.ElapsedMilliseconds < nextFrame || !visible()) return;
        busy = true;
        try
        {
            var result = await Task.Run(DecodeFrame);
            if (disposed || result == null) return;
            image.Source = result.Value.Source;
            nextFrame = clock.ElapsedMilliseconds + Math.Clamp(result.Value.Delay, 40, 10000);
        }
        catch { Dispose(); }
        finally { busy = false; }
    }
    private (ImageSource Source, int Delay)? DecodeFrame()
    {
        lock (gate)
        {
            if (disposed) return null;
            frames ??= ImageResult.AnimatedGifFramesFromStream(input, ColorComponents.RedGreenBlueAlpha).GetEnumerator();
            if (!frames.MoveNext())
            {
                frames.Dispose(); input.Position = 0;
                frames = ImageResult.AnimatedGifFramesFromStream(input, ColorComponents.RedGreenBlueAlpha).GetEnumerator();
                if (!frames.MoveNext()) return null;
            }
            var frame = frames.Current;
            if (frame.Width <= 0 || frame.Height <= 0 || (long)frame.Width * frame.Height > 1024 * 1024) throw new InvalidDataException("GIF frame exceeds bounds.");
            var scale = Math.Min(1, Math.Min(520d / frame.Width, 380d / frame.Height));
            var width = Math.Max(1, (int)(frame.Width * scale)); var height = Math.Max(1, (int)(frame.Height * scale));
            var bgra = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var source = ((y * frame.Height / height) * frame.Width + x * frame.Width / width) * 4;
                var target = (y * width + x) * 4;
                bgra[target] = frame.Data[source + 2]; bgra[target + 1] = frame.Data[source + 1];
                bgra[target + 2] = frame.Data[source]; bgra[target + 3] = frame.Data[source + 3];
            }
            return (ImageSource.FromBgraPixels(width, height, bgra), frame.DelayInMs);
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Dispose();
        // Wait for an in-flight decoder on a worker, never block the UI thread.
        _ = Task.Run(() => { lock (gate) { frames?.Dispose(); frames = null; input.Dispose(); } });
    }
}
