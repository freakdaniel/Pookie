# Pookie

Native SoundCloud client using MewUI and SoundFlow. Browser login/API calls use
InfiniFrame 0.62.1: WebView2 on Windows, WebKitGTK on Linux and WKWebView on macOS.
Windows protected audio runs in the same native browser worker through EME/MSE;
Linux protected audio uses the installed Widevine CDM with SoundFlow.

Run `dotnet run --project src/Pookie.App`. No npm/browser download is required
for a normal .NET build. Windows needs WebView2 Runtime; Linux needs
`libwebkit2gtk-4.1-0`. Browser profiles reuse `Pookie/Browser/InfiniFrame` or
`Pookie/Browser/WebKit`. Saved account credentials remain in the session vault.

Local wave storage, audio-vector search and incremental preference learning are
documented in [Pookie.Recommendations](src/Pookie.Recommendations/README.md).
Wave settings offer Balance, Favorites and Discoveries, without mood filtering.
Like/Dislike icons in both players supply explicit feedback; Dislike excludes a
song from Wave and can be cancelled. A bounded personal portrait learns several
sound preferences. During the first 24 learned reactions, existing likes and
confirmed good plays provide a fading cold-start prior. Background analysis caches
sound embeddings without running the mood classifier; unknown songs remain eligible.
Upcoming candidates receive bounded background sound analysis while the wave plays.
Feedback training uses newly obtained audio while retaining the original forecast
for evaluation. Local quality reports are available through the smoke-test host's
`--wave-quality-report [days]` command, without contacting SoundCloud.
