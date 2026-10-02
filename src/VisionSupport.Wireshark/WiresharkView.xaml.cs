using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ScottPlot;
using VisionSupport.Wireshark.ViewModels;
using PlotColor = ScottPlot.Color;
using PlotFonts = ScottPlot.Fonts;

namespace VisionSupport.Wireshark;

public partial class WiresharkView : UserControl, IDisposable
{
    private const string HangulFontAlias = "VisionSupport Hangul";
    private readonly WiresharkViewModel _viewModel;
    /// <summary>X is seconds from here. Never left at MinValue: an empty chart still labels its
    /// default -10..10 s axis, and MinValue minus ten seconds throws.</summary>
    private DateTime _timeOrigin = DateTime.Now;

    public WiresharkView(WiresharkViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        StyleChart();
        viewModel.ChartUpdated += OnChartUpdated;
        Loaded += (_, _) =>
        {
            if (_viewModel.Nics.Count == 0) _viewModel.RefreshNicsCommand.Execute(null);
            _viewModel.DetectCxp();
        };
    }

    /// <summary>Called by the feature when the window closes: the view model outlives this view.</summary>
    public void Dispose() => _viewModel.ChartUpdated -= OnChartUpdated;

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        if ((sender as FrameworkElement)?.DataContext is TargetCardViewModel card)
        {
            _viewModel.FocusCardCommand.Execute(card);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "pcapng (*.pcapng)|*.pcapng",
            FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.pcapng",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            int count = _viewModel.ExportTo(dialog.FileName);
            MessageBox.Show($"{count:N0}개 패킷을 저장했습니다.", "통신 모니터");
        }
        catch (IOException ex)
        {
            MessageBox.Show("저장 실패: " + ex.Message, "통신 모니터", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCxpDiagnostics(object sender, RoutedEventArgs e)
    {
        Cursor = Cursors.Wait;
        try
        {
            string path = _viewModel.WriteCxpDiagnostics();
            MessageBox.Show($"진단 결과를 저장했습니다.\n{path}", "통신 모니터");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("진단 저장 실패: " + ex.Message, "통신 모니터", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void StyleChart()
    {
        PlotFonts.Default = RegisterHangulFont();
        Plot plot = Chart.Plot;
        plot.FigureBackground.Color = PlotColor.FromHex("#1E1E1E");
        plot.DataBackground.Color = PlotColor.FromHex("#1E1E1E");
        plot.Axes.Color(PlotColor.FromHex("#9D9D9D"));
        plot.Grid.MajorLineColor = PlotColor.FromHex("#2F2F2F");
        plot.Axes.Left.Label.Text = "송수신 B/s";
        plot.Axes.Right.Label.Text = "응답 ms";
        plot.Axes.Bottom.Label.Text = "시각";
        foreach (var axis in new IAxis[] { plot.Axes.Bottom, plot.Axes.Left, plot.Axes.Right })
        {
            axis.Label.FontName = PlotFonts.Default;
            axis.TickLabelStyle.FontName = PlotFonts.Default;
        }
        if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic ticks)
        {
            ticks.LabelFormatter = seconds => _timeOrigin.AddSeconds(seconds).ToString("HH:mm:ss");
        }
        Chart.UserInputProcessor.Disable();
        Chart.Refresh();
    }

    private void OnChartUpdated(object? sender, EventArgs e)
    {
        ChartHistory? history = _viewModel.FocusedHistory;
        ChartHint.Visibility = history is { Times.Count: > 1 } ? Visibility.Collapsed : Visibility.Visible;
        if (history is not { Times.Count: > 1 }) return;

        Plot plot = Chart.Plot;
        plot.Clear();
        _timeOrigin = history.Times[0];
        double[] xs = history.Times.Select(t => (t - _timeOrigin).TotalSeconds).ToArray();

        var traffic = plot.Add.Scatter(xs, history.BytesPerSecond.ToArray());
        traffic.MarkerSize = 0;
        traffic.Color = PlotColor.FromHex("#1F9CF0");

        var response = plot.Add.Scatter(xs, history.ResponseMs.ToArray());
        response.MarkerSize = 0;
        response.Color = PlotColor.FromHex("#DCDCAA");
        response.Axes.YAxis = plot.Axes.Right;

        plot.Axes.AutoScale();
        Chart.Refresh();
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (DependencyObject? n = node; n is not null; n = System.Windows.Media.VisualTreeHelper.GetParent(n))
        {
            if (n is T match) return match;
        }
        return null;
    }

    /// <summary>
    /// Same reason as the memory monitor's copy: ScottPlot draws through SkiaSharp, which cannot
    /// find Malgun Gothic by name here, so the font files are registered directly (bold too, for titles).
    /// </summary>
    private static string RegisterHangulFont()
    {
        string fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string regular = Path.Combine(fontDir, "malgun.ttf");
        string bold = Path.Combine(fontDir, "malgunbd.ttf");
        if (!File.Exists(regular)) return PlotFonts.Detect("응답");
        PlotFonts.AddFontFile(HangulFontAlias, regular, bold: false, italic: false);
        PlotFonts.AddFontFile(HangulFontAlias, File.Exists(bold) ? bold : regular, bold: true, italic: false);
        return HangulFontAlias;
    }
}
