using Pookie.SoundCloud;
using System.Runtime.InteropServices;

namespace Pookie.App.Browser;

internal sealed partial class WebKitLoginWindow
{
    private bool verificationVisible;

    partial void OnBrowserEvent(BrowserRequestEvent? message)
    {
        if (browserEvent == null || !origin.StartsWith("http:", StringComparison.Ordinal)) return;
        if (message?.Kind is "checking" or "blocked" && message.Interactive) verificationVisible = true;
        if (message?.Kind is "passed" or "challenge-error") verificationVisible = false;
        if ((IsWindowMapped(rootWindow) != 0) != verificationVisible)
            throw new InvalidOperationException("Некорректная видимость окна проверки.");
    }

    partial void CustomizeScript(ref string scriptBody, bool login)
    {
        if (login && origin.StartsWith("http:", StringComparison.Ordinal))
            scriptBody = "window.fetch=()=>Promise.resolve(new Response('{}'));" + scriptBody;
    }

    partial void ConfigureView(nint view)
    {
        // The local popup fixture opens its fake provider without a manual click.
        // Only this test host allows scripted popups; production keeps WebKit's policy.
        if (!background && origin.StartsWith("http:", StringComparison.Ordinal))
            AllowScriptedPopups(Native.webkit_web_view_get_settings(view), 1);
    }

    [DllImport("libwebkit2gtk-4.1.so.0", EntryPoint = "webkit_settings_set_javascript_can_open_windows_automatically")]
    private static extern void AllowScriptedPopups(nint settings, int enabled);

    [DllImport("libgtk-3.so.0", EntryPoint = "gtk_widget_get_mapped")]
    private static extern int IsWindowMapped(nint window);
}
