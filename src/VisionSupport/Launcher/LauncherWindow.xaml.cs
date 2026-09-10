using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VisionSupport.Launcher;

/// <summary>
/// The floating icon. The only thing on screen when nothing is being used.
///
/// The window grows from 72x72 to 340x340 while the menu is open and shrinks back afterwards. A
/// permanently 340-wide topmost window would sit invisibly over a corner of the desktop; clicks
/// pass through its empty area, but OLE drag-and-drop targeting is less forgiving, and the image
/// converter accepts dropped files. Keeping the big window short-lived removes that question.
/// </summary>
public partial class LauncherWindow : Window
{
    private const double CollapsedSize = 72;

    /// <summary>How far the pointer has to move before a press counts as a drag, not a click.</summary>
    private const double DragThreshold = 4;

    private readonly LauncherSettings _settings;

    private Point _pressOrigin;
    private Point _pressWindowOrigin;
    private bool _pressed;
    private bool _dragged;

    public LauncherWindow(LauncherViewModel viewModel, LauncherSettings settings)
    {
        InitializeComponent();

        DataContext = viewModel;
        _settings = settings;

        Left = settings.IconLeft;
        Top = settings.IconTop;

        Fab.MouseLeftButtonDown += OnFabPressed;
        Fab.MouseMove += OnFabMoved;
        Fab.MouseLeftButtonUp += OnFabReleased;

        OverviewMenuItem.Click += (_, _) => viewModel.ShowOverview();
        ExitMenuItem.Click += async (_, _) => await viewModel.ExitAsync();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Collapse();
        };

        // Clicking anywhere else - another window, the desktop - is the ordinary way out of a
        // menu that has no dismiss button.
        Deactivated += (_, _) => Collapse();

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.IsExpanded) && !viewModel.IsExpanded)
            {
                Collapse();
            }
        };
    }

    private LauncherViewModel ViewModel => (LauncherViewModel)DataContext;

    private void OnFabPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragged = false;
        _pressOrigin = PointToScreen(e.GetPosition(this));
        _pressWindowOrigin = new Point(Left, Top);
        Fab.CaptureMouse();
    }

    private void OnFabMoved(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;

        Point now = PointToScreen(e.GetPosition(this));
        Vector moved = now - _pressOrigin;
        if (!_dragged && moved.Length < DragThreshold) return;

        _dragged = true;

        // Screen pixels to DIPs: on a 150% monitor the window would otherwise run 1.5x ahead of
        // the pointer.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = _pressWindowOrigin.X + moved.X / scale;
        Top = _pressWindowOrigin.Y + moved.Y / scale;
    }

    private void OnFabReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        Fab.ReleaseMouseCapture();

        if (_dragged)
        {
            RememberPosition();
            return;
        }

        if (ViewModel.IsExpanded) Collapse();
        else Expand();
    }

    private void Expand()
    {
        Resize(LauncherViewModel.ExpandedSize);
        NudgeOntoScreen();

        Menu.Visibility = Visibility.Visible;
        Animate(0.6, 1.0);
        ViewModel.IsExpanded = true;
    }

    private void Collapse()
    {
        if (!ViewModel.IsExpanded && Menu.Visibility == Visibility.Collapsed) return;

        ViewModel.IsExpanded = false;
        Menu.Visibility = Visibility.Collapsed;
        Resize(CollapsedSize);
        RememberPosition();
    }

    /// <summary>Resizes around the icon's centre, so growing the window does not move the icon.</summary>
    private void Resize(double size)
    {
        double centreX = Left + Width / 2;
        double centreY = Top + Height / 2;

        Width = size;
        Height = size;
        Left = centreX - size / 2;
        Top = centreY - size / 2;
    }

    /// <summary>
    /// Pulls the expanded window back onto the desktop. An icon parked in a corner would
    /// otherwise open half its menu off the edge of the screen.
    /// </summary>
    private void NudgeOntoScreen()
    {
        double minLeft = SystemParameters.VirtualScreenLeft;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - Width;
        double maxTop = minTop + SystemParameters.VirtualScreenHeight - Height;

        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
    }

    private void Animate(double from, double to)
    {
        var scale = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void RememberPosition()
    {
        _settings.IconLeft = Left;
        _settings.IconTop = Top;
        _settings.Save(LauncherSettings.DefaultPath);
    }
}
