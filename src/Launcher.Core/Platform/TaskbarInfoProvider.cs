using System.Runtime.InteropServices;
using static Launcher.Core.Platform.Native.NativeMethods;

namespace Launcher.Core.Platform;

/// <summary>
/// 通过 SHAppBarMessage 读取主任务栏的位置与所在显示器，再做 DPI 换算。
/// </summary>
public static class TaskbarInfoProvider
{
    public static TaskbarInfo? GetPrimaryTaskbar()
    {
        var hwnd = FindWindowW("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero) return null;

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
        };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == IntPtr.Zero) return null;

        var taskbarRc = data.rc;
        var hMonitor = MonitorFromRect(ref taskbarRc, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return null;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi)) return null;

        var hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _);
        double scale = hr == 0 ? dpiX / 96.0 : 1.0;

        return new TaskbarInfo(
            Bounds: ToPixelRect(taskbarRc),
            Edge: (TaskbarEdge)data.uEdge,
            MonitorBounds: ToPixelRect(mi.rcMonitor),
            MonitorWorkArea: ToPixelRect(mi.rcWork),
            ScaleFactor: scale);
    }

    private static PixelRect ToPixelRect(RECT r)
        => new(r.Left, r.Top, r.Right, r.Bottom);
}