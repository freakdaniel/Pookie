using System.Runtime.InteropServices;

namespace Pookie.App;

internal static partial class DesktopMotion
{
    internal static bool PrefersReducedMotion()
    {
        if (OperatingSystem.IsWindows())
            return SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out var enabled, 0) != 0 && enabled == 0;
        if (OperatingSystem.IsMacOS())
        {
            var workspace = Send(GetClass("NSWorkspace"), Selector("sharedWorkspace"));
            return SendBool(workspace, Selector("accessibilityDisplayShouldReduceMotion"));
        }
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var settings = GetGtkSettings();
                if (settings == 0) return false;
                GValue value = default;
                GetProperty(settings, "gtk-enable-animations", ref value);
                try { return GetBoolean(ref value) == 0; }
                finally { Unset(ref value); }
            }
            catch (DllNotFoundException) { } // GTK is optional on X11.
        }
        return false;
    }

    [LibraryImport("user32", EntryPoint = "SystemParametersInfoW")]
    private static partial int SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SendBool(nint receiver, nint selector);

    [StructLayout(LayoutKind.Sequential)]
    private struct GValue { public nuint Type; public ulong Data0, Data1; }

    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_settings_get_default")]
    private static partial nint GetGtkSettings();
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_get_property", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void GetProperty(nint obj, string name, ref GValue value);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_boolean")]
    private static partial int GetBoolean(ref GValue value);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_unset")]
    private static partial void Unset(ref GValue value);
}
