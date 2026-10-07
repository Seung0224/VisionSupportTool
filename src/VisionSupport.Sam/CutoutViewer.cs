using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VisionSupport.Sam;

/// <summary>
/// A small floating window with one cutout on a checkerboard, so the transparent part reads as
/// transparent. Dragged by the picture, closed by × or Esc, copied or saved from its right-click
/// menu. Each pick opens its own; they are independent of each other and of the mode that made them.
/// </summary>
public sealed class CutoutViewer : Window
{
    public const double MaxSide = 400;

    /// <summary>Border around the picture, each side.</summary>
    private const double Frame = 6;

    private readonly CutoutImage _image;

    public CutoutViewer(CutoutImage image)
    {
        _image = image;
        (double width, double height) = DisplaySize(image.Width, image.Height);

        Title = "누끼";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));

        var picture = new Image
        {
            Source = CutoutFiles.ToBitmapSource(image),
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        var close = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Focusable = false,
            ToolTip = "닫기",
        };
        close.Click += (_, _) => Close();

        var root = new Grid();
        root.Children.Add(new Border { Margin = new Thickness(Frame), Background = Checkerboard(), Child = picture });
        root.Children.Add(close);
        Content = root;

        var copy = new MenuItem { Header = "복사" };
        copy.Click += (_, _) => Copy();
        var save = new MenuItem { Header = "저장…" };
        save.Click += (_, _) => Save();
        ContextMenu = new ContextMenu { Items = { copy, save } };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Closed += (_, _) => AutomationDisconnect.Disconnect(this);
    }

    /// <summary>Shrinks to fit <see cref="MaxSide"/> on the longer side; never enlarges, because a
    /// small cutout blown up shows pixels that are not in the file.</summary>
    internal static (double Width, double Height) DisplaySize(int width, int height)
    {
        double scale = Math.Min(1.0, MaxSide / Math.Max(width, height));
        return (width * scale, height * scale);
    }

    /// <summary>
    /// Opens just below and right of where the object was picked, or on the other side of the point
    /// when that would run off the monitor. Placed before it is shown, so it never flashes somewhere
    /// else first.
    /// </summary>
    public void ShowNear(int screenX, int screenY)
    {
        IReadOnlyList<MonitorInfo> monitors = Monitors.All();
        MonitorInfo monitor = Monitors.At(monitors, screenX, screenY) ?? monitors[0];
        double scale = monitor.Scale;

        (double width, double height) = DisplaySize(_image.Width, _image.Height);
        double outerWidth = width + Frame * 2;
        double outerHeight = height + Frame * 2;

        double left = screenX / scale + 16;
        double top = screenY / scale + 16;
        if (left + outerWidth > monitor.Bounds.Right / scale) left = screenX / scale - 16 - outerWidth;
        if (top + outerHeight > monitor.Bounds.Bottom / scale) top = screenY / scale - 16 - outerHeight;

        Left = Math.Max(monitor.Bounds.X / scale, left);
        Top = Math.Max(monitor.Bounds.Y / scale, top);

        Show();
        Activate();
    }

    private void Copy()
    {
        try
        {
            CutoutClipboard.Copy(_image);
        }
        catch (ExternalException ex)
        {
            // Another program is holding the clipboard open.
            MessageBox.Show(this, "클립보드에 넣지 못했습니다: " + ex.Message, "누끼", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save()
    {
        var dialog = new SaveFileDialog
        {
            FileName = CutoutFiles.DefaultName(DateTime.Now),
            DefaultExt = ".png",
            Filter = "PNG 이미지|*.png",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            CutoutFiles.SavePng(_image, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "저장하지 못했습니다: " + ex.Message, "누끼", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Brush Checkerboard()
    {
        var dark = new GeometryGroup();
        dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        dark.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), null, dark));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
