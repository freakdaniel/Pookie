using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Pookie.Discord;

// Discord's framed local IPC protocol. DTOs and JSON metadata are known at
// compile time; no Newtonsoft.Json contracts or runtime code generation.
internal sealed class PresenceRpcClient(string applicationId, Func<CancellationToken, Task<Stream>>? connect = null) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writes = new(1);
    private readonly Channel<bool> changed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private PresenceActivity? current;
    private Task? worker;
    private int disposed;
    internal PresenceActivity? CurrentPresence => Volatile.Read(ref current);
    internal void Initialize() => worker ??= Task.Run(RunAsync);
    internal void SetPresence(PresenceActivity? activity) { Volatile.Write(ref current, activity); changed.Writer.TryWrite(true); }
    internal void ClearPresence() => SetPresence(null);
    internal Task Completion => worker ?? Task.CompletedTask;
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; lifetime.Cancel(); changed.Writer.TryComplete(); }

    private async Task RunAsync()
    {
        var token = lifetime.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var stream = await (connect?.Invoke(token) ?? ConnectAsync(token));
                    using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
                    await SendAsync(stream, 0, JsonSerializer.SerializeToUtf8Bytes(new PresenceHandshake(1, applicationId), PresenceJson.Default.PresenceHandshake), token);
                    using (var ready = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        ready.CancelAfter(TimeSpan.FromSeconds(5));
                        while (true)
                        {
                            var (opcode, body) = await ReadFrameAsync(stream, ready.Token);
                            if (opcode == 3) { await SendAsync(stream, 4, body, ready.Token); continue; }
                            if (opcode != 1) throw new IOException("Discord closed its handshake.");
                            using var document = JsonDocument.Parse(body);
                            if (document.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "READY") break;
                            throw new IOException("Discord did not accept its handshake.");
                        }
                    }
                    var reader = ReceiveAsync(stream, session);
                    try
                    {
                        await SendPresenceAsync(stream, CurrentPresence, session.Token);
                        while (await changed.Reader.WaitToReadAsync(session.Token))
                        {
                            while (changed.Reader.TryRead(out _)) { }
                            await SendPresenceAsync(stream, CurrentPresence, session.Token);
                        }
                    }
                    finally
                    {
                        session.Cancel();
                        try { await reader; } catch (Exception error) when (error is IOException or OperationCanceledException or SocketException) { }
                        if (token.IsCancellationRequested)
                        {
                            using var clear = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                            try { await SendPresenceAsync(stream, null, clear.Token); } catch (Exception error) when (error is IOException or OperationCanceledException or SocketException) { }
                        }
                    }
                }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or JsonException or UnauthorizedAccessException)
                { /* Discord may be absent, restarting or in a desktop sandbox. */ }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lifetime.Dispose(); writes.Dispose(); }
    }

    private async Task ReceiveAsync(Stream stream, CancellationTokenSource session)
    {
        try
        {
            while (true)
            {
                var (opcode, body) = await ReadFrameAsync(stream, session.Token);
                if (opcode == 2) return;
                if (opcode == 3) await SendAsync(stream, 4, body, session.Token);
            }
        }
        finally { session.Cancel(); }
    }
    private Task SendPresenceAsync(Stream stream, PresenceActivity? presence, CancellationToken token) =>
        SendAsync(stream, 1, JsonSerializer.SerializeToUtf8Bytes(new PresenceRequest("SET_ACTIVITY", new(Environment.ProcessId, presence), Guid.NewGuid().ToString("N")), PresenceJson.Default.PresenceRequest), token);

    private async Task SendAsync(Stream stream, int opcode, byte[] body, CancellationToken token)
    {
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await writes.WaitAsync(token);
        try { await stream.WriteAsync(frame, token); await stream.FlushAsync(token); }
        finally { writes.Release(); }
    }
    internal static async Task<(int Opcode, byte[] Body)> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[8]; await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > 65536) throw new IOException("Invalid Discord frame size.");
        var body = new byte[length]; await stream.ReadExactlyAsync(body, token);
        return (BinaryPrimitives.ReadInt32LittleEndian(header), body);
    }
    private static async Task<Stream> ConnectAsync(CancellationToken token)
    {
        for (var index = 0; index < 10; index++)
        {
            if (OperatingSystem.IsWindows())
            {
                var pipe = new NamedPipeClientStream(".", "discord-ipc-" + index, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await pipe.ConnectAsync(150, token); return pipe; }
                catch (Exception error) when (error is IOException or TimeoutException) { pipe.Dispose(); }
                catch { pipe.Dispose(); throw; }
            }
            else
            {
                foreach (var directory in SocketDirectories())
                {
                    var path = Path.Combine(directory, "discord-ipc-" + index);
                    if (!File.Exists(path)) continue;
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(200);
                    try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), attempt.Token); return new NetworkStream(socket, ownsSocket: true); }
                    catch (Exception error) when (error is SocketException or OperationCanceledException) { socket.Dispose(); token.ThrowIfCancellationRequested(); }
                }
            }
        }
        throw new IOException("Discord IPC is unavailable.");
    }
    private static IEnumerable<string> SocketDirectories()
    {
        var roots = new[] { Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Environment.GetEnvironmentVariable("TMPDIR"), Environment.GetEnvironmentVariable("TMP"), Environment.GetEnvironmentVariable("TEMP"), "/tmp" };
        foreach (var root in roots.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            yield return root!;
            yield return Path.Combine(root!, "app", "com.discordapp.Discord");
            yield return Path.Combine(root!, "snap.discord");
            yield return Path.Combine(root!, ".flatpak", "com.discordapp.Discord", "xdg-run");
        }
    }
}
