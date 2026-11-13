# Application integration checks

`--image-clipboard-smoke-test [image-path]` checks Linux image copying using a
private X selection: a separate process receives the exact PNG bytes and no JPEG
alternative is advertised. It verifies colour channels before transport and never
reads or overwrites the user's clipboard. The optional image can be PNG or JPEG.

`POOKIE_COMPACT_INTERACTION_PROBE=1` with `--collections-ui-smoke-test --no-audio`
isolates compact row interactions. A delayed like response checks that leaving
the button clears its hover while pending and after the request completes.

The normal application uses one embedded website session for authenticated web
API operations, protection checks and browser audio. Login and the worker share
one durable profile. A visible slider presents the existing worker in MewUI's
modal overlay, then returns it to the background. Android OAuth, APK identity,
SDK telemetry, mobile routes and isolated mobile challenge profiles were removed.

Windows uses CoreWebView2, Linux uses WebKitGTK 4.1 inside a GtkPlug (X11/XWayland),
and macOS uses a WKWebView subview. No separate browser runtime is introduced by
embedding: the Windows NuGet reference supplies the SDK for the existing WebView2
Runtime. InfiniFrame 0.62.1 remains for standalone browser diagnostics and legacy
helper commands; its window builder does not expose an embeddable MewUI control.

Browser challenge logs include whitelisted `ChallengeType` and `Interactive`
fields and retain the challenge type on success/failure. This distinguishes
hidden automatic checks from an interactive Slider in Fetch/XHR flows. Initial
document-navigation challenges cannot be reconstructed from JS Tag events.
Cookie values, account tokens and arbitrary challenge URLs/text are not logged.

This executable contains native UI checks, local browser fixtures, and storage/audio
integration scenarios. It is separate from the production `Pookie` executable

Run native UI checks sequentially. On Linux, set `XDG_DATA_HOME` to a dedicated test
directory when the production app is open: MewUI's extracted font files otherwise
share the same cache and another process can remove them when it exits.

`POOKIE_EMOJI_QUALITY_PROBE=1` with `--chat-emoji-ui-smoke-test --no-audio`
compares native GPU emoji pixels against high-quality sampling at 96/110/144/192
DPI and 18.2/22/28 DIP. It verifies fractional placement and restoration of the
incoming sampling mode. `POOKIE_UI_PREVIEW` optionally saves the reference images.

`--tooltips-ui-smoke-test --no-audio` checks the shared Material 3 plain hints:
inverse surface, wrapped text/emoji, anchor-centred placement and edge fallback,
96/110/144/192 DPI, pixel-verified enter/exit opacity, uninterrupted reversal,
hover-delay cancellation, keyboard focus/Escape, dynamic and recycled labels,
scroll/disable/deactivation/click dismissal, input transparency and overlay cleanup.
App controls register/update plain text with `.Hint(text)`; `.Hint(null)` removes
it. Native `.ToolTip` popups are intentionally not used for these hints.
The window owns one reusable surface; its small bitmap cache is released on close.
Appearance follows [Material 3 plain tooltip tokens](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/tokens/PlainTooltipTokens.kt)
and [tooltip layout/scale](https://github.com/androidx/androidx/blob/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/Tooltip.kt).
Desktop timing is 500 ms initial hover, 100 ms when switching between hints,
150 ms entrance and 75 ms exit. Each transition reverses from its current state.

`--chat-drawer-ui-smoke-test --no-audio` runs the fixture navigation setup and
checks opening/closing the profile drawer in overlay, docked and compact layouts
at 96/110/144/192 DPI. Physical widths and glass/profile raster identities must
remain stable while moving. It also checks reuse of the profile body, rejection
of a delayed palette from an earlier opening, and clearing account data on reset.
These checks also run in the complete chat-navigation scenario. They assert
layout/resource behavior; frame-cadence measurements use separate native input
probes without forced frames or detailed profiling.

`--user-profile-ui-smoke-test --no-audio` checks hydrated profile headers,
responsive banner/avatar layout, all 257 offline country flags, independent verification
and Artist Pro badges, long names at narrow/wide widths, independent delayed activity/sidebar skeletons,
avatar-derived fallback gradients, cached banner crossfades and stale-image isolation,
pixel-verified header action blur (including confirmed follow), foreground exclusion,
cached stable snapshots and blur updates during gradient transitions,
content reaching the shared bottom fade, seven sections, followers/following navigation,
linked selectable biography, library-sized artwork and right-column action rows,
activity waveforms, playlist previews and confirmed playlist likes/reposts/queue actions,
likes/followers/following and recent comments sidebar, height-based biography overflow,
zero-track metric hiding, complete user comment pagination, shared header/body scrolling, activity append/dedup
and playback for listeners with no own tracks, virtualized track pagination, retry,
history/scroll restoration, artist playback context, confirmed follow writes and
late-response/account isolation. All writes use a fixture account.
`--user-profile-network-probe` reads the saved account's real public profile,
external links and all music/social sections (including activity and next pages), without writes.
Set `POOKIE_PROFILE_PROBE_PATH` to a dedicated temporary directory to run it while
the normal app is open; it creates a separate browser profile without copying cookies.

`--chat-emoji-ui-smoke-test --no-audio` checks the anchored Emoji/GIF panel,
native caret/selection/undo and atomic keyboard backspace, a centred two-mode icon
footer with passive selection, clipboard probing without dimming the composer buttons, Russian search
and its header transition (categories return after clearing, interrupted animation,
and switching back from GIFs without reopening the panel), virtualized
offline artwork and its bounded cache, GIF skeletons/previews with a local HTTP
handler (separate delayed metadata/thumbnail skeletons), animation cleanup, quick and expanded message reactions, 36-character
transport references, reaction/timestamp geometry, dismissal after pointer leave, narrow/wide windows, Escape,
conversation changes, and 10,000 menu rebinds. It never sends real messages or
opens the microphone. GIFs remain available through the existing KLIPY setting.

Emoji artwork comes from Apple Color Emoji via samuelngs/apple-emoji-ttf release
`macos-26-20260722-484daf4e`; source checksum and licensing scope are recorded in
`licenses/LicenseRef-Apple-Color-Emoji.txt`. The 3941 offline 72px images are shared
by the picker, reactions and the application text engine: names, comments, track
labels, menus, search fields/results, reply previews and message bodies. Inline presentation keeps
Unicode text, selection, whole-sequence deletion, undo and sent bodies unchanged.
Decoded artwork and reusable inline GPU textures are each capped at 192 entries.
The three standalone symbols absent from that font remain ordinary text and are
not offered as blank picker cells. Native checks include inline rendering and
Unicode transport alongside the existing DPI, music-chip and reaction scenarios.
Ordinary TextBox/TextBlock checks verify that rendering does not depend on a chat
specific control. Delayed GIF fixtures check the first restored frame, bottom
anchoring after intrinsic-size changes, and encrypted disk reuse by a fresh loader.
GIF bytes share the account media budget and favorite pinning; uploaded attachment
containers are cached immediately. All requests in these checks use local handlers.

`PUBLIC_EMOJI_EDITOR_OK` additionally disables the compatibility text-layout hook
while checking the composer's public generator/transformer pipeline: family/skin-tone/
flag artwork metrics, original Unicode selection, Backspace/Undo and stacked-mark
protection. Standard labels and single-line editors retain their compatibility hook.
Chat geometry checks sample rendered frames without an injected Window callback.
Metadata checks traverse the logical comment tree so parked rows remain covered;
the visual tree deliberately contains only realized rows for rendering and captures.

The chat music/media checks also hold an older network page while scrolling the
initial 30-message conversation to its upper edge. History must load automatically,
preserve the visible message anchor and stop requesting an exhausted cursor; there
is no manual earlier-history button. They cancel an in-flight page by changing
conversations, resume automatic paging on return, and walk 300 cached messages in
both directions with at most 200 live messages. The bubble anchor must survive
date-badge changes and asynchronous SQLite metadata reads. Footer fixtures use their
own complete SQLite history so automatic paging cannot mix unrelated messages into
the synthetic rows. The cold-search fixture explicitly excludes the already loaded
older page from SQLite before scanning; automatic timeline prefetch must not turn
a network/indexing test into a cache hit. Repeated fixture pages retain stable
timestamps and message identities.

Local GIF API credentials use .NET User Secrets, shared by the app and this host
through `UserSecretsId=Pookie-development`. Set `Klipy:ApiKey` with:

```sh
dotnet user-secrets set "Klipy:ApiKey" "<your-key>" --project src/Pookie.App
```

Restart the app after changing the key. The secret stays outside the repository
and is not embedded or copied into published builds. A missing or invalid local
secrets file leaves GIF search unavailable. Automated checks use local HTTP
fixtures and do not consume the real API quota. No `.env` file is needed.

On Windows, `POOKIE_RENDER_PROBE=1` with `--expanded-glass-ui-smoke-test --no-audio`
runs an isolated rendering diagnostic. It records player opening time, lyric frame
times with the underlying page visible/hidden, and scrolling frame times. For the
hover check it briefly brings the fixture window above other windows, waits for
stable DWM presentation, and checks three native background pixels while hovering
both feedback buttons. Run this diagnostic with the desktop unlocked and no other
UI checks running. Clear the environment variable afterwards.

`POOKIE_SCROLL_PROBE=1` with `--track-page-ui-smoke-test --no-audio` measures
the detail pages with 500 long/threaded comments, appending 50 comments without
moving the viewport, returning from the bottom to the hero, and a 1000-track
playlist. Each page gets 540 continuous 12-DIP scroll steps across the hero,
middle and bottom, plus large cold viewport jumps in both directions. These are
separate results: `CONTINUOUS_SCROLL_RESULT` and `DETAIL_SCROLL_RESULT`.
`POOKIE_SCROLL_PLAYLIST_ONLY=1` skips the synthetic comment workload. The
1000-track fixture uses preview mode with disabled like buttons, while the
authenticated playlist probe uses the regular account controls.
`POOKIE_SCROLL_CONTINUOUS_ONLY=1` skips the large-jump paths. Samples measure
input to `FrameRendered`, before native presentation; they are not display FPS.
Add `POOKIE_SCROLL_PLAYING=1` and omit `--no-audio` to exercise the real playback
poll timer, advancing fixture position, waveform colors and progress animation
while scrolling both pages. The fixture replaces the audio transport only.
`POOKIE_SCROLL_SETTLED=1` additionally waits for initial artwork/palette and player
height transitions; omit it to include playback startup in the scroll samples.
On Windows, `POOKIE_SCROLL_TOUCHPAD=1` replaces the continuous offset steps with
2160 native fractional wheel messages per page, direction changes and a comment
avatar hover. It exposes the fixture window and verifies alternating pixels on
the desktop for every produced state, plus periodic samples of the scrolling
page body. `FrameRendered` alone cannot detect a frozen presentation. Its timing
includes DWM synchronization/readback and is not a scrolling FPS benchmark.
The probe rejects a collapsed/minimized viewport and times out a stalled frame.
Use a Release build, run it alone without background builds, and clear the
environment variables afterwards. Optional `POOKIE_SCROLL_PROFILE=1` enables
the framework profiler for attribution and adds measurement overhead.
`--comments-ui-smoke-test` also compares the cached paragraph renderer against
standard TextBlock pixels across fractional widths, Unicode and emoji.
The chrome check verifies native-DPI page sampling,
and pixel-readback motion of a contrast edge through the blur: each one-device-pixel
step must remain continuous instead of freezing and jumping across the reduced grid.
Windows Direct2D keeps capture and Gaussian blur on GPU surfaces at native DPI;
the transparent filter halo stays outside the visible header. CPU fallback keeps
exact area reduction, while other reduction paths use aligned half-size stages.
The real window's computed backdrop is checked before any isolated render: visible
pixels must be opaque, contain page variation and change after scrolling. Isolated
GPU-effect checks use GPU-compatible intermediate targets, then read pixels back.
The check also covers
shared SVG visibility across independent render targets,
and that first/repeated Direct2D blur passes preserve the caller's target, DPI,
transform and clip. OpenGL passes preserve independent read/draw framebuffer
bindings, viewport, program, VAO and texture bindings. The chat music/media scenarios
also check wrapped text and timestamp placement at 96, 110, 144 and 192 DPI.
Expanded-player checks
require spring overshoot in both directions, retained layout and no panel overlap.

`--external-browser-smoke-test` checks Linux external URL launching with a private
`xdg-open` fixture: exact argument passing, stdout/stderr isolation including
browser descendants, independent browser lifetime and nonzero opener exit codes.
It does not launch a real browser or change desktop associations.

`POOKIE_SEARCH_ONLY=1` with `--chrome-ui-smoke-test` runs the existing search layout,
virtualization and scrolling checks after the header glass pixel check.

`--chrome-ui-smoke-test --no-audio` verifies the three glass header cards,
same-frame backdrop updates with clear gaps between cards, square scroll viewports, the player-edge fade,
one-to-one page scrolling with virtualized lists, the final item, responsive bounds,
and back navigation with scroll restoration. The native scrollbar's track and hit
area start below the glass header; native thumb dragging covers both ends of the range.
It also runs the search/overview wheel,
grouped-pagination anchor and continuous-scroll checks. Set `POOKIE_UI_PREVIEW` to
capture the resting and scrolled chrome at desktop sizes.
The same check opens the header's glass search at 1000 and 1600 DIP widths,
checks native input alignment/binding and outside-click dismissal, and activates
the compact connected history button near its inner edge without a stuck focus cue.
Fade checks compare rendered pixels with/without the compact mask on Home and a
virtualized cover grid. They verify soft gradient endpoints, visible content at the
player boundary, persistent fading without the player, animated opacity changes,
and a fully readable final item at the end of scrolling. On Windows the gradient
is cached as one physical column at native DPI; these checks also catch partial
column coverage at fractional DPI reducing the mask's opacity.
Glass-edge and conversation-fade checks also compare the reused local-coordinate
geometry/brushes against the original absolute-coordinate native drawing at
96/110/144/192 DPI and fractional positions. Moving a list row or chat drawer must
retain the original native coverage without rebuilding its geometry and gradient
stop collections for every new scroll position.

Pure queue model checks live in `tests/Pookie.Playback.Tests` and run without a
native window: `dotnet test tests/Pookie.Playback.Tests`. They cover manual entry
priority/editing/duplicates, actual history and forward traversal, stable shuffle,
repeat/end behavior, immutable snapshots and independent cancellable pagination.

For the glass performance regression, run `--mixes-ui-smoke-test --no-audio`
with `POOKIE_GLASS_PERF=1` and `POOKIE_PERF_CAPTURE=/tmp/glass-frames.json`.
The isolated fixture reports repeated profile-menu frame times, first visible
menu paint, and continuous waveform scrolling in a mix. Omit `POOKIE_UI_PREVIEW`
when comparing timings. `GLASS_MOTION_CACHE_OK` in the chrome check independently
asserts that fractional movement and hover reuse one optical raster.

`--expanded-glass-ui-smoke-test` checks fullscreen glass controls with a synthetic
cover: actual pixel softening, fresh samples after artwork replacement, connected
like/repost selection shapes, a native shuffle click and pointer-focus feedback.
It also verifies fullscreen opening/closing, cover hover and repeated paused reentry.
Transport controls must share a vertical center, and rendered glass buttons keep
at least 4.5:1 foreground contrast on both white and nearly black artwork fixtures.
Use `POOKIE_UI_PREVIEW` for the cover controls and selected feedback screenshots.

`--feed-ui-smoke-test --no-audio` checks the feed with an isolated metadata
fixture: virtualized track/playlist rows, event actors and dates, original/repost
filtering, custom Track Art, nonzero counter layout, 184px publication geometry,
responsive sidebar pinned below the shared chrome after scrolling, shared hover-arrow
history heading and cover alignment,
deduplicated pagination, filtered playback-queue pagination,
history/scroll restoration, retained content on refresh failure and stale replies.
Set `POOKIE_UI_PREVIEW` to capture wide/narrow layouts and playlist previews.
`--feed-network-probe` reads two pages using the saved account, reports event
counts/shape without identities or credentials, and makes no account writes.

`--settings-ui-smoke-test --no-audio` checks the settings gear through native
clicks, all seven library-style tabs at 1000×680, back/forward section and scroll
restoration, the persistent likes view, real playback/lyrics/history policy
switches, preserved current/manual queue entries, and configuration round trips.
The 100/150/200% audio limit updates both players' sliders and the backend;
reducing the limit also clamps the current and remembered unmute volume.
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
Selecting an upcoming track keeps the ordinary player's queue panel open.
It uses isolated account data and fixture metadata/streams with a fake audio player.
Queue unit tests also cover stale/cancelled results, single-flight prefetch,
source pagination, deduplication, explicit recommendation selection, transient
retry, empty results, ordered related-page traversal and explicit radio with autoplay disabled.

`--chat-media-ui-smoke-test --no-audio` runs the full rich-attachment checks
independently of composer layout: file/image/video fixtures, draft previews,
viewer playback and controls, and music fading/restoration. It uses isolated
account data and fixture transports; no real messages or uploads are sent.

`--home-ui-smoke-test --no-audio` checks official SoundCloud mixes and trending
tracks, independent loading and retry, responsive grids, cached Home content,
account isolation, and native radio/play/card clicks. Radio and continuation use
SoundCloud's related-track order and filter exclusions, unavailable tracks and
likely reuploads. Explicit speed edits remain distinct. Exclusions from the
former Wave profile are imported into the account's radio settings.

`--mixes-ui-smoke-test --no-audio` focuses on the library's personal mixes: all
mixes, Daily Drops and Weekly Wave in one overview shelf and a dedicated tab,
shared discovery caching, the mix header with leading artwork and playback/queue
actions, hidden library tabs on detail pages, filtering, back navigation and
retry after a failed load. It uses an isolated fixture account and metadata
transport. `POOKIE_UI_PREVIEW=<directory>` saves wide/narrow detail and library
previews.

For a passive carousel performance probe, set `POOKIE_MIX_PROBE=fixture` or
`POOKIE_MIX_PROBE=live` and run the same Home command. Live mode reads the saved
account's actual mixes and artwork through a separate temporary browser profile;
it does not submit playback, history, likes or messages. The probe observes
`Window.FrameRendered` while the covers rest, during five side-cover clicks,
and after the pointer settles. It does not request frames during measurement.
Active frame intervals exclude the intentional idle wait between completed turns
and the next click. Optional `POOKIE_PERF_CAPTURE=<absolute-json-path>` enables
MewUI timing/allocation counters; `POOKIE_UI_PREVIEW=<directory>` saves a visual
capture. `icon_raster_builds` reports SVG texture creation during each phase.

`--track-page-ui-smoke-test` checks native title navigation without starting audio,
centred player metadata and animated title/artist links, artist navigation and
idempotent title clicks, comment skeletons and avatar fades, waveform preview
appearance/resizing/dismissal, live glass waveform previews with white author names and seeking passthrough,
timed comment cues with dense-comment skipping and hover priority,
responsive reference hero, stable single-line title/artist/metadata geometry across
repeated narrow/wide resizes with long text, metadata/artwork/waveform, threaded comments and replies,
sidebar fans and 2x2 playlist previews, shared library profile/playlist cards and
waveform rows in related tracks, compact preview alignment and duration spacing,
continuous resize in both directions, nonpersistent action focus, track subsections
(reposts/albums/playlists/related), infinite loading and deduplicated pagination,
back/forward restoration, cancellation and stale replies, and the shared player's
queue and seek. Description checks cover wrapped links, hover and click, drag
selection without navigation, Unicode copying and noneditable text, smooth link
colour transitions, circular favicon/fallback rendering, PNG/ICO decoding and favicon
memory/disk caching. Telegram shorthand checks cover TG/TGK/ТГ/ТГК labels,
click/hover, source-text copying and exclusion of nested or invalid matches. Description
replacement during hover checks Unicode boundaries with emoji, combining marks,
CRLF and empty text. Focused descriptions are pixel-checked for an absent insertion
caret across a blink interval; descriptions share message-body selection and
are TextBlock controls without an editor or text input client. On Linux it
opens Share from the hero and list, clicks its copy button, resolves a missing
permalink and reads the new URL from a separate process using a private X selection;
the user's clipboard is untouched. The cover menu is checked on real artwork
and suppressed on a placeholder. The writer and search inputs also work with
`GDK_BACKEND=wayland` and no GTK clipboard service. The hero checks rendered
forward/reverse background, icon and count colours, interruption, and the absence
of a faded disabled state during pending requests. It uses an isolated demo profile
and a local browser-transport fixture.

`--track-description-ui-smoke-test --no-audio` isolates the focus regression,
including a routed click on a description taller than the viewport. Focus must
preserve every enclosing scroll offset while keyboard selection remains available.
`--scrolling-ui-smoke-test --no-audio` additionally checks that a stalled spring
and window deactivation/reactivation discard old destinations before the next input.

`--track-playback-ui-smoke-test --no-audio` checks playback with 500 comments:
restricted 30-second metadata uses the full duration for the card, waveform and
comment timestamps, including a late preview response after playback resolves;
the model and footer advance while the offscreen hero's label/waveform stay stable;
scrolling back and closing the expanded player catch up from the retained position,
including pause and a track change. It also runs pixel comparisons of cached comment
text against TextBlock, including ellipsis, Unicode and repeated fractional widths.
The same check verifies that a small avatar uses its DPI-dependent pixel budget,
shares repeated requests and does not replace a full-size cover in the artwork cache.
Set `POOKIE_TRACK_DURATION_ONLY=1` to run only the preview/full-duration regression.

`--waveform-ui-smoke-test --no-audio` checks the 620ms left-to-right spring reveal
in a list row and the track hero. Pixel readbacks verify intermediate frames,
unchanged layout, identical settled pixels and stopped recycled animations.
It also checks first-paint reveal history through the real list loader, unchanged
data without restart, and the existing hover/play/progress/seek color transitions.

`--likes-filter-ui-smoke-test --no-audio` checks library-only title/author filtering
in grid and list views. It follows six delayed metadata pages, including three
empty pages and more matches after a full viewport, without global search or writes.
It checks exclusive loading/empty states, completed-cache reuse, failure and retry,
query changes during a request, clearing the filter, cursor-cycle protection and
navigation cancellation. Filtered pagination uses a compact progress notice instead
of unrelated waveform/card skeletons.

`--likes-edits-ui-smoke-test` uses an isolated account fixture to check per-track
pending state, duplicate suppression, animated removal/insertion and movement in
likes, stable scroll and pagination, focus recycling, failed writes and late replies.
Like/unlike hearts update before initial ID/session checks and stay selected through
pending verification. The fixture samples intermediate rollback colors and covers
background cache refreshes, cancellation and account changes without committing
unconfirmed IDs. The embedded web challenge fixture also rejects an action after
the slider passes and checks that the optimistic heart rolls back.
It also checks existing reposts, add/remove state, isolated pending buttons, duplicate
suppression, failed writes, own-track restrictions and stale session replies. All
account mutations use a local fixture transport.

`--comments-ui-smoke-test` checks the animated avatar composer, Escape/outside-click
collapse with draft preservation, uninterrupted send focus, send click, confirmed insertion,
duplicate suppression, retained drafts after failures, server sorting races, timestamp hover,
own-only deletion, reply preservation, disabled comments, and stale account responses.
It uses a fixture transport and never posts or deletes real SoundCloud comments.

`--collections-ui-smoke-test` checks the compact playlist rows without numbering, row menus,
fixed like/duration columns, playback counts, full-cover play scrims and reversible
hover-action fades with keyboard focus and hidden hit targets. Card/list switching
uses the likes view controls, virtualized cards and original playlist positions;
it preserves the filter and playback queue. It also checks
filtering without changing the full playback source, expandable description,
narrow/wide hero layout, back-navigation filter/scroll, playlist likes/reposts,
following/unfollowing, isolated pending states, playlist selection and stale account
replies. All writes use a local fixture transport.

`--share-ui-smoke-test --no-audio` runs the Share checks independently of the
collection action/hover scenarios. It measures the first and two repeated opens,
checks stable body geometry, one GPU glass backdrop per opening, cached standard
perspective/blur animation and backdrop reuse during hover. It verifies sampled
red/blue page colors through the glass and then exercises all Share tabs/actions.
The initial body size is settled before opening; only live tab changes resize it.
The body/plaque graphics are warmed before the startup spinner starts, and the
external close button uses its glass finish without CPU backdrop readbacks.
Native frame intervals must have p95 at most 33.4 ms and no gap over 100 ms.
Run without `POOKIE_UI_PREVIEW` for timing, then enable it for screenshots.

Tab resizing has its own checks: nine transitions cover all tabs, repeated entry
and empty recipients, followed by interruption/reversal. Their p95 budget is
16.7 ms with no interval over 50 ms. The menu samples its maximum body envelope
once and reuses that blur across all heights, including the settled state. The
GPU optical composition updates in the same fixed-capacity surface on every
intermediate frame; there is no frozen optical image replaced at completion.
Checks assert the texture's identity, current geometry, zero output allocations,
and zero background/blur or reflection raster regeneration during each resize.
Rounded ends of the sheen retain their size while only its flat middle stretches.
Foreground controls keep their normal layout and font sizes.
The shadow uses a cached corner patch. Hidden tab graphics are warmed before the
startup spinner, without opening Share or making recipient requests.
Preview mode also captures an intermediate frame as `share-tab-resize`.
The dark matte tint leaves only subtle background color. A pixel check compares
glass immediately before the opening animation hands off to live content with
the settled result, preventing a last-frame material change.

`--playlist-dialog-ui-smoke-test --no-audio` runs the matching playlist picker
checks separately from collection hover actions. Share and the picker use the
same matte tint/body builder and GPU backdrop path. The picker also reserves a
maximum-height transition surface, so a fast list response during opening does
not allocate a new snapshot for each intermediate height. Its list, creation
form and skeleton are warmed before the startup spinner starts; the close button
uses the light glass finish without CPU backdrop readbacks.
Checks measure cold opening, loading-to-list, list/form transitions, reversal and
an immediate list response during entrance. They assert texture identity, current
GPU optics, no extra background captures/reflection rasters, and pixel continuity
at the final animation handoff. Existing picker checks cover empty/long/error
lists, fixed scroll viewports, drafts/privacy, single seeded playlist creation,
write-error handling, keyboard focus/dismissal and snapshot cleanup. Service
writes use only the local fixture transport.

The picker has a stationary Share-style Add/Create tab header, with no plus/back
navigation or redundant page title. Both dialogs use `DialogPageDeck` for the
same 260ms fade-through/24 DIP slide, independent page viewports and continuous
retargeting; outgoing and incoming contents never paint simultaneously. Share
keeps the native widget behind its cached frame for the complete tab transition.
The empty creation form omits the submit action and its space. Typing reveals it
with opacity/slide and a 280ms card-height transition; clearing the title reverses
that reveal. Drafts and privacy survive tab changes. Both track plaques show
duration and compact play count (or an em dash when unknown).

The Share checks use the same `TrackDialogChrome` plaque as playlist dialogs,
with equal 16 DIP insets and no playback controls or redundant dialog headings.
They cover five destination icons (Telegram/VK/X/Snapchat brand marks and email),
even spacing with the outer button edges aligned to the URL row, and the official
white/black Snapchat Ghost on yellow and X on black. The URL and timecode share
one rounded glass surface; copying remains a separate circular button with the
same blue glass accent as Widevine acceptance. Its white copy/check glyph animates
both ways, returns after two seconds, restarts that delay on repeat copying, and
clears on link changes or closing. Failed clipboard writes never show success.
The URL and widget code use single-line text displays without a caret. Share opens
with neutral focus; changing tabs never activates an editor automatically.
The five destinations are colored circular buttons
with captions beneath them. The timecode popup always shows minutes/seconds and adds hours for long tracks,
focuses the first visible field, and validates drafts without changing the applied
link. Invalid inputs and digits animate red and shake; correcting them restores
their colors. Fields have labels without redundant tooltips or error prose.
Checks also cover the plaque's right-aligned duration and play count, Enter/Escape/Tab,
popup focus, blank-area clicks that dismiss the caret without closing the popup,
pixel-verified live backdrop colors and blurred edges without foreground feedback,
native retained card repainting before offscreen probes can mask a stale image,
animated time label/button width, zero timestamps, private URL
preservation, and focus return after closing. The message tab has glass search and
message fields, a white input magnifier with reversible spinner animation, clickable
scrollbars in the card gutter, a glass pencil button and a blue glass Send button.
Send keeps its label while a spinner appears and the button fades smoothly. The
busy appearance remains through successful modal dismissal and resets after closing.
Sending commits to the durable outbox before one POST and never navigates to chats
or modifies their drafts. Checks cover duplicate clicks, closing/reopening while a
send is pending, rejected sends and lost acknowledgements without automatic replay.
Opening Share never sends a message. Recipient checks cover circular cached avatars,
left-aligned names and profile details, recent conversations, clear/loading states,
cancelled searches and row selection.
List captions are omitted; an empty search shows the bundled route sticker with
the “А тут тупик” title and its description, and clearing the query restores users.
The Widget tab checks parse the generated iframe, verify escaped/private URLs,
layout/color settings, disabled autoplay, invalid-color handling, and exact HTML
copying with an isolated clipboard. No real message or social post is sent.
The live preview starts its own isolated browser only on the Widget tab, uses
the official iframe/API, reflects color/layout changes, and pauses/hides outside
that tab. The 450px visual player fits in a taller dialog; small windows scale
the complete preview above the compact code/copy row. Transitions use a cached
frame while the native browser is hidden, and closing releases its profile/engine.
Iframe navigation never launches the system browser.
The player mode uses the playlist privacy segmented control with an opt-in glass
pill that stretches during travel. Its fixed optical raster is transformed on the
GPU; inactive options animate only their icon/text color on hover.
A separate color row offers five population-ranked artwork colors, HEX editing
and an HSV/RGB picker. HEX and the palette action form a connected glass group;
the action has the same light glass hover state as the other dialog buttons.
Sampling is bounded to 64×64 pixels and runs off the UI
thread; a late palette never overwrites the selected color. Presets, HEX and the
picker all update the iframe using its exact requested URL in the existing host,
retaining the displayed frame without restarting the whole browser page or loader.
The production `api.js` currently coerces `load()` options (including `color`) to
booleans. Refreshing the iframe and binding a fresh widget proxy preserves actual
HEX colors and a fresh READY boundary. The live probe checks more than protocol
completion: two distinct preset colors must appear in captured player pixels,
in both compact and visual layouts.
The picker uses the
in-app animated glass popup rather than a native popup window. Before it opens,
the browser's current frame is captured and painted, then the mapped native child
moves offscreen without a GTK unmap/remap or pause/reload. The frame stays through
the handoff: on X11 moving the child and replacing its exposed front-buffer
rectangle use one server transaction, avoiding the server's app-background clear.
The code waits for the outer window present, not the earlier retained-surface
`FrameRendered` callback. The saved frame also stays through
the complete popup exit; color edits retain it and defer widget refresh until the
popup is gone. Modal dismissal also captures/presents the current player before
its staged exit. Native tests sample actual desktop pixels every 5ms and after
window frames during popup entry/exit, preset refresh and modal dismissal, rather
than checking cached pixels alone. Loading uses the captcha overlay's SoundCloud
mark with its transparent asset gutters cropped; the visible mark/spinner group
is centered with a 16 DIP gap. Share's tab indicators snap when resetting to Link,
and repeated Widget/Message dismissal/reopen cycles assert only Link is underlined.
Both the
web page's clipped viewport and the cached frame keep the same 12 DIP corner
radius through mode changes and modal dismissal; native checks verify pixels.
The copy tooltip prepares the same frozen native frame before its first visible
paint and retains it through dismissal. A desktop-pixel check verifies that its
inverse surface really covers the browser, including the overlapping preview area.

Set `POOKIE_WIDGET_NATIVE=1` with the Share smoke test to exercise the actual platform
browser on an isolated local page. For a read-only live widget check, set
`POOKIE_WIDGET_REAL_ONLY=1` and `POOKIE_WIDGET_REAL_LINK` to a public SoundCloud URL.
It loads the real widget with autoplay disabled, captures its pixels, and closes it.
`POOKIE_SHARE_FUNCTIONAL_ONLY=1` runs the Share interaction/regression scenarios
independently of the opening/resize performance benchmark, useful when unrelated
desktop work makes its 16.7ms frame budget unstable. The default benchmark and its
thresholds remain unchanged; native desktop-pixel handoff checks still run with
`POOKIE_WIDGET_NATIVE=1` in the functional run.

`--memory-ui-smoke-test` creates and discards three batches of 100 sidebar cards with
500px artwork, including cards removed during an active loading shimmer. Weak-reference
checks verify that removed controls/pixels collect, cached SVG icons do not retain views,
and live/recycled loading layers stop and resume their clocks. It prints managed memory
and process RSS for diagnostics. Its rendered-resource checks also bound GPU growth
when Linux DRM counters are available. It uses isolated app data and makes no SoundCloud requests.

`--playlist-page-network-probe <SoundCloud playlist URL>` verifies real playlist
metadata, ordered track loading and playlist like/repost state reads through the native WebView.
Close the normal app first: this probe preserves and leases the saved browser profile.
It makes no account writes.

`--playlist-page-live-ui-probe <SoundCloud playlist URL>` opens a public playlist
with at least 100 tracks in the real Windows UI, using the saved account with
isolated browser and artwork caches. It measures cold opening, sends native
fractional wheel messages on a separate timer while covers load, then runs the
same continuous-scroll probe used by the 1000-track fixture. It checks movement
with playback stopped and read-only account requests. The wheel result reports
frame notification intervals. The continuous-scroll result reports elapsed
time from an offset update to the frame notification.
Set `POOKIE_PLAYLIST_COVER_PROFILE=1` to replay the same native wheel path with
cold and prewarmed artwork, and split frame intervals by newly visible artwork
sources. This mode also checks that pointer controls hide during movement and
return under the stationary cursor when scrolling settles.

`--track-page-network-probe <SoundCloud track URL>` verifies real metadata, timed
comments/replies and pagination, sidebar fans/collections, all four subsection endpoints and their next pages, related tracks and waveform through the production browser
transport. `--track-page-live-ui-probe` additionally opens the real "Этажи" track in
an isolated application window, clicks title/play/pause/waveform/comment timestamps,
decodes actual audio at zero volume and checks back/forward. The UI probe uses
the saved account with an isolated browser profile, so the normal app can stay
open. The network probe still leases the saved browser profile and needs the
normal app closed.
They do not change likes or write to the clipboard.

`POOKIE_SCROLL_RESIZE=1` with `--track-page-live-ui-probe` sends alternating
native wheel packets through the track hero immediately after a resize. Use
`POOKIE_SCROLL_TRACK=<track ID>` to select the track and `POOKIE_SCROLL_NOTCH=1`
to send full wheel notches. Add `POOKIE_SCROLL_LOADING=1` to start playback
immediately before the resize and verify that the first wheel packet is
handled while audio is preparing. `POOKIE_PERF_CAPTURE` enables the first
resize frame's render timing and frame indices. `resize_to_movement` measures
from the native resize request until the first frame with a changed scroll
offset returns from the window's Present call. Slow frame intervals include
timestamps and loading state for comparison with CPU traces.
The total and the wheel/draw/present intervals also work with profiling disabled,
which keeps the timing path the same as a regular Release build.

`POOKIE_LIVE_SESSION=1` with `--track-page-live-ui-probe` visits Home, Feed,
search and its playlist tab, a real playlist, Likes, a protected track, the
expanded player, the artist profile and the Chats list. Use `POOKIE_PERF_CAPTURE`
for individual render times. A worker posts one outstanding dispatcher sample
at a time. The probe logs slow render frames and dispatcher waits separately,
with stage and UTC timestamps for matching them to a runtime trace.

`POOKIE_SEEK_LIVE=1` exercises the slider and waveform
on `POOKIE_SEEK_TRACK=<track ID>` and measures when playback advances from the new
position. Add `POOKIE_SEEK_IMMEDIATE=1` for a far waveform jump as playback starts.

`POOKIE_SCROLL_LIVE=1` with `--track-page-live-ui-probe` loads up to 500 real
comments, plays the real stream at zero volume, and measures 540 scroll frames
across the hero, middle and end. It reports unique decoded avatar dimensions and
RGBA byte counts before and after scrolling. It also measures six composer
open/close cycles and native wheel direction changes before and after eight
seconds in an inactive native window. It neither substitutes the audio player
nor generates comment/portrait fixtures. Add `POOKIE_SCROLL_PRESENTATION=1` to
send another 540 native wheel notches and verify each presented state through
DWM desktop pixels, including hovering a timed comment. Those timings include
desktop readback and are separate from application frame intervals.

`POOKIE_COMMENT_PROBE=1` with `--comments-ui-smoke-test --no-audio` measures the
same composer and wheel paths with 500 local comments and an advancing fixture
player. It prints frame interval percentiles and UI-thread allocations across
the opening/closing cycles; the ordinary comments test retains the pixel-level
in-place action opacity, fixed button bounds, nonoverlapping width transition,
focus, reversal, draft and send checks.

Run from the repository root:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --memory-ui-smoke-test
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

`--login-ui-smoke-test` also checks the shared arc spinner at 12, 16, 22, 24 and 34 DIP:
rendered pixels change while animating, centres remain transparent, and clocks stop for
hidden, transparent, detached and inactive controls, then resume on reattachment.

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
Audio unit tests exercise full-track encoded MP3/HLS downloads while PCM is not
consumed, reuse across seeks, sparse byte ranges, non-range disk spooling and
temporary-file cleanup. Recovery fixtures interrupt responses, restore connectivity,
verify that underruns preserve the playhead without reporting end-of-track, and
cancel retries on stop. Browser audio tests check encrypted disk prefetch while
paused, reconnecting without replacing the media element, cancellation/cache cleanup
and terminal authorization errors. These checks use local fixtures rather than a VPN.

`--content-blocker-smoke-test` checks reviewed rules and actual Chromium request
cancellation. The same embedded rules apply before navigation in the login and
worker partitions. SoundCloud API/media/license, OAuth and challenge resources
remain allowed.

`--startup-network-probe` validates a saved account with the standalone InfiniFrame diagnostic
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

`--stream-formats-probe TRACK_URL` compares the advertised lists from `/resolve`
and `/tracks/{id}`, then inspects the latter (used by production playback) with
the saved account in an isolated browser profile. It reports endpoint status,
protocol, preview flags, playlist duration and Widevine/PlayReady/FairPlay/AES-128 markers
without printing signed URLs or tokens. It does not load a CDM, request a DRM
license, play audio or record playback history.

The native reference check runs independently of the SoundCloud profile and
can run while the app is open. It prepares Pookie's own Widevine CDM of the same
architecture as the process, acquires a license for the public Shaka Angel One
sample, and decodes one encrypted AAC fragment to non-silent PCM. Run the same
command on Windows, Linux and macOS:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --native-widevine-reference-probe
```

On Linux/macOS, start Pookie normally and accept the Widevine terms in its startup
overlay before running native CDM probes. Normal Windows startup uses PlayReady
and skips Widevine setup. Explicit native Widevine diagnostics on Windows require
a separately provisioned component and acceptance receipt; the default Windows
protected-audio scenario uses PlayReady. Probes do not accept terms on the user’s behalf.
Before showing a new consent card, startup queries Google's component updater,
verifies the offered archive's exact size and SHA-256, and reads its `LICENSE`.
After acceptance it installs that same downloaded archive into Pookie's
`Components/Widevine/<runtime>/<version>` data directory. No browser or Firefox
metadata is needed, and no Widevine terms are embedded in the app. Later sessions
validate and reuse the accepted installed component offline; a new consent card
requires fresh terms from Google. Interrupted downloads do not replace an existing
installation. The original vendor `LICENSE` stays beside the library. See
[`Audio/Drm/README.md`](../../src/Pookie.App/Audio/Drm/README.md) for supported
platforms, updates and the separate Widevine licensing requirement.

`NATIVE_WIDEVINE_REFERENCE_PCM_OK` confirms the local CDM and decoder work with
the public sample. On Windows or Linux with a valid Pookie session, use
`--protected-audio-smoke-test TRACK_URL --drm-native-cdm` for the SoundCloud
license and playback check on the same host. In the Windows comparison recorded
in `be564f28`, the public license succeeded while the SoundCloud native license
request returned HTTP 403.

To check the native host's VMP status separately, run the Castlabs lab's public
Tears of Steel license exchange through Pookie's CDM. The default is Widevine
UAT. Add `--vmp-production` to use the lab's production backend for the same
test content:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --native-widevine-vmp-probe
```

`NATIVE_WIDEVINE_VMP_STATUS` reports the explicit status in the server's license
response. `not-reported` means the response provided no recognized status.
`PLATFORM_UNVERIFIED` describes this host for the public test and is expected on
Linux. To attribute a SoundCloud 403 to VMP, look for an explicit host verification
rejection in the `POOKIE_DRM_DIAGNOSTICS=1` log on Windows, or compare the same
native host and CDM against the same SoundCloud track before and after approved
VMP signing. A public test license can succeed with an unverified host.

On Windows, a comparison for «Тяжело» and «Не ревнуй» sent both challenges from
the same WebView2 page using the same token and request headers. SoundCloud
returned `403` for the native host's challenge and `200` for WebView2's EME
challenge. The rejection did not include a specific verification error.

The default protected-audio scenario uses native PlayReady/Media Foundation on
Windows and native Widevine/SoundFlow on Linux. It checks decoded
playback progress, duration, seek, pause/resume, one-shot end, restart and stop.
The Windows scenario is muted by default; `--system-audio` uses 15% volume to
verify unmuted playback. `--drm-native-cdm` selects the native Widevine route
and `--drm-browser-audio` selects standalone WebView2 EME on Windows for comparison.

On Windows, `--direct-protected-audio-smoke-test TRACK_URL [TRACK_URL ...]`
restores a copy of the saved session into an isolated app profile, resolves
and plays each track through the main window, and checks that WebView2 never
starts during startup or playback.

`--embedded-protected-audio-smoke-test TRACK_URL [TRACK_URL ...]`
uses the main window and its normal playback resolver. It verifies PlayReady
playback at 15% volume, a forward seek while paused, buffered playback after
resume, backward seek and switching tracks. Its isolated WebView2 profile is
used for SoundCloud requests, and the test asserts it receives no audio events.
The check skips playback history.

`--browser-audio-state-smoke-test` uses local fake transports without a website
or CDM. It verifies routing, volume, seek, pause, one-shot end, ignored stale-track
events, worker failure and cancellation when switching tracks.

`--browser-drm-capability-probe` reports AAC/MSE and Widevine key-system support
in an isolated local fixture without real credentials, media or license requests.
Unavailable/disabled key systems are reported by the browser diagnostic as
`unsupported` at the EME stage, rather than a generic decoding failure.

`--native-drm-probe TRACK_URL` inspects the actual protected playlist and attempts
PlayReady license acquisition in the current Windows host. It returns a nonzero
exit code on unsupported APIs or failed acquisition, without exposing credentials
or payloads. It does not claim to verify playback. The official SoundCloud player
and VMP lab probes, observations and current limitations are documented in
[`tools/drm-probe/README.md`](../../tools/drm-probe/README.md).

`--native-playready-audio-probe TRACK_URL` runs the protected audio smoke test
on Windows with system audio and an isolated browser profile. PlayReady and
Media Foundation are called from C# in the smoke host process.

```powershell
dotnet run --project tests/Pookie.App.SmokeTests -- --native-playready-audio-probe https://soundcloud.com/billboards21/tyazhelo
```

Regional playback uses the authenticated primary connection first. Only fresh
`SNIP`/`BLOCK` metadata activates the anonymous pool. It discovers ProxyScrape and
Proxifly JSON lists and the current public relay configuration of Sound Unblocker
from Mozilla's extension API. The extension is read as data, never installed or
executed. Lists refresh on demand after ten minutes; cached working exits remain
usable during a list outage. Each attempt resolves full metadata and a usable
transcoding, with at most four concurrent candidates, 24 candidates per resolution,
an eight-second candidate timeout and a forty-second overall budget. TLS validation
stays enabled. Audio segments and DRM licenses use the existing direct player path.
No account OAuth, cookies or DataDome session go through pool exits.

`POOKIE_REGIONAL_PROXY=off` disables discovery/fallback; an explicit HTTP, HTTPS or
SOCKS5 URI selects a fixed exit. Isolated UI fixtures do not enable discovery by
default. The protected audio smoke uses the automatic production pool when the
variable is unset. Add `--isolated-browser-profile` to avoid leasing the user's
browser profile and `--default-stream-selection` to use normal format ordering.
These probes are muted.
`--regional-cache-probe` resolves a fresh link a second time through the same pool
and reports the warm resolution time before playing it.

`--record-playback-history` is an explicit opt-in **real account write**, only
after actual playback exceeds one second. It submits one authenticated play through
the normal account API and reads back the remote history to verify the track is
first. It never submits plays for seek/restart checks. Production uses the existing
listened-time guard and respects the SendPlaybackHistory preference; history reads
fresh account metadata/authorization independently of anonymous regional tokens.

Use `history` instead of `TRACK_URL` to select an accessible protected track from
the saved account's first 20 history entries. The probe stays muted and checks
the normal seek/pause/end lifecycle.
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

`--menus-ui-smoke-test --no-audio` checks the profile popup and the queue drawer
joined to the player: the profile's attachment-menu surface/shadow, the queue's
expanding surface and shared player gradient, animated opening/closing and reversal, real
trigger clicks without hover activation, repeat-click dismissal, staying open after
pointer departure, outside clicks and focus transfer, internal focus, Escape and
focus restoration, window deactivation, exclusive open state, transparent hit testing
and anchoring at 1000×680, 1280×840 and 1600×900. Queue checks include current/manual/next
groups, actual playback order, scrolling, collapse action and hiding with the player.
Context menu checks toggle an open menu off on a repeated right click on the same
owner, while another owner opens its own context. Programmatic reopens at overlapping positions relocate it
during its entrance, require fresh glass and intermediate animation frames, and
retain continuous close reversal at the same anchor.
It uses demo queue data.

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
Checks the glass search/composer and separate recipient/options header cards,
opens the three-action dialog menu, and verifies the composer's 16 DIP bottom gap
with and without the player. Composer growth must keep the latest message anchored
throughout the height animation.
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
It plays silently and sends/uploads nothing. Linux x64 and macOS (Apple Silicon
and Intel) development and published builds carry libVLC and codec dependencies.
To test `dotnet run` and the published Linux bundle without setting library paths:

```sh
env -u LD_LIBRARY_PATH -u VLC_PLUGIN_PATH dotnet run --project tests/Pookie.App.SmokeTests -- --chat-video-smoke-test
dotnet publish tests/Pookie.App.SmokeTests -c Release -r linux-x64 --self-contained true -o artifacts/video-smoke
env -u LD_LIBRARY_PATH -u VLC_PLUGIN_PATH artifacts/video-smoke/Pookie.App.SmokeTests --chat-video-smoke-test
```

For macOS use `-r osx-arm64` or `-r osx-x64` when publishing and clear
`DYLD_LIBRARY_PATH` / `DYLD_FALLBACK_LIBRARY_PATH` as well. The normal `dotnet run`
command needs no RID and gets both macOS architectures from the NuGet package.

The check verifies the loaded libVLC paths through `/proc/self/maps` on Linux or
dyld's loaded-image list on macOS, native
demuxer errors on corrupt input, and exception messages/inner exceptions/stacks
in a temporary log. CI runs both development and relocated published video checks
on Linux, Apple Silicon and Intel macOS. Windows builds carry the native package.

`--chat-navigation-ui-smoke-test --no-audio` also checks chat folders and membership,
local pins/favorites, ordered multiple selection, linked replies, idempotent reactions,
FTS search, docked/overlay details with Escape, 96/110/144/192 DPI layout passes,
and voice drafts using synthetic PCM. It never captures a microphone or sends real messages.

Chat history uses account-scoped SQLite/FTS5 under `Cache/Chats`, with a sequential
worker and a 256 MiB budget. Encrypted media has a separate 512 MiB LRU cache.
`Chats/<account>/workspace.json` and the existing `drafts.json` retain folders,
pins, favorites, positions, drafts and uncertain sends independently of cache cleanup.
Favorited ciphertext is retained under `Chats/<account>/FavoriteMedia`.
Plain text, `p1` media and legacy inline `p2:` envelopes remain readable. New
text replies and reactions use `p3:` references of at most 36 characters through
the existing encrypted attachment hosting; JSON/Base64 stays outside SoundCloud.
The locally authenticated payload is stored separately from the original wire
body, preserving confirmation, FTS search, offline replies and reaction replay.
Unresolved payloads can be retried; history search also retries cached unresolved
references and reports incomplete results when hosting is unavailable. Extension
downloads are deduplicated, limited to four at once and capped at 1 MiB each;
decoded payload caching has a 4 MiB budget. `ChatEnvelopeHostingTests` use only a
local HTTP handler, including corrupted/cross-conversation/oversized packages,
20,000-character replies, durable outbox, ciphertext cache and database restoration.
Unknown envelopes remain visible text.
Voice uses mono Opus when available, otherwise 16 kHz/16-bit PCM WAV, up to five
minutes/16 MiB. The unit suite exercises encoding/restoring voice without capture devices.

Chat visual regressions include a neutral conversation background, standalone local
saved messages, inline thread search, sidebar message search, intermediate panel-width
animation frames, readable information panels, and scrim boundaries,
idle loading controls, and centered microphone/send glyphs at every tested DPI.
Preview captures use fixture conversations only.

Chat workspace checks cover full-width layouts from 780 to 2560 logical pixels.
Side panels stay 360px wide, while the header, message rows and composer share
a centered column up to 696px. Docking requires room for that complete column;
below a 1480px client width, details overlay the unchanged conversation. Tests
check the 188px center shift, intermediate opening/closing frames, the history
scrollbar at the outer edge, and restoring ordinary page limits after navigation.

Chat search opens from the left header without a separate page title. The suite
checks intermediate sidebar-opacity and connected-result-height frames, passive
active tabs, cached avatars resolving before attachment, automatic indexing of
older server pages, cancellation on obsolete queries, and the recording surface
inside the composer. Synthetic tone envelopes supply the recording/voice previews.
Search and attachment discovery fill missing history ranges automatically; there
is no separate “search older messages” action.

Voice recording checks use the production PCM meter with synthetic quiet/loud
segments. Meter windows are 40 ms with a bounded rolling history; callback chunk
boundaries, silence after a loud segment, clearing, and nonfinite samples are
covered without opening an audio device. Native checks sample both directions
of the 150 ms composer cross-fade, verify newly captured peaks reach the scrolling
waveform, keep the timer separate, and confirm pause freezes the live history.

Opus integration now requires a real OGG result and decodes it again to check
duration and a quiet signal. Capture stays at 16 kHz; a bounded polyphase conversion
feeds the bundled 48 kHz encoder. Tests also verify conversion across block
boundaries, a monotonic waveform cursor under packet arrival jitter, and visible
paused-waveform geometry after actual encoding. Wave edges fade through alpha,
so previews keep their translucent parent surface.

`--memory-ui-smoke-test` also renders 400 frames of transient offscreen text:
its GPU text storage must stop growing after warmup. It repeatedly creates,
renders, removes and reattaches glass cards, checking process GPU allocations
through Linux DRM fdinfo when available. It also rebinds a message menu 10,000
times, holds voice initialization to verify superseded VLC ownership, and checks
that cleanup preserves voice drafts before account restoration. Voice fixtures use
synthetic silent PCM; no real account, messages or microphone are used.
See `tools/mewui-patch/README.md` for the pinned MewUI X11 fix.

`--playback-memory-ui-smoke-test --no-audio` exercises a selected chat track's
queue and fullscreen player with real local PCM decoding through SoundFlow's
silent output, an inactive window, the queue, synced lyrics and minimization.
It prints actual GPU allocations, managed heap, RSS, text/scratch cache sizes and
native image counts. Default wall time is 120 seconds;
`POOKIE_PLAYBACK_SOAK_SECONDS=2400` runs the same scenario for 40 real minutes
(allowed range 40–3600 seconds). Audio and all API replies are local fixtures;
`--no-audio` disables the ordinary player before the test creates its silent one.
Stream resolution and external Discord/MPRIS integrations are outside this UI
fixture. `PlaybackMemoryTests` separately drains 40 minutes of PCM through the
production HTTP-range, disk-cache, decoder and bounded-queue path at full speed,
including analysis capture and cache cleanup, without a 40-minute allocation.

### Frame measurements

Set `POOKIE_PERF_CAPTURE` to an absolute JSON file path to retain the native
window's public `LastFramePerformanceStats` during a UI scenario. The host writes
the samples after the window closes, so file I/O does not run on each UI frame.
Repeated frame indices are discarded: `FrameRendered` may observe the previous
completed frame before native presentation finishes.

The timings describe application frame work, including layout, rendering and
the backend's presentation call. They are **not display FPS or input latency**.
Compare the same viewport, DPI, backend, playback state and scroll pattern;
separate continuous scrolling from first-time loading and large jumps. Keep
profiling, builds and other UI scenarios out of a timed run. Do not enable frame
capture for lifetime/memory checks: retaining the samples changes their heap.

Windows geometry assertions allow one physical pixel for layout rounding.
Requested wide windows are limited to the monitor work area, and the actual
tested width is printed as `UI_VIEWPORT_LIMIT`; this does not verify resolutions
larger than the attached monitor.

The scrolling fixture also checks Windows precision-wheel routing. Fractional
wheel input, coalesced whole deltas within that gesture and direction reversal
must update the offset immediately, without a second spring or trailing motion.
Ordinary wheel notches must retain their intermediate animated positions.

A probe that changes offsets once per rendered frame cannot establish input
responsiveness: it slows its own input whenever rendering stalls. For a live
session, produce native wheel messages independently on wall time and retain
send/receive times, frame notification intervals, playback state and window
activation. Frame work, notification cadence and input handling latency are
different measurements; none alone establishes displayed FPS.

Detailed element profiling adds work of its own. Compare live input cadence with
the profiler disabled as well; `LastFramePerformanceStats` may then be stale and
must not be used to report render-work or presentation durations.

The chrome fixture compares SIMD blur output byte for byte with direct clamped
box sums, including one-pixel strips and kernels larger than the image. It also
checks capture-surface reuse, three detach/reattach cycles, native-DPI backdrop
sampling and continuous one-pixel movement through the blurred header.
Before any standalone header rendering, it also inspects the blur produced by
the normal native window frame: opaque page alpha, visible color variation and
changed pixels after scrolling are required. Offscreen-only checks cannot detect
a nested GPU capture that caches an empty readback during retained rendering.

### Embedded web login and verification

`dotnet run --project tests/Pookie.App.SmokeTests -- --embedded-login-smoke-test`
opens the actual Pookie login screen with an embedded browser. Loopback fixtures
verify a provider origin with restrictive CSP, viewport resize, the web session
and cookie, profile release, cancellation/retry, and a website popup that
retains its opener and shares cookies. The browser stays in the main window;
the existing `ModalDialog` supplies its unmodified spring/tilt, scrim and external
close button. Its content is only the browser viewport, without another card or
header. In-memory engine snapshots participate in the original opening/closing
animation; the live native view is enabled after both opening motion and the
ready-page crossfade finish.
An opaque backdrop matches the browser viewport. The centered SoundCloud logo
remains still during modal opening, then slides upwards while the spinner appears
below it. A delayed-document/SPA fixture with initially transparent form controls
requires the loader to remain visible until those controls become available, a GPU
pixel-verified crossfade when the engine supplies a preview, and retention of the closing snapshot until the entire
modal exit finishes. Scanline checks cover five tilt poses at four DPI scales
to detect transparent strip seams or repeated alpha blending.
Pixel samples check the page's intermediate opacity, and pointer-opened close
buttons must retain their glass backdrop without a keyboard highlight. Immediate,
loaded and repeated cancellation must restore the enabled idle login button.
On Linux, X11 readback of the actual parent window must contain the fixture's
blue page before closing, after resizing, and after reopening. Browser snapshots
and `NativePresented` alone do not prove the child window was mapped onscreen.
The GtkPlug host explicitly maps/unmaps its X11 child, and staging coordinates
are clamped to X11's signed 16-bit range at HiDPI. X11 geometry must match the
arranged viewport: GTK size hints constrain late page-driven size requests, and
the page fills a GtkBox instead of growing freely inside GtkFixed.
Link-only controls are also exercised with animation frames disabled. Readiness
polling has a timer fallback and recognizes a fade-in transition suspended by an
offscreen native view. Preparation times out after 30 seconds with a visible,
retryable error; the fixture exercises this with a shortened deadline and checks
profile release. Preview capture has its own five-second deadline, after which
the interactive native page is shown directly. On that path the fixture captures
the displayed page to verify it really painted.
Neither real credentials nor a SoundCloud account are used. Windows uses
CoreWebView2, Linux uses WebKitGTK 4.1 in a GtkPlug inside MewUI's X11 window
(also under XWayland), and macOS uses a WKWebView subview. Run this test on the
target OS to verify its native implementation; cross-building is not runtime QA.

`dotnet run --project tests/Pookie.App.SmokeTests -- --embedded-login-live-probe`
opens the real SoundCloud page in a temporary signed-out profile, waits 15 seconds,
asserts that the native page replaced the loading surface, and saves a screenshot
to `/tmp/pookie-embedded-live-page.png`. On Linux it also verifies color variation
inside the actual displayed viewport and captures the full X11 application window
to `/tmp/pookie-embedded-live-window.ppm` before cancellation.
It enters no credentials or account data
and deletes its isolated profile after cancellation. This check requires network
access and is separate from the deterministic loopback tests.

`dotnet run --project tests/Pookie.App.SmokeTests -- --embedded-web-challenge-smoke-test`
uses the production in-app worker with a local website and a slider iframe. It
checks that the existing view appears over the workspace without another profile
or navigation, one rejected action resumes once after passing, later actions reuse
that website, and closing the overlay stops the pending write. A new operation
can reopen the durable profile after cancellation. Invisible checks stay hidden;
a hard block is terminal. The fixture also verifies shutdown and profile release.
No real SoundCloud account or account mutation is used.

`--collections-ui-smoke-test --web-playlist-editing-only --no-audio` checks the
matte glass playlist editor and its four sliding tabs (Basic, Tracks, Release, Access),
neutral initial focus, custom release-type popup/Escape, inline date validation,
bounded small-window layout and changed-only saving. Fields use shared rounded
groups, inline labels/values and inset separators. Basic places artwork beside
the title and multiline description; hovering dims the artwork and reveals the
same glass action as track covers, with an edit glyph. Release stacks release/category/publishing groups
in one column; Access contains the permalink and privacy. Compact rows and a
narrower card let Release fit a small window even with inline validation. A round
cyan glass check appears in the top-right corner only when the draft changes;
reverting fades it away without moving tabs or resizing the card. Invalid drafts retain
a disabled check. Closing cancels the draft, and there are no editing footer buttons.
Dropdown highlights extend beyond their text. Invalid permalinks smoothly tint
the glass address field red without a validation footer or height change; other
validation remains visible. Small Access glass surfaces are prepared before the
modal animates, avoiding first-paint raster work during tab changes. All four tab
transitions are timed in both directions, excluding idle waits after animation,
checking frame cadence and retained glass textures/reflections. Deletion retains
its explicit confirmation action. The card
measures each page and animates its height, including shrinking a track draft down
to one row and interrupted tab changes, while retaining the glass backdrop. Complete lists have no count/help
heading. Extended settings include
the permalink, genre/tags, release date/type, purchase URL, label and license.
Reorder/removal stays in a cancelable draft until Save; incomplete snapshots can
still update metadata without replacing hidden tracks. Pending edits cannot be
dismissed or repeated. The fixture verifies metadata/order confirmation,
deletion confirmation and library refresh without changing a real account.
JavaScript and HTTP unit tests cover optional metadata/clear/type mapping, input
validation, numeric web routes, multipart JPEG artwork, owner/source-order
validation and no blind replay after an unconfirmed write.

`--playlist-editor-live-ui-probe --no-audio` checks the editor on X11/Linux with
the saved account, a separate browser profile and a real owned playlist/artwork.
It only changes a local draft and closes it; it never saves or deletes a playlist.
Native window pixels verify cover dimming and error-colour interpolation (including
red label/input text and no brightness overshoot). Timing uses the native
presentation event without pixel readbacks. A separate pass samples the painted
card contour and confirms that tab text is already visible halfway through each
fade. This avoids mistaking a frozen page with a final-frame pop for smooth motion.
Editor resize and page motion use a softer shared easing curve; the standard
modal opening animation is unchanged. `POOKIE_UI_PREVIEW` saves native captures
for this live probe, instead of rendering a separate preview surface.

The retained `--native-session-recovery-smoke-test` exercises the optional library
HTTP transport and its standalone browser verifier against loopback fixtures. The
normal application sends authenticated actions through its embedded website.
Cross-building Linux/macOS does not verify their native runtime behavior.

## Search links

`dotnet run --project tests/Pookie.App.SmokeTests -- --search-links-ui-smoke-test --no-audio`
uses local API fixtures to verify that pasted URLs immediately display a chip, metadata resolves
before Enter, and Enter opens track pages without playback, user profiles, playlists, albums and
system mixes directly, without a search frame or a duplicate resolve request. It also checks
atomic chip editing, native undo, normal text search and section selection, back navigation, late response isolation, unavailable links
and recovery after an error. Short SoundCloud share links are covered by `MusicLinkTests`.

The `--chat-music-ui-smoke-test --no-audio` scenario also exercises the shared link
preview in the composer and search: profile URLs, typed URL continuation, the loading
spinner, crossfades and editable URL fallback, shared metadata reads, single-line height,
vertical text alignment and image attachment insets. Preview captures are written when
`POOKIE_UI_PREVIEW` points to an output directory.

### Mandatory Widevine consent

Normal startup requires this flow on Linux/macOS only. Windows uses PlayReady
without a Widevine download or consent card. The isolated fixture explicitly
enables the Widevine flow on every host so its UI can still be tested on Windows.

```bash
POOKIE_UI_PREVIEW=artifacts/ui-requested dotnet run --project tests/Pookie.App.SmokeTests -- --widevine-consent-ui-smoke-test
```

Uses an isolated temporary consent store, a fake license fetcher and component installer; it never
accepts terms for the real profile or downloads a proprietary module. Verifies
the continuous loader behind dense matte glass (without extra logo fades),
fixed right-column width and aligned action edges across 1000/1280 viewport
widths, flexible left column, top-aligned license and white Widevine brand appearing with the card,
equally sized actions with
a blue glass primary action, standard perspective/blur animation reusing a single
card capture, stable text layout, cached glass backdrop, absence of an extra checkbox/waiting status, clickable
vendor URL, dynamically supplied license text, failed license retrieval and retry
before the card, mandatory button acceptance, Escape/backdrop handling, persisted
acceptance, failed download/retry on the loading screen and release after setup.
The fixture deliberately blocks the preparation worker for 450 ms and verifies
that the visible spinner continues producing distinct animation phases.
Captures the overlay at 1000×680 and 1280×840. Other UI fixtures disable component
setup through test host options so they do not depend on network or legal consent.

For a frame-interval comparison of opening and repeated action-button hovers:

```bash
POOKIE_WIDEVINE_PERF=1 dotnet run --project tests/Pookie.App.SmokeTests -- --widevine-consent-ui-smoke-test
```

Run without `POOKIE_UI_PREVIEW` so screenshot readbacks do not affect timings.
The performance run also reports the maximum spinner frame gap around card
opening/closing, and verifies that hover does not rebuild the background sample
or the blue button’s optical rim. Resize/reopen still refresh the sample.
