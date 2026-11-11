using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;
using static Pookie.App.OpenGlBackdropCopy;

namespace Pookie.App;

// Separate the inexpensive, low-resolution blur from the optical pass. Only
// the bevel needs displaced RGB samples; the interior takes one blurred texel.
internal sealed unsafe class OpenGlPopupLens : IDisposable
{
    private static readonly uint[] capabilities = [0x0BE2, 0x0B71, 0x0B90, 0x0C11, 0x0B44];
    private readonly IGraphicsFactory graphics;
    private readonly Win32BackdropBlur.Gl gl = new();
    private readonly uint program;
    private readonly int sharpLocation, softLocation, regionLocation, sizeLocation, radiusLocation, tintLocation;

    internal OpenGlPopupLens(IGraphicsFactory graphics)
    {
        this.graphics = graphics;
        program = gl.CreateProgram(Win32BackdropBlur.VertexSource, FragmentSource);
        sharpLocation = gl.Location(program, "sharp");
        softLocation = gl.Location(program, "soft");
        regionLocation = gl.Location(program, "region");
        sizeLocation = gl.Location(program, "size");
        radiusLocation = gl.Location(program, "radius");
        tintLocation = gl.Location(program, "tint");
    }

    internal bool Paint(Output output, DialogSnapshot sample, Rect bounds, double radius, double scale, Color tint)
    {
        if (sample.CaptureSurface is not IGpuTextureSource sharp || sample.BlurSurface is not IGpuTextureSource soft)
            return false;
        var sharpTexture = (uint)sharp.GetTextureHandle();
        var softTexture = (uint)soft.GetTextureHandle();
        if (sharpTexture == 0 || softTexture == 0) return false;
        var surface = output.GetSurface(graphics, scale);
        if (surface is not IExternalWritableGpuSurface writable) return false;

        var oldDraw = Get(0x8CA6);
        var oldRead = Get(0x8CAA);
        var oldProgram = Get(0x8B8D);
        var oldArray = Get(0x85B5);
        var oldActive = Get(0x84E0);
        int* viewport = stackalloc int[4];
        int* colorMask = stackalloc int[4];
        GetIntegers(0x0BA2, viewport);
        GetIntegers(0x0C23, colorMask);
        Span<byte> enabled = stackalloc byte[capabilities.Length];
        for (var i = 0; i < capabilities.Length; i++) enabled[i] = IsEnabled(capabilities[i]);
        gl.ActiveTexture(0x84C1);
        var oldSoft = Get(0x8069);
        gl.ActiveTexture(0x84C0);
        var oldSharp = Get(0x8069);
        uint array = 0;
        try
        {
            using var write = writable.BeginExternalWrite();
            foreach (var capability in capabilities) Disable(capability);
            ColorMask(1, 1, 1, 1);
            gl.GenVertexArrays(1, &array);
            gl.BindVertexArray(array);
            gl.UseProgram(program);
            gl.ActiveTexture(0x84C0);
            BindTexture(0x0DE1, sharpTexture);
            gl.ActiveTexture(0x84C1);
            BindTexture(0x0DE1, softTexture);
            gl.Uniform1i(sharpLocation, 0);
            gl.Uniform1i(softLocation, 1);
            var source = sample.Bounds;
            gl.Uniform4f(regionLocation, (float)((bounds.X - source.X) / source.Width),
                (float)((source.Bottom - bounds.Bottom) / source.Height),
                (float)(bounds.Width / source.Width), (float)(bounds.Height / source.Height));
            gl.Uniform2f(sizeLocation, (float)bounds.Width, (float)bounds.Height);
            gl.Uniform1f(radiusLocation, (float)radius);
            gl.Uniform4f(tintLocation, tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f);
            DrawArrays(0x0004, 0, 3);
        }
        finally
        {
            BindFramebuffer(0x8CA9, (uint)oldDraw);
            BindFramebuffer(0x8CA8, (uint)oldRead);
            Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            gl.BindVertexArray((uint)oldArray);
            if (array != 0) gl.DeleteVertexArrays(1, &array);
            gl.UseProgram((uint)oldProgram);
            gl.ActiveTexture(0x84C1);
            BindTexture(0x0DE1, (uint)oldSoft);
            gl.ActiveTexture(0x84C0);
            BindTexture(0x0DE1, (uint)oldSharp);
            gl.ActiveTexture((uint)oldActive);
            ColorMask((byte)colorMask[0], (byte)colorMask[1], (byte)colorMask[2], (byte)colorMask[3]);
            for (var i = 0; i < capabilities.Length; i++)
                if (enabled[i] != 0) Enable(capabilities[i]); else Disable(capabilities[i]);
        }
        writable.MarkExternalContentChanged();
        return true;
    }

    public void Dispose()
    {
        using var rendering = graphics.AcquireBackgroundRenderScope();
        gl.DeleteProgram(program);
    }

    internal sealed class Output(Size size) : IDisposable
    {
        internal Size Size => size;
        private IRenderSurface? surface;
        internal IImage? Image { get; private set; }

        internal IRenderSurface GetSurface(IGraphicsFactory graphics, double scale)
        {
            var (width, height) = LiquidGlassLens.PixelSize(size, scale);
            if (surface == null || surface.PixelWidth != width || surface.PixelHeight != height)
            {
                Dispose();
                surface = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale,
                    debugName: "PopupLiquidGlass"));
                Image = graphics.CreateImageView(surface);
            }
            return surface;
        }

        public void Dispose()
        {
            Image?.Dispose(); Image = null;
            surface?.Dispose(); surface = null;
        }
    }

    // Same Snell profile as the artwork lens. The texture and
    // fragment coordinates both run bottom-up on the OpenGL path.
    private const string FragmentSource = """
        #version 140
        in vec2 uv;
        out vec4 color;
        uniform sampler2D sharp;
        uniform sampler2D soft;
        uniform vec4 region;
        uniform vec2 size;
        uniform float radius;
        uniform vec4 tint;

        float ray(vec2 point, int channel, float clarity) {
            return mix(texture(soft, point)[channel], texture(sharp, point)[channel], clarity);
        }

        void main() {
            vec2 p = (uv - 0.5) * size;
            float r = min(radius, min(size.x, size.y) * 0.5);
            vec2 q = abs(p) - size * 0.5 + r;
            vec2 corner = max(q, 0.0);
            float distance = length(corner);
            float depth = r - distance - min(max(q.x, q.y), 0.0);
            vec2 scene = region.xy + uv * region.zw;
            if (depth >= 9.0) {
                color = vec4(mix(texture(soft, scene).rgb, tint.rgb, tint.a), 1.0);
                return;
            }
            vec2 normal = distance > 0.0 ? (corner / distance) * sign(p)
                : q.x > q.y ? vec2(sign(p.x), 0.0) : vec2(0.0, sign(p.y));
            float rim = 1.0 - clamp(depth / 9.0, 0.0, 1.0);
            float incidence = asin(rim * rim);
            float transmission = asin(sin(incidence) / 1.48);
            vec2 bend = -normal * (8.0 * tan(incidence - transmission)) * region.zw / size;
            float clarity = 0.48 * rim * rim;
            vec3 refracted = vec3(ray(scene + bend * 1.14, 0, clarity),
                                  ray(scene + bend, 1, clarity),
                                  ray(scene + bend * 0.86, 2, clarity));
            // GlassSheenRaster supplies the single reflection layer above tint,
            // just as it does for the CPU lens. Do not bake in a second rim.
            color = vec4(mix(refracted, tint.rgb, tint.a), 1.0);
        }
        """;
}
