# MewUI 0.22.1 private extensions

The 0.22.1 X11 offscreen frame session flushes MewVG image releases but omits `TextCache.ReleasePendingDeletes()`. The X11 window session already performs that cleanup. Without it, transient text allocates a new texture slot on every frame and evicted text textures remain queued indefinitely. Backdrop captures and animated opacity layers can therefore grow GPU memory for as long as the application runs. Clearing Pookie's disk cache cannot release it

`MewUiPatch.targets` builds this tool and patches **only a private assembly copy under the consumer's obj directory**. Compile, runtime copying and publishing use that copy. NuGet's cache is untouched. Mono.Cecil is a build dependency only; there is no runtime reflection or patching, including in NativeAOT builds

Both extensions require assembly version 0.22.1.0 and check their target methods. The offscreen cleanup changes only `Aprillz.MewUI.Backend.MewVG.X11`. A dependency update or an already fixed implementation fails with an instruction to review/remove the workaround instead of silently modifying unfamiliar code

Equivalent upstream change, in `MewVGX11GraphicsContext.X11OffscreenFrameSession.EndFrame`:

```diff
 _offscreenProvider.ReleasePendingImagesForVg(_offscreen.Vg);
+TextCache.ReleasePendingDeletes();
 NvgStrokeHelper.ReleasePendingGradientLutDeletes(_offscreen.Vg);
```

Inspected source: https://github.com/aprillz/MewUI/tree/77f453ec9d625de9810a0e252795b77c31109c53
Upstream license: `licenses/MIT.txt`.

Verification:

```sh
dotnet run --project tests/Pookie.App.SmokeTests -- --memory-ui-smoke-test --no-audio
```

This checks repeated offscreen text draws and rendered glass detach/reattach. On Linux AMD DRM it also compares actual process GPU allocations, deduplicating file descriptors by DRM client ID and including buffers evicted to RAM. The text check works independently of DRM counters on all platforms

## Application emoji presentation

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
objects and unchanged Unicode text. Review both extensions on every MewUI update.
