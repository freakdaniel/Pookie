using System.Diagnostics;
using Pookie.App.Platform;

namespace Pookie.App.Diagnostics;

internal static class ExternalBrowserSmokeTest
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) { Console.WriteLine("EXTERNAL_BROWSER_SKIP: Linux desktop launcher check"); return; }
        var root = Directory.CreateTempSubdirectory("pookie-external-browser-").FullName;
        try
        {
            var opener = Path.Combine(root, "xdg-open");
            await File.WriteAllTextAsync(opener, """
                #!/bin/sh
                printf '%s' "$1" > "$POOKIE_TEST_URL"
                printf 'NOISY_BROWSER_STDOUT\n'
                printf 'NOISY_BROWSER_STDERR\n' >&2
                if [ "$POOKIE_TEST_FAILURE" = 1 ]; then exit 4; fi
                (sleep .3; printf 'LATE_BROWSER_STDOUT\n'; printf 'LATE_BROWSER_STDERR\n' >&2; printf alive > "$POOKIE_TEST_ALIVE") &
                exit 0
                """);
            File.SetUnixFileMode(opener, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var value = new Uri("https://example.com/path?q=$HOME&command=$(touch%20injected)&tick=`id`&quote='hello'#fragment");
            foreach (var fail in new[] { false, true })
            {
                var urlFile = Path.Combine(root, fail ? "failure-url" : "url");
                var alive = Path.Combine(root, "browser-alive");
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(ExternalBrowserSmokeTest).Assembly.Location);
                start.ArgumentList.Add("--external-browser-child"); start.ArgumentList.Add(value.AbsoluteUri);
                start.Environment["PATH"] = root + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                start.Environment["POOKIE_TEST_URL"] = urlFile;
                start.Environment["POOKIE_TEST_ALIVE"] = alive;
                start.Environment["POOKIE_TEST_FAILURE"] = fail ? "1" : "0";
                using var child = Process.Start(start)!;
                var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                var output = await stdout.WaitAsync(TimeSpan.FromSeconds(3));
                var errors = await stderr.WaitAsync(TimeSpan.FromSeconds(3));
                if (child.ExitCode != (fail ? 1 : 0) || output.Contains("BROWSER_", StringComparison.Ordinal) || errors.Contains("BROWSER_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Browser output leaked into Pookie or the desktop opener's failure was lost.");
                if (await File.ReadAllTextAsync(urlFile) != value.AbsoluteUri)
                    throw new InvalidOperationException("The desktop opener did not receive the exact URL as one literal argument.");
                if (fail && !errors.Contains("кодом 4", StringComparison.Ordinal))
                    throw new InvalidOperationException("The desktop opener's error code was not reported.");
                if (!fail)
                {
                    for (var i = 0; i < 60 && !File.Exists(alive); i++) await Task.Delay(50);
                    if (!File.Exists(alive)) throw new InvalidOperationException("The browser child did not outlive Pookie's launcher.");
                }
            }
            try { await ExternalBrowser.OpenAsync("javascript:alert(1)", CancellationToken.None); throw new InvalidOperationException("Non-web URI was accepted."); }
            catch (ArgumentException) { }
            Console.WriteLine("EXTERNAL_BROWSER_OK: system opener, literal URL, isolated stdout/stderr including browser descendants, browser lifetime and failure exit code");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
