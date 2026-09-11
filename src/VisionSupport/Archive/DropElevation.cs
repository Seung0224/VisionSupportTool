using System.Runtime.InteropServices;

namespace VisionSupport.Archive;

/// <summary>
/// Re-permits the drag-and-drop messages Windows filters out on the way into an elevated process.
///
/// This process runs elevated, because the memory monitor needs administrator rights for its ETW
/// sessions. Explorer runs at medium integrity, and UIPI drops the messages it would send to a
/// window above it - so a drag over the launcher produces a refusal cursor, no events, and
/// nothing at all to debug from.
///
/// Two calls, because the older one is not reliable enough to trust on its own:
///
///  - ChangeWindowMessageFilter is process-wide and deprecated. It is kept because it also covers
///    the hidden windows OLE creates for a drag, which are not ours to name.
///  - ChangeWindowMessageFilterEx names a window and reports whether it worked. That report is
///    the whole reason it is here: without it, a failure is indistinguishable from a drop handler
///    that was simply never written.
/// </summary>
internal static class DropElevation
{
    private const uint WM_COPYGLOBALDATA = 0x0049;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_DROPFILES = 0x0233;

    private const uint MSGFLT_ADD = 1;
    private const uint MSGFLT_ALLOW = 1;

    private static readonly uint[] DropMessages = { WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilter(uint message, uint flag);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action,
                                                           IntPtr changeInfo);

    /// <summary>Process-wide, before any window exists.</summary>
    public static void AllowDropMessages()
    {
        foreach (uint message in DropMessages) ChangeWindowMessageFilter(message, MSGFLT_ADD);
    }

    /// <summary>
    /// Allows the same messages through to one window, and says how it went.
    /// </summary>
    /// <returns>
    /// Null when every message was allowed, or a description of what failed - suitable for the
    /// activity log, because an elevated process that cannot be a drop target should say so
    /// rather than look broken.
    /// </returns>
    public static string? AllowDropMessagesFor(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "창 핸들이 아직 없습니다";

        var refused = new List<string>();

        foreach (uint message in DropMessages)
        {
            if (ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, IntPtr.Zero)) continue;

            refused.Add($"0x{message:X4}({Marshal.GetLastWin32Error()})");
        }

        return refused.Count == 0 ? null : "허용 실패: " + string.Join(", ", refused);
    }
}
