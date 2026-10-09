# Application integration checks

This executable contains native UI checks, local browser fixtures, and storage/audio
integration scenarios. It is separate from the production `Pookie` executable

Pure queue model checks live in `tests/Pookie.Playback.Tests` and run without a
native window: `dotnet test tests/Pookie.Playback.Tests`. They cover manual entry
priority/editing/duplicates, actual history and forward traversal, stable shuffle,
repeat/end behavior, immutable snapshots and independent cancellable pagination.

`--track-page-ui-smoke-test` checks native title navigation without starting audio,
responsive reference hero, metadata/artwork/waveform, threaded comments and replies,
sidebar fans and 2x2 playlist previews, shared library profile/playlist cards and
waveform rows in related tracks, compact preview alignment and duration spacing,
continuous resize in both directions, nonpersistent action focus, track subsections
(reposts/albums/playlists/related), infinite loading and deduplicated pagination,
back/forward restoration, cancellation and stale replies, and the shared player's
queue and seek. On Linux it clicks the hero and list copy buttons, resolves a missing
permalink and reads the new URL from a separate process using a private X selection;
the user's clipboard is untouched. The writer and search inputs also work with
`GDK_BACKEND=wayland` and no GTK clipboard service. The hero checks rendered
forward/reverse background, icon and count colours, interruption, and the absence
of a faded disabled state during pending requests. It uses an isolated demo profile
and a local browser-transport fixture.

`--likes-edits-ui-smoke-test` uses an isolated account fixture to check per-track
pending state, duplicate suppression, animated removal/insertion and movement in
likes, stable scroll and pagination, focus recycling, failed writes and late replies.
It also checks existing reposts, add/remove state, isolated pending buttons, duplicate
suppression, failed writes, own-track restrictions and stale session replies. All
account mutations use a local fixture transport.

`--collections-ui-smoke-test` checks the playlist hero and shared track list at
narrow/wide sizes, queue source, back-navigation scroll, following/unfollowing,
isolated pending states, playlist selection and stale account replies. All writes
use a local fixture transport.

`--track-page-network-probe <SoundCloud track URL>` verifies real metadata, timed
comments/replies and pagination, sidebar fans/collections, all four subsection endpoints and their next pages, related tracks and waveform through the production browser
transport. `--track-page-live-ui-probe` additionally opens the real "Этажи" track in
an isolated application window, clicks title/play/pause/waveform/comment timestamps,
decodes actual audio at zero volume and checks back/forward. Close the normal app
first: these two probes preserve and lease the saved SoundCloud browser profile.
They do not change likes or write to the clipboard.

Run from the repository root:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --scrolling-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --track-page-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --likes-edits-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --collections-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --pagination-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --system-media-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --media-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-media-isolation-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --expanded-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --lyrics-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --buffer-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --login-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --startup-log-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --content-blocker-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --storage-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --session-vault-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --audio-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --login-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-worker-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-persistence-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-audio-state-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-shutdown-smoke-test
```

`--startup-log-smoke-test` checks default/explicit console and file thresholds,
worker file output, console fallback when files are unavailable, stage timings,
exception propagation and filtering of private text, using isolated temporary data.

`--system-media-smoke-test` exports the real MPRIS interfaces through a private
D-Bus TCP peer, checking wire serialization, metadata, properties, change signals,
command routing, stale track-ID rejection and clearing. On Windows it also checks
SMTC discovery, metadata/status and an OS Play/Pause round trip.

`--media-ui-smoke-test` enables the system session in an isolated demo host and
checks commands against real local audio. Windows uses OS session requests for
play/pause, seek, next and stop/resume; duplicate play/pause commands are idempotent.
It also advances through three real audio endings, then requests Next through
Windows, checking track metadata against both player views after each change.
Other UI tests keep system integration disabled to avoid taking over media keys.

`--browser-media-isolation-smoke-test` runs a Windows WebView worker against a
local audio page that repeatedly publishes website metadata and registers Next.
It verifies audio progress, absence of a competing SMTC session, and routing of
OS Next to Pookie with an updated track title. The fixture has an isolated profile
and does not use the real account. WebView2 starts with HardwareMediaKeyHandling
and MediaSessionService disabled; Pookie owns the system session and queue.

`--expanded-ui-smoke-test` checks cover hover, native fullscreen and window-state
restore, rendered intermediate animation frames, centered/split composition,
lyrics/queue panel changes, queue selection, shared seeking and buffered progress,
interrupted close/reopen and reset. It checks switching to the lyrics panel;
actual lyric behavior is covered by the dedicated scenario below.
It also repeats fullscreen entry/exit while paused and simulates a resize viewport
that misses the retained motion layer. Cover hover must produce intermediate and
final native frames without clicking or hovering the progress bar; an opacity
change alone is insufficient.
Queue checks cover the retained playback source/current track, row backgrounds
extending beyond heading edges, hidden scrollbars, native wheel interpolation,
animated movement of earlier tracks and a stable now-playing source heading while
playback advances, including after scrolling away and selecting a distant track.
GPU-rendered background/edge-fade pixels and the window's presented frame follow
intermediate and settled colors, including an interrupted transition. Background
and edge fades each reuse one mutable image, including the final frame. Queue rows
share one timeline with zero initial/final velocity. Queue advance reports native frame intervals
for performance comparisons; these depend on the machine and are not an FPS gate.
Rebinding the same queue item or replacing resolved track metadata must not reset
its movement or recycle its retained row. Three additional cover Next clicks use
the real SoundFlow decoder/device lifecycle with local audio and a silent backend,
rather than the instant fixture player; their native frame intervals are reported.
These checks do not reproduce SoundCloud network delays or physical output drivers.
Hover backgrounds remain inside the viewport, with their rounded
corners verified in rendered pixels and covers aligned to section headings.
`--ui-smoke-test` also checks that pending artwork keeps the previous palette until
the new cover arrives, failed/missing covers fall back to neutral, and a one-level
colour change produces at least 90 distinct dithered frames without a channel jump.
`--ui-smoke-test` checks occurrence-specific active rows and queue menus, the
three-second Previous rule, system command order, single-result search playback,
all repeat modes, a finite source ending, and source pagination across navigation.
A delayed Next cannot replace a newer selection.
The queue fixture contains 1,000 tracks. Motion translates retained layers without
remeasuring or arranging their rows; closing slides the panel toward the cover
and fades it while the cover returns to the center. The check verifies that
visible panels never overlap the cover on closing
and reports observed frame intervals. Each rendered motion frame also checks
that translated content stays within its host's repaint bounds, including after
the renderer switches from recorded commands to live drawing. Cover volume uses native mouse events to
check unfolding, binding, delayed hiding, reentry and smooth collapse. Progress
time labels are hidden initially and fade on native hover, remain visible during
dragging, and hide after a debounce without changing layout. Closing fullscreen
restores keyboard focus without leaving the small cover's hover overlay visible.

`--buffer-ui-smoke-test` renders and samples the played, buffered and empty parts
of the timeline, then checks seek previews, buffer replacement after seeking,
continuous filling from the start, loading shimmer visibility and track changes
with an isolated audio fixture. It also samples intermediate rendered frames for
playback/buffer growth and contraction, checks that animation never issues seek
commands, and keeps the timeline visible during a seek or temporary underrun.
Audio unit tests also exercise encoded MP3/HLS
lookahead, reuse across seeks, sparse byte ranges and temporary-file cleanup.

`--content-blocker-smoke-test` checks host/path boundaries and parity between the
Windows matcher and generated WebKit rules. An isolated native browser fixture
then requests blocked tracking URLs and verifies that ordinary page scripts and
the authorized profile API still work. The fixture uses a temporary profile and
never loads a real SoundCloud account. Windows returns an inert response for
blocked resources; WebKit rejects them through its native content rules.

UI and local browser scenarios require a desktop session and the same native
dependencies as the application. Run UI scenarios sequentially: keyboard focus
and window events belong to the shared desktop. UI checks use temporary app data,
synthetic accounts, local audio, and isolated clipboard selections

Set `POOKIE_UI_PREVIEW=/tmp/pookie-preview` to save PPM renders during UI checks.
The `--smoke-test` flag opens the local preview and closes it after five seconds

`--browser-shutdown-smoke-test` opens an isolated local WebView worker, replaces
its UI subscription, then closes the application. It awaits worker cleanup and
delivers captured callbacks after the UI loop has ended to check the shutdown race.

`--session-vault-smoke-test` verifies Windows DPAPI storage with a synthetic
account: encrypted bytes, reload in another process, session rotation, rejection
of damaged/oversized data and deletion on logout. It uses an isolated directory.

The following scenarios use the real SoundCloud service and must be run explicitly:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --startup-network-probe
dotnet run --project tests/Pookie.App.SmokeTests -- --web-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --login-handoff-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --session-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test <track-url>
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test <track-url> --drm-webview-transport
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test <track-url> --drm-webview-cdm
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test <track-url> --drm-service-certificate
dotnet run --project tests/Pookie.App.SmokeTests -- --drm-webview-capabilities
dotnet run --project tests/Pookie.App.SmokeTests -- --drm-browser-media-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test <track-url> --drm-native-cdm
```

`--startup-network-probe` validates the saved account through the production Windows
WebView worker and exits. Close the normal app first so the probe can open its
existing profile. It preserves the session and records startup milestones at
Debug without account details. Set `POOKIE_LOG_CONSOLE_LEVEL=Debug` to see these
milestones in the console; the default file level already includes them.
Setting `POOKIE_STARTUP_DEBUG_PORT` to a port from 1024 to 65535
enables InfiniFrame's loopback CDP endpoint for an explicitly configured diagnostic
run. This endpoint exposes browser debugging capabilities; it is disabled by default.

On 2026-10-07, a local Windows capture found `pixel.quantserve.com` requests timing
out after 42.3 seconds. The document reached DOMContentLoaded after 1.4 seconds,
but InfiniFrame's initial-navigation readiness awaited the full page load at 45.8
seconds. A controlled run blocking only that domain through CDP completed the saved
session probe in 4.5 seconds, compared with 46.2 seconds in the baseline. This
blocking was confined to that diagnostic run. Production now installs its shared,
embedded content rules before the first navigation, including Quantserve filtering.
The preserved Windows session probe completed in 3.3 seconds with those filters
on 2026-10-07; Lucidream's full DRM playback smoke test also passed.

The Windows adapter uses InfiniFrame 0.62.1's runtime HTTP(S) request dispatch,
returning null content to preserve allowed requests and an inert response for
blocked ones. Register HTTP(S) on the created window, not on the builder: these
are existing WebView2 protocols. This integration depends on InfiniFrame's native
dispatch behavior; run the native smoke check when upgrading it. Linux WebKitGTK
compiles the same rules into a native content filter before loading a document,
caching it by rule-set SHA-256 under the browser profile's `ContentFilters/`.
Filters apply to both the login and background service paths. The rules do not
target API/media/CDM, OAuth providers, or DataDome resources. The complete
Ghostery engine, scriptlets, and cosmetic rules are not included.

The session scenario sends an idempotent like request for an already liked track;
the handoff scenario opens the real sign-in flow. These are not run by `dotnet test`

For DRM checks, close the normal app first: the diagnostic host needs the same
SoundCloud profile. These checks preserve its existing session. Set
`POOKIE_DRM_DIAGNOSTICS=1` for bounded failure metadata without credentials or
license payloads in the output.

The default protected-audio scenario uses the production platform route: browser
EME/MSE on Windows and native Widevine/SoundFlow on Linux. It checks decoded
playback progress, duration, seek, pause/resume, one-shot end, restart and stop.
The Windows scenario is muted by default; `--system-audio` uses 15% volume to
verify unmuted autoplay and system output. `--drm-native-cdm` retains the old
native route for a controlled Windows comparison.

Windows audio shares the account's existing API WebView and does not open a
second profile. Its service window stays active off-screen without focus or a
taskbar entry: WebView2 defers MSE attachment in an `SW_HIDE` window. Interactive
website checks restore the window to its normal position. Playback commands run
independently of API reads, so a slow read cannot hold up pause or stop.
Ordinary audio continues to use SoundFlow. Browser media and license responses
remain in the browser; no content keys are exported. The hidden website's own
media elements are paused, and the app selects only its resolved track stream.

`--browser-audio-state-smoke-test` uses local fake transports without a website
or CDM. It verifies routing, volume, seek, pause, one-shot end, ignored stale-track
events, worker failure and cancellation when switching tracks.

`--drm-webview-transport` is a Windows diagnostic: it sends the native CDM's
license challenge through the app's own WebView, with the same fixed SoundCloud
endpoint and application ID. It reports the HTTP result and browser EME support.
`DRM_WEBVIEW_LICENSE_OK` confirms that the native CDM accepted a license; it does
not confirm audio playback. This transport is not enabled in the production player.

`--drm-webview-cdm` uses the browser's EME implementation for the same track's
initialization data and license authorization. It handles certificate and license
messages and waits for `keystatuseschange`, including changes after `update()`
resolves. `DRM_BROWSER_CDM_LICENSE_OK` requires usable browser CDM keys; it does
not confirm audio decoding or playback. Browser license responses stay inside
their browser session and are never exported to the native CDM.

`--drm-service-certificate` requests the service's signed privacy certificate,
installs it through the native CDM's `SetServerCertificate`, then attempts the
normal native license exchange. This optional experiment does not change the
production player's default behavior.

`--drm-webview-capabilities` only queries EME on a secure SoundCloud-origin
document in an isolated temporary profile. It does not use an account or send a
license request. All transport probes use a static SoundCloud document instead
of its player, so website music and advertising cannot generate their requests.
Their IPC carries credentials through private pipes, with only bounded status
metadata written to the console.

`--drm-browser-media-smoke-test` additionally verifies that a browser MediaSource
opens in the off-screen service window. It uses no account, license or audio data.

Windows observations on 2026-10-06, Da Lumé — Lucidream (track `2404736589`):

| Check | Observed result |
| --- | --- |
| Production native CDM and HTTP transport | License HTTP 403 before audio decoding |
| Same native CDM challenge through WebView transport | License HTTP 403 |
| Browser CDM, same track initialization data and authorization flow | Two exchanges; HTTP 200 and usable keys |
| Native CDM with service privacy certificate | Certificate HTTP 200, accepted by CDM; content license still HTTP 403 |
| Native CDM against the public Shaka Angel One reference server | HTTP 200 and usable key callback |
| Windows production EME/MSE route | Lucidream passed playback, duration, seek, pause/resume, end, restart and stop |
| Windows production EME/MSE route, «спасибо» (track `2178826375`) | Same full cycle passed at 15% volume |

The browser license probe does not play audio. Actual Lucidream playback in the
app's test WebView was separately confirmed manually. The static-document probes
do not start SoundCloud's player or advertising. These results isolate the failure
to the native Windows CDM request/integration rather than a captured advertisement
or a missing browser DRM capability. The license service gives no detailed refusal
reason, so host verification/VMP remains a hypothesis, not a confirmed diagnosis.
The Windows production route now uses browser EME/MSE through `WindowsAudioPlayer`;
the comparison modes above keep the native path available for diagnosis.

`--lyrics-ui-smoke-test` uses a fake provider and local player. It checks 1000
virtualized lines, smooth hover/click-to-seek with native and speed-adjusted
timestamps, the real seven-second idle timer
and its reset on wheel input, larger dots with lyric shading and smooth wave
start/settling, skeleton crossfade to text/missing, direct hidden-panel reveal
without an outgoing queue,
end-of-text attribution, plain/missing states without action buttons, responsive
layout and ignored late replies. It sends no real lyrics or SoundCloud requests.
Pure matching/parser/storage/retry checks live in `tests/Pookie.Lyrics.Tests` and
are included in `dotnet test Pookie.slnx`.

`--artwork-ui-smoke-test` seeds the isolated disk cache with different 100px and
500px images. It verifies the player's thumbnail URL is upgraded through the
same shared loader as cards, including an older compact-row cache, first/repeated
fullscreen entry and artwork arriving while fullscreen is already open. It checks
the native image resource before CPU previews can realize another resource and
rejects stale track completions. No real artwork or SoundCloud requests are sent.

## Source boundaries

`Views/` contains the verification partials for `MainWindow`; `Auth/` contains local
WebView servers and assertions; `Diagnostics/` contains scenario dispatch, audio/storage
checks, and the clipboard child process

The test executable compiles the application's original sources as linked files,
excluding the production entry point. This lets integration checks inspect private
UI state without adding public test APIs or duplicating application logic. The
dependencies and embedded resources are shared through `Pookie.App.Shared.props`.
Only this project supplies the optional lifecycle hook implementations; the C#
compiler removes unimplemented hooks from the production assembly

The production app keeps its standalone `--demo` preview mode. It contains no
verification methods, test fixture servers, or diagnostic scenario runner
