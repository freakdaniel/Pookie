namespace Pookie.App.Browser;

// Run before the async application host so native browser creation retains the
// entry thread's STA apartment. Both app and diagnostics use the same dispatch.
internal static class BrowserChildHost
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 2;
        if (args.Length == 0 || args[0] is not ("--web-login" or "--web-requests")) return false;
        if (args.Length < 4 || args[2] != "--login-profile") return true;
        string? fixture = null;
        var hidden = false;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--login-fixture" && i + 1 < args.Length) fixture = args[++i];
            else if (args[i] == "--restore-session") hidden = true;
            else return true;
        }
        exitCode = args[0] == "--web-requests" ? NativeBrowserSession.RunChild(args[1], args[3], fixture)
            : NativeWebLogin.RunChild(args[1], args[3], fixture, hidden);
        return true;
    }
}
