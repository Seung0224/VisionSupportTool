using System.Runtime.InteropServices;

namespace VisionSupport.ImageConverter;

/// <summary>
/// Re-permits the file-drop window messages that Windows UIPI filters out.
///
/// The support shell runs elevated (its manifest asks for administrator, because the memory
/// monitor needs it for ETW). Windows then drops the drag-and-drop messages Explorer sends from
/// medium integrity - WM_DROPFILES and the two that carry the CF_HDROP payload - so the legacy
/// drop path <see cref="ImageConverterView"/> uses would never receive them. This allows them
/// process-wide (ChangeWindowMessageFilter, not the per-window ...Ex, because OLE routes some of
/// them through a hidden window we do not own). On a non-elevated process it is a harmless no-op.
/// </summary>
internal static class DragDropElevation
{
    private const uint WM_COPYGLOBALDATA = 0x0049;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_DROPFILES = 0x0233;
    private const uint MSGFLT_ADD = 1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilter(uint message, uint flag);

    public static void AllowDropMessages()
    {
        ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
        ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
        ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);
    }
}
