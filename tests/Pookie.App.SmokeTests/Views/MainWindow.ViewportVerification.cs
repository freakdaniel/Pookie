using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Native Windows track-size limits can clamp a 2560-DIP request on a
    // 1920-pixel monitor. Exercise the largest available reading layout and
    // report its actual width instead of timing out waiting for an impossible
    // native size. Large-monitor/X11 runs retain the requested wide cases.
    private int VerificationWindowWidth(int requested)
    {
        if (!OperatingSystem.IsWindows()) return requested;
        var monitor = new VerificationMonitorInfo { Size = Marshal.SizeOf<VerificationMonitorInfo>() };
        if (!VerificationGetMonitorInfo(VerificationMonitorFromWindow(Window.Handle, 2), ref monitor))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var available = (monitor.Work.Right - monitor.Work.Left) / Window.DpiScale;
        var actual = Math.Min(requested, (int)Math.Floor(available - 32));
        if (actual < 760) throw new InvalidOperationException("Native UI verification requires a 760-DIP viewport.");
        if (actual != requested) Console.WriteLine($"UI_VIEWPORT_LIMIT: requested={requested} actual={actual} workArea={available:F1} DPI={Window.Dpi}");
        return actual;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct VerificationScreenRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct VerificationMonitorInfo { internal int Size; internal VerificationScreenRect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern nint VerificationMonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VerificationGetMonitorInfo(nint monitor, ref VerificationMonitorInfo info);
}
