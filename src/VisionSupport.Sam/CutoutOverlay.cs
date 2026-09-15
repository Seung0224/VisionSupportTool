using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VisionSupport.Sam;

/// <summary>
/// One monitor's dark overlay.
///
/// It is an ordinary opaque window rather than a translucent one, and that is forced. Windows only
/// excludes a window from screen capture when it is not layered, and WPF's AllowsTransparency makes
/// every window layered - the call fails with error 8. Without exclusion the overlay could not be
/// captured through. So the desktop cannot show through it; instead the overlay paints the live
/// capture of what is under it and darkens that.
/// </summary>
internal sealed class CutoutOverlay : Window
{
    /// <summary>55% black.</summary>
    public const byte DimAlpha = 140;

    private const uint ExcludeFromCapture = 0x11;
    private const uint NoActivate = 0x0010;

    private readonly MonitorInfo _monitor;
    private readonly WriteableBitmap _screen;
    private readonly RectangleGeometry _whole;
    private readonly Path _dim;
    private readonly Image _cropImage = new() { Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status;
    private readonly TextBlock _levelLabel;
    private WriteableBitmap? _cropBitmap;
    private PixelRect? _shownCrop;
    private int _shownVersion;

    public CutoutOverlay(MonitorInfo monitor)
    {
        _monitor = monitor;
        double width = monitor.Bounds.Width / monitor.Scale;
        double height = monitor.Bounds.Height / monitor.Scale;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Left = monitor.Bounds.X / monitor.Scale;
        Top = monitor.Bounds.Y / monitor.Scale;
        Width = width;
        Height = height;

        _screen = new WriteableBitmap(monitor.Bounds.Width, monitor.Bounds.Height,
                                      96 * monitor.Scale, 96 * monitor.Scale, PixelFormats.Bgr32, null);

        _whole = new RectangleGeometry(new Rect(0, 0, width, height));
        _whole.Freeze();

        // Aliased, so the dark area and the crop overlay meet without an antialiased seam between them.
        _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(DimAlpha, 0, 0, 0)), Data = _whole };
        RenderOptions.SetEdgeMode(_dim, EdgeMode.Aliased);

        _status = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 13,
            Visibility = Visibility.Collapsed,
        };

        // The wheel's granularity level, "1/3" - drawn small and raised beside the cursor tip like an
        // exponent, since there is nowhere else the wheel's effect would otherwise show at all.
        _levelLabel = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
            Padding = new Thickness(4, 0, 4, 1),
            FontSize = 11,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        var marks = new Canvas();
        marks.Children.Add(_cropImage);
        marks.Children.Add(_status);
        marks.Children.Add(_levelLabel);

        var root = new Grid();
        root.Children.Add(new Image
        {
            Source = _screen,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });
        root.Children.Add(_dim);
        root.Children.Add(marks);
        Content = root;

        SourceInitialized += (_, _) => PlaceAndExclude();
    }

    /// <summary>True once Windows has agreed to leave this window out of captures. The session does
    /// not run without it: an overlay that captured itself would sink darker with every frame.</summary>
    public bool Excluded { get; private set; }

    /// <summary>Repaints from the monitor's frame if a newer capture has landed.</summary>
    public void ShowFrame(ScreenFrame frame)
    {
        int version = frame.Version;
        if (version == _shownVersion) return;
        _shownVersion = version;

        int width = _monitor.Bounds.Width;
        frame.WithLatest(pixels =>
            _screen.WritePixels(new Int32Rect(0, 0, width, _monitor.Bounds.Height), pixels, width * 4, 0));
    }

    /// <summary>
    /// Lays <paramref name="overlay"/> - crop-sized BGRA from <see cref="MaskOverlay"/> - over
    /// <paramref name="crop"/>. With nulls the whole monitor is dark: the cursor is on another one.
    /// </summary>
    public void ShowCrop(PixelRect? crop, byte[]? overlay)
    {
        if (crop is not PixelRect c || overlay is null)
        {
            if (_shownCrop is null) return;
            _shownCrop = null;
            _dim.Data = _whole;
            _cropImage.Visibility = Visibility.Collapsed;
            return;
        }

        if (_shownCrop != c)
        {
            _shownCrop = c;
            Rect dips = SamGeometry.ToDips(c, _monitor.Bounds, _monitor.Scale);

            var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, _whole, new RectangleGeometry(dips));
            geometry.Freeze();
            _dim.Data = geometry;

            Canvas.SetLeft(_cropImage, dips.X);
            Canvas.SetTop(_cropImage, dips.Y);
            _cropImage.Width = dips.Width;
            _cropImage.Height = dips.Height;

            if (_cropBitmap is null || _cropBitmap.PixelWidth != c.Width || _cropBitmap.PixelHeight != c.Height)
            {
                _cropBitmap = new WriteableBitmap(c.Width, c.Height, 96 * _monitor.Scale, 96 * _monitor.Scale,
                                                  PixelFormats.Bgra32, null);
                _cropImage.Source = _cropBitmap;
            }
        }

        _cropBitmap!.WritePixels(new Int32Rect(0, 0, c.Width, c.Height), overlay, c.Width * 4, 0);
        _cropImage.Visibility = Visibility.Visible;
    }

    /// <summary>A short note beside the cursor ("준비 중"), or null to hide it.</summary>
    public void ShowStatus(string? text, int cursorX, int cursorY)
    {
        if (text is null)
        {
            _status.Visibility = Visibility.Collapsed;
            return;
        }

        _status.Text = text;
        Canvas.SetLeft(_status, (cursorX - _monitor.Bounds.X) / _monitor.Scale + 18);
        Canvas.SetTop(_status, (cursorY - _monitor.Bounds.Y) / _monitor.Scale + 18);
        _status.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The "1/3" wheel counter, or null to hide it - there is nothing under the cursor to count
    /// levels of. Placed up and to the right of the cursor tip, the way a real superscript sits
    /// beside the character it belongs to.
    /// </summary>
    public void ShowLevel(string? text, int cursorX, int cursorY)
    {
        if (text is null)
        {
            _levelLabel.Visibility = Visibility.Collapsed;
            return;
        }

        _levelLabel.Text = text;
        Canvas.SetLeft(_levelLabel, (cursorX - _monitor.Bounds.X) / _monitor.Scale + 13);
        Canvas.SetTop(_levelLabel, (cursorY - _monitor.Bounds.Y) / _monitor.Scale - 15);
        _levelLabel.Visibility = Visibility.Visible;
    }

    /// <summary>Placed in physical pixels, because DIP placement across monitors of different
    /// scale is where a full-screen window ends up a few pixels short.</summary>
    private void PlaceAndExclude()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        PixelRect b = _monitor.Bounds;
        SetWindowPos(hwnd, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, NoActivate);
        Excluded = SetWindowDisplayAffinity(hwnd, ExcludeFromCapture);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
