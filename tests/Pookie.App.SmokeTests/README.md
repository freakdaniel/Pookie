# Application integration checks

Browser scenarios use the production InfiniFrame host: WebView2 on Windows,
WebKitGTK on Linux and WKWebView on macOS. The app executable starts its browser
child on an STA thread, using a private pipe and persistent native browser profile.
Normal .NET builds restore the host package without installing a separate browser.
Windows requires WebView2 Runtime; Linux requires `libwebkit2gtk-4.1-0`. The Linux
GTK fallback keeps initialization on the main thread; `POOKIE_LOGIN_ENGINE=infiniframe`
selects the packaged InfiniFrame implementation for explicit comparisons.

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

`--settings-ui-smoke-test --no-audio` checks the settings gear through native
clicks, all seven library-style tabs at 1000×680, back/forward section and scroll
restoration, the persistent likes view, real playback/lyrics/history/Wave policy
switches, preserved current/manual queue entries, and configuration round trips.
It also verifies native Material switch geometry, animated travel and pressed size,
cancelled/disabled input, dropdown mouse/keyboard interaction, and the cache chart's
real file-size proportions, hover details and empty state after clearing.
It uses an isolated fixture account, metadata transport and fake audio player;
no real account writes, messages or audio requests are made. Set
`POOKIE_UI_PREVIEW=/absolute/output/path` to capture each tab and long-section
scroll states for visual QA. The excluded reduced-animation preference must stay absent.

`--queue-continuation-ui-smoke-test --no-audio` verifies default automatic
continuation after a track/playlist: prefetch, manual/source/recommendation priority,
missing recommendation skip, retained original context and history identity,
separate "Похожие дальше" sections in both queues, and repeat taking precedence.
It uses isolated account data and fixture metadata/streams with a fake audio player.
Queue unit tests also cover stale/cancelled results, single-flight prefetch,
source pagination, deduplication, explicit recommendation selection, transient
retry, empty results, related-page traversal and Wave mode isolation.

`--chat-media-ui-smoke-test --no-audio` runs the full rich-attachment checks
independently of composer layout: file/image/video fixtures, draft previews,
viewer playback and controls, and music fading/restoration. It uses isolated
account data and fixture transports; no real messages or uploads are sent.

`--home-ui-smoke-test` checks personal shortcut cards, the inline settings drawer
across repeated width/height changes, its independent scroll and close behavior,
continuous/interrupted palette transitions while playing and paused, the animated wave hero and native settings clicks,
favorite/discovery boundaries, current/manual queue preservation, dislike, continuous
refill and account-scoped persistence, fallback without mixes, independent sections, skeleton-to-card
transitions, responsive grids and scrolling, account-scoped cache, partial failures
and refresh, stale account replies, and native play/card clicks for Home, compact
rows and liked tiles, including compact title hit bounds. It uses local account
data and requests no real audio.

`--wave-queue-ui-smoke-test` checks the actual three-track committed horizon in the
popup and fullscreen queue. Background checkpoints leave rows/keys/animations alone;
advancing retains remaining rows, animates their movement and adds one candidate.
Manual entries have a separate heading, mode changes dissolve changed recommendations,
and advancing does not move a viewport reading playback history. It uses isolated demo
data and a fixture player, with no SoundCloud requests or account writes.

The Home scenario also checks the local wave journal: native dislike, like/unlike,
reset exclusions, actual listened time, shutdown classification and account isolation.
Journal/storage regression checks run in `tests/Pookie.Playback.Tests` without a window.

`--wave-journal-ui-smoke-test` runs the journal integration independently of Home
layout: native dislike, like/unlike, reset blocks, counted listening and account
switch. It uses isolated data, no real audio or SoundCloud requests.
Deleted-track fixtures verify automatic Wave advancement, persistent availability
exclusions without taste penalties, rejection of stale imports, unchanged handling
of rate limits, stale playback responses, and bounded failure chains.
It also checks captured PCM reaching the MusiCNN vector index and the learned
account checkpoint through the production journal pipeline, the actual player
dislike icon, cancellation outside Wave, mutual exclusion with Like, and restoring
a rejected song through Like. Home checks native wave mode selection while preserving
the current track and manual queue, including the settings drawer at narrow widths.
The journal scenario also verifies audio-vector updates reranking the queue at an
unchanged model checkpoint, without replacing current/manual entries. Cached mood
metadata must not affect the base wave's order or eligibility.
The journal scenario also checks sequential background candidate analysis,
nonblocking scheduling, 24 attempts per session, cancellation in Favorites,
unchanged listening/rewards and preserved current/manual entries. All previews
use injected local PCM; no real media is downloaded by UI checks.

`--wave-quality-report [days]` reads the existing wave journal without changing it
or contacting SoundCloud. The default is 7 days; allowed periods are 1-90 days.
JSON groups actual automatic Wave plays by account, algorithm version, mode and
familiar/new/unknown cohort, with early skips, >=60% listening, natural ends,
rejections, linked likes, distinct tracks/artists and prediction audio coverage.
Short interrupted plays are excluded from the evaluated denominator. Older
versions have null like rates because their feedback lacked playback identities.
This describes retained observations; it is not a causal A/B quality comparison.

`--audio-mood-probe URL... [--preview] [--cache-results]` uses the saved account
and production resolver/decoder to inspect short audio excerpts of real tracks.
Preview samples start at min(30 seconds, duration / 4); protected tracks use
ordinary silent playback instead. Without `--cache-results` the wave database
is untouched; that flag refreshes only vectors, never listening rewards or likes.
Console output contains public track identity and scores, no session/stream tokens.

`--track-page-ui-smoke-test` checks native title navigation without starting audio,
responsive reference hero, stable single-line title/artist/metadata geometry across
repeated narrow/wide resizes with long text, metadata/artwork/waveform, threaded comments and replies,
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

`--memory-ui-smoke-test` creates and discards three batches of 100 sidebar cards with
500px artwork, including cards removed during an active loading shimmer. Weak-reference
checks verify that removed controls/pixels collect, cached SVG icons do not retain views,
and live/recycled loading layers stop and resume their clocks. It prints managed memory
and process RSS for diagnostics; assertions check object lifetimes rather than OS memory
thresholds. It uses isolated app data and makes no SoundCloud requests.

`--playlist-page-network-probe <SoundCloud playlist URL>` verifies real playlist
metadata, ordered track loading and playlist like/repost state reads through the native WebView.
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
dotnet run --project tests/Pookie.App.SmokeTests -- --memory-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --wave-queue-ui-smoke-test
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

`--content-blocker-smoke-test` checks reviewed rules and actual Chromium request
cancellation. The same embedded rules apply before navigation in the login and
worker partitions. SoundCloud API/media/license, OAuth and challenge resources
remain allowed.

`--startup-network-probe` validates a saved account with the production InfiniFrame
worker. Close Pookie first to release the persistent profile. Diagnostics record
only whitelisted startup stages and status codes; no tokens or raw browser output.

`--playback-history-network-probe <SoundCloud track URL> [context URN]` writes one
play through Pookie's production browser transport, then reads uncached server
history for up to ten seconds to verify that the track became the latest entry.
Choose a track other than the current latest entry and close Pookie first to
release its saved profile. This probe changes real SoundCloud listening history.
It does not require audio playback. The local `--browser-worker-smoke-test` also
checks that a legacy `204` alone does not persist track history, that the audio/play
event does, and that failed events propagate without being replayed.

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

The Windows browser-audio adapter uses the same hidden InfiniFrame worker as API
calls, with background throttling and Chromium's system media integration disabled.
The Windows system WebView2 runtime supplies EME/Widevine. CDM initialization
and actual SoundCloud license acceptance are checked separately. No Castlabs
runtime, external Chrome or application EVS signing is used by this backend.
Ordinary audio and the Linux native Widevine/SoundFlow route remain unchanged.

`--browser-audio-state-smoke-test` uses local fake transports without a website
or CDM. It verifies routing, volume, seek, pause, one-shot end, ignored stale-track
events, worker failure and cancellation when switching tracks. It also verifies
bounded PCM transfer into the real MusiCNN encoder and rejection of stale chunks.

`--browser-audio-capture-smoke-test` uses real Chromium to decode a local WAV,
the production analysis Worklet, the paired browser pipe and WindowsAudioPlayer.
It verifies a real neural embedding while master volume is zero and track gain is
negative. The local media transport substitutes for EME; it does not validate a CDM.

`--browser-drm-capability-probe` reports AAC/MSE and Widevine key-system support
in an isolated local fixture without real credentials, media or license requests.
Unavailable/disabled key systems are reported by production playback as
`unsupported` at the EME stage, rather than a generic decoding failure.

`--native-drm-probe TRACK_URL` inspects the actual protected playlist and attempts
PlayReady license acquisition in the current Windows host. It returns a nonzero
exit code on unsupported APIs or failed acquisition, without exposing credentials
or payloads. It does not claim to verify playback. The official SoundCloud player
and VMP lab probes, observations and current limitations are documented in
[`tools/drm-probe/README.md`](../../tools/drm-probe/README.md).

For actual EME PCM validation with the saved Pookie session and a protected track:

```powershell
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test TRACK_URL --analysis-probe
```

Use `history` instead of `TRACK_URL` to select an accessible protected track from
the saved account's first 20 history entries. The probe stays muted, verifies a non-silent neural embedding from browser PCM,
and then checks the normal seek/pause/end lifecycle. On Windows it uses the
production InfiniFrame/WebView2 worker and its Widevine CDM.
`--normalization-probe` additionally verifies the fixed waveform correction.
`--default-stream-selection` uses the production resolver's normal protocol order;
if it selects an ordinary stream, the probe reports it without attempting DRM.
`--isolated-browser-profile` avoids a running Pookie profile's lease using a fresh
diagnostic profile under `artifacts/drm-probe-*`; it does not copy browser cookies.
`--browser-profile PATH` reuses a diagnostic API profile across repeated checks.

`--drm-service-certificate` exercises the native certificate exchange separately
from the Windows browser EME route.

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
Native WebView fixture servers and assertions; `Diagnostics/` contains scenario dispatch, audio/storage
checks, and the clipboard child process

The test executable compiles the application's original sources as linked files,
excluding the production entry point. This lets integration checks inspect private
UI state without adding public test APIs or duplicating application logic. The
dependencies and embedded resources are shared through `Pookie.App.Shared.props`.
Only this project supplies the optional lifecycle hook implementations; the C#
compiler removes unimplemented hooks from the production assembly

The production app keeps its standalone `--demo` preview mode. It contains no
verification methods, test fixture servers, or diagnostic scenario runner

`--menus-ui-smoke-test --no-audio` checks the shared queue/profile/settings popovers:
attachment-menu surface and shadow, animated opening/closing and reversal, real
trigger clicks and hover, crossing the pointer gap, Escape, exclusive open state,
transparent hit testing and anchoring during window resize. It uses demo queue data.

`--chat-navigation-ui-smoke-test --no-audio` checks opening and reopening Chats,
resizing between desktop and compact widths, opening a conversation, restoring a
history anchor and following the latest message while the composer grows. It waits
for rendered frames and uses isolated fixture data. Run it at fractional Windows
display scaling to catch measure/arrange constraint oscillation; the regression was
reproduced at 110 DPI before removing the remeasure from `ChatViewport.ArrangeOverride`.

`--chat-music-ui-smoke-test --no-audio` runs the music attachment scenarios independently:
shared full/compact track rows, inline clickable artwork/title mentions with wrapping,
composer recognition and atomic editing, copy/paste/undo, and a message-scoped queue
with attachment ordering, Next/Previous, skipped-track selection and recycled bindings.
Delayed metadata/waveform fixtures check animated skeleton-to-content reveals and
intermediate bubble sizes; short lists use a minimum width while full cards retain
their artwork size and shorten the waveform.
It uses local fixture messages and sends no requests to real SoundCloud accounts.

`--chats-ui-smoke-test --no-audio` verifies the Chats navigation immediately after
Library, virtualized inbox/thread, responsive two-pane/single-pane layout, stable
polling while reading history, older-message pagination, per-conversation drafts,
duplicate-submit prevention, durable unknown sends and reconciliation. Uses only a
local fake SoundCloud transport. `POOKIE_UI_PREVIEW` saves synthetic UI previews.
Also checks the loading skeleton, local sending spinner and failure state, persisted
pre-send IDs (an older identical message cannot confirm a failed send), media
thumbnails in inbox/composer/bubbles, date-only sticky separators, time-only message
footers, image send round trips through a fake host, and bottom anchoring after resize.
Media regressions cover a 4K JPEG, small images without upscaling, circular avatar
frames, one check mark for sent messages, and a document card when an image cannot
be decoded. Also checks media skeletons without technical labels, caption previews
in the inbox, blurred backdrops, bounded unbroken captions and hover-only timestamps
on image-only messages. Optional `POOKIE_CHAT_IMAGE_PROBE=/absolute/path/to/photo.jpg` checks
a local image in the bubble, inbox and composer through the fake host only.
Stability regressions keep the same photo/source objects across sends and acknowledgements,
exercise clock skew and coarse server timestamps, verify inline text/time bounds,
unindented text previews and the date overlay's horizontal alignment and idle fade.
Attachment-input checks route enter/leave/drop events through the framework drag
router and verify preview feedback, append/deduplication, count/size limits, caption
preservation and temporary-image cleanup. The compact drop hint must keep both icon
and label inside the row, with a neutral surface and no nested outline; focusing the
composer must retain its resting background. On Linux a separate process owns a private
clipboard selection for real PNG, copied-file URI and plain-text paste tests,
including Ctrl+V, Unicode paths and undo. The user's clipboard is never changed.
Late clipboard results are rejected after clearing attachments or switching chats.
The browser worker fixture also exercises chat list/thread/POST/mark-read over the
real browser bridge, including a device-check-rejected POST resuming exactly once,
without sending messages to actual SoundCloud users. JavaScript fixtures distinguish
that known rejection from an interrupted POST, ordinary failures and a dismissed check.
Protocol, privacy boundaries, hosting limits and KLIPY setup: [MESSAGES.md](../../MESSAGES.md).

`--chat-media-hosting-live-probe pomf` explicitly uploads a small encrypted test
package to the public primary host, downloads it and verifies authenticated
decryption and exact file bytes. `--chat-media-hosting-live-probe quax` simulates
an HTTP 503 from Pomf and exercises the real ZIP fallback and its download referrer.
An optional final file path replaces the synthetic payload (up to 16 MiB).
These live probes upload only ciphertext and do not log keys, codes or object IDs;
they do not access SoundCloud or send messages. They are never run by unit/UI tests.

Progressive-media verification pauses a synthetic HTTP stream after its encrypted
preview prefix, checks the visible blurred thumbnail/caption, and verifies identical
bubble geometry after the original arrives. Unread tests deliver four messages
between explicit polls and check the numeric inbox badge while reading history.
`ChatMediaPreviewTests` exercise the same paused download through Pomf and ZIP
fallback, preview authentication, payload-splicing rejection and legacy decoding.
`ChatUnreadTests` cover batching, duplicate snapshots, own messages, external reads,
unknown historic counts and restoration of the saved watermark.

Chat caching regressions cover a legacy PKM1 download held before its first byte,
intermediate bubble widths during the skeleton-to-photo resize, original-file LRU
eviction without tearing down visible pixels, immediate cached-thread restoration
while the network is delayed, scroll restoration, and unchanged inbox avatar sources.
Per-paint checks reject intermediate scroll positions when restoring a cached
thread or acknowledging a media send, and verify stable timeline item identity
without collection replacements. Incoming, outgoing and photo-caption timestamps
are checked against the same lower text boundary.
Composer checks cover the 56 px compact row, growth from automatic line wrapping,
intermediate growth/shrink frames, bottom-pinned actions and thread anchoring during
resize. Long drafts exercise the height cap, caret visibility, hidden/noninteractive
scrollbars and the top mask's appearance and disappearance during caret navigation.
Text/placeholder insets are sampled throughout resizing and after undoing a newline
mid-animation, rejecting a final-frame switch from top alignment to centering.
The outgoing-media scenario holds the server acknowledgement and checks that the
same photo frame and bubble dimensions survive it without downloading the upload.

Rich attachment checks cover document cards with the size/time footer on one row,
images explicitly sent as documents, the paperclip's three choices, music-picker
search for tracks and playlists, canonical SoundCloud link cards and all five video
MIME mappings. The video modal must display a decoded frame and release its session
when closed.
Music messages must reuse the horizontal Liked track row, with artwork, contained
playback controls and a loaded waveform in both wide and narrow conversations.
Routed play/pause and waveform-seek clicks preserve the current queue and keep
the chat open. Playlist rows retain their title/count while following the current
track waveform and progress. Checks also require the artwork backdrop, glass action
buttons and delivery time inside the actions/caption row; recycled rows release their
playback bindings and restore the ordinary text bubble layout. SoundCloud API unit
checks cover mobile share-link redirects, relative hops, playlists, secret tokens,
browser transport routing and bounded/rejected redirect chains. Composer checks
exercise a recognized inline title/circular thumbnail through native selection, whole-link Backspace,
Undo, clicking through to its track page, navigation back and sending the original
URL/caption. Music captions must retain the ordinary message bubble color. The embedded waveform
is shorter, with the play/actions aligned to the artwork edges.
Menu checks verify the composer-aligned gap, matching surface, animation completion,
hit-testing through to the paperclip and suppression while
a file picker is open. A portrait video checks inline geometry, silent autoplay,
stopping after a second native window takes focus, resuming on activation and
stopping while the viewer is open. Composer checks require a square video poster.
Reopening the viewer three times must immediately retain a poster without loading
text or another download. Normal idle playback keeps its control panel visible. Checks also cover document/
music inbox glyphs, video volume/rate reaching LibVLC, the speed menu, intermediate
geometry during enlargement, black backdrop, the compact control width, unique
speed-preset selection and arbitrary slider speeds without dismissing the menu,
control/arrow fade and reveal in enlarged mode,
and retaining the picture during the closing fade. Outer icon positions must align
with the elapsed/remaining labels. On Linux, X11 pixel checks capture the underlying
chat before opening, then verify uniform translucent scrim compositing after each
reopening and solid black in enlarged mode; CPU previews alone cannot detect native
compositing regressions.
Native frame sampling also bounds backdrop darkness throughout opening and
reopening, rather than checking only the final frame. Replacing an enlarged video
retains its enlargement and backdrop; reopening during the closing fade preserves
the current reveal amount and reverses the animation.
The scrim must also preserve an inherited partial repaint clip, leaving pixels
outside that clip unchanged. `POOKIE_CHAT_VIDEO_FIXTURE` adds three complete
playbacks of a local video, including hover/menu activity and a large window;
native samples include letterboxed areas and distinguish live media loading from
extra darkening.
`--chat-video-live-ui-probe` runs the same playback checks over a real conversation
using the saved account and isolated local app data. Set
`POOKIE_CHAT_VIDEO_LIVE_PEER` to the explicitly authorized recipient and
`POOKIE_CHAT_VIDEO_FIXTURE` to the local video path. It opens that conversation
(so normal read receipts apply) and does not send messages or upload the video.
Routed clicks on the picture and controls must keep the viewer open, while both
letterboxing and the outer backdrop close it. A recording audio backend verifies
intermediate fade volumes, pause only after fade-out, resumption after close,
preserved selected volume, initially paused music, rapid close/reopen, replacement
of the current video and explicit transport actions cancelling automatic resumption.
`ChatRichAttachmentTests` cover video poster/
dimension/duration authentication, the inclusive 15 MiB autoplay file-size limit
(independent of duration), authenticated document metadata,
music-link extraction, caption preservation and rejected non-SoundCloud URLs.

`--chat-video-smoke-test` decodes synthetic MP4, WebM, MOV, MKV and AVI fixtures
through the production LibVLC session. Each case checks a real bitmap, pause/resume,
seeking, disposal, and decoder ownership after the caller's input buffer is cleared.
It plays silently and sends/uploads nothing. Linux requires LibVLC 3 and codec
plugins; Windows builds carry the native package, and macOS uses installed VLC.
The current native playback validation was run on Linux only.
