using Pookie.App.Storage;
using Pookie.SoundCloud;
using System.Security.Cryptography;
using System.Text;

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
        ? new WindowsSessionVault(paths) : new LinuxSessionVault(Scope(paths));

    internal static string Scope(AppDataPaths paths) => paths.Root == Path.GetFullPath(AppDataPaths.DefaultRoot()) ? "pookie" :
        "pookie-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root)))[..16];
}
