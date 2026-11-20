using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Pookie.Media.MacOS;

namespace Pookie.App.Platform;

internal static class ShortcutMouseInput
{
    // Read side buttons before hit testing. In particular the pinned X11 backend
    // aliases unrecognised buttons to a left click on both press and release.
    internal static void Route(NativeMessageEventArgs args, Action<string> pressed)
    {
        if (args.Handled) return;
        int button;
        bool down;
        switch (args)
        {
#if !POOKIE_WINDOWS && !POOKIE_MACOS
            case X11NativeMessageEventArgs { EventType: 4 or 5 } x11:
                var x = Marshal.PtrToStructure<LinuxSearchInput.XKeyEvent>(x11.EventPointer);
                button = (int)x.KeyCode - 4; // X11 buttons 4–7 are scroll, 8/9 are Back/Forward.
                down = x11.EventType == 4;
                break;
#endif
#if !POOKIE_LINUX && !POOKIE_MACOS
            case Win32NativeMessageEventArgs { Msg: 0x020B or 0x020C or 0x020D } win:
                button = 3 + (int)((win.WParam >> 16) & 0xffff);
                down = win.Msg != 0x020C; // Include WM_XBUTTONDBLCLK as the second press.
                break;
#endif
#if !POOKIE_LINUX && !POOKIE_WINDOWS
            case MacOSNativeMessageEventArgs { EventType: 25 or 26 } mac:
                button = 1 + (int)ObjC.Send(mac.NSEvent, ObjC.S("buttonNumber"));
                down = mac.EventType == 25;
                break;
#endif
            default: return;
        }
        if (button is not (4 or 5)) return;
        args.Handled = true;
        args.Result = 1;
        if (down) pressed(button == 4 ? "Mouse4" : "Mouse5");
    }
}
