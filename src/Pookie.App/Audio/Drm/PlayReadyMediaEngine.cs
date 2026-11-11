// SPDX-FileCopyrightText: Microsoft Corporation
// SPDX-FileCopyrightText: 2026 Daniel Freak
// SPDX-License-Identifier: BSD-3-Clause AND MIT
#if WINDOWS
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Pookie.Logging;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio;

internal sealed unsafe class PlayReadyMediaEngine(nint engine, PlayReadyEngineEvents events, PlayReadyVolumeTransform volumeEffect)
{
    private static readonly Guid EngineFactory = new("4d645ace-26aa-4688-9be1-df3516990b93");
    private static readonly Guid ProtectedContent = new("9f8021e8-9c8c-487e-bb5c-79aa4779938c");
    private bool boostRequested, boostWarningLogged;

    internal static Task RunAsync(HttpClient http, PlayReadyHlsStream media, string authorization, string store,
        Action<PlayReadyMediaEngine> playback, CancellationToken stop)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Own the COM MTA for the engine's entire lifetime. This keeps COM setup,
        // commands and teardown off the UI and out of Media Foundation callbacks.
        var worker = new Thread(() =>
        {
            try
            {
                Run(http, media, authorization, store, playback, stop);
                completion.SetResult();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { completion.SetCanceled(stop); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Pookie PlayReady" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        return completion.Task;
    }

    private static void Run(HttpClient http, PlayReadyHlsStream media, string authorization, string store,
        Action<PlayReadyMediaEngine> playback, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        const uint multithreaded = 0, mfVersion = 0x00020070, fullStartup = 0;
        Check(CoInitializeEx(0, multithreaded), "CoInitializeEx");
        try
        {
            Check(MFStartup(mfVersion, fullStartup), "MFStartup");
            try { RunSession(http, media, authorization, store, playback, stop); }
            finally { LogTeardown(MFShutdown(), "MFShutdown"); }
        }
        finally { CoUninitialize(); }
    }

    private static void RunSession(HttpClient http, PlayReadyHlsStream media, string authorization, string store,
        Action<PlayReadyMediaEngine> playback, CancellationToken stop)
    {
        using var factory = PlayReadyCdm.CreateFactory();
        // Assign after the CDM is created and release the agile reference after it.
        using var host = new ComReference(0);
        using var cdm = PlayReadyCdm.Create(factory.Value, store);
        using var services = PlayReadyCdm.Services(cdm.Value);
        using var configuredHost = PlayReadyCdm.SetHost(cdm.Value, services.Value);
        host.TakeFrom(configuredHost);
        var callbacks = new CdmSessionCallbacks();
        using var session = PlayReadyCdm.CreateSession(cdm.Value, callbacks);
        try
        {
            PlayReadyCdm.GenerateRequest(session.Value, PlayReadyLicense.Pssh(media.InitData));
            var challenge = callbacks.Challenge.Task.WaitAsync(TimeSpan.FromSeconds(15), stop).GetAwaiter().GetResult();
            byte[] license;
            try { license = PlayReadyLicense.AcquireAsync(http, authorization, challenge, stop).GetAwaiter().GetResult(); }
            finally { CryptographicOperations.ZeroMemory(challenge); }
            try { PlayReadyCdm.Update(session.Value, license); }
            finally { CryptographicOperations.ZeroMemory(license); }
            AppLog.For("Pookie.Audio.PlayReady").Debug("NATIVE_MF_PLAYREADY_LICENSE_ACCEPTED in process {ProcessId}, thread {ThreadId}",
                Environment.ProcessId, Environment.CurrentManagedThreadId);
            RunEngine(factory.Value, cdm.Value, services.Value, media, playback, stop);
        }
        finally { LogTeardown(Call(session.Value, 9), "IMFContentDecryptionModuleSession.Close"); }
    }

    private static void RunEngine(nint factory4, nint cdm, nint services, PlayReadyHlsStream media,
        Action<PlayReadyMediaEngine> playback, CancellationToken stop)
    {
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(stop);
        using var source = OpenSource(media, reads.Token);
        using var wrapped = new PlayReadyMediaSource(source.Value, cdm);
        // The generated, agile COM wrapper works across Media Foundation's
        // apartments without the runtime's built-in COM marshaller.
        using var exportedSource = Export<IProtectedMediaSource>(wrapped);
        using var extension = Export<IMediaEngineExtension>(new PlayReadySourceExtension(exportedSource.Value));
        using var protection = Export<IContentProtectionManager>(new PlayReadyProtectionManager(cdm, services));
        var events = new PlayReadyEngineEvents();
        using var notify = Export<IMediaEngineNotify>(events);
        Check(MFCreateAttributes(out var attributesPtr, 4), "MFCreateAttributes (Media Engine)");
        using var attributes = new ComReference(attributesPtr);
        SetAttribute(attributesPtr, new("c60381b8-83a4-41f8-a3d0-de05076849a9"), notify.Value);
        SetAttribute(attributesPtr, new("e0350223-5aaf-4d76-a7c3-06de70894db4"), 1u); // Enable protected content.
        SetAttribute(attributesPtr, new("4e0212e2-e18f-41e1-95e5-c0e7e9235bc3"), new Guid("a6f3e465-3aca-442c-a3f0-ad6ddad839ae"));
        SetAttribute(attributesPtr, new("3109fd46-060d-4b62-8dcf-faff811318d2"), extension.Value);
        using var factory = Query(factory4, EngineFactory, "IMFMediaEngineClassFactory");
        nint enginePtr;
        const uint audioOnly = 1;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(factory.Value, 3))(
            factory.Value, audioOnly, attributesPtr, &enginePtr), "IMFMediaEngineClassFactory.CreateInstance");
        using var engine = new ComReference(enginePtr);
        using var volumeEffect = new PlayReadyVolumeTransform();
        using var exportedVolume = Export<IVolumeTransform>(volumeEffect);
        try
        {
            using var extendedEngine = Query(enginePtr, new("83015ead-b1e6-40d0-a98a-37145ffe1ad1"), "IMFMediaEngineEx");
            // Protected output policies may reject application effects. Keep
            // playback available when the native protected path cannot use it.
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int>)Slot(extendedEngine.Value, 61))(
                extendedEngine.Value, exportedVolume.Value, 1), "IMFMediaEngineEx.InsertAudioEffect");
            using var protectedEngine = Query(enginePtr, ProtectedContent, "IMFMediaEngineProtectedContent");
            Check(Call(protectedEngine.Value, 7, protection.Value), "SetContentProtectionManager");
            var name = Marshal.StringToBSTR("customSrc");
            try { Check(Call(enginePtr, 6, name), "IMFMediaEngine.SetSource"); }
            finally { Marshal.FreeBSTR(name); }
            playback(new(enginePtr, events, volumeEffect));
        }
        finally
        {
            reads.Cancel();
            LogTeardown(Call(enginePtr, 42), "IMFMediaEngine.Shutdown");
        }
    }

    private static ComReference OpenSource(PlayReadyHlsStream media, CancellationToken stop)
    {
        using var stream = Export<IEncryptedMediaStream>(new PlayReadyMediaStream(media, stop));
        Check(MFCreateMFByteStreamOnStream(stream.Value, out var byteStreamPtr), "MFCreateMFByteStreamOnStream");
        using var byteStream = new ComReference(byteStreamPtr);
        Check(MFCreateSourceResolver(out var resolverPtr), "MFCreateSourceResolver");
        using var resolver = new ComReference(resolverPtr);
        int type;
        nint sourcePtr;
        const uint mediaSource = 1;
        fixed (char* name = "track.mp4")
            Check(((delegate* unmanaged[Stdcall]<nint, nint, char*, uint, nint, int*, nint*, int>)Slot(resolverPtr, 4))(
                resolverPtr, byteStreamPtr, name, mediaSource, 0, &type, &sourcePtr), "CreateObjectFromByteStream (encrypted fMP4)");
        using var source = new ComReference(sourcePtr);
        return Query(sourcePtr, Source, "IMFMediaSource");
    }

    internal Exception? Failure => events.Failure;
    internal double Position => ((delegate* unmanaged[Stdcall]<nint, double>)Slot(engine, 16))(engine); // GetCurrentTime
    internal ushort ReadyState => ((delegate* unmanaged[Stdcall]<nint, ushort>)Slot(engine, 14))(engine); // GetReadyState
    internal bool Seeking => Call(engine, 15) != 0; // IsSeeking
    internal bool Paused => Call(engine, 20) != 0; // IsPaused
    internal bool Ended => Call(engine, 27) != 0; // IsEnded
    internal bool HasAudio => Call(engine, 39) != 0; // HasAudio

    internal void Pause(bool paused)
    {
        if (paused) Check(Call(engine, 33), "IMFMediaEngine.Pause");
        else Check(Call(engine, 32), "IMFMediaEngine.Play");
    }

    internal void Volume(double value)
    {
        boostRequested = value > 1;
        volumeEffect.Gain = (float)Math.Max(1, value);
        Check(((delegate* unmanaged[Stdcall]<nint, double, int>)Slot(engine, 37))(engine, Math.Clamp(value, 0, 1)), "IMFMediaEngine.SetVolume");
    }

    internal void ReportVolumeSupport()
    {
        if (!boostRequested || boostWarningLogged || volumeEffect.IsProcessing) return;
        boostWarningLogged = true;
        AppLog.For("Pookie.Audio.PlayReady").Warning("Защищённый аудиотракт Windows не применил эффект усиления; громкость этого потока ограничена 100%");
    }

    internal void Seek(double seconds) =>
        Check(((delegate* unmanaged[Stdcall]<nint, double, int>)Slot(engine, 17))(engine, seconds), "IMFMediaEngine.SetCurrentTime");

    private static void LogTeardown(int hr, string operation)
    {
        if (hr < 0) AppLog.For("Pookie.Audio.PlayReady").Debug("{Operation} (0x{HResult:X8})", operation, hr);
    }
}
#endif
