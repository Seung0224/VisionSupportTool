using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VisionSupport.Launcher;

namespace VisionSupport.Windows;

/// <summary>
/// Two pieces of window chrome WPF cannot express on Windows 10: constant alpha for a whole
/// window, and rounded outer corners.
///
/// Alpha goes through the Win32 layered-window API rather than <see cref="Window.Opacity"/>,
/// because Window.Opacity requires AllowsTransparency=true, which drops the window onto a
/// software rendering path. Every window in this process is meant to be see-through - including
/// the feature projects' own dialogs, which this assembly must not modify - so that cost would
/// land everywhere, on the very tool whose job is to watch resource use.
///
/// Corners take two routes. Windows 11 has a DWM attribute that rounds them with proper
/// antialiasing. Windows 10 has nothing, so the HWND itself is clipped to a rounded region;
/// the result is aliased at the corners, which is the price of keeping GPU rendering.
/// </summary>
public static class WindowEffects
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int LwaAlpha = 0x00000002;

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE. Silently ignored before Windows 11.</summary>
    private const int DwmwaWindowCornerPreference = 33;

    private const int DwmwcpRound = 2;

    /// <summary>The build where DWM learned to round corners itself.</summary>
    private const int Windows11Build = 22000;

    // The Ptr variants are the correct 64-bit API and this app pins PlatformTarget to x64, so
    // there is no 32-bit host to fall back for.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, int flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
                                                    int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region,
                                           [MarshalAs(UnmanagedType.Bool)] bool redraw);

    /// <summary>Opacity as the 0-255 alpha the layered window API wants, clamped to a usable range.</summary>
    public static byte ToAlphaByte(double opacity)
        => (byte)Math.Round(LauncherSettings.ClampOpacity(opacity) * 255, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Makes one window see-through. Does nothing before the window has an HWND, so callers
    /// should be on Loaded or SourceInitialized.
    /// </summary>
    public static void ApplyAlpha(Window window, double opacity)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExLayered));
        SetLayeredWindowAttributes(hwnd, 0, ToAlphaByte(opacity), LwaAlpha);
    }

    /// <summary>
    /// Re-applies alpha to every open window. The slider changes one number and the whole app
    /// has to follow, including dialogs this assembly never sees the source of.
    /// </summary>
    public static void ApplyAlphaToAll(double opacity)
    {
        if (Application.Current is not { } app) return;

        foreach (Window window in app.Windows)
        {
            // The launcher paints its own shape and needs real per-pixel transparency; layering
            // a constant alpha over it would wash out the icon.
            if (window.AllowsTransparency) continue;
            ApplyAlpha(window, opacity);
        }
    }

    /// <summary>
    /// Rounds a window's outer corners and keeps them rounded.
    ///
    /// On Windows 10 the region is in physical pixels and has to be rebuilt whenever the window
    /// resizes or moves to a monitor with different scaling, so this subscribes rather than
    /// applying once. A maximised window gets no region at all - clipping one leaves four
    /// notches of desktop showing at the screen corners.
    /// </summary>
    public static void AttachRoundedCorners(Window window, double radius)
    {
        void Apply() => ApplyRoundedCorners(window, radius);

        Apply();
        window.SizeChanged += (_, _) => Apply();
        window.StateChanged += (_, _) => Apply();
        window.DpiChanged += (_, _) => Apply();
    }

    private static void ApplyRoundedCorners(Window window, double radius)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (Environment.OSVersion.Version.Build >= Windows11Build)
        {
            int preference = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        double scale = PresentationSource.FromVisual(window) is HwndSource source
            ? source.CompositionTarget.TransformToDevice.M11
            : 1.0;

        int width = (int)Math.Round(window.ActualWidth * scale);
        int height = (int)Math.Round(window.ActualHeight * scale);
        if (width <= 0 || height <= 0) return;

        // CreateRoundRectRgn takes the ellipse's full width and height, not its radius, and its
        // right/bottom bounds are exclusive - hence the doubling and the +1.
        int ellipse = (int)Math.Round(radius * scale) * 2;

        // SetWindowRgn takes ownership of the region; deleting it here would blank the window.
        SetWindowRgn(hwnd, CreateRoundRectRgn(0, 0, width + 1, height + 1, ellipse, ellipse), true);
    }
}
