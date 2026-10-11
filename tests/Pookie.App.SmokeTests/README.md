# Application integration checks

Browser scenarios now use the production Electron.NET/Chromium helper on every
platform. Build with `-p:PookieInstallElectron=true` once (Node.js/npm required).
The helper and its runtime are copied into each executable's `BrowserHost/`.
`node tests/electron-browser.smoke.cjs` additionally checks real Chromium
isolation, blank OAuth popups, persistent HttpOnly cookies and cold restart.
There is no WebKit compatibility identity or alternate login-engine switch.

Browser challenge logs include whitelisted `ChallengeType` and `Interactive`
fields and retain the challenge type on success/failure. This distinguishes
hidden automatic checks from an interactive Slider in Fetch/XHR flows. Initial
document-navigation challenges cannot be reconstructed from JS Tag events.
Cookie values, account tokens and arbitrary challenge URLs/text are not logged.

This executable contains native UI checks, local browser fixtures, and storage/audio
integration scenarios. It is separate from the production `Pookie` executable

Pure queue model checks live in `tests/Pookie.Playback.Tests` and run without a
native window: `dotnet test tests/Pookie.Playback.Tests`. They cover manual entry
priority/editing/duplicates, actual history and forward traversal, stable shuffle,
repeat/end behavior, immutable snapshots and independent cancellable pagination.

`--home-ui-smoke-test` checks personal shortcut cards, the inline settings drawer
across repeated width/height changes, its independent scroll and close behavior,
continuous/interrupted palette transitions while playing and paused, the animated wave hero and native settings clicks,
favorite/discovery boundaries, current/manual queue preservation, dislike, continuous
refill and account-scoped persistence, fallback without mixes, independent sections, skeleton-to-card
transitions, responsive grids and scrolling, account-scoped cache, partial failures
and refresh, stale account replies, and native play/card clicks for Home, compact
rows and liked tiles, including compact title hit bounds. It uses local account
data and requests no real audio.

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

`--comments-ui-smoke-test` checks the animated avatar composer, Escape/outside-click
collapse with draft preservation, uninterrupted send focus, send click, confirmed insertion,
duplicate suppression, retained drafts after failures, server sorting races, timestamp hover,
own-only deletion, reply preservation, disabled comments, and stale account responses.
It uses a fixture transport and never posts or deletes real SoundCloud comments.

`--collections-ui-smoke-test` checks the compact playlist rows without numbering, row menus,
filtering without changing the full playback source, expandable description,
narrow/wide hero layout, back-navigation filter/scroll, playlist likes/reposts,
following/unfollowing, isolated pending states, playlist selection and stale account
replies. All writes use a local fixture transport.

`--playlist-page-network-probe <SoundCloud playlist URL>` verifies real playlist
metadata, ordered track loading and playlist like/repost state reads through Electron.
Close the normal app first: this probe preserves and leases the saved browser profile.
It makes no account writes.

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
and does not use the real account. Electron starts with HardwareMediaKeyHandling
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

`--content-blocker-smoke-test` checks reviewed rules and actual Chromium request
cancellation. The same embedded rules apply before navigation in the login and
worker partitions. SoundCloud API/media/license, OAuth and challenge resources
remain allowed.

`--startup-network-probe` validates a saved account with the production Electron
worker. Close Pookie first to release the persistent profile. Diagnostics record
only whitelisted startup stages and status codes; no tokens or raw browser output.

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

The Windows browser-audio adapter now uses the same hidden Electron worker as API
calls, with background throttling and Chromium's system media integration disabled.
Electron does not ship a Widevine CDM by default: successful login/API checks do
not establish protected EME playback. This remains a runtime capability requirement;
Windows protected playback needs separate validation with an appropriate CDM.
Ordinary audio and the Linux native Widevine/SoundFlow route remain unchanged.

`--browser-audio-state-smoke-test` uses local fake transports without a website
or CDM. It verifies routing, volume, seek, pause, one-shot end, ignored stale-track
events, worker failure and cancellation when switching tracks.

The former InfiniFrame/WebView2 transport/CDM probes have been removed with that
backend. `--drm-service-certificate` still exercises the native certificate exchange.

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
Electron fixture servers and assertions; `Diagnostics/` contains scenario dispatch, audio/storage
checks, and the clipboard child process

The test executable compiles the application's original sources as linked files,
excluding the production entry point. This lets integration checks inspect private
UI state without adding public test APIs or duplicating application logic. The
dependencies and embedded resources are shared through `Pookie.App.Shared.props`.
Only this project supplies the optional lifecycle hook implementations; the C#
compiler removes unimplemented hooks from the production assembly

The production app keeps its standalone `--demo` preview mode. It contains no
verification methods, test fixture servers, or diagnostic scenario runner
