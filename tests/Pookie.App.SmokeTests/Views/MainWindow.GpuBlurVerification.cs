using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private unsafe void VerifyGpuBlurState()
    {
        if (!OperatingSystem.IsWindows() || !headerGlass.UsesGpuBlur) return;
        if (Win32BackdropBlur.CurrentContextIdentity == 0)
        {
            VerifyNativeBlurState();
            return;
        }
        var graphics = Window.GraphicsFactory;
        using var rendering = graphics.AcquireBackgroundRenderScope();
        using var source = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(16, 16, 1));
        using var renderer = graphics.CreateContext(source);
        renderer.BeginFrame(source); renderer.Clear(Color.White); renderer.EndFrame();
        using var caller = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(8, 8, 1));
        using var write = ((IExternalWritableGpuSurface)caller).BeginExternalWrite();
        var bindFramebuffer = (delegate* unmanaged<uint, uint, void>)GetBlurGlAddress("glBindFramebuffer");
        // Readback leaves READ at zero while drawing can still target a texture.
        // Preserve these independent bindings rather than replacing both by DRAW.
        bindFramebuffer(0x8CA8, 0);
        uint[] states = [0x8CA6, 0x8CAA, 0x8B8D, 0x85B5, 0x84E0, 0x8069];
        int[] State() => states.Select(name => { ReadBlurGlInteger(name, out var value); return value; }).ToArray();
        var before = State();
        var viewport = new int[4]; ReadBlurGlViewport(0x0BA2, viewport);
        using var blur = new Win32BackdropBlur(graphics, 16, 16, 1, 2);
        // Both output FBOs are deliberately uninitialized. Their first-use
        // allocation must not erase the caller's non-default framebuffer.
        for (var pass = 0; pass < 2; pass++)
        {
            blur.Update(source);
            var afterViewport = new int[4]; ReadBlurGlViewport(0x0BA2, afterViewport);
            if (!before.SequenceEqual(State()) || !viewport.SequenceEqual(afterViewport))
                throw new InvalidOperationException("GPU blur changed the caller's framebuffer, viewport, program, VAO or texture binding.");
        }
        Console.WriteLine("GPU_BLUR_STATE_OK: first-use and repeated blur preserve independent read/draw framebuffers, viewport, program, VAO and texture bindings");
    }

    private void VerifyNativeBlurState()
    {
        var graphics = Window.GraphicsFactory;
        using var rendering = graphics.AcquireBackgroundRenderScope();
        using var source = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(64, 64, 1));
        using (var paint = graphics.CreateContext(source))
        { paint.BeginFrame(source); paint.Clear(Color.White); paint.EndFrame(); }
        using var caller = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(64, 64, 1.5));
        using var context = graphics.CreateContext(caller);
        using var blur = new NativeBackdropBlur(graphics, 6);
        for (var pass = 0; pass < 2; pass++)
        {
            context.BeginFrame(caller);
            try
            {
                context.Clear(Color.Black); context.Translate(4, 5);
                context.SetClip(new Rect(0, 0, 16, 16));
                var transform = context.GetTransform();
                var clip = context.GetClipBoundsLocal();
                blur.Update(source, new Rect(0, 0, 64, 64));
                if (context.GetTransform() != transform || context.GetClipBoundsLocal() != clip)
                    throw new InvalidOperationException("Native blur changed the caller's transform or clip.");
                context.FillRectangle(new Rect(0, 0, 100, 100), Color.FromRgb(255, 0, 0));
            }
            finally { context.EndFrame(); }
            var cpu = (ICpuPixelSurface)caller;
            var pixels = cpu.GetReadOnlyPixelSpan();
            var inside = 10 * cpu.StrideBytes + 8 * 4;
            var outside = 35 * cpu.StrideBytes + 35 * 4;
            if (pixels[inside + 2] < 250 || pixels[inside] > 5 || pixels[outside + 2] > 5)
                throw new InvalidOperationException("Native blur changed the caller's target, DPI, transform or native clip.");
        }
        Console.WriteLine("NATIVE_BLUR_STATE_OK: first-use and repeated effects preserve caller target, DPI, transform and clip");
    }

    [DllImport("opengl32.dll", EntryPoint = "glGetIntegerv")]
    private static extern void ReadBlurGlInteger(uint name, out int value);
    [DllImport("opengl32.dll", EntryPoint = "glGetIntegerv")]
    private static extern void ReadBlurGlViewport(uint name, [Out] int[] values);
    [DllImport("opengl32.dll", EntryPoint = "wglGetProcAddress", CharSet = CharSet.Ansi)]
    private static extern nint GetBlurGlAddress(string name);
}
