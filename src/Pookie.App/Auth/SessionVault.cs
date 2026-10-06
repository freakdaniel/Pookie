using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

internal interface ISessionVault : IDisposable
{
    WebSession? Load();
    void Save(WebSession session);
    void Delete();
}

internal static class SessionVault
{
    public static ISessionVault Open(AppDataPaths paths) => OperatingSystem.IsWindows()
        ? new WindowsSessionVault(paths) : new LinuxSessionVault();
}
