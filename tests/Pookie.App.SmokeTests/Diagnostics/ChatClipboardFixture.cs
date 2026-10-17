using System.Runtime.InteropServices;
using Pookie.App.Platform;

namespace Pookie.App.Diagnostics;

internal static class ChatClipboardFixture
{
    private static byte[] bytes = [];
    private static string format = "";
    private static readonly GetCallback get = (_, data, _, _) => gtk_selection_data_set(data, gdk_atom_intern(format, 0), 8, bytes, bytes.Length);
    private static readonly ClearCallback clear = (_, _) => { };
    internal static void Run(string selection, string mime, string path)
    {
        var pump = GtkClipboardService.TryCreate(selection) ?? throw new InvalidOperationException("GTK unavailable");
        bytes = File.ReadAllBytes(path); format = mime;
        var clipboard = gtk_clipboard_get(gdk_atom_intern(selection, 0));
        if (gtk_clipboard_set_with_data(clipboard, [new() { Target = mime }], 1, get, clear, 0) == 0)
            throw new InvalidOperationException("Cannot own private clipboard selection");
        Console.WriteLine("CLIPBOARD_READY");
        var stop = Task.Run(() => Console.ReadLine()); var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!stop.IsCompleted && DateTime.UtcNow < deadline) { pump.Pump(); Thread.Sleep(2); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct TargetEntry
    { [MarshalAs(UnmanagedType.LPUTF8Str)] public string Target; public uint Flags, Info; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GetCallback(nint clipboard, nint data, uint info, nint state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ClearCallback(nint clipboard, nint state);
    [DllImport("libgtk-3.so.0")] private static extern nint gtk_clipboard_get(nint selection);
    [DllImport("libgtk-3.so.0")] private static extern int gtk_clipboard_set_with_data(nint clipboard, [In] TargetEntry[] targets, uint count, GetCallback get, ClearCallback clear, nint state);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_selection_data_set(nint data, nint type, int bits, byte[] bytes, int length);
    [DllImport("libgdk-3.so.0")] private static extern nint gdk_atom_intern([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int exists);
}
