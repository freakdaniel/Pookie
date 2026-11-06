# Mobile DataDome challenge flow

Pookie uses a dedicated, temporary challenge WebView, selected explicitly for
the desktop application. It does not run Android or the DataDome Android SDK.
It implements the SDK's challenge-page callbacks with the actual desktop engine
UA. Acceptance of its cookie by SoundCloud remains a live integration question.

## API flow

`SoundCloudHttpTransport` detects `X-DD-B` / `X-SF-CC-X-dd-b`, provider redirects,
same-origin `dduser-challenge` redirects and JSON challenge URLs. A bounded
descriptor contains the request URL, challenge URL or HTML, previous protection
cookie and locale. It never contains the authorization header or mutation body.

`NativeMobileChallenge` starts a separate child process with a fresh profile.
Challenge state travels through stdin and a private inherited pipe; URLs, HTML,
OAuth credentials and cookies are not put in command arguments or logs. Only
provider origins and the relevant API challenge origin are accepted. The site
`soundcloud.com` and its desktop login/profile are not involved.

The page receives the SDK callbacks `Android.onCaptchaSuccess(setCookie)` and
`Android.onAdditionalChallengeRedirection(type)`. A valid, changed cookie from
the success callback is required. A cookie merely observed in page storage is
not proof of completion. Native messages require the per-window pairing nonce
and an allowed current origin. Linux installs the bridge at document start,
including challenge frames, and supports HTML denials at their API base URI.
InfiniFrame supports URL pages and HTML content; its HTML content has the
engine's `about:blank` origin, which may affect server compatibility.

The desktop-rendered page can also use DataDome's web completion envelope:
`postMessage` with `eventType: "passed"`, `responseType` and a `cookie` string.
This is the format consumed by the [official JS tag](https://js.datadome.co/tags.js).
The isolated renderer accepts this only from a trusted provider origin and a
browser-generated message event, then forwards it through the paired native
bridge. A load notification or synthetic message is not accepted as completion.

The cookie is returned to the mobile session and the captured original method,
URL and body are replayed once. Parallel API rejections share one recovery.
Hard blocks, invalid URLs, invalid/unchanged cookies and second rejections stop
the operation. Network failures and unconfirmed writes never trigger this replay.
The web operation fallback is not used. Existing session persistence saves a
verified cookie; web worker events cannot replace mobile protection state.

Cancellation and a three-minute timeout terminate the child and its process
tree. Canceling the last waiter of a shared read also cancels its challenge;
another active waiter keeps the shared operation alive. The temporary profile
is removed after the child exits, with bounded retries for OS file locks.

OAuth code exchange, legacy token exchange and refresh have the same bounded
challenge replay, retaining their original form and authentication. Login-page
web cookies are not imported into the mobile OAuth session. Mobile stream
requests preserve Range and other media headers during recovery; the challenge
cookie remains scoped to mobile API requests and is not sent to CDN URLs.
The app disables the HTTP handler's automatic cookie jar for mobile sessions
and media. This prevents an older jar cookie from being appended alongside the
explicit verified cookie. Custom library hosts must likewise disable automatic
redirects and implicit cookies on the mobile API HTTP handler.

The APK uses `AuthBuilder` and `api-auth.soundcloud.com` for authorization-code
exchange. Refresh and legacy exchange use a public builder and
`secure.soundcloud.com`; AUTH body encoding alone does not select the auth host.
These paths were rechecked against `AuthTokenFetcher`, `ApiMobileAuthTokenRefresh`,
`ApiRequest.Builder` and `ApiUrlFactory` in the saved APK decompilation.

## Verification

The configured app opens OAuth itself; an exported user access token is not
required. `mobile-oauth.json` contains the APK client configuration and a stable
per-installation identity. The local profile created for this run uses the
2026.11.02 APK client/build, a random persistent installation identifier,
`Pookie Desktop` as the model, and the APK compile codename as a declared
compatibility version. It contains no measured Android signals or advertising ID.
These identity fields are compatibility metadata, not observations of a device.
The client secret remains in the private local configuration outside the repo.

To open the normal app and start sign-in when no session has been restored:

```bash
dotnet run --project src/Pookie.App -- --sign-in
```

PKCE authorization is completed in the app's OAuth window, and returned access
and refresh tokens are saved using the existing session vault. User credentials
and tokens do not need to be supplied in chat.

Unit tests cover request replay, challenge types, redirects, inline HTML,
concurrency, cancellation, account changes, cookie validation, OAuth and media.
The Linux integration fixture exercises the real GTK/WebKit child, top-level
and frame SDK callbacks, an original native mutation, hard block, cancellation,
inline HTML and bridge isolation without an actual SoundCloud account:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --mobile-challenge-smoke-test
```

A saved mobile profile is required for live verification. This read-only probe
can show a real challenge, refresh/persist the mobile session, and read profile
and optional track/mobile playlist metadata. It never likes or edits playlists:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --mobile-challenge-network-probe
# Optional: append a SoundCloud track URL to also probe mobile contracts/streams.
```

## Live verification, 2026-11-06

Sign-in through Pookie completed with the APK client configuration and PKCE;
the saved mobile session then passed the read-only network probe. Observed:
profile/API reads and GraphQL returned 200; mobile configuration returned 200
with 9 features; the mobile owned-playlists list returned 4 entries; the first
playlist returned its complete 2-track collection; the mobile stream route
returned a 302 media location. No playlist or like mutations were sent.

The live response also confirmed Android `ApiUser.badges` is a string array;
the mobile boundary now maps it to the web model's boolean badge flags.

A subsequent complete picker check read all four owned playlists. One declares
330 tracks but returns 326 with explicit terminal `_links: {}`. The mobile reader
now uses terminal cursor metadata to mark available-track enumeration complete,
preserving the declared count. The picker renders all four choices; append still
sends one track URN and never rewrites the filtered track list.

The read-only probe did not require a challenge. A subsequent real like did:
the user's completed slider initially remained open because only the Android
SDK callback was handled. After adding the provider's passed postMessage path,
the live app recorded `403 -> SessionPrepared -> NativeRetry -> 200 ->
NativeAccepted`. This confirms that the solved challenge cookie was accepted
on the replayed mobile mutation in that session.

Windows/macOS runtime behavior remains unverified by the Linux fixture. A desktop
WebView does not reproduce Android TLS, device signals or the Android WebView
runtime. A hard block remains terminal for this flow.
