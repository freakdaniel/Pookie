using Pookie.App.Messages;
using Aprillz.MewUI;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatPresentationTests
{
    [Fact]
    public void CaptionsWidenSmallPhotosWithoutMakingLongMessagesUnbounded()
    {
        Assert.Equal(100, ChatImagePreview.MessageWidth(new(100, 100), null));
        Assert.Equal(220, ChatImagePreview.MessageWidth(new(100, 100), 25));
        Assert.Equal(440, ChatImagePreview.MessageWidth(new(100, 100), 4000));
        Assert.Equal(300, ChatImagePreview.MessageWidth(new(3840, 2160), 25));
    }
    [Fact]
    public void InvalidAndOversizedImagesDoNotAllocateAFullDecode()
    {
        Assert.Null(ChatImagePreview.Decode([0xFF, 0xD8, 0xFF, 0x00]));
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "chat-small.png"));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 5000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 4000);
        Assert.Null(ChatImagePreview.ReadSize(bytes));
        Assert.Null(ChatImagePreview.Decode(bytes));
    }

    [Theory]
    [InlineData(48, 32, 48, 32)]
    [InlineData(3840, 2160, 300, 168)]
    [InlineData(800, 1600, 130, 260)]
    [InlineData(1000, 10, 300, 3)]
    public void MediaKeepsAspectRatioAndNeverUpscales(int width, int height, int expectedWidth, int expectedHeight)
    {
        var size = ChatImagePreview.Fit(new(width, height));
        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
    }

    [Theory]
    [InlineData("chat-4k.jpg", 3840, 2160, 520, 292)]
    [InlineData("chat-small.png", 48, 32, 48, 32)]
    public void PhotoDecodesToBoundedPixelsAndThumbnail(string name, int width, int height, int previewWidth, int previewHeight)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", name));
        Assert.Equal(new Size(width, height), ChatImagePreview.ReadSize(bytes));
        using var preview = ChatImagePreview.Decode(bytes);
        Assert.NotNull(preview);
        Assert.Equal(previewWidth, preview.PixelWidth);
        Assert.Equal(previewHeight, preview.PixelHeight);
        using var thumbnail = ChatImagePreview.Decode(bytes, 72, 72);
        Assert.NotNull(thumbnail);
        Assert.InRange(thumbnail.PixelWidth, 1, 72);
        Assert.InRange(thumbnail.PixelHeight, 1, 72);
        var pixels = new byte[preview.PixelWidth * preview.PixelHeight * 4];
        preview.CopyPixels(pixels, preview.PixelWidth * 4);
        Assert.Contains(pixels, value => value != 0);
    }

    private static DateTimeOffset Local(int day, int hour = 12, int minute = 30)
    {
        var date = new DateTime(2026, 10, day, hour, minute, 0);
        return new(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }
    [Fact]
    public void InboxUsesTodayThenCurrentCalendarWeekThenDate()
    {
        var sunday = Local(18);
        Assert.Equal("09:04", ChatPresentation.InboxTime(Local(18, 9, 4), sunday));
        Assert.Equal("Пн", ChatPresentation.InboxTime(Local(12), sunday));
        Assert.Equal("Сб", ChatPresentation.InboxTime(Local(17), sunday));
        Assert.Equal("11.10.26", ChatPresentation.InboxTime(Local(11), sunday));
        Assert.Equal("18.10.26", ChatPresentation.InboxTime(Local(18), Local(19)));
        Assert.Equal("18 октября", ChatPresentation.Day(sunday));
        Assert.Equal("12:30", ChatPresentation.Time(sunday));
    }
    [Fact]
    public void MediaPreviewsUseHumanLabelsAndPlainTextCollapsesWhitespace()
    {
        Assert.Equal("Фотография", ChatPresentation.MediaLabel(new(42, 90, "", [new("image.png", "image/png", [])])));
        Assert.Equal("GIF", ChatPresentation.MediaLabel(new(42, 90, "", [new("image.gif", "image/gif", [])])));
        Assert.Equal("Видео", ChatPresentation.MediaLabel(new(42, 90, "", [new("clip.mp4", "video/mp4", [])])));
        Assert.Equal("hello world", ChatPresentation.Preview("hello\r\n   world"));
    }
}
