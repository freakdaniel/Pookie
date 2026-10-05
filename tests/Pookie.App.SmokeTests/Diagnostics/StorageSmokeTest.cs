using Pookie.App.Auth;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class StorageSmokeTest
{
    public static async Task RunAsync()
    {
        var root = Directory.CreateTempSubdirectory("pookie-storage-test-").FullName;
        try
        {
            var paths = new AppDataPaths(root);
            if (!Directory.Exists(paths.Downloads) || !Directory.Exists(paths.Cache) || !Directory.Exists(paths.Config)) throw new InvalidOperationException("Data directories not created");
            if (AppDataPaths.ResolveRoot("linux", "/home/test", "/ignored", "/xdg") != Path.Combine("/xdg", "Pookie") ||
                AppDataPaths.ResolveRoot("linux", "/home/test", "/ignored", "relative") != Path.Combine("/home/test", ".local", "share", "Pookie") ||
                AppDataPaths.ResolveRoot("macos", "/Users/test", "/ignored", null) != Path.Combine("/Users/test", "Library", "Application Support", "Pookie") ||
                AppDataPaths.ResolveRoot("windows", "/ignored", "/LocalAppData", null) != Path.Combine("/LocalAppData", "Pookie"))
                throw new InvalidOperationException("Platform-specific path resolution failed");
            var store = new ConfigurationStore(paths);
            var saved = new AppConfiguration(34, 86, false, true, true);
            store.Save(saved);
            if (new ConfigurationStore(paths).Load() != saved) throw new InvalidOperationException("Settings did not survive reopening");
            File.WriteAllText(Path.Combine(paths.Config, "settings.json"), "invalid-json");
            if (store.Load() != new AppConfiguration()) throw new InvalidOperationException("Corrupt configuration did not fall back to defaults");
            var cache = new ImageDiskCache(paths);
            byte[] payload = [1, 2, 3];
            await cache.WriteAsync("https://i1.sndcdn.com/fixture.jpg", payload, default);
            if (!(await new ImageDiskCache(paths).ReadAsync("https://i1.sndcdn.com/fixture.jpg", default))!.SequenceEqual(payload))
                throw new InvalidOperationException("Disk cache did not survive reopening");
            File.SetLastWriteTimeUtc(Directory.GetFiles(paths.Images).Single(), DateTime.UtcNow.AddDays(-31));
            cache.Prune();
            if (Directory.GetFiles(paths.Images).Length != 0) throw new InvalidOperationException("Expired cache retained");
            var waveforms = new WaveformDiskCache(paths);
            var waveUrl = "https://wave.sndcdn.com/fixture_m.json";
            byte[] wavePayload = System.Text.Encoding.UTF8.GetBytes("{\"height\":140,\"samples\":[0,70,140]}");
            await waveforms.WriteAsync(waveUrl, wavePayload, default);
            if (!(await new WaveformDiskCache(paths).ReadAsync(waveUrl, default))!.SequenceEqual(wavePayload))
                throw new InvalidOperationException("Waveform cache did not survive reopening");
            File.SetLastWriteTimeUtc(Directory.GetFiles(paths.Waveforms).Single(), DateTime.UtcNow.AddDays(-31));
            if (await waveforms.ReadAsync(waveUrl, default) != null) throw new InvalidOperationException("Expired waveform cache reused");
            await waveforms.WriteAsync("oversized", new byte[WaveformDiskCache.MaxEntrySize + 1], default);
            if (await waveforms.ReadAsync("oversized", default) != null) throw new InvalidOperationException("Unbounded waveform cache accepted");
            var profilePath = Path.Combine(paths.Browser, "WebKit");
            using (var profile = BrowserProfile.Open(path: profilePath))
            {
                try { using var second = BrowserProfile.Open(path: profilePath); throw new IOException("Concurrent profile accepted"); }
                catch (InvalidOperationException) { }
                File.WriteAllText(Path.Combine(profilePath, "fixture"), "data");
                var account = new WebSession("fixtureid", "private-oauth", "fixture-engine");
                var protection = new BrowserProtectionStore(profilePath);
                protection.Save(account, "rotated-protection");
                protection.Restore(account);
                if (account.DataDomeClientId != "rotated-protection" || File.ReadAllText(Path.Combine(profilePath, "protection.json")).Contains("private-oauth"))
                    throw new InvalidOperationException("Protection fallback lost rotation or stored OAuth in plain text");
                var other = new WebSession("fixtureid", "another-oauth", "fixture-engine");
                protection.Restore(other);
                if (other.DataDomeClientId != null) throw new InvalidOperationException("Protection session leaked to another account");
            }
            if (!File.Exists(Path.Combine(profilePath, "fixture"))) throw new InvalidOperationException("Persistent profile was deleted");
            File.WriteAllText(Path.Combine(paths.Downloads, "keep-fixture"), "download");
            BrowserProfile.Clear(paths);
            if (File.Exists(Path.Combine(profilePath, "fixture")) || !File.Exists(Path.Combine(paths.Downloads, "keep-fixture")) ||
                !File.Exists(Path.Combine(paths.Config, "settings.json"))) throw new InvalidOperationException("Logout cleared unrelated data or retained the browser profile");
            Console.WriteLine("STORAGE_SMOKE_OK: platform paths, settings roundtrip, corrupt JSON fallback, persistent/expired cache exclusive browser profile and logout preserving settings/downloads");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
