using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal sealed partial class WebKitLoginWindow
{
    public static SoundCloudMobileChallengeResult? RunMobileChallenge(SoundCloudMobileChallenge challenge, string profilePath,
        string? fixture, Action<SoundCloudMobileChallengeResult> completed)
    {
        SoundCloudMobileChallengeResult? result = null;
        var start = fixture ?? challenge.Url ?? challenge.RequestUrl;
        var app = new WebKitLoginWindow(_ => { }, new Uri(start).GetLeftPart(UriPartial.Authority), false,
            mobileChallenge: challenge, challengeCompleted: value => { result = value; completed(value); }, challengeFixture: fixture);
        app.hiddenLogin = !challenge.Interactive;
        RunCore(app, start, profilePath);
        return result;
    }

    private static partial class Native
    {
        [System.Runtime.InteropServices.DllImport(WebKit)] public static extern void webkit_web_context_set_preferred_languages(nint context, nint languages);
        [System.Runtime.InteropServices.DllImport(WebKit)] public static extern void webkit_web_view_load_html(nint view,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string html,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string baseUri);
    }
}
