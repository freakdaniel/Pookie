# MewUI private application extension

Pookie builds MewUI from commit `6cda4f9872f7fce8fc980b34f6f9322f5e5a2c39`.
The package bootstrap is in [`../mewui-source`](../mewui-source/Program.cs).
Normal `dotnet restore`, `build` and `run` commands prepare the pinned local
packages automatically on the first restore, using .NET 10 and Git. Subsequent
restores reuse them. Concurrent restores serialize package preparation through
a lock in the source cache. The PowerShell entry point used by CI invokes the
same bootstrap.

The upstream X11 offscreen frame session now drains `TextCache.ReleasePendingDeletes()`.
The old X11 assembly patch has been removed; the upstream implementation is used unchanged.

`MewUiPatch.targets` patches **only a private Core assembly copy under the consumer's
obj directory** for offscreen rendering, window frame scheduling and compatibility emoji
presentation in standard text controls. Compile, runtime
copying and publishing use that copy. NuGet's cache is untouched. Mono.Cecil is a
build dependency only; runtime and NativeAOT builds do not patch assemblies.

The extension checks the exact source SHA in `AssemblyInformationalVersion` and
its target methods. Update that guard only after reviewing a new upstream revision.
Inspected source: https://github.com/aprillz/MewUI/tree/6cda4f9872f7fce8fc980b34f6f9322f5e5a2c39
Upstream license: `licenses/MIT.txt`.

## Offscreen rendering fixes

`WindowPresentation` scopes the thread-local cull viewport for secondary surfaces.
Offscreen captures scope the cull viewport to their target surface, as the framework's
window frame does, then restore it. No runtime reflection is used for these paths.
The thread-local capture scope also suppresses window visibility/cache notifications
inside `UIElement.Render`: clipping an element out of a small glass capture must not
mark it culled in the actual window or dispose its live bitmap cache.
The direct-render opacity group also uses that viewport in surface coordinates,
without applying the current transform a second time. This keeps translated,
cropped captures of partially transparent menus and tooltips inside their target.

## Windows resize and wheel input

The main window can coalesce the first paint after a track-page resize with
pending wheel input. A native dispatcher timer releases the paint when no input
arrives. During this interval the dispatcher keeps work queued and suppresses
its own repost, so native input and the timer can run before another batch.
Releasing the interval posts the wake again. The frame completion hook runs
after the window's outer present, which
allows Pookie to schedule comment and backdrop updates between presented frames
and lets the live probe include presentation in its latency measurement.
The Direct2D main window also keeps spare pixels in its intermediate frame
surface so nearby resize dimensions can retain its renderer and text runs.
Presentation copies the actual client rectangle at its original pixel scale.

## Application layout without framework extensions

Chat scroll anchors and the animated composer are reconciled in `ChatViewport`'s
arrange pass. There is no injected Window callback or pre-scene field.
`ViewportStackPanel` exposes realized rows through the public visual-tree contract
and keeps every row in Panel's logical tree. Its viewport clip, hit testing and tab
stops follow the realized set. Initial root attachment and detachment visit all rows.
It does not access the framework's internal virtualization flag.

## Application emoji presentation

`ChatEmojiEditor` uses public `MultiLineTextBox.Extensions`: an element generator
provides emoji objects, and a line transformer applies combining-mark protection
and preserves existing music inlines. Its native check temporarily disables the
compatibility hook and verifies artwork metrics, selection, backspace and undo.
The shared projection never rewrites document text.

The remaining hook is needed for unchanged standard labels and single-line
`TextBox` controls, which do not expose an equivalent public inline-object pipeline
in the pinned revision. It remains a compatibility extension, not a rendering fix.

The core assembly `Aprillz.MewUI` receives an optional typed static delegate on
`TextLayoutRequest`. `ManagedTextEngine.CreateLayout` and `GetOrCreateLayout`
apply that delegate before measuring, caching or drawing a request. Ownerless
content-cache helpers fall back to uncached layouts when inlines are present,
as required by the engine; owner-backed controls retain normal caching. Pookie sets
it once during startup through a static `UnsafeAccessor`; no reflection or dynamic
code generation is used. This keeps the existing graphics factory and backend
capabilities intact and allows the same presentation in ordinary labels, styled
runs, menus and single/multiline editors.

`ApplicationEmojiText` adds artwork inlines for recognized whole Unicode emoji
sequences. It retains source text, UTF-16 indices and existing music inlines.
Objects and scanned text are bounded separately; requests without emoji are
returned unchanged. Decoded images and renderer textures retain their independent
192-entry budgets. The patch introduces no dependency on Pookie into MewUI: an
unset delegate leaves its behavior unchanged.

`--chat-emoji-ui-smoke-test --no-audio` exercises the hook through ordinary native
TextBox/TextBlock controls as well as the composer, search and reply surfaces.
`ApplicationEmojiTextTests` covers styled runs, source slices, existing inline
objects and unchanged Unicode text. Review this extension on every MewUI update.
