using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VisionSupport.Archive;

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

    /// <summary>When the folder list last shut itself, so a press that caused that is not read
    /// as a press asking for it back.</summary>
    private DateTime _folderListClosedAt;

    private DateTime _toolBoxClosedAt;

    private ArchiveSettingsDialog? _archiveSettings;


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
        ArchiveMenuItem.Click += (_, _) => ShowArchiveSettings();
        ExitMenuItem.Click += async (_, _) => await viewModel.ExitAsync();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Collapse();
        };

        // Clicking anywhere else - another window, the desktop - is the ordinary way out of a
        // menu that has no dismiss button.
        Deactivated += (_, _) => Collapse();

        FolderList.Closed += (_, _) => _folderListClosedAt = DateTime.UtcNow;
        ToolBox.Closed += (_, _) => _toolBoxClosedAt = DateTime.UtcNow;

        SourceInitialized += (_, _) => AcceptFileDrops();
        StartHoverWatch();

        viewModel.Drops.PropertyChanged += (_, _) => ShowProgress();
        viewModel.Appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherAppearance.IconMarkSize)) ShowProgress();
        };

        Loaded += (_, _) => ShowProgress();

        viewModel.CollapseNowRequested += (_, _) =>
        {
            FolderList.IsOpen = false;
            ToolBox.IsOpen = false;
            Collapse(animate: false);
        };

        // The folder tile and the tools tile open beside the menu and leave the menu up: picking
        // from them is a second step, and closing the ring underneath would make it look like the
        // click had already done something.
        viewModel.FolderListRequested += (_, _) => ToggleBesideRing(FolderList, _folderListClosedAt);
        viewModel.ToolBoxRequested += (_, _) => ToggleBesideRing(ToolBox, _toolBoxClosedAt);

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

    /// <summary>
    /// Freezes the menu into a bitmap for the length of an animation.
    ///
    /// Scaling five tiles - each a rounded border with a drop shadow, a vector icon and a label -
    /// re-rasterises all of it every frame on a transparent window, which renders in software.
    /// Cached, the animation scales one ready-made bitmap.
    ///
    /// It comes off the moment the animation ends. A cache left on is a picture of the menu
    /// rather than the menu, and it shows: soft edges on everything, permanently. Baked at the
    /// monitor's own pixel density for the same reason - a bitmap rendered at 1.0 and displayed
    /// on a 150% screen is stretched on its way there.
    /// </summary>
    private void HoldMenuAsBitmap()
        => Menu.CacheMode = new BitmapCache
        {
            RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX,
            SnapsToDevicePixels = true,
        };

    private void ReleaseMenuBitmap() => Menu.CacheMode = null;

    /// <summary>
    /// Opens the folder list or the tools box clear of the ring, or shuts it if it is already up.
    ///
    /// The popup is placed against the centre icon, which is the only element that stays put, so
    /// the offset has to carry it past the tiles: out to the ring, plus half a tile for the one
    /// sitting at nine o'clock, less the half icon the placement already skipped, plus a gap.
    /// Negative because it goes to the left, and all three sizes are settings the user can move,
    /// so it is worked out each time rather than written into the markup.
    ///
    /// The guard is what makes a second press on the tile close the popup. The popup does not
    /// stay open when something else is clicked, so by the time the tile's click arrives the
    /// popup has already shut itself and reopening would look like the press did nothing.
    /// </summary>
    private void ToggleBesideRing(Popup popup, DateTime closedAt)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 250) return;

        LauncherAppearance look = ViewModel.Appearance;

        popup.HorizontalOffset =
            -(look.MenuRadius + look.TileSize / 2 - look.IconSize / 2 + 20);

        popup.IsOpen = true;
    }

    // ---- Drag and drop -----------------------------------------------------------------------

    private const int WM_DROPFILES = 0x0233;

    private const int VK_LBUTTON = 0x01;

    /// <summary>Fast enough to feel immediate, slow enough to be free.</summary>
    private static readonly TimeSpan HoverPoll = TimeSpan.FromMilliseconds(80);

    private DispatcherTimer? _hoverWatch;
    private bool _hovering;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder? file, uint length);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr drop);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    /// <summary>
    /// Makes the icon a drop target the old way, which is the only way that works here.
    ///
    /// WPF's AllowDrop registers an OLE IDropTarget. This process is elevated and Explorer is not,
    /// so Explorer cannot reach that interface - and while it is registered, Explorer will not
    /// fall back to the legacy WM_DROPFILES route either. The result is a refusal cursor and no
    /// events at all, which looks exactly like a feature nobody implemented.
    ///
    /// So the OLE target is revoked, the window asks for dropped files directly, and the messages
    /// that carry them are let back through the integrity filter. The image converter arrived at
    /// the same three lines for the same reason.
    ///
    /// What this costs is drag-over feedback: WM_DROPFILES arrives on the drop and never before
    /// it, so the icon cannot swell while a file is held over it. The progress ring covers the
    /// part the user actually needs to see.
    /// </summary>
    /// <summary>
    /// Lights the icon up while something is dragged over it.
    ///
    /// This has to be inferred rather than received. Drag-over is part of the OLE protocol, and
    /// that protocol is what UIPI blocks for an elevated window - during a drag from Explorer this
    /// window is sent nothing at all, not even a mouse move, because the drag source holds the
    /// mouse capture. The legacy route that does work only speaks on the drop itself.
    ///
    /// So: the left button is held and the pointer is over the icon. That is also true while the
    /// user drags the icon itself, which is excluded, and while they drag anything else across it,
    /// which is not - and a brief highlight in that case is a target saying "here", not a lie.
    ///
    /// The cost is one GetAsyncKeyState per tick, and the position is only asked for when the
    /// button is actually down.
    /// </summary>
    private void StartHoverWatch()
    {
        _hoverWatch = new DispatcherTimer(HoverPoll, DispatcherPriority.Background,
                                          (_, _) => CheckHover(), Dispatcher);
        _hoverWatch.Start();
        Closed += (_, _) => _hoverWatch.Stop();
    }

    private void CheckHover()
    {
        bool held = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool over = held && !_pressed && !ViewModel.Drops.IsBusy && PointerIsOverIcon();

        if (over == _hovering) return;

        _hovering = over;
        AnimateFabZoom(over ? 1.18 : 1.0);
        Animate(DropGlow, UIElement.OpacityProperty, over ? 0.28 : 0.0, 120);
    }

    private bool PointerIsOverIcon()
    {
        if (!GetCursorPos(out NativePoint cursor)) return false;

        // Screen pixels on both sides: PointToScreen already returns device coordinates, so the
        // monitor's scaling never enters into it.
        Point topLeft = Fab.PointToScreen(new Point(0, 0));
        Point bottomRight = Fab.PointToScreen(new Point(Fab.ActualWidth, Fab.ActualHeight));

        return cursor.X >= topLeft.X && cursor.X <= bottomRight.X
            && cursor.Y >= topLeft.Y && cursor.Y <= bottomRight.Y;
    }

    private static void Animate(UIElement target, DependencyProperty property, double to, int milliseconds)
        => target.BeginAnimation(property,
            new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

    private void AcceptFileDrops()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;

        DropElevation.AllowDropMessages();

        string? refused = DropElevation.AllowDropMessagesFor(source.Handle);
        if (refused is not null) ViewModel.NoteDropFilter(refused);

        RevokeDragDrop(source.Handle);
        DragAcceptFiles(source.Handle, true);
        source.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_DROPFILES) return IntPtr.Zero;

        string[] paths = ReadDroppedFiles(wParam);
        handled = true;

        if (paths.Length > 0) _ = HandleDrop(paths);
        return IntPtr.Zero;
    }

    private static string[] ReadDroppedFiles(IntPtr drop)
    {
        try
        {
            // 0xFFFFFFFF asks for the count rather than a name.
            uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            var paths = new List<string>((int)count);

            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFileW(drop, i, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                DragQueryFileW(drop, i, buffer, (uint)buffer.Capacity);
                paths.Add(buffer.ToString());
            }

            return paths.ToArray();
        }
        finally
        {
            DragFinish(drop);
        }
    }

    private async Task HandleDrop(string[] paths)
    {
        _hovering = false;
        AnimateFabZoom(1.0);
        Animate(DropGlow, UIElement.OpacityProperty, 0.0, 120);

        ViewModel.NoteDrag(paths.Length, ViewModel.Drops.CanAccept);

        Collapse(animate: false);
        await ViewModel.Drops.HandleAsync(paths);
    }

    /// <summary>
    /// Draws whatever the drop is doing onto the icon.
    ///
    /// Two readouts, because they answer different questions. The ring says how far along at a
    /// glance - it is one ellipse whose dash pattern is sized to its own circumference, so the
    /// dash is the finished part and growing it draws the arc. The mark carries the number, and
    /// then the tick or the bang, which is what the original showed and what says whether it
    /// worked.
    ///
    /// The percentage is set smaller than the V: "100%" is four characters where the V is one.
    /// </summary>
    private void ShowProgress()
    {
        DropCoordinator drops = ViewModel.Drops;
        bool working = drops.State == DropState.Working;

        FabMark.Text = drops.Mark;
        FabMark.FontSize = Math.Max(8, ViewModel.Appearance.IconMarkSize * drops.MarkScale);
        FabMark.Foreground = drops.State switch
        {
            DropState.Done => (Brush)FindResource("StateRunning"),
            DropState.Failed => (Brush)FindResource("StateFaulted"),
            _ => Brushes.White,
        };


        Progress.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        if (!working) return;

        double radius = (ViewModel.Appearance.IconSize - 8) / 2;
        double circumference = 2 * Math.PI * radius / Progress.StrokeThickness;
        double filled = circumference * drops.Percent / 100.0;

        Progress.StrokeDashArray = new DoubleCollection { filled, Math.Max(0.01, circumference - filled) };
    }

    /// <summary>
    /// Opens the drop options, and keeps only one of them open. Same shape as the overview and
    /// the links editor, because it is the same kind of thing.
    /// </summary>
    private void ShowArchiveSettings()
    {
        Collapse(animate: false);

        if (_archiveSettings is not null)
        {
            _archiveSettings.Activate();
            return;
        }

        _archiveSettings = new ArchiveSettingsDialog(_settings);
        _archiveSettings.Closed += (_, _) => _archiveSettings = null;
        _archiveSettings.Show();
    }

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
        Left = Math.Round(_pressWindowOrigin.X + moved.X / scale);
        Top = Math.Round(_pressWindowOrigin.Y + moved.Y / scale);
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

        Resize(ViewModel.Appearance.MenuSize);
        NudgeOntoScreen();

        Menu.Visibility = Visibility.Visible;
        HoldMenuAsBitmap();

        // Decelerating, with no overshoot. A springy ease sends every tile past its resting place
        // and back, and because the places are arranged in a circle that reads as the whole menu
        // scattering outwards rather than as one thing springing open.
        AnimateMenu(1.0, 1.0, new CubicEase { EasingMode = EasingMode.EaseOut }, 240,
                    onDone: ReleaseMenuBitmap);
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
        FolderList.IsOpen = false;
        ToolBox.IsOpen = false;
        int token = ++_animationToken;

        if (animate)
        {
            HoldMenuAsBitmap();
            AnimateMenu(FoldedScale, 0.0, new CubicEase { EasingMode = EasingMode.EaseIn }, 170,
                        () =>
                        {
                            ReleaseMenuBitmap();
                            FinishCollapse(token);
                        });
        }
        else
        {
            ReleaseMenuBitmap();

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

    /// <summary>
    /// Resizes around the icon's centre, so growing the window does not move the icon.
    ///
    /// Everything is rounded to whole pixels. A transparent window sitting on a half pixel is
    /// resampled onto the screen grid, and every edge in it - the circle, the tiles, the letter -
    /// comes out soft. Half a pixel is all it takes, and the icon size comes off a slider.
    /// </summary>
    private void Resize(double size)
    {
        double whole = Math.Round(size);
        double centreX = Left + Width / 2;
        double centreY = Top + Height / 2;

        Width = whole;
        Height = whole;
        Left = Math.Round(centreX - whole / 2);
        Top = Math.Round(centreY - whole / 2);
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

        Left = Math.Round(Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft)));
        Top = Math.Round(Math.Clamp(Top, minTop, Math.Max(minTop, maxTop)));
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
