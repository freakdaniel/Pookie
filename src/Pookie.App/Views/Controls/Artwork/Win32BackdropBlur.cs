using System.Runtime.InteropServices;
using System.Text;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

// MewUI 0.22.1 loads the GL 1.1 glDrawArrays entry through wglGetProcAddress.
// Windows exports it from opengl32.dll, so that loader disables its GPU filters.
// Use the public external-write contract and import core GL functions directly.
internal sealed unsafe class Win32BackdropBlur : IDisposable
{
    internal static nint CurrentContextIdentity => OperatingSystem.IsWindows() ? Gl.CurrentContext() : 0;
    private readonly IGraphicsFactory factory;
    private readonly IRenderSurface scratch, target;
    private readonly Gl gl = new();
    private readonly uint program;
    private readonly int direction, sampler, radius, weights;
    private readonly float[] kernel;
    private readonly Dictionary<nint, uint> arrays = [];
    private static readonly Dictionary<nint, List<uint>> pendingArrays = [];
    internal IImage Image { get; }

    internal Win32BackdropBlur(IGraphicsFactory factory, int width, int height, double scale, double sigma)
    {
        this.factory = factory;
        program = gl.CreateProgram(VertexSource, FragmentSource);
        direction = gl.Location(program, "direction"); sampler = gl.Location(program, "image");
        radius = gl.Location(program, "radius"); weights = gl.Location(program, "weights");
        var count = Math.Clamp((int)Math.Ceiling(sigma * 3), 1, 32);
        kernel = Enumerable.Range(0, count + 1).Select(i => (float)Math.Exp(-i * i / (2 * sigma * sigma))).ToArray();
        var total = kernel[0] + 2 * kernel.Skip(1).Sum();
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= total;
        try
        {
            scratch = factory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale));
            try
            {
                target = factory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale));
                try { Image = factory.CreateImageView(target); }
                catch { target.Dispose(); throw; }
            }
            catch { scratch.Dispose(); throw; }
        }
        catch { gl.DeleteProgram(program); throw; }
    }

    internal void Update(IRenderSurface source)
    {
        if (source is not IGpuTextureSource texture || scratch is not IExternalWritableGpuSurface first || target is not IExternalWritableGpuSurface second)
            throw new NotSupportedException("Backdrop surfaces do not support external GPU writes.");
        var context = Gl.CurrentContext();
        if (!arrays.TryGetValue(context, out var array))
        {
            uint created = 0; gl.GenVertexArrays(1, &created); array = created; arrays.Add(context, array);
        }
        lock (pendingArrays)
            if (pendingArrays.Remove(context, out var retired))
                foreach (var value in retired) { var item = value; gl.DeleteVertexArrays(1, &item); }
        var oldProgram = Gl.Integer(0x8B8D); var oldArray = Gl.Integer(0x85B5);
        var oldFramebuffer = Gl.Integer(0x8CA6);
        var oldReadFramebuffer = Gl.Integer(0x8CAA);
        int* viewport = stackalloc int[4]; Gl.GetIntegers(0x0BA2, viewport);
        var oldActive = Gl.Integer(0x84E0);
        gl.ActiveTexture(0x84C0);
        var oldTexture = Gl.Integer(0x8069);
        uint[] capabilities = [0x0BE2, 0x0B71, 0x0B90, 0x0C11, 0x0B44];
        var enabled = capabilities.Select(Gl.IsEnabled).ToArray();
        int* mask = stackalloc int[4]; Gl.GetIntegers(0x0C23, mask);
        try
        {
            foreach (var capability in capabilities) Gl.Disable(capability);
            Gl.ColorMask(1, 1, 1, 1);
            gl.UseProgram(program); gl.BindVertexArray(array);
            gl.Uniform1i(sampler, 0); gl.Uniform1i(radius, kernel.Length - 1);
            fixed (float* data = kernel) gl.Uniform1fv(weights, kernel.Length, data);
            var sourceTexture = (uint)texture.GetTextureHandle();
            if (sourceTexture == 0) throw new InvalidOperationException("Backdrop source texture is unavailable.");
            using (var write = first.BeginExternalWrite())
            {
                Gl.BindTexture(0x0DE1, sourceTexture);
                gl.Uniform2f(direction, 1f / source.PixelWidth, 0);
                Gl.DrawArrays(0x0004, 0, 3);
            }
            first.MarkExternalContentChanged();
            using (var write = second.BeginExternalWrite())
            {
                Gl.BindTexture(0x0DE1, (uint)((IGpuTextureSource)scratch).GetTextureHandle());
                gl.Uniform2f(direction, 0, 1f / source.PixelHeight);
                Gl.DrawArrays(0x0004, 0, 3);
            }
            second.MarkExternalContentChanged();
        }
        finally
        {
            // First-use FBO allocation can reset the binding before the
            // external-write scope records it. Restore our entry state too.
            gl.BindFramebuffer(0x8CA9, (uint)oldFramebuffer);
            gl.BindFramebuffer(0x8CA8, (uint)oldReadFramebuffer);
            Gl.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            gl.UseProgram((uint)oldProgram); gl.BindVertexArray((uint)oldArray);
            Gl.BindTexture(0x0DE1, (uint)oldTexture); gl.ActiveTexture((uint)oldActive);
            Gl.ColorMask((byte)mask[0], (byte)mask[1], (byte)mask[2], (byte)mask[3]);
            for (var i = 0; i < capabilities.Length; i++)
                if (enabled[i] != 0) Gl.Enable(capabilities[i]); else Gl.Disable(capabilities[i]);
        }
    }

    public void Dispose()
    {
        using var rendering = factory.AcquireBackgroundRenderScope();
        gl.DeleteProgram(program);
        lock (pendingArrays)
            foreach (var (context, array) in arrays)
            {
                if (context == Gl.CurrentContext()) { var item = array; gl.DeleteVertexArrays(1, &item); }
                else
                {
                    if (!pendingArrays.TryGetValue(context, out var pending)) pendingArrays[context] = pending = [];
                    pending.Add(array);
                }
            }
        arrays.Clear(); Image.Dispose(); target.Dispose(); scratch.Dispose();
    }

    private const string VertexSource = """
        #version 140
        out vec2 uv;
        void main() {
            vec2 p = vec2((gl_VertexID == 1) ? 3.0 : -1.0, (gl_VertexID == 2) ? 3.0 : -1.0);
            uv = p * 0.5 + 0.5;
            gl_Position = vec4(p, 0.0, 1.0);
        }
        """;
    private const string FragmentSource = """
        #version 140
        in vec2 uv;
        out vec4 color;
        uniform sampler2D image;
        uniform vec2 direction;
        uniform int radius;
        uniform float weights[33];
        void main() {
            vec4 sum = texture(image, uv) * weights[0];
            for (int i = 1; i <= 32; i++) {
                if (i > radius) break;
                vec2 offset = direction * float(i);
                sum += (texture(image, uv + offset) + texture(image, uv - offset)) * weights[i];
            }
            color = sum;
        }
        """;

    private sealed class Gl
    {
        internal readonly delegate* unmanaged<uint, uint> CreateShader = (delegate* unmanaged<uint, uint>)Load("glCreateShader");
        internal readonly delegate* unmanaged<uint, int, byte**, int*, void> ShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)Load("glShaderSource");
        internal readonly delegate* unmanaged<uint, void> CompileShader = (delegate* unmanaged<uint, void>)Load("glCompileShader");
        internal readonly delegate* unmanaged<uint, uint, int*, void> GetShader = (delegate* unmanaged<uint, uint, int*, void>)Load("glGetShaderiv");
        internal readonly delegate* unmanaged<uint, int, int*, byte*, void> ShaderLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Load("glGetShaderInfoLog");
        internal readonly delegate* unmanaged<uint, void> DeleteShader = (delegate* unmanaged<uint, void>)Load("glDeleteShader");
        internal readonly delegate* unmanaged<uint> NewProgram = (delegate* unmanaged<uint>)Load("glCreateProgram");
        internal readonly delegate* unmanaged<uint, uint, void> Attach = (delegate* unmanaged<uint, uint, void>)Load("glAttachShader");
        internal readonly delegate* unmanaged<uint, void> Link = (delegate* unmanaged<uint, void>)Load("glLinkProgram");
        internal readonly delegate* unmanaged<uint, uint, int*, void> GetProgram = (delegate* unmanaged<uint, uint, int*, void>)Load("glGetProgramiv");
        internal readonly delegate* unmanaged<uint, int, int*, byte*, void> ProgramLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Load("glGetProgramInfoLog");
        internal readonly delegate* unmanaged<uint, void> DeleteProgram = (delegate* unmanaged<uint, void>)Load("glDeleteProgram");
        internal readonly delegate* unmanaged<uint, void> UseProgram = (delegate* unmanaged<uint, void>)Load("glUseProgram");
        internal readonly delegate* unmanaged<uint, byte*, int> UniformLocation = (delegate* unmanaged<uint, byte*, int>)Load("glGetUniformLocation");
        internal readonly delegate* unmanaged<int, int, void> Uniform1i = (delegate* unmanaged<int, int, void>)Load("glUniform1i");
        internal readonly delegate* unmanaged<int, float, float, void> Uniform2f = (delegate* unmanaged<int, float, float, void>)Load("glUniform2f");
        internal readonly delegate* unmanaged<int, int, float*, void> Uniform1fv = (delegate* unmanaged<int, int, float*, void>)Load("glUniform1fv");
        internal readonly delegate* unmanaged<int, uint*, void> GenVertexArrays = (delegate* unmanaged<int, uint*, void>)Load("glGenVertexArrays");
        internal readonly delegate* unmanaged<int, uint*, void> DeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)Load("glDeleteVertexArrays");
        internal readonly delegate* unmanaged<uint, void> BindVertexArray = (delegate* unmanaged<uint, void>)Load("glBindVertexArray");
        internal readonly delegate* unmanaged<uint, uint, void> BindFramebuffer = (delegate* unmanaged<uint, uint, void>)Load("glBindFramebuffer");
        internal readonly delegate* unmanaged<uint, void> ActiveTexture = (delegate* unmanaged<uint, void>)Load("glActiveTexture");

        private static nint Load(string name)
        {
            var address = GetProc(name);
            if (address is 0 or 1 or 2 or 3 or -1) throw new NotSupportedException("OpenGL entry point is unavailable: " + name);
            return address;
        }

        internal int Location(uint program, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name + '\0');
            fixed (byte* value = bytes) return UniformLocation(program, value);
        }

        private uint Shader(uint type, string code)
        {
            var shader = CreateShader(type);
            if (shader == 0) throw new NotSupportedException("Could not create backdrop shader.");
            var bytes = Encoding.UTF8.GetBytes(code + '\0');
            fixed (byte* value = bytes) { var pointer = value; ShaderSource(shader, 1, &pointer, null); }
            CompileShader(shader);
            int success = 0; GetShader(shader, 0x8B81, &success);
            if (success != 0) return shader;
            byte* log = stackalloc byte[2048]; int length = 0;
            ShaderLog(shader, 2048, &length, log); DeleteShader(shader);
            throw new NotSupportedException("Backdrop shader: " + Encoding.UTF8.GetString(new ReadOnlySpan<byte>(log, Math.Clamp(length, 0, 2048))));
        }

        internal uint CreateProgram(string vertex, string fragment)
        {
            var first = Shader(0x8B31, vertex);
            uint second = 0, program = 0;
            try
            {
                second = Shader(0x8B30, fragment); program = NewProgram();
                Attach(program, first); Attach(program, second); Link(program);
                int success = 0; GetProgram(program, 0x8B82, &success);
                if (success != 0) return program;
                byte* log = stackalloc byte[2048]; int length = 0;
                ProgramLog(program, 2048, &length, log);
                throw new NotSupportedException("Backdrop shader link: " + Encoding.UTF8.GetString(new ReadOnlySpan<byte>(log, Math.Clamp(length, 0, 2048))));
            }
            catch { if (program != 0) DeleteProgram(program); throw; }
            finally { DeleteShader(first); if (second != 0) DeleteShader(second); }
        }

        internal static int Integer(uint name) { int value = 0; GetIntegers(name, &value); return value; }
        [DllImport("opengl32.dll", EntryPoint = "wglGetProcAddress", CharSet = CharSet.Ansi)] private static extern nint GetProc(string name);
        [DllImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")] internal static extern nint CurrentContext();
        [DllImport("opengl32.dll", EntryPoint = "glGetIntegerv")] internal static extern void GetIntegers(uint name, int* value);
        [DllImport("opengl32.dll", EntryPoint = "glBindTexture")] internal static extern void BindTexture(uint target, uint texture);
        [DllImport("opengl32.dll", EntryPoint = "glDrawArrays")] internal static extern void DrawArrays(uint mode, int first, int count);
        [DllImport("opengl32.dll", EntryPoint = "glDisable")] internal static extern void Disable(uint capability);
        [DllImport("opengl32.dll", EntryPoint = "glEnable")] internal static extern void Enable(uint capability);
        [DllImport("opengl32.dll", EntryPoint = "glIsEnabled")] internal static extern byte IsEnabled(uint capability);
        [DllImport("opengl32.dll", EntryPoint = "glColorMask")] internal static extern void ColorMask(byte red, byte green, byte blue, byte alpha);
        [DllImport("opengl32.dll", EntryPoint = "glViewport")] internal static extern void Viewport(int x, int y, int width, int height);
    }
}
