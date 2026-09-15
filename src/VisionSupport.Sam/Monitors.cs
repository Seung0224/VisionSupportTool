using System.Runtime.InteropServices;

namespace VisionSupport.Sam;

/// <summary>A monitor in physical pixels, and its scale factor (DPI / 96).</summary>
public sealed record MonitorInfo(PixelRect Bounds, double Scale);

public static class Monitors
{
    /// <summary>
    /// Every monitor in physical pixels. That holds only in a per-monitor DPI aware process - the
    /// shell is one, through its manifest - otherwise Windows hands back scaled coordinates.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> All()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr dc, ref NativeRect rect, IntPtr data) =>
        {
            double scale = GetDpiForMonitor(handle, 0, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
            monitors.Add(new MonitorInfo(
                new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), scale));
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    /// <summary>The monitor containing the point, or null in the gaps a staggered desk leaves.</summary>
    public static MonitorInfo? At(IReadOnlyList<MonitorInfo> monitors, int x, int y)
        => monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));

    /// <summary>The cursor in physical pixels.</summary>
    public static (int X, int Y) Cursor()
        => GetCursorPos(out NativePoint point) ? (point.X, point.Y) : (int.MinValue, int.MinValue);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
