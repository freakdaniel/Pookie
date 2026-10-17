using System.Buffers.Binary;
using Pookie.App.Messages;
using Pookie.App.Platform;
using StbImageSharp;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatAttachmentTests
{
    [Fact]
    public void ClipboardFileListAcceptsLocalUrisAndDoesNotTreatOtherTextAsFiles()
    {
        var files = ChatClipboard.FileUris("copy\nfile:///tmp/%D1%84%D0%BE%D1%82%D0%BE%20one.png\r\n#comment\nhttps://example.com/image.png\nfile://remote-host/share/a.png\n/tmp/ordinary-text\nfile:///tmp/another.jpg");
        Assert.Equal(new[] { "/tmp/фото one.png", "/tmp/another.jpg" }, files);
    }

    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 24)]
    [InlineData(false, 32)]
    [InlineData(true, 32)]
    public void WindowsClipboardBitmapBecomesPngWithCorrectRowOrderAndColours(bool topDown, int bits)
    {
        const int stride = 8;
        var dib = new byte[40 + stride * 2];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), topDown ? -2 : 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), (ushort)bits);
        var top = topDown ? 40 : 48; var bottom = topDown ? 48 : 40;
        dib[top + 2] = 255; dib[top + bits / 8 + 1] = 255;
        dib[bottom] = 255;
        var png = ClipboardBitmap.PngFromDib(dib);
        Assert.True(ChatMediaCrypto.IsRasterImage(png));
        var image = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(2, image.Width); Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 255 }, image.Data);
    }

    [Fact]
    public void StagingIsPrivateAndOnlyDeletesOwnedClipboardImages()
    {
        using var staging = new ChatAttachmentStaging();
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "chat-small.png"));
        var image = staging.AddImage(bytes, ".png");
        var original = Path.GetTempFileName();
        try
        {
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(image));
            staging.Keep([image, original]); Assert.True(File.Exists(image));
            staging.Keep([original]); Assert.False(File.Exists(image)); Assert.True(File.Exists(original));
        }
        finally { File.Delete(original); }
    }
}
