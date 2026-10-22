using System.Diagnostics;

namespace Pookie.App.Platform;

internal static class ExternalBrowser
{
    internal static async Task OpenAsync(string value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0)
            throw new ArgumentException("Некорректная ссылка", nameof(value));
        var start = OperatingSystem.IsLinux() ? LinuxStartInfo(uri) :
            new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить системный браузер");
        if (!OperatingSystem.IsLinux()) return;
        // Cancellation only releases our wait. The user's browser must outlive Pookie.
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Системный обработчик ссылок завершился с кодом {process.ExitCode}");
    }

    internal static ProcessStartInfo LinuxStartInfo(Uri uri)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-c");
        // Use the desktop's selected browser. Give it /dev/null rather than
        // Pookie's terminal or pipes that a long-lived browser could keep open.
        // The URL is a separate positional argument, never shell source.
        start.ArgumentList.Add("exec xdg-open \"$1\" </dev/null >/dev/null 2>&1");
        start.ArgumentList.Add("pookie-open");
        start.ArgumentList.Add(uri.AbsoluteUri);
        return start;
    }
}
