using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using Xunit;

namespace Pookie.Discord.Tests;

public sealed class PresenceTransportTests
{
    [Fact]
    public async Task LocalRpcHandshakesPreservesActivityAnswersPingAndClearsExplicitly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token; var name = "pookie-discord-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new PresenceRpcClient("fixture", async stop =>
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(stop); return pipe;
        });
        var now = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        client.SetPresence(PresenceService.CreatePresence(new("Трек 🎵", "Artist", null, "https://soundcloud.com/artist/song", 5, 20, true), now));
        client.Initialize(); await server.WaitForConnectionAsync(token);
        var handshake = await PresenceRpcClient.ReadFrameAsync(server, token);
        Assert.Equal(0, handshake.Opcode);
        using (var json = JsonDocument.Parse(handshake.Body)) Assert.Equal("fixture", json.RootElement.GetProperty("client_id").GetString());
        await WriteAsync(server, 1, "{\"evt\":\"READY\"}"u8.ToArray(), token);
        var activity = await ReadActivityAsync(server, token);
        using (activity)
        {
            var value = activity.RootElement.GetProperty("args").GetProperty("activity");
            Assert.Equal("Трек 🎵", value.GetProperty("details").GetString());
            Assert.Equal(2, value.GetProperty("type").GetInt32());
            Assert.Equal(2, value.GetProperty("status_display_type").GetInt32());
            Assert.Equal(new DateTimeOffset(now.AddSeconds(-5)).ToUnixTimeMilliseconds(), value.GetProperty("timestamps").GetProperty("start").GetInt64());
            Assert.Equal("soundcloud-logo", value.GetProperty("assets").GetProperty("large_image").GetString());
        }
        await WriteAsync(server, 3, "ping"u8.ToArray(), token);
        while (true)
        {
            var frame = await PresenceRpcClient.ReadFrameAsync(server, token);
            if (frame.Opcode == 4) { Assert.Equal("ping"u8.ToArray(), frame.Body); break; }
        }
        client.ClearPresence();
        using (var clear = await ReadActivityAsync(server, token))
            Assert.Equal(JsonValueKind.Null, clear.RootElement.GetProperty("args").GetProperty("activity").ValueKind);
        client.Dispose(); await client.Completion.WaitAsync(token);
    }

    [Fact]
    public async Task RpcRejectsOversizedFrameBeforeAllocatingItsPayload()
    {
        var header = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), int.MaxValue);
        await Assert.ThrowsAsync<IOException>(() => PresenceRpcClient.ReadFrameAsync(new MemoryStream(header), CancellationToken.None));
    }

    [Fact]
    public async Task RpcReadsPartialHeadersAndBodies()
    {
        var frame = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(frame, 3); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 4);
        "ping"u8.CopyTo(frame.AsSpan(8));
        var read = await PresenceRpcClient.ReadFrameAsync(new FragmentedStream(frame), CancellationToken.None);
        Assert.Equal(3, read.Opcode); Assert.Equal("ping"u8.ToArray(), read.Body);
    }
    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], token);
    }
    private static async Task<JsonDocument> ReadActivityAsync(Stream stream, CancellationToken token)
    {
        while (true)
        {
            var frame = await PresenceRpcClient.ReadFrameAsync(stream, token);
            if (frame.Opcode == 1) return JsonDocument.Parse(frame.Body);
        }
    }
    private static async Task WriteAsync(Stream stream, int opcode, byte[] body, CancellationToken token)
    {
        var frame = new byte[8 + body.Length]; BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length); body.CopyTo(frame, 8);
        await stream.WriteAsync(frame, token); await stream.FlushAsync(token);
    }
}
