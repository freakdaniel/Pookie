using System.Diagnostics;
using System.Text;
using Pookie.App.Auth;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class SessionVaultSmokeTest
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This check verifies Windows DPAPI.");
        var root = Directory.CreateTempSubdirectory("pookie-vault-test-").FullName;
        try
        {
            var paths = new AppDataPaths(root);
            using (var saved = SessionVault.Open(paths)) saved.Save(FixtureSession());
            var file = Path.Combine(paths.Config, "soundcloud-session.bin");
            var bytes = File.ReadAllBytes(file);
            if (Encoding.UTF8.GetString(bytes).Contains("fixture-private-token", StringComparison.Ordinal))
                throw new InvalidOperationException("Session was saved as plaintext.");
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(SessionVaultSmokeTest).Assembly.Location);
            start.ArgumentList.Add("--session-vault-child"); start.ArgumentList.Add(root);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Session reload process did not start.");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
            if (child.ExitCode != 0) throw new InvalidOperationException("Cross-process session reload failed: " + await stderr);
            if (!(await stdout).Contains("SESSION_VAULT_CHILD_OK", StringComparison.Ordinal))
                throw new InvalidOperationException("Session reload was not confirmed.");
            using var reopened = SessionVault.Open(paths);
            if (reopened.Load()?.DataDomeClientId != "fixture-rotated") throw new InvalidOperationException("Session rotation was not persisted.");
            bytes = File.ReadAllBytes(file);
            bytes[^1] ^= 1;
            File.WriteAllBytes(file, bytes);
            ExpectRejected(reopened);
            File.WriteAllBytes(file, new byte[WindowsSessionVault.MaxSize + 1]);
            ExpectRejected(reopened);
            reopened.Save(FixtureSession());
            reopened.Delete();
            if (reopened.Load() != null || File.Exists(file)) throw new InvalidOperationException("Logout retained the saved session.");
            Console.WriteLine("SESSION_VAULT_OK: encrypted Windows storage, reload in a new process, rotation, tampering/size rejection and logout deletion");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void RunChild(string root)
    {
        using var vault = SessionVault.Open(new AppDataPaths(root));
        if (vault.Load() != FixtureSession()) throw new InvalidOperationException("Session fields changed across restart.");
        vault.Save(FixtureSession() with { DataDomeClientId = "fixture-rotated" });
        Console.WriteLine("SESSION_VAULT_CHILD_OK");
    }

    private static WebSession FixtureSession() => new("fixtureid", "fixture-private-token", "fixture-engine")
        { DataDomeClientId = "fixture-initial", AppVersion = "fixture-version", AppLocale = "ru" };

    private static void ExpectRejected(ISessionVault vault)
    {
        try { vault.Load(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Damaged session data was accepted.");
    }
}
