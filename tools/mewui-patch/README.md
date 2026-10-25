# MewUI 0.22.1 X11 offscreen text cleanup

The 0.22.1 X11 offscreen frame session flushes MewVG image releases but omits `TextCache.ReleasePendingDeletes()`. The X11 window session already performs that cleanup. Without it, transient text allocates a new texture slot on every frame and evicted text textures remain queued indefinitely. Backdrop captures and animated opacity layers can therefore grow GPU memory for as long as the application runs. Clearing Pookie's disk cache cannot release it

`MewUiPatch.targets` builds this tool and patches **only a private assembly copy under the consumer's obj directory**. Compile, runtime copying and publishing use that copy. NuGet's cache is untouched. Mono.Cecil is a build dependency only; there is no runtime reflection or patching, including in NativeAOT builds

The patch is restricted to `Aprillz.MewUI.Backend.MewVG.X11` 0.22.1.0 and checks the cleanup method's shape. Other backends are unchanged. A dependency update or an already fixed implementation fails with an instruction to review/remove the workaround instead of silently modifying unfamiliar code

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
