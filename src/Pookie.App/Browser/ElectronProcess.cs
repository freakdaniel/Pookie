using System.Diagnostics;

namespace Pookie.App.Browser;

internal static class ElectronProcess
{
    public static ProcessStartInfo Create(string mode, string pipe, string profile, string? fixture)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "BrowserHost");
        var binary = Path.Combine(directory, "Pookie.Browser.Electron" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(binary)) throw new InvalidOperationException("В установке Pookie отсутствует браузерный процесс Electron. Пересобери приложение.");
        var runtime = Path.Combine(directory, ".electron", "node_modules", "electron", "dist",
            OperatingSystem.IsWindows() ? "electron.exe" : OperatingSystem.IsMacOS() ? "Electron.app/Contents/MacOS/Electron" : "electron");
        if (!File.Exists(runtime)) throw new InvalidOperationException("Не установлен Electron. Собери Pookie с -p:PookieInstallElectron=true (нужны Node.js и npm).");
        var start = new ProcessStartInfo(binary)
        {
            WorkingDirectory = directory, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-unpackeddotnet", mode, "--pipe", pipe, "--profile", profile }) start.ArgumentList.Add(arg);
        if (fixture != null) { start.ArgumentList.Add("--fixture"); start.ArgumentList.Add(fixture); }
        return start;
    }
}
