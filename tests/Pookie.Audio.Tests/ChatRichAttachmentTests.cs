using System.Security.Cryptography;
using Pookie.App.Messages;
using Pookie.App;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatRichAttachmentTests
{
    [Fact]
    public void DocumentPresentationSurvivesAuthenticatedRoundTripWithoutAnImagePreview()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/Media/chat-small.png"));
        var package = new ChatMediaPackage(42, 90, "Подпись", [new("photo.png", "image/png", bytes, AsFile: true)]);
        var key = RandomNumberGenerator.GetBytes(16);
        var ciphertext = ChatMediaCrypto.Encrypt(package, key);
        var preview = ChatMediaCrypto.ReadPreview(ciphertext, key, 42, 90)!;
        Assert.True(preview.Files[0].AsFile); Assert.Null(preview.Images[0]);
        var restored = ChatMediaCrypto.Decrypt(ciphertext, key, 42, 90);
        Assert.True(restored.Attachments[0].AsFile); Assert.Equal(bytes, restored.Attachments[0].Data);
        Assert.Equal("photo.png", ChatPresentation.MediaLabel(restored));
    }
    [Fact]
    public void VideoGeometryDurationAndPosterSurviveBothAuthenticatedEnvelopes()
    {
        var info = new ChatVideoInfo(270, 480, 10000, 18, 32, new byte[18 * 32 * 4]);
        var package = new ChatMediaPackage(42, 90, "Видео", [new("clip.mov", "video/quicktime", [1, 2, 3], Video: info)]);
        var key = RandomNumberGenerator.GetBytes(16);
        var encrypted = ChatMediaCrypto.Encrypt(package, key);
        var preview = ChatMediaCrypto.ReadPreview(encrypted, key, 42, 90)!;
        Assert.Equal(480, preview.Images[0]!.Height);
        Assert.Equal(10000, preview.Files[0].Video!.DurationMs);
        var restored = ChatMediaCrypto.Decrypt(encrypted, key, 42, 90).Attachments[0].Video!;
        Assert.True(restored.Valid); Assert.Equal(info.Pixels, restored.Pixels);
        // Encrypt must not erase the sender's thumbnail while clearing temporary preview pixels.
        info.Pixels[0] = 17;
        ChatMediaCrypto.Encrypt(package, key); Assert.Equal(17, info.Pixels[0]);
    }
    [Theory]
    [InlineData(8388608, 30000, true)]
    [InlineData(8388609, 10000, false)]
    [InlineData(1024, 30001, false)]
    [InlineData(1024, 0, false)]
    [InlineData(0, 10000, false)]
    public void AutoplayHasExplicitSizeAndDurationBounds(int bytes, long duration, bool expected) =>
        Assert.Equal(expected, ChatVideoFrame.CanAutoPlay(bytes, duration));
    [Fact]
    public void MalformedVideoMetadataCannotBeUploaded()
    {
        var package = new ChatMediaPackage(42, 90, "", [new("clip.mp4", "video/mp4", [1],
            Video: new(0, 480, 1000, 1, 1, new byte[4]))]);
        Assert.Throws<InvalidDataException>(() => ChatMediaCrypto.Encrypt(package, RandomNumberGenerator.GetBytes(16)));
    }
    [Theory]
    [InlineData("https://soundcloud.com/artist/song")]
    [InlineData("https://soundcloud.com/artist/sets/album?secret_token=s-fixture")]
    [InlineData("https://on.soundcloud.com/example")]
    public void NativeMusicLinksKeepCaptionAndRejectDuplicates(string url)
    {
        var links = ChatMusicLink.Find("Послушай\n" + url + "\n" + url);
        Assert.Equal([url], links); Assert.Equal("Послушай", ChatMusicLink.Caption("Послушай\n" + url, links));
    }
    [Theory]
    [InlineData("https://soundcloud.com.evil.test/a/song")]
    [InlineData("https://soundcloud.com@evil.test/a/song")]
    [InlineData("https://evil@soundcloud.com/a/song")]
    [InlineData("http://soundcloud.com/a/song")]
    [InlineData("https://soundcloud.com:8443/a/song")]
    [InlineData("https://soundcloud.com/you/library")]
    [InlineData("https://soundcloud.com/artist")]
    public void UnrelatedAndCredentialBearingUrlsAreNotMusicAttachments(string url) => Assert.False(ChatMusicLink.IsMusicUrl(url));
}
