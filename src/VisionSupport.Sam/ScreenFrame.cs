using System.Runtime.InteropServices;

namespace VisionSupport.Sam;

/// <summary>
/// The latest picture of one monitor. A capture thread refreshes it; the overlay redraws from it and
/// the inference loop cuts crops out of it, so a crop never costs a capture of its own.
///
/// The overlay windows are excluded from capture, so this is the screen as it is underneath them.
/// </summary>
public sealed class ScreenFrame : IDisposable
{
    private const uint SourceCopy = 0x00CC0020;

    /// <summary>Includes layered windows - the launcher icon, tooltips - which a plain blit leaves out.</summary>
    private const uint CaptureLayered = 0x40000000;

    private readonly object _gate = new();
    private readonly byte[] _latest;
    private IntPtr _screenDc;
    private IntPtr _memoryDc;
    private IntPtr _dib;
    private IntPtr _bits;
    private IntPtr _previous;
    private int _version;

    public ScreenFrame(PixelRect bounds)
    {
        Bounds = bounds;
        _latest = new byte[bounds.Width * bounds.Height * 4];
    }

    public PixelRect Bounds { get; }

    /// <summary>How many captures have landed; 0 until the first.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Takes a new picture. Call it from one thread only, and dispose on that same thread: the GDI
    /// handles are created on first use and GDI wants a DC released by the thread that got it.
    ///
    /// The blit goes into a DIB section outside the lock - it is the slow part, about 28ms for a
    /// 2560×1600 monitor - and only the copy into the shared buffer holds readers up.
    /// </summary>
    public void Capture()
    {
        if (_memoryDc == IntPtr.Zero) CreateSurface();

        BitBlt(_memoryDc, 0, 0, Bounds.Width, Bounds.Height, _screenDc, Bounds.X, Bounds.Y, SourceCopy | CaptureLayered);

        lock (_gate)
        {
            Marshal.Copy(_bits, _latest, 0, _latest.Length);
        }
        Interlocked.Increment(ref _version);
    }

    /// <summary>Runs <paramref name="read"/> with the latest frame held still. Keep it short; the
    /// capture thread waits for it.</summary>
    public void WithLatest(Action<byte[]> read)
    {
        lock (_gate)
        {
            read(_latest);
        }
    }

    /// <summary>
    /// Copies <paramref name="region"/>, in screen coordinates and inside the frame, into
    /// <paramref name="destination"/> - rows of region.Width × 4 bytes. The destination is a buffer
    /// the caller reuses, so it may be longer than the region needs.
    /// </summary>
    public void CopyRegion(PixelRect region, byte[] destination)
    {
        if (region.Width <= 0 || region.Height <= 0 || region.X < Bounds.X || region.Y < Bounds.Y
            || region.Right > Bounds.Right || region.Bottom > Bounds.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "영역이 화면 버퍼 밖으로 나갑니다.");
        }

        if (destination.Length < region.Width * region.Height * 4)
        {
            throw new ArgumentException("복사할 버퍼가 영역보다 작습니다.", nameof(destination));
        }

        lock (_gate)
        {
            for (int y = 0; y < region.Height; y++)
            {
                int source = ((region.Y - Bounds.Y + y) * Bounds.Width + region.X - Bounds.X) * 4;
                Buffer.BlockCopy(_latest, source, destination, y * region.Width * 4, region.Width * 4);
            }
        }
    }

    public void Dispose()
    {
        if (_memoryDc == IntPtr.Zero) return;

        SelectObject(_memoryDc, _previous);
        DeleteObject(_dib);
        DeleteDC(_memoryDc);
        ReleaseDC(IntPtr.Zero, _screenDc);
        _memoryDc = IntPtr.Zero;
    }

    private void CreateSurface()
    {
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);

        var header = new BitmapInfoHeader
        {
            Size = 40,
            Width = Bounds.Width,
            Height = -Bounds.Height,
            Planes = 1,
            BitCount = 32,
        };
        _dib = CreateDIBSection(_memoryDc, ref header, 0, out _bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero) throw new InvalidOperationException("화면 캡처 버퍼를 만들지 못했습니다.");

        _previous = SelectObject(_memoryDc, _dib);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
