# Embedded website sessions

The desktop application uses the existing web API behavior. Authenticated reads,
likes, follows, playlists, comments, chat and play history execute through
`BrowserRequestScript` in the logged-in website. The website's live fetch and its
own DataDome tag own checks, cookies and any permitted retry. The library's optional
native HTTP transport remains available for standalone consumers and diagnostics;
the app attaches `NativeBrowserSession` directly as its transport.

Login and `EmbeddedWebWorker` open the same durable `BrowserProfile` in sequence.
The native cookie database retains its attributes; exported .NET metadata never
recreates browser cookies. Android identity, mobile OAuth/refresh, SDK telemetry,
mobile API adapters and isolated mobile challenge windows have been removed.
Old Android token snapshots are not accepted as web sessions.

An interactive website `checking` event presents the worker's existing native
view inside the same `ModalDialog` used for login. It preserves the website, iframe,
network request, native UA and cookie store. The modal does not navigate to a
separate challenge URL or export a solved cookie into another browser. Passed/error
events hide the view after the normal closing animation; invisible checks stay
in the background and hard blocks stop the operation. Dismissing the modal stops
the worker so a canceled write cannot execute later. The next explicit action
can reopen the same durable profile.

`IEmbeddedBrowser` hosts CoreWebView2 on Windows, WebKitGTK in a GtkPlug on Linux
(X11/XWayland), and a WKWebView subview on macOS. MewUI supplies the scrim, external
close control and snapshot transitions. Windows shutdown closes controllers and
waits for the owned browser process before releasing the profile, without requiring
a continuation on a stopped UI dispatcher.

InfiniFrame 0.62.1 remains for legacy standalone helper commands and diagnostics.
Its public window builder does not expose a native control for embedding inside
MewUI. `Microsoft.Web.WebView2` 1.0.4314.35 supplies the direct Windows SDK interface
used by the embedded host; it uses the existing WebView2 Runtime. Login, requests
and slider presentation all use this same embedded host in the normal app.

Verification commands:

```text
dotnet test tests/Pookie.SoundCloud.Tests
node --test tests/browser-requests.test.cjs
dotnet run --project tests/Pookie.App.SmokeTests -- --embedded-login-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --embedded-web-challenge-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --collections-ui-smoke-test --web-playlist-editing-only --no-audio
```

The browser fixtures use local servers and synthetic accounts. Runtime behavior
is checked on Windows; Linux and macOS require their own native runtime checks.
