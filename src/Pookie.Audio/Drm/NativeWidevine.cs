using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Pookie.Audio;

internal sealed record CdmEvent(int Kind, uint Promise, int Code, byte[] Payload);

// Written in C#; the only native component loaded here is Google's installed CDM.
// Every call on a CDM instance, including timers and Destroy, uses one OS thread.
internal sealed unsafe class NativeWidevine : IDisposable
{
    private readonly BlockingCollection<Action> commands = new();
    private readonly ConcurrentQueue<CdmEvent> events = new();
    private readonly PriorityQueue<nint, long> timers = new();
    private readonly HashSet<nint> buffers = [];
    private readonly Thread thread;
    private readonly nint createInstance;
    private GCHandle managed;
    private nint host, cdm;
    private int disposed;
    private readonly Action releaseModule;
    public byte[] Session { get; set; } = [];

    public NativeWidevine(string path) : this(LoadModule(path), CdmModule.ReleaseInstance) { }

    // Injectable export for ABI tests; production loads only the installed CDM.
    internal NativeWidevine(nint createInstance, Action releaseModule)
    {
        this.createInstance = createInstance;
        this.releaseModule = releaseModule;
        thread = new Thread(Run) { IsBackground = true, Name = "Pookie Widevine CDM" };
        try
        {
            managed = GCHandle.Alloc(this);
            host = (nint)NativeMemory.AllocZeroed((nuint)sizeof(CdmAbi.HostObject));
            *(CdmAbi.HostObject*)host = new() { Vtable = CdmAbi.HostTable, Managed = GCHandle.ToIntPtr(managed) };
            commands.Add(Initialize);
            thread.Start();
        }
        catch
        {
            NativeMemory.Free((void*)host);
            if (managed.IsAllocated) managed.Free();
            commands.Dispose(); releaseModule(); throw;
        }
    }

    private static nint LoadModule(string path)
    {
        if (IntPtr.Size != 8 || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new DrmPlaybackException("Прямой Widevine interop поддерживает только 64-битные x64/ARM64 платформы.");
        return CdmModule.Load(path);
    }

    private void Initialize()
    {
        ReadOnlySpan<byte> system = "com.widevine.alpha"u8;
        fixed (byte* keySystem = system)
            cdm = ((delegate* unmanaged<int, byte*, uint, delegate* unmanaged<int, nint, nint>, nint, nint>)createInstance)
                (CdmAbi.Version, keySystem, (uint)system.Length, &CdmAbi.GetHost, host);
        if (cdm == 0) { Emit(1, 0, -2); return; }
        ((delegate* unmanaged<nint, byte, byte, byte, void>)CdmAbi.Method(cdm, CdmAbi.Initialize))(cdm, 0, 0, 0);
    }

    private void Run()
    {
        try
        {
            while (!commands.IsCompleted)
            {
                var now = Stopwatch.GetTimestamp();
                if (timers.TryPeek(out var context, out var due) && due <= now)
                {
                    timers.Dequeue();
                    if (cdm != 0) ((delegate* unmanaged<nint, nint, void>)CdmAbi.Method(cdm, CdmAbi.TimerExpired))(cdm, context);
                    continue;
                }
                var wait = timers.TryPeek(out _, out due) ? Math.Clamp((due - now) * 1000 / Stopwatch.Frequency, 1, 1000) : 1000;
                if (commands.TryTake(out var action, (int)wait))
                {
                    try { action(); } catch { Emit(4, 0, -1); }
                }
            }
        }
        finally
        {
            if (cdm != 0) { ((delegate* unmanaged<nint, void>)CdmAbi.Method(cdm, CdmAbi.Destroy))(cdm); cdm = 0; }
            foreach (var buffer in buffers.ToArray()) FreeBuffer(buffer);
            CryptographicOperations.ZeroMemory(Session);
            while (events.TryDequeue(out var message)) CryptographicOperations.ZeroMemory(message.Payload);
            NativeMemory.Free((void*)host); host = 0; managed.Free();
            releaseModule();
        }
    }

    private void Post(Action action)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        commands.Add(action);
    }

    private T Invoke<T>(Func<T> action)
    {
        var completed = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => { try { completed.SetResult(action()); } catch (Exception error) { completed.SetException(error); } });
        return completed.Task.GetAwaiter().GetResult();
    }

    public bool TryPoll(out CdmEvent message) => events.TryDequeue(out message!);
    internal void Emit(int kind, uint promise, int code, byte[]? payload = null)
    {
        if (events.Count < 256) events.Enqueue(new(kind, promise, code, payload ?? []));
        else if (payload != null) CryptographicOperations.ZeroMemory(payload);
    }

    public void Begin(uint promise, byte[] initData)
    {
        if (initData.Length is < 1 or > 65536) throw new DrmPlaybackException("Некорректные данные инициализации Widevine.");
        var bytes = initData.ToArray();
        Post(() =>
        {
            if (cdm == 0) { Emit(4, promise, -2); return; }
            fixed (byte* init = bytes)
                ((delegate* unmanaged<nint, uint, uint, uint, byte*, uint, void>)CdmAbi.Method(cdm, CdmAbi.CreateSession))
                    (cdm, promise, 0, 0, init, (uint)bytes.Length);
        });
    }

    public void Update(uint promise, byte[] license)
    {
        if (license.Length is < 1 or > 2 * 1024 * 1024) throw new DrmPlaybackException("Слишком большой или пустой ответ лицензии Widevine.");
        // LicenseAsync clears the HTTP array immediately after enqueueing.
        var bytes = license.ToArray();
        Post(() =>
        {
            try
            {
                if (cdm == 0 || Session.Length == 0) { Emit(4, promise, -3); return; }
                fixed (byte* session = Session, response = bytes)
                    ((delegate* unmanaged<nint, uint, byte*, uint, byte*, uint, void>)CdmAbi.Method(cdm, CdmAbi.UpdateSession))
                        (cdm, promise, session, (uint)Session.Length, response, (uint)bytes.Length);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        });
    }

    public byte[] Decrypt(byte[] data, byte[] kid, byte[] iv, CencSubsample[] subsamples) => Invoke(() =>
    {
        if (cdm == 0 || data.Length is < 1 or > 32 * 1024 * 1024 || kid.Length != 16 || iv.Length is not (8 or 16) || subsamples.Length > 65536 ||
            subsamples.Sum(sample => (long)sample.ClearBytes + sample.CipherBytes) != data.Length)
            throw new DrmPlaybackException("Некорректные параметры CENC-сэмпла.");
        var block = (CdmAbi.BlockObject*)NativeMemory.AllocZeroed((nuint)sizeof(CdmAbi.BlockObject));
        block->Vtable = CdmAbi.BlockTable;
        try
        {
            fixed (byte* audio = data, keyId = kid, vector = iv)
            fixed (CencSubsample* samples = subsamples)
            {
                var input = new CdmAbi.InputBuffer { Data = audio, DataSize = (uint)data.Length, EncryptionScheme = 1,
                    KeyId = keyId, KeyIdSize = (uint)kid.Length, Iv = vector, IvSize = (uint)iv.Length,
                    Subsamples = samples, SubsampleCount = (uint)subsamples.Length };
                var status = ((delegate* unmanaged<nint, CdmAbi.InputBuffer*, nint, uint>)CdmAbi.Method(cdm, CdmAbi.Decrypt))(cdm, &input, (nint)block);
                if (status != 0 || block->Buffer == 0)
                    throw new DrmPlaybackException(status == 2 ? "Widevine не предоставил разрешённый ключ для этого аудиосэмпла." : "Widevine отклонил расшифровку аудио.");
            }
            var size = ((delegate* unmanaged<nint, uint>)CdmAbi.Method(block->Buffer, 4))(block->Buffer);
            var output = ((delegate* unmanaged<nint, nint>)CdmAbi.Method(block->Buffer, 2))(block->Buffer);
            if (size != data.Length || output == 0) throw new DrmPlaybackException("Widevine вернул некорректный аудиобуфер.");
            return new ReadOnlySpan<byte>((void*)output, (int)size).ToArray();
        }
        finally
        {
            if (block->Buffer != 0) ((delegate* unmanaged<nint, void>)CdmAbi.Method(block->Buffer, 0))(block->Buffer);
            NativeMemory.Free(block);
        }
    });

    internal nint Allocate(uint capacity)
    {
        if (capacity > 32 * 1024 * 1024 || buffers.Count >= 4096) return 0;
        var value = (CdmAbi.BufferObject*)NativeMemory.AllocZeroed((nuint)sizeof(CdmAbi.BufferObject));
        try
        {
            *value = new() { Vtable = CdmAbi.BufferTable, Owner = host, Data = (byte*)NativeMemory.Alloc(capacity), Capacity = capacity };
            buffers.Add((nint)value); return (nint)value;
        }
        catch { if (value->Data != null) NativeMemory.Free(value->Data); NativeMemory.Free(value); throw; }
    }

    internal void FreeBuffer(nint buffer)
    {
        if (!buffers.Remove(buffer)) return;
        var value = (CdmAbi.BufferObject*)buffer;
        if (value->Data != null) { NativeMemory.Clear(value->Data, value->Capacity); NativeMemory.Free(value->Data); }
        NativeMemory.Free(value);
    }

    internal void SetTimer(long delay, nint context)
    {
        if (timers.Count < 1024) timers.Enqueue(context, Stopwatch.GetTimestamp() + Math.Clamp(delay, 0, 86400000) * Stopwatch.Frequency / 1000);
    }

    internal void RejectPlatformChallenge() => Post(() =>
    {
        // 48-byte PlatformChallengeResponse with no signed data/certificate.
        var response = stackalloc byte[48]; new Span<byte>(response, 48).Clear();
        if (cdm != 0) ((delegate* unmanaged<nint, byte*, void>)CdmAbi.Method(cdm, CdmAbi.PlatformChallengeResponse))(cdm, response);
    });
    internal void ReportUnverifiedOutput() => Post(() =>
    {
        if (cdm != 0) ((delegate* unmanaged<nint, uint, uint, uint, void>)CdmAbi.Method(cdm, CdmAbi.OutputProtectionStatus))(cdm, 1, 0, 0);
    });
    internal void ReportNoStorage(uint version) => Post(() =>
    {
        if (cdm != 0) ((delegate* unmanaged<nint, uint, nint, uint, void>)CdmAbi.Method(cdm, CdmAbi.StorageId))(cdm, version, 0, 0);
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        commands.CompleteAdding();
        thread.Join(); commands.Dispose();
    }
}

internal static unsafe class CdmModule
{
    private static readonly object gate = new();
    private static nint library, create;
    private static string? loadedPath;
    private static int instances;

    public static nint Load(string path)
    {
        path = Path.GetFullPath(path);
        lock (gate)
        {
            if (library == 0)
            {
                var loaded = NativeLibrary.Load(Path.GetFullPath(path));
                try
                {
                    var initialize = NativeLibrary.GetExport(loaded, "InitializeCdmModule_4");
                    create = NativeLibrary.GetExport(loaded, "CreateCdmInstance");
                    ((delegate* unmanaged<void>)initialize)();
                    library = loaded; loadedPath = path;
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => UnloadAtExit();
                }
                catch { NativeLibrary.Free(loaded); throw; }
            }
            else if (loadedPath != path) throw new DrmPlaybackException("Для смены версии Widevine перезапусти Pookie.");
            instances++; return create;
        }
    }
    public static void ReleaseInstance() { lock (gate) instances--; }
    private static void UnloadAtExit()
    {
        lock (gate)
        {
            if (instances != 0 || library == 0) return;
            if (NativeLibrary.TryGetExport(library, "DeinitializeCdmModule", out var deinitialize)) ((delegate* unmanaged<void>)deinitialize)();
            NativeLibrary.Free(library); library = 0;
        }
    }
}
