using System.Runtime.InteropServices;

namespace Pookie.Media.MacOS;

internal static class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";
    internal static readonly nint Media = NativeLibrary.Load("/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer");
    private static readonly nint appKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    internal static readonly nint StackBlock = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteStackBlock");
    internal static nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Media, name));
    [DllImport(Library)] internal static extern nint objc_getClass(string name);
    [DllImport(Library)] internal static extern nint sel_registerName(string name);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern nint Send(nint obj, nint selector);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern nint Send(nint obj, nint selector, nint argument);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern void Set(nint obj, nint selector, nint argument);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern void SetInt(nint obj, nint selector, long value);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern void SetBool(nint obj, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern void DictionarySet(nint obj, nint selector, nint value, nint key);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern nint Number(nint obj, nint selector, double value);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern double Double(nint obj, nint selector);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern nint Data(nint obj, nint selector, byte[] bytes, nuint length);
    [DllImport(Library, EntryPoint = "objc_msgSend")] internal static extern nint Artwork(nint obj, nint selector, Size size, nint block);
    [StructLayout(LayoutKind.Sequential)] internal readonly record struct Size(double Width, double Height);
    internal static nint S(string name) => sel_registerName(name);
    internal static void Release(nint obj) { if (obj != 0) Send(obj, S("release")); }
    internal static nint String(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(objc_getClass("NSString"), S("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
    internal sealed class Pool : IDisposable
    {
        private readonly nint pool = Send(Send(objc_getClass("NSAutoreleasePool"), S("alloc")), S("init"));
        public void Dispose() => Release(pool);
    }
    // Copy/dispose helpers root the managed callback until the last native block copy dies.
    internal sealed class Block : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Literal
        { public nint Isa; public int Flags, Reserved; public nint Invoke, Descriptor, Context; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CopyHelper(nint destination, nint source);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DisposeHelper(nint block);
        private sealed class State(Delegate callback, Action? release)
        { internal readonly Delegate Callback = callback; internal readonly Action? Release = release; internal nint Descriptor; internal int Copies; }
        private static readonly CopyHelper copy = (destination, source) =>
        {
            var context = Marshal.ReadIntPtr(source, contextOffset);
            Marshal.WriteIntPtr(destination, contextOffset, context);
            Interlocked.Increment(ref ((State)GCHandle.FromIntPtr(context).Target!).Copies);
        };
        private static readonly DisposeHelper destroy = block =>
        {
            var handle = GCHandle.FromIntPtr(Marshal.ReadIntPtr(block, contextOffset));
            var state = (State)handle.Target!;
            if (Interlocked.Decrement(ref state.Copies) != 0) return;
            state.Release?.Invoke(); Marshal.FreeHGlobal(state.Descriptor); handle.Free();
        };
        private static readonly int contextOffset = (int)Marshal.OffsetOf<Literal>(nameof(Literal.Context));
        private nint pointer;
        internal nint Pointer => pointer;
        public Block(Delegate callback, Action? release = null)
        {
            var state = new State(callback, release);
            var handle = GCHandle.Alloc(state);
            var descriptor = state.Descriptor = Marshal.AllocHGlobal(4 * nint.Size);
            Marshal.WriteIntPtr(descriptor, 0, 0);
            Marshal.WriteIntPtr(descriptor, nint.Size, Marshal.SizeOf<Literal>());
            Marshal.WriteIntPtr(descriptor, 2 * nint.Size, Marshal.GetFunctionPointerForDelegate(copy));
            Marshal.WriteIntPtr(descriptor, 3 * nint.Size, Marshal.GetFunctionPointerForDelegate(destroy));
            var stack = Marshal.AllocHGlobal(Marshal.SizeOf<Literal>());
            try
            {
                Marshal.StructureToPtr(new Literal { Isa = StackBlock, Flags = 1 << 25,
                    Invoke = Marshal.GetFunctionPointerForDelegate(callback), Descriptor = descriptor, Context = GCHandle.ToIntPtr(handle) }, stack, false);
                pointer = _Block_copy(stack);
            }
            finally { Marshal.FreeHGlobal(stack); }
        }
        public void Dispose()
        { var block = Interlocked.Exchange(ref pointer, 0); if (block != 0) _Block_release(block); }
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern nint _Block_copy(nint block);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern void _Block_release(nint block);
    }
}
