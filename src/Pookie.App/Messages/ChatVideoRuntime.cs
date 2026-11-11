using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Pookie.Logging;

namespace Pookie.App.Messages;

internal static class ChatVideoRuntime
{
    private static bool initialized;
    private static bool resolverInstalled;
    private static nint library;

    // Called under ChatVideoSession's initialization lock, before any VLC P/Invoke.
    internal static void Initialize()
    {
        if (initialized) return;
        var directory = Path.Combine(AppContext.BaseDirectory, "libvlc");
        if (OperatingSystem.IsLinux() && Directory.Exists(directory))
        {
            var path = Path.Combine(directory, "libvlc.so.5");
            // Each bundled ELF has an origin-relative RUNPATH, including plugins.
            // Loading by absolute path also works when Pookie is started directly,
            // without a launcher script or a process-wide LD_LIBRARY_PATH.
            if (!resolverInstalled)
            {
                library = NativeLibrary.Load(path);
                RegisterResolver();
            }
            AppLog.For("Pookie.Video").Information("Видеодвижок: libVLC из {RuntimeDirectory}", directory);
        }
        if (OperatingSystem.IsMacOS())
        {
            var rid = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                Architecture.X64 => "osx-x64",
                var architecture => throw new PlatformNotSupportedException($"Unsupported macOS video architecture: {architecture}")
            };
            directory = Path.Combine(directory, rid, "lib");
            if (!resolverInstalled)
            {
                // Load the core first: libvlc and its plugins refer to its
                // @rpath install name. Keep both native handles for process life.
                NativeLibrary.Load(Path.Combine(directory, "libvlccore.dylib"));
                library = NativeLibrary.Load(Path.Combine(directory, "libvlc.dylib"));
                RegisterResolver();
            }
            Core.Initialize(directory);
            AppLog.For("Pookie.Video").Information("Видеодвижок: libVLC из {RuntimeDirectory}", directory);
        }
        else Core.Initialize();
        initialized = true;
    }

    private static void RegisterResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibVLC).Assembly, (name, _, _) =>
            name is "libvlc" or "libvlc.so" or "libvlc.so.5" or "libvlc.dylib" ? library : nint.Zero);
        resolverInstalled = true;
    }

    // Video input is an in-memory attachment. Keep its original exception details
    // here; AppLog.Failure deliberately omits messages for network/account errors.
    internal static void LogFailure(Exception error, bool preview = false)
    {
        AppLog.For("Pookie.Video").ForContext("StackTrace", error.ToString()).Write(
            preview ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Error,
            "{Action}: {ErrorType}; {Reason}", preview ? "Не удалось подготовить кадр видео" : "Не удалось открыть видео",
            error.GetType().FullName, error.GetBaseException().Message);
    }
}
