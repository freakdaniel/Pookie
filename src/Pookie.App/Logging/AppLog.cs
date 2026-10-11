using System.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Pookie.Logging;

public sealed record LoggingOptions(LogEventLevel? ConsoleMinimum = LogEventLevel.Information,
    LogEventLevel? FileMinimum = LogEventLevel.Debug);

// Shared by the application and audio library. Callers log fixed actions and safe
// metadata, never session objects, URLs with queries, response bodies or exception messages.
public static class AppLog
{
    private static readonly object gate = new();
    public static bool IsInitialized { get; private set; }
    public static string? DirectoryPath { get; private set; }
    public static ILogger For(string category) => Log.ForContext("SourceContext", category);

    public static void Initialize(string dataRoot, LoggingOptions? options = null, bool worker = false)
    {
        lock (gate)
        {
            if (IsInitialized) return;
            var invalid = new List<string>();
            options ??= new(ReadLevel("POOKIE_LOG_CONSOLE_LEVEL", LogEventLevel.Information, invalid),
                ReadLevel("POOKIE_LOG_FILE_LEVEL", LogEventLevel.Debug, invalid));
            if (worker) options = options with { ConsoleMinimum = null };
            var configuration = new LoggerConfiguration().MinimumLevel.Verbose()
                .Enrich.WithProperty("ProcessId", Environment.ProcessId);
            if (options.ConsoleMinimum is { } console)
                configuration.WriteTo.Console(restrictedToMinimumLevel: console,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}",
                    standardErrorFromLevel: LogEventLevel.Warning);
            Exception? fileError = null;
            DirectoryPath = Path.GetFullPath(Environment.GetEnvironmentVariable("POOKIE_LOG_DIRECTORY") ?? Path.Combine(dataRoot, "Logs"));
            if (options.FileMinimum is { } file)
            {
                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    configuration.WriteTo.File(new JsonFormatter(renderMessage: true), Path.Combine(DirectoryPath, "pookie-.log"),
                        restrictedToMinimumLevel: file, rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: 8 * 1024 * 1024, rollOnFileSizeLimit: true,
                        retainedFileCountLimit: 14, shared: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { fileError = error; }
            }
            Log.Logger = configuration.CreateLogger();
            IsInitialized = true;
            foreach (var setting in invalid)
                For("Pookie.Logging").Warning("Неизвестный уровень в {Setting}; используется значение по умолчанию", setting);
            if (fileError != null) Failure("Pookie.Logging", "Запись журнала в файл недоступна", fileError, LogEventLevel.Warning);
            if (!worker) For("Pookie").Information("Логи: {LogDirectory}; уровень консоли: {ConsoleLevel}",
                DirectoryPath, options.ConsoleMinimum?.ToString() ?? "None");
        }
    }

    public static void ConfigureChild(ProcessStartInfo start)
    {
        if (DirectoryPath != null) start.Environment["POOKIE_LOG_DIRECTORY"] = DirectoryPath;
        start.Environment["POOKIE_LOG_CONSOLE_LEVEL"] = "None";
    }

    public static void Failure(string category, string action, Exception error, LogEventLevel level = LogEventLevel.Error, int? httpStatus = null) =>
        For(category).ForContext("ErrorType", error.GetType().Name).ForContext("HttpStatus", httpStatus)
            .ForContext("StackTrace", error.StackTrace)
            .Write(level, "{Action}: {ErrorType}; HTTP {HttpStatus}", action, error.GetType().Name, httpStatus);

    public static void Shutdown()
    {
        lock (gate)
        {
            Log.CloseAndFlush();
            IsInitialized = false;
            DirectoryPath = null;
        }
    }

    private static LogEventLevel? ReadLevel(string name, LogEventLevel fallback, List<string> invalid)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value)) return fallback;
        switch (value)
        {
            case "TRACE": case "VERBOSE": return LogEventLevel.Verbose;
            case "DEBUG": return LogEventLevel.Debug;
            case "INFO": case "INFORMATION": return LogEventLevel.Information;
            case "WARN": case "WARNING": return LogEventLevel.Warning;
            case "ERROR": return LogEventLevel.Error;
            case "FATAL": case "CRITICAL": return LogEventLevel.Fatal;
            case "NONE": case "OFF": return null;
            default: invalid.Add(name); return fallback;
        }
    }
}
