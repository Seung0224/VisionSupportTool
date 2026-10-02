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

    // Chart window, same model as the memory monitor: a width, and either following the newest
    // sample or parked where the scrollbar left it (seconds since the history's Start).
    private double _viewSpanSeconds = 600;
    private bool _followLive = true;
    private double _viewLeftEdge;
    private bool _syncingScroll;
    private ChartHistory? _shownHistory;

    public WiresharkView(WiresharkViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        StyleChart();
        UpdateSpanLabel();
        Chart.MouseMove += OnChartMouseMove;
        Chart.MouseLeave += (_, _) => HideReadout();
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
        // No axis titles: the right one clips in a narrow window. The legend names both lines.
        plot.Legend.FontName = PlotFonts.Default;
        plot.Legend.FontColor = PlotColor.FromHex("#D4D4D4");
        plot.Legend.BackgroundColor = PlotColor.FromHex("#252526");
        plot.Legend.OutlineColor = PlotColor.FromHex("#3F3F46");
        plot.Legend.Alignment = Alignment.UpperRight;
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

    private void OnChartUpdated(object? sender, EventArgs e) => RedrawChart();

    private void RedrawChart()
    {
        ChartHistory? history = _viewModel.FocusedHistory;
        if (!ReferenceEquals(history, _shownHistory))
        {
            // Another card: start from its newest data.
            _shownHistory = history;
            _followLive = true;
            HideReadout();
        }

        bool hasData = history is { Times.Count: > 1 };
        ChartHint.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        if (!hasData) return;

        _timeOrigin = history!.Start;
        double[] seconds = history.Seconds();
        double bufferStart = seconds[0];
        double latest = seconds[^1];
        (double viewStart, double viewEnd) = ChartTimeWindow.View(
            bufferStart, latest, _viewSpanSeconds, _followLive, _viewLeftEdge);
        SyncTimeScroll(bufferStart, latest, viewStart);

        // Only the samples inside the window (plus one either side), as the memory monitor does:
        // twelve hours at 1 Hz is too many points to redraw every second.
        int first = FirstIndexAtOrAfter(seconds, viewStart);
        if (first > 0) first--;
        int end = FirstIndexAtOrAfter(seconds, viewEnd);
        if (end < seconds.Length) end++;
        int count = end - first;

        double[] xs = seconds.AsSpan(first, count).ToArray();
        double[] bytes = history.BytesPerSecond.GetRange(first, count).ToArray();
        double[] response = history.ResponseMs.GetRange(first, count).ToArray();

        Plot plot = Chart.Plot;
        plot.Clear();
        var traffic = plot.Add.Scatter(xs, bytes);
        traffic.MarkerSize = 0;
        traffic.LineWidth = 2;
        traffic.Color = PlotColor.FromHex("#1F9CF0");
        traffic.LegendText = "송수신 B/s (왼쪽)";
        var answer = plot.Add.Scatter(xs, response);
        answer.MarkerSize = 0;
        answer.LineWidth = 2;
        answer.Color = PlotColor.FromHex("#DCDCAA");
        answer.Axes.YAxis = plot.Axes.Right;
        answer.LegendText = "응답 ms (오른쪽)";
        plot.ShowLegend();

        plot.Axes.AutoScale();                     // Y fits the visible points...
        plot.Axes.SetLimitsX(viewStart, viewEnd);  // ...then X is pinned to the time window
        Chart.Refresh();
    }

    private static int FirstIndexAtOrAfter(double[] seconds, double value)
    {
        int lo = 0, hi = seconds.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (seconds[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Pushes the window position into the scrollbar without tripping its Scroll event
    /// (meant for user drags only), and refreshes the range label.</summary>
    private void SyncTimeScroll(double bufferStart, double latest, double viewStart)
    {
        (double min, double max, double viewport) = ChartTimeWindow.Scrollbar(bufferStart, latest, _viewSpanSeconds);
        _syncingScroll = true;
        try
        {
            TimeScroll.Minimum = min;
            TimeScroll.Maximum = max;
            TimeScroll.ViewportSize = viewport;
            TimeScroll.LargeChange = viewport;
            TimeScroll.SmallChange = viewport / 10;
            TimeScroll.Value = _followLive ? max : Math.Clamp(viewStart, min, max);
            TimeScroll.IsEnabled = max > min;
        }
        finally
        {
            _syncingScroll = false;
        }

        TimeRangeLabel.Text = _followLive
            ? $"실시간 · 최근 {_viewSpanSeconds / 60:0}분"
            : $"{_timeOrigin.AddSeconds(viewStart):HH:mm:ss} ~ {_timeOrigin.AddSeconds(viewStart + _viewSpanSeconds):HH:mm:ss}";
    }

    private void OnTimeScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        if (_syncingScroll) return;
        _followLive = ChartTimeWindow.AtLiveEdge(e.NewValue, TimeScroll.Maximum);
        _viewLeftEdge = e.NewValue;
        RedrawChart();
    }

    private void OnZoomPreset(object sender, RoutedEventArgs e)
    {
        _viewSpanSeconds = double.Parse((string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _followLive = true;
        UpdateSpanLabel();
        RedrawChart();
    }

    private void OnFollowLive(object sender, RoutedEventArgs e)
    {
        _followLive = true;
        RedrawChart();
    }

    private void UpdateSpanLabel() => SpanLabel.Text = $"표시 {_viewSpanSeconds / 60:0}분";

    /// <summary>Crosshair snapped to the nearest sample, with that second's numbers beside it.</summary>
    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        ChartHistory? history = _shownHistory;
        if (history is not { Times.Count: > 1 })
        {
            HideReadout();
            return;
        }

        Pixel mouse = Chart.GetPlotPixelPosition(e);
        double hovered = Chart.Plot.GetCoordinates(mouse).X;
        double[] seconds = history.Seconds();
        int i = Math.Clamp(FirstIndexAtOrAfter(seconds, hovered), 0, seconds.Length - 1);
        if (i > 0 && hovered - seconds[i - 1] < seconds[i] - hovered) i--;

        ReadoutTime.Text = $"{history.Times[i]:HH:mm:ss}";
        ReadoutBytes.Text = $"{history.BytesPerSecond[i]:N0} B/s";
        ReadoutResponse.Text = $"{history.ResponseMs[i]:0} ms";

        double scale = Chart.DisplayScale <= 0 ? 1 : Chart.DisplayScale;
        double x = Chart.Plot.GetPixel(new Coordinates(seconds[i], 0)).X / scale;
        Crosshair.X1 = Crosshair.X2 = x;
        Crosshair.Y1 = 0;
        Crosshair.Y2 = Overlay.ActualHeight;
        Crosshair.Visibility = Visibility.Visible;

        Readout.Visibility = Visibility.Visible;
        Readout.UpdateLayout();
        Point cursor = e.GetPosition(Overlay);
        double left = x + 14;
        if (left + Readout.ActualWidth > Overlay.ActualWidth) left = x - Readout.ActualWidth - 14;
        double top = Math.Clamp(cursor.Y - Readout.ActualHeight / 2, 4, Math.Max(4, Overlay.ActualHeight - Readout.ActualHeight - 4));
        Canvas.SetLeft(Readout, Math.Max(4, left));
        Canvas.SetTop(Readout, top);
    }

    private void HideReadout()
    {
        Crosshair.Visibility = Visibility.Collapsed;
        Readout.Visibility = Visibility.Collapsed;
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
