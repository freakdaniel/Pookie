using System.Runtime.InteropServices;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal sealed partial class WebKitLoginWindow
{
    public static MobileAuthorizationResult? RunMobile(MobileAuthorizationRequest request, string profilePath, string? fixtureUri,
        Action<MobileAuthorizationResult>? authorized = null)
    {
        var startUrl = NativeMobileLogin.StartUrl(request, fixtureUri);
        MobileAuthorizationResult? result = null;
        var app = new WebKitLoginWindow(_ => { }, new Uri(startUrl).GetLeftPart(UriPartial.Authority), false,
            mobileAuthorization: request, authorized: value => { result = value; authorized?.Invoke(value); });
        RunCore(app, startUrl, profilePath);
        return result;
    }

    private int DecideMobilePolicy(nint view, nint decision, int kind, nint data)
    {
        if (mobileAuthorization == null || kind is not (0 or 1)) return 0;
        try
        {
            var action = Native.webkit_navigation_policy_decision_get_navigation_action(decision);
            var request = Native.webkit_navigation_action_get_request(action);
            var url = Marshal.PtrToStringUTF8(Native.webkit_uri_request_get_uri(request));
            var result = mobileAuthorization.ReadCallback(url);
            if (result != null)
            {
                Native.webkit_policy_decision_ignore(decision);
                authorized?.Invoke(result with { DataDomeClientId = mobileCookie });
                done = true; Native.gtk_main_quit(); return 1;
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Scheme is "http" or "https" or "about") return 0;
            Native.webkit_policy_decision_ignore(decision);
            return 1;
        }
        catch (Exception error)
        {
            Logging.AppLog.Failure("Pookie.Login", "Не удалось обработать мобильный callback", error);
            Native.webkit_policy_decision_ignore(decision); Native.gtk_main_quit(); return 1;
        }
    }

    private static partial class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int PolicyCallback(nint view, nint decision, int kind, nint data);
        [DllImport(WebKit)] public static extern nint webkit_navigation_policy_decision_get_navigation_action(nint decision);
        [DllImport(WebKit)] public static extern nint webkit_navigation_action_get_request(nint action);
        [DllImport(WebKit)] public static extern nint webkit_uri_request_get_uri(nint request);
        [DllImport(WebKit)] public static extern void webkit_policy_decision_ignore(nint decision);
    }
}
