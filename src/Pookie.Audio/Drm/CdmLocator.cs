namespace Pookie.Audio;

internal static class CdmLocator
{
    // Discover the user's installed module; never copy or redistribute proprietary CDM binaries.
    public static string? Find()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();
        if (OperatingSystem.IsLinux())
        {
            var platform = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "linux_arm64" : "linux_x64";
            foreach (var root in new[] { "/opt/google/chrome", "/opt/brave.com/brave", "/usr/lib/chromium", "/usr/lib64/chromium" })
                candidates.Add(Path.Combine(root, $"WidevineCdm/_platform_specific/{platform}/libwidevinecdm.so"));
            foreach (var root in new[] { Path.Combine(home, ".config/google-chrome/WidevineCdm"),
                Path.Combine(home, ".config/BraveSoftware/Brave-Browser/WidevineCdm"), Path.Combine(home, ".mozilla/firefox") })
                AddUnder(candidates, root, "libwidevinecdm.so", 5);
        }
        else if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                foreach (var relative in new[] { "Google/Chrome/Application", "BraveSoftware/Brave-Browser/Application", "Mozilla Firefox", "Google/Chrome/User Data/WidevineCdm" })
                    AddUnder(candidates, Path.Combine(root, relative), "widevinecdm.dll", 5);
        }
        else if (OperatingSystem.IsMacOS())
            foreach (var root in new[] { "/Applications/Google Chrome.app/Contents/Frameworks", "/Applications/Brave Browser.app/Contents/Frameworks",
                Path.Combine(home, "Library/Application Support/Google/Chrome/WidevineCdm") })
                AddUnder(candidates, root, "libwidevinecdm.dylib", 7);
        return candidates.Where(File.Exists).FirstOrDefault();
    }

    private static void AddUnder(List<string> candidates, string root, string name, int depth)
    {
        if (depth == 0 || !Directory.Exists(root)) return;
        try
        {
            candidates.AddRange(Directory.EnumerateFiles(root, name));
            foreach (var child in Directory.EnumerateDirectories(root).OrderDescending()) AddUnder(candidates, child, name, depth - 1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
