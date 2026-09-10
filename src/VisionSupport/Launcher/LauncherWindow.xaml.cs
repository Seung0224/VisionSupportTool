using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VisionSupport.Launcher;

/// <summary>
/// The floating icon. The only thing on screen when nothing is being used.
///
/// The window grows from 72x72 to 360x360 while the menu is open and shrinks back afterwards. A
/// permanently 360-wide topmost window would sit invisibly over a corner of the desktop; clicks
/// pass through its empty area, but OLE drag-and-drop targeting is less forgiving, and the image
/// converter accepts dropped files. Keeping the big window short-lived removes that question.
///
/// The window resize itself is invisible - the window is transparent - so all the motion the user
/// sees comes from the storyboards here. Both directions are animated: the tiles fly out of the
/// icon with a slight overshoot, and fly back into it before the window shrinks. Shrinking the
/// window first would make them vanish mid-flight.
/// </summary>
public partial class LauncherWindow : Window
{
    /// <summary>Breathing room around the icon, so the drop shadow is not clipped by the window.</summary>
    private const double IconMargin = 12;

    /// <summary>How far the pointer has to move before a press counts as a drag, not a click.</summary>
    private const double DragThreshold = 4;

    /// <summary>Where the menu starts from and returns to: small, centred on the icon.</summary>
    private const double FoldedScale = 0.35;

    private readonly LauncherSettings _settings;

    private Point _pressOrigin;
    private Point _pressWindowOrigin;
    private bool _pressed;
    private bool _dragged;
    private bool _menuOpen;

    /// <summary>
    /// Invalidates the completion handler of a run that has been overtaken. Without it, expanding
    /// during a collapse would let the collapse's handler shrink the window under the new menu.
    /// </summary>
    private int _animationToken;

    public LauncherWindow(LauncherViewModel viewModel, LauncherSettings settings)
    {
        InitializeComponent();

        DataContext = viewModel;
        _settings = settings;

        Width = CollapsedSize;
        Height = CollapsedSize;
        Left = settings.IconLeft;
        Top = settings.IconTop;

        // Resizing the icon resizes the window it lives in - the collapsed window is only ever
        // as big as the icon plus its shadow.
        viewModel.Appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherAppearance.IconSize) && !_menuOpen)
            {
                Resize(CollapsedSize);
            }
        };

        Fab.MouseLeftButtonDown += OnFabPressed;
        Fab.MouseMove += OnFabMoved;
        Fab.MouseLeftButtonUp += OnFabReleased;
        Fab.MouseEnter += (_, _) => AnimateFabZoom(1.07);
        Fab.MouseLeave += (_, _) => AnimateFabZoom(1.0);

        OverviewMenuItem.Click += (_, _) => viewModel.ShowOverview();
        LinksMenuItem.Click += (_, _) => viewModel.ShowLinks();
        ExitMenuItem.Click += async (_, _) => await viewModel.ExitAsync();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Collapse();
        };

        // Clicking anywhere else - another window, the desktop - is the ordinary way out of a
        // menu that has no dismiss button.
        Deactivated += (_, _) => Collapse();

        viewModel.CollapseNowRequested += (_, _) => Collapse(animate: false);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.IsExpanded) && !viewModel.IsExpanded)
            {
                Collapse();
            }
        };
    }

    private LauncherViewModel ViewModel => (LauncherViewModel)DataContext;

    private double CollapsedSize => ViewModel.Appearance.IconSize + IconMargin * 2;

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

        if (_menuOpen) Collapse();
        else Expand();
    }

    private void Expand()
    {
        if (_menuOpen) return;

        _menuOpen = true;
        _animationToken++;
        ViewModel.IsExpanded = true;

        Resize(LauncherViewModel.ExpandedSize);
        NudgeOntoScreen();

        Menu.Visibility = Visibility.Visible;

        // A little overshoot on the way out is what makes it read as "sprung open" rather than
        // "resized". Coming back in it eases straight, because an overshoot on the way to nothing
        // just looks like a stutter.
        AnimateMenu(1.0, 1.0, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 },
                    260, onDone: null);
        AnimateFabAngle(180);
    }

    /// <param name="animate">
    /// False when something heavy is about to be built on this thread - opening a feature window
    /// blocks for as long as its view takes to construct, and an animation playing into that
    /// freezes halfway and jumps. Closing at once looks deliberate; stuttering does not.
    /// </param>
    private void Collapse(bool animate = true)
    {
        if (!_menuOpen) return;

        _menuOpen = false;
        ViewModel.IsExpanded = false;
        int token = ++_animationToken;

        if (animate)
        {
            AnimateMenu(FoldedScale, 0.0, new CubicEase { EasingMode = EasingMode.EaseIn }, 170,
                        () => FinishCollapse(token));
        }
        else
        {
            // Hand the properties back from the animations before setting them, or the animation
            // clock keeps its hold and the values are ignored.
            MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Menu.BeginAnimation(OpacityProperty, null);
            MenuScale.ScaleX = FoldedScale;
            MenuScale.ScaleY = FoldedScale;
            Menu.Opacity = 0;

            FinishCollapse(token);
        }

        AnimateFabAngle(0);
    }

    private void FinishCollapse(int token)
    {
        if (token != _animationToken) return;

        Menu.Visibility = Visibility.Collapsed;

        // Shrink on a later pass, once the hidden menu has actually been painted away. Resizing a
        // transparent window in the same breath as hiding its content leaves the old pixels
        // sitting on the desktop until something else happens to repaint over them - the trail
        // that looks like the menu is still half there.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (token != _animationToken) return;

            Resize(CollapsedSize);
            RememberPosition();
        }));
    }

    private void AnimateMenu(double scale, double opacity, IEasingFunction ease, int milliseconds,
                             Action? onDone)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));

        // Separate instances per property: one Timeline started twice raises Completed twice.
        var scaleX = new DoubleAnimation(scale, duration) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(scale, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(opacity,
            new Duration(TimeSpan.FromMilliseconds(milliseconds * 0.75)));

        if (onDone is not null) scaleX.Completed += (_, _) => onDone();

        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        Menu.BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateFabAngle(double angle)
        => FabRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });

    private void AnimateFabZoom(double scale)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(130));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        FabZoom.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = ease });
        FabZoom.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = ease });
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

    /// <summary>
    /// Saves the icon's position, but only when it has actually moved. This runs at the end of
    /// every collapse, and a settings file rewritten on the UI thread every time the menu closes
    /// is a disk write in the middle of an animation.
    /// </summary>
    private void RememberPosition()
    {
        if (Math.Abs(_settings.IconLeft - Left) < 0.5 && Math.Abs(_settings.IconTop - Top) < 0.5)
        {
            return;
        }

        _settings.IconLeft = Left;
        _settings.IconTop = Top;
        _settings.Save(LauncherSettings.DefaultPath);
    }
}
