using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MemMon.Models;
using MemMon.ViewModels;
using ScottPlot;
using PlotColor = ScottPlot.Color;
using PlotFonts = ScottPlot.Fonts;

namespace MemMon;

/// <summary>
/// The memory monitor's screen. Was MemMon's MainWindow; the window chrome (title, size, dark
/// non-client area) now belongs to the support shell, and the view model is handed in rather
/// than created here so the shell's feature module can start, pause and dispose it.
/// </summary>
public partial class MonitorView : UserControl
{
    private const double Mb = 1024.0 * 1024.0;
    private const string HangulFontAlias = "MemMon Hangul";

    /// <summary>One line on the chart. Declared once so the plot and the hover readout cannot drift apart.</summary>
    private sealed record ChartSeries(string Name, string Hex, Func<MemorySample, ulong> Bytes);

    private static readonly ChartSeries[] SeriesDefinitions =
    {
        new("Private Bytes", "#858585", s => (ulong)Math.Max(0, s.PrivateBytes)),
        new("Gen 2", "#F48771", s => s.Gen2Bytes),
        new("LOH", "#C586C0", s => s.LohBytes),
        new("Gen 1", "#DCDCAA", s => s.Gen1Bytes),
        new("Gen 0", "#4EC9B0", s => s.Gen0Bytes),
    };

    private readonly MainViewModel _viewModel;
    private readonly TextBlock[] _readoutValues = new TextBlock[SeriesDefinitions.Length];
    private TextBlock? _readoutTime;

    /// <summary>Width of the visible time window, set by the preset buttons (5/10/30/60 min).</summary>
    private double _viewSpanSeconds = ChartTimeWindow.DefaultSpanSeconds;

    /// <summary>
    /// True while the chart tracks the newest sample. The scrollbar turns this off when the
    /// user drags back into history and on again when they drag fully right.
    /// </summary>
    private bool _followLive = true;

    /// <summary>Left edge of the frozen window, in elapsed seconds. Only read when not following live.</summary>
    private double _viewLeftEdge;

    /// <summary>Set while <see cref="RedrawChart"/> writes the scrollbar, so its Scroll event
    /// (user drags only) is not confused with our own updates.</summary>
    private bool _syncingScroll;

    /// <summary>
    /// Wall-clock time of ElapsedSeconds == 0, recomputed on every redraw so that re-attaching
    /// (which restarts the elapsed clock) is picked up by the time-axis labels.
    /// </summary>
    private DateTime _timeOrigin = DateTime.Now;

    /// <summary>
    /// Raised when this page opens a window of its own. The shell's feature module tracks these
    /// so that stopping the feature closes them - a glossary window left over from a stopped
    /// monitor is the kind of leftover this shell exists to avoid.
    /// </summary>
    public event EventHandler<Window>? WindowOpened;

    public MonitorView(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        StyleChart();
        BuildReadout();

        _viewModel.SamplesChanged += (_, _) => RedrawChart();
        Chart.MouseMove += OnChartMouseMove;
        Chart.MouseLeave += (_, _) => HideReadout();
        UpdateSpanLabel();
    }

    /// <summary>One glossary window at a time; reopening just brings the existing one forward.</summary>
    private GcReferenceWindow? _referenceWindow;

    private void OnShowGcReference(object sender, RoutedEventArgs e)
    {
        if (_referenceWindow is null)
        {
            _referenceWindow = new GcReferenceWindow { Owner = Window.GetWindow(this) };
            _referenceWindow.Closed += (_, _) => _referenceWindow = null;
            _referenceWindow.Show();
            WindowOpened?.Invoke(this, _referenceWindow);
            return;
        }

        if (_referenceWindow.WindowState == WindowState.Minimized)
            _referenceWindow.WindowState = WindowState.Normal;
        _referenceWindow.Activate();
    }

    // ---- chart -----------------------------------------------------------

    private void StyleChart()
    {
        // ScottPlot draws text through SkiaSharp with its own font, not WPF's, and the default
        // face has no Hangul glyphs - Korean labels come out as tofu boxes.
        PlotFonts.Default = RegisterHangulFont();

        Plot plot = Chart.Plot;
        plot.FigureBackground.Color = PlotColor.FromHex("#1E1E1E");
        plot.DataBackground.Color = PlotColor.FromHex("#1E1E1E");
        plot.Axes.Color(PlotColor.FromHex("#9D9D9D"));
        plot.Grid.MajorLineColor = PlotColor.FromHex("#2F2F2F");
        plot.XLabel("시각");
        plot.YLabel("MB");
        plot.Axes.Bottom.Label.FontName = PlotFonts.Default;
        plot.Axes.Left.Label.FontName = PlotFonts.Default;
        plot.Axes.Bottom.TickLabelStyle.FontName = PlotFonts.Default;
        plot.Axes.Left.TickLabelStyle.FontName = PlotFonts.Default;
        plot.Legend.FontName = PlotFonts.Default;

        // The X data is elapsed seconds; show it as wall-clock time so an hours-wide view does
        // not read "18000". _timeOrigin is kept current by RedrawChart.
        if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic bottomTicks)
            bottomTicks.LabelFormatter = seconds => _timeOrigin.AddSeconds(seconds).ToString("HH:mm:ss");

        // ScottPlot's own pan/zoom would fight the per-sample redraw, which pins the axes every
        // second. The scrollbar below the chart moves the time window instead.
        Chart.UserInputProcessor.Disable();
        Chart.Refresh();
    }

    /// <summary>
    /// Registers a Hangul-capable face with ScottPlot and returns its name.
    ///
    /// Two things bite here. SkiaSharp cannot resolve "Malgun Gothic" by family name on this
    /// machine - it silently hands back Segoe UI, which has no Hangul - so the file is loaded
    /// directly instead. And ScottPlot renders axis titles bold, so the bold face must be
    /// registered as well; registering only the regular weight leaves the title as tofu boxes.
    /// </summary>
    private static string RegisterHangulFont()
    {
        string fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string regular = System.IO.Path.Combine(fontDir, "malgun.ttf");
        string bold = System.IO.Path.Combine(fontDir, "malgunbd.ttf");

        if (!System.IO.File.Exists(regular)) return PlotFonts.Detect("경과 시간");

        PlotFonts.AddFontFile(HangulFontAlias, regular, bold: false, italic: false);
        PlotFonts.AddFontFile(HangulFontAlias, System.IO.File.Exists(bold) ? bold : regular,
            bold: true, italic: false);
        return HangulFontAlias;
    }

    private void RedrawChart()
    {
        IReadOnlyList<MemorySample> samples = _viewModel.Samples;
        if (samples.Count == 0) return;

        ChartHint.Visibility = Visibility.Collapsed;

        MemorySample newest = samples[^1];
        _timeOrigin = newest.Timestamp.AddSeconds(-newest.ElapsedSeconds);

        double bufferStart = samples[0].ElapsedSeconds;
        double latest = newest.ElapsedSeconds;

        (double viewStart, double viewEnd) = ChartTimeWindow.View(
            bufferStart, latest, _viewSpanSeconds, _followLive, _viewLeftEdge);

        SyncTimeScroll(bufferStart, latest, viewStart);

        // Hand the plot only the samples inside the window (a few hundred to a few thousand
        // points per line at 1 Hz), plus one on each side so the line enters and leaves cleanly.
        int first = FirstIndexAtOrAfter(samples, viewStart);
        if (first > 0) first--;
        int lastExclusive = FirstIndexAtOrAfter(samples, viewEnd);
        if (lastExclusive < samples.Count) lastExclusive++;
        int count = lastExclusive - first;

        double[] xs = new double[count];
        for (int i = 0; i < count; i++) xs[i] = samples[first + i].ElapsedSeconds;

        Plot plot = Chart.Plot;
        plot.Clear();
        foreach (ChartSeries series in SeriesDefinitions)
        {
            double[] ys = new double[count];
            for (int i = 0; i < count; i++) ys[i] = series.Bytes(samples[first + i]) / Mb;
            ScottPlot.Plottables.Scatter line = plot.Add.Scatter(xs, ys);
            line.LegendText = series.Name;
            line.Color = PlotColor.FromHex(series.Hex);
            line.LineWidth = 2;
            line.MarkerSize = 0;
        }

        // Mark where each snapshot was taken, so a spike can be tied to a snapshot pair. Only
        // those inside the window are drawn; the axis limits below are set explicitly, so an
        // off-window marker no longer drags the time axis over to it.
        foreach ((double seconds, string label) in _viewModel.SnapshotMarkers)
        {
            if (seconds < viewStart || seconds > viewEnd) continue;
            ScottPlot.Plottables.VerticalLine marker = plot.Add.VerticalLine(seconds);
            // ScottPlot reads hex as RRGGBBAA, not WPF's AARRGGBB - an 8-digit value
            // here silently shifts the channels and paints the wrong colour.
            marker.LineColor = PlotColor.FromHex("#9CDCFE");
            marker.LineWidth = 1;
            marker.LinePattern = LinePattern.Dashed;
            marker.LabelText = label;
        }

        plot.ShowLegend();
        plot.Axes.AutoScale();                     // Y fits the visible points...
        plot.Axes.SetLimitsX(viewStart, viewEnd);  // ...then X is pinned to the time window
        Chart.Refresh();
    }

    /// <summary>First index whose ElapsedSeconds is at least <paramref name="seconds"/>. Samples
    /// are in ascending time order, so a binary search is enough.</summary>
    private static int FirstIndexAtOrAfter(IReadOnlyList<MemorySample> samples, double seconds)
    {
        int lo = 0, hi = samples.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (samples[mid].ElapsedSeconds < seconds) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // ---- time scrollbar --------------------------------------------------

    /// <summary>
    /// Pushes the current window position into the scrollbar without tripping its Scroll event
    /// (which is meant for user drags only), and refreshes the range readout.
    /// </summary>
    private void SyncTimeScroll(double bufferStart, double latest, double viewStart)
    {
        (double min, double max, double viewport) =
            ChartTimeWindow.Scrollbar(bufferStart, latest, _viewSpanSeconds);

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
        finally { _syncingScroll = false; }

        UpdateTimeRangeLabel(viewStart);
    }

    private void OnTimeScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        if (_syncingScroll) return;
        _followLive = ChartTimeWindow.AtLiveEdge(e.NewValue, TimeScroll.Maximum);
        _viewLeftEdge = e.NewValue;
        RedrawChart();
    }

    /// <summary>Preset buttons under the toolbar: pick a window width and jump back to live.</summary>
    private void OnZoomPreset(object sender, RoutedEventArgs e)
    {
        _viewSpanSeconds = double.Parse(
            (string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _followLive = true;
        UpdateSpanLabel();
        RedrawChart();
    }

    private void UpdateSpanLabel()
    {
        if (SpanLabel is not null) SpanLabel.Text = $"표시 {_viewSpanSeconds / 60:0}분";
    }

    private void UpdateTimeRangeLabel(double viewStart)
    {
        if (TimeRangeLabel is null) return;
        if (_followLive)
        {
            TimeRangeLabel.Text = $"실시간 · 최근 {_viewSpanSeconds / 60:0}분";
            return;
        }
        DateTime from = _timeOrigin.AddSeconds(viewStart);
        TimeRangeLabel.Text =
            $"{from:HH:mm:ss} ~ {from.AddSeconds(_viewSpanSeconds):HH:mm:ss}";
    }

    // ---- hover readout ---------------------------------------------------

    private void BuildReadout()
    {
        _readoutTime = new TextBlock
        {
            Foreground = Brush("#D4D4D4"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        ReadoutPanel.Children.Add(_readoutTime);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });

        for (int i = 0; i < SeriesDefinitions.Length; i++)
        {
            ChartSeries series = SeriesDefinitions[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 9,
                Height = 9,
                RadiusX = 2,
                RadiusY = 2,
                Fill = Brush(series.Hex),
                Margin = new Thickness(0, 0, 8, 3),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            Grid.SetRow(swatch, i);
            Grid.SetColumn(swatch, 0);
            grid.Children.Add(swatch);

            var name = new TextBlock { Text = series.Name, Foreground = Brush("#9D9D9D") };
            Grid.SetRow(name, i);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var value = new TextBlock
            {
                Foreground = Brush("#D4D4D4"),
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            };
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);
            _readoutValues[i] = value;
        }

        ReadoutPanel.Children.Add(grid);
    }

    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        IReadOnlyList<MemorySample> samples = _viewModel.Samples;
        if (samples.Count == 0)
        {
            HideReadout();
            return;
        }

        Pixel mouse = Chart.GetPlotPixelPosition(e);
        double hoveredSeconds = Chart.Plot.GetCoordinates(mouse).X;

        MemorySample nearest = samples[0];
        foreach (MemorySample sample in samples)
        {
            if (Math.Abs(sample.ElapsedSeconds - hoveredSeconds) <
                Math.Abs(nearest.ElapsedSeconds - hoveredSeconds))
            {
                nearest = sample;
            }
        }

        _readoutTime!.Text = $"{nearest.Timestamp:HH:mm:ss}   +{nearest.ElapsedSeconds:F0}초";
        for (int i = 0; i < SeriesDefinitions.Length; i++)
            _readoutValues[i].Text = ByteSize.Format(SeriesDefinitions[i].Bytes(nearest));

        // Snap the crosshair to the sample, not the cursor, so the numbers and the line agree.
        double scale = Chart.DisplayScale <= 0 ? 1 : Chart.DisplayScale;
        double snappedX = Chart.Plot.GetPixel(new Coordinates(nearest.ElapsedSeconds, 0)).X / scale;

        Crosshair.X1 = Crosshair.X2 = snappedX;
        Crosshair.Y1 = 0;
        Crosshair.Y2 = Overlay.ActualHeight;
        Crosshair.Visibility = Visibility.Visible;

        Readout.Visibility = Visibility.Visible;
        Readout.UpdateLayout();
        Point cursor = e.GetPosition(Overlay);
        double left = snappedX + 14;
        if (left + Readout.ActualWidth > Overlay.ActualWidth) left = snappedX - Readout.ActualWidth - 14;
        double top = Math.Clamp(cursor.Y - Readout.ActualHeight / 2, 4, Math.Max(4, Overlay.ActualHeight - Readout.ActualHeight - 4));
        Canvas.SetLeft(Readout, Math.Max(4, left));
        Canvas.SetTop(Readout, top);
    }

    private void HideReadout()
    {
        Crosshair.Visibility = Visibility.Collapsed;
        Readout.Visibility = Visibility.Collapsed;
    }

    private static SolidColorBrush Brush(string hex)
        => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
