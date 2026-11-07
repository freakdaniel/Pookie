using System.Diagnostics;
using Serilog;
using Serilog.Events;
using System.Globalization;
using Serilog.Formatting;
using Serilog.Formatting.Display;

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
                    var fileLogger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.File(new ReadableLogFormatter(), Path.Combine(DirectoryPath, "pookie-text-.log"),
                        restrictedToMinimumLevel: file, rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: 8 * 1024 * 1024, rollOnFileSizeLimit: true,
                        retainedFileCountLimit: 14, shared: true).CreateLogger();
                    configuration.WriteTo.Sink(new BufferedFileSink(fileLogger), restrictedToMinimumLevel: file);
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

    public static void Failure(string category, string action, Exception error, LogEventLevel level = LogEventLevel.Error, int? httpStatus = null)
    {
        var root = error.GetBaseException();
        var rootOperation = root.TargetSite == null ? null : root.TargetSite.DeclaringType?.Name + "." + root.TargetSite.Name;
        httpStatus ??= root is HttpRequestException { StatusCode: { } status } ? (int)status : null;
        For(category).ForContext("ErrorType", error.GetType().Name).ForContext("HttpStatus", httpStatus)
            .ForContext("StackTrace", error.StackTrace).ForContext("RootStackTrace", root.StackTrace)
            .Write(level, "{Action}: {ErrorType}; причина {RootErrorType} ({RootOperation}); HTTP {HttpStatus}",
                action, error.GetType().Name, root.GetType().Name, rootOperation, httpStatus);
    }

    public static void Shutdown()
    {
        lock (gate)
        {
            Log.CloseAndFlush();
            IsInitialized = false;
            DirectoryPath = null;
        }
    }

    // A separate filename keeps new text records out of an older process's JSON log.
    // Exception messages are intentionally omitted; Failure supplies safe stacks separately.
    private sealed class ReadableLogFormatter : ITextFormatter
    {
        private readonly MessageTemplateTextFormatter message = new(
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] [pid={ProcessId}] {Message:lj}{NewLine}",
            CultureInfo.InvariantCulture);

        public void Format(LogEvent logEvent, TextWriter output)
        {
            message.Format(logEvent, output);
            var stack = Stack("StackTrace");
            var rootStack = Stack("RootStackTrace");
            WriteStack(stack);
            if (rootStack != stack) WriteStack(rootStack);

            string? Stack(string key) => logEvent.Properties.TryGetValue(key, out var value) &&
                value is ScalarValue { Value: string text } ? text : null;
            void WriteStack(string? text)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                foreach (var line in text.Split('\n')) output.WriteLine("    " + line.TrimEnd('\r'));
            }
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
