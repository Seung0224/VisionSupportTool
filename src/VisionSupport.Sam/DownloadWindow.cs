using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionSupport.Sam;

/// <summary>
/// The one-time model download. Its own small window rather than the dark overlay: a download of
/// this size can take a while, and the overlay would hold the whole screen hostage for it.
/// </summary>
internal sealed class DownloadWindow : Window
{
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 10, 0, 0) };

    public DownloadWindow(long totalBytes)
    {
        Title = "누끼 모델";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;

        // The shell's dark theme does not reach a window type it has no style for, so the colours
        // are set here to match it.
        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Width = 320,
            Children =
            {
                new TextBlock { Text = $"처음 한 번 누끼 모델을 내려받습니다 ({totalBytes / 1048576.0:F0}MB)" },
                _bar,
            },
        };

        Progress = new Progress<double>(value => _bar.Value = value);
        Closed += (_, _) => AutomationDisconnect.Disconnect(this);
    }

    /// <summary>Created on the UI thread, so reports from the download's thread land back here.</summary>
    public IProgress<double> Progress { get; }
}
