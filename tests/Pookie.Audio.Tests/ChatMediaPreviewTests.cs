using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Pookie.App.Messages;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatMediaPreviewTests
{
    private static ChatMediaPackage Package() => new(42, 90, "Подпись", [new("photo.png", "image/png",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "chat-small.png"))), new("large.bin", "application/octet-stream", new byte[256 * 1024])]);
    [Fact]
    public void AuthenticatedPrefixHasDimensionsCaptionAndSmallPixelsBeforeOriginalPayload()
    {
        var package = Package(); var key = RandomNumberGenerator.GetBytes(16);
        var encrypted = ChatMediaCrypto.Encrypt(package, key);
        var length = 36 + BinaryPrimitives.ReadInt32LittleEndian(encrypted.AsSpan(4));
        Assert.True(length < encrypted.Length / 4);
        Assert.Null(ChatMediaCrypto.ReadPreview(encrypted.AsSpan(0, length - 1), key, 42, 90));
        var preview = ChatMediaCrypto.ReadPreview(encrypted.AsSpan(0, length), key, 42, 90)!;
        Assert.Equal(package.Caption, preview.Caption);
        Assert.Equal(48, preview.Images[0]!.Width); Assert.Equal(32, preview.Images[0]!.Height);
        Assert.InRange(preview.Images[0]!.Pixels.Length, 4, 32 * 32 * 4);
        Assert.Null(preview.Images[1]);
        Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.ReadPreview(encrypted.AsSpan(0, length), key, 90, 42));
        var tampered = encrypted.ToArray(); tampered[36] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.ReadPreview(tampered, key, 42, 90));
        Assert.Equal(package.Attachments[0].Data, ChatMediaCrypto.Decrypt(encrypted, key, 42, 90).Attachments[0].Data);
        // A valid preview from another payload cannot be spliced in front of this file.
        var other = ChatMediaCrypto.Encrypt(package with { Caption = "Другая" }, key);
        var otherLength = 36 + BinaryPrimitives.ReadInt32LittleEndian(other.AsSpan(4));
        var mixed = other[..otherLength].Concat(encrypted[length..]).ToArray();
        Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.Decrypt(mixed, key, 42, 90));
        Assert.Equal(package.Caption, ChatMediaCrypto.Decrypt(ChatMediaCrypto.EncryptLegacy(package, key), key, 42, 90).Caption);
    }

    [Theory]
    [InlineData('p')]
    [InlineData('q')]
    public async Task PreviewArrivesWhileDownloadIsBlockedBeforeOriginalBytes(char provider)
    {
        var key = RandomNumberGenerator.GetBytes(16); var encrypted = ChatMediaCrypto.Encrypt(Package(), key);
        var prefixLength = 36 + BinaryPrimitives.ReadInt32LittleEndian(encrypted.AsSpan(4));
        var bytes = provider == 'q' ? ChatMediaArchive.Wrap(encrypted) : encrypted;
        var archiveOffset = provider == 'q' ? 30 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) : 0;
        var stream = new PausedStream(bytes, archiveOffset + prefixLength);
        using var hosting = new ChatMediaHosting(new DownloadHandler(stream));
        var reference = new ChatMediaReference(provider, "abcdef", key);
        var previewReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var load = hosting.DownloadAsync(reference, 42, 90, CancellationToken.None, preview => { Assert.Equal(48, preview.Images[0]!.Width); previewReady.SetResult(); return Task.CompletedTask; });
        try
        {
            await previewReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(load.IsCompleted);
        }
        finally { stream.Resume.TrySetResult(); }
        Assert.Equal("Подпись", (await load).Caption);
    }
    private sealed class DownloadHandler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
    }
    private sealed class PausedStream(byte[] bytes, int boundary) : MemoryStream(bytes, writable: false)
    {
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= boundary) await Resume.Task.WaitAsync(token);
            else buffer = buffer[..Math.Min(buffer.Length, boundary - (int)Position)];
            return await base.ReadAsync(buffer, token);
        }
    }
}
