using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using VisionSupport.ImageConverter.Services;
using VisionSupport.ImageConverter.ViewModels;

namespace VisionSupport.ImageConverter;

/// <summary>
/// The image converter page. The view model is handed in (the shell's feature module owns its
/// lifetime); the code-behind does the drop target and shows the right compression control for
/// the chosen output format.
///
/// Drag-and-drop is done the legacy way (DragAcceptFiles + a WM_DROPFILES hook) rather than with
/// WPF's AllowDrop. The shell runs elevated, and WPF registers an OLE IDropTarget on the window:
/// Explorer then talks OLE only, which UIPI blocks across the integrity boundary, and the shell
/// never falls back to WM_DROPFILES while that OLE target is registered. Revoking it and taking
/// the legacy path - a plain message to a window we own, allowed through UIPI by
/// <see cref="DragDropElevation"/> - is what actually works here.
/// </summary>
public partial class ImageConverterView : UserControl
{
    private const int WM_DROPFILES = 0x0233;

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, StringBuilder? file, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    private readonly ImageConverterViewModel _viewModel;
    private HwndSource? _hwndSource;

    public ImageConverterView(ImageConverterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        UpdateFormatPanels();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hwndSource is not null || PresentationSource.FromVisual(this) is not HwndSource source) return;

        _hwndSource = source;
        DragDropElevation.AllowDropMessages();
        RevokeDragDrop(source.Handle);      // drop WPF's OLE target so the legacy path is used
        DragAcceptFiles(source.Handle, true);
        source.AddHook(WndProc);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hwndSource is null) return;

        DragAcceptFiles(_hwndSource.Handle, false);
        _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DROPFILES) return IntPtr.Zero;

        IntPtr hDrop = wParam;
        try
        {
            uint count = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFileW(hDrop, i, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                DragQueryFileW(hDrop, i, buffer, (uint)buffer.Capacity);
                paths.Add(buffer.ToString());
            }

            _viewModel.AddPaths(paths);
        }
        finally
        {
            DragFinish(hDrop);
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e) => UpdateFormatPanels();

    private void UpdateFormatPanels()
    {
        // SelectionChanged can fire while the template is still being built.
        if (JpegQualityPanel is null) return;

        ImageFormat format = _viewModel.Options.TargetFormat;
        CompressionControl control = FormatCatalog.CompressionOf(format);

        JpegQualityPanel.Visibility = Show(control == CompressionControl.JpegQuality);
        JpegXrQualityPanel.Visibility = Show(control == CompressionControl.JpegXrQuality);
        TiffCompressionPanel.Visibility = Show(control == CompressionControl.TiffCompression);
        PngNote.Visibility = Show(format == ImageFormat.Png);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
