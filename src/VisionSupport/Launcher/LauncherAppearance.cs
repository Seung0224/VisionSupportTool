using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSupport.Launcher;

/// <summary>
/// The look of the tool, live. One object shared by the launcher window and the overview dialog:
/// the dialog moves a slider, this raises a change, the launcher redraws. Nothing polls and
/// nothing has to be reopened for a setting to take.
///
/// It writes through to <see cref="LauncherSettings"/> as it goes, so the file on disk is always
/// the current look; saving is left to the moments that already save (closing the dialog, moving
/// the icon, exiting), because a slider drag would otherwise write a file per frame.
/// </summary>
public sealed partial class LauncherAppearance : ObservableObject
{
    /// <summary>
    /// Fixed saturation and value for the icon. Only the hue is exposed: a slider that can also
    /// reach "pale grey on a white desktop" is a slider that can make the icon impossible to
    /// find, and this is the one control the user cannot get back to without it.
    /// </summary>
    private const double IconSaturation = 0.72;

    private const double IconValue = 0.90;

    private readonly LauncherSettings _settings;

    public LauncherAppearance(LauncherSettings settings) => _settings = settings;

    public double IconHue
    {
        get => _settings.IconHue;
        set
        {
            double wrapped = LauncherSettings.ClampHue(value);
            if (Nearly(_settings.IconHue, wrapped)) return;

            _settings.IconHue = wrapped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconBrush));
            OnPropertyChanged(nameof(IconEdgeBrush));
        }
    }

    public double IconOpacity
    {
        get => _settings.IconOpacity;
        set
        {
            double clamped = LauncherSettings.ClampOpacity(value);
            if (Nearly(_settings.IconOpacity, clamped)) return;

            _settings.IconOpacity = clamped;
            OnPropertyChanged();
        }
    }

    public double IconSize
    {
        get => _settings.IconSize;
        set
        {
            // Whole pixels only. A 61.4px circle lands the window on a half pixel, and a
            // transparent window off the pixel grid is resampled and looks soft.
            double clamped = Math.Round(LauncherSettings.ClampIconSize(value));
            if (Nearly(_settings.IconSize, clamped)) return;

            _settings.IconSize = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconMarkSize));

            // The ring keeps its distance from the icon, so growing the icon can push it out.
            OnPropertyChanged(nameof(MenuRadius));
            OnPropertyChanged(nameof(MenuSize));
        }
    }

    /// <summary>One tile of the opened menu.</summary>
    public double TileSize => LauncherSettings.MenuTileSize;

    /// <summary>The vector icon inside a tile.</summary>
    public double TileIconSize => Math.Round(TileSize * 0.43);

    /// <summary>
    /// The ring the tiles sit on.
    ///
    /// It follows the tile size, so smaller tiles gather in closer to the icon instead of
    /// floating out on a ring sized for tiles that are no longer there. The floor keeps them off
    /// the icon itself: small tiles around a large icon would otherwise sit on top of it.
    /// </summary>
    public double MenuRadius
        => Math.Round(Math.Max(TileSize * 2, IconSize / 2 + TileSize / 2 + 16));

    /// <summary>
    /// How big the launcher window grows to hold the open menu: the ring, plus half an item on
    /// each side, plus room for the labels.
    ///
    /// Nothing hangs below a tile any more, so the margin is only breathing room - enough that a
    /// tile at its hover size is not clipped by the window edge.
    /// </summary>
    public double MenuSize => Math.Round((MenuRadius + TileSize / 2) * 2 + 32);

    /// <summary>The icon's fill. Frozen: it is rebuilt on every hue change and read from the
    /// render thread.</summary>
    public Brush IconBrush => Frozen(FromHue(IconHue, IconSaturation, IconValue));

    /// <summary>A lighter rim of the same hue, so the circle has an edge on a dark wallpaper.</summary>
    public Brush IconEdgeBrush => Frozen(FromHue(IconHue, IconSaturation * 0.55, 1.0));

    /// <summary>The V scales with the circle rather than staying a fixed 26pt in a 36px button.</summary>
    public double IconMarkSize => Math.Round(IconSize * 0.44);

    public double MinOpacity => LauncherSettings.MinOpacity;

    public double MaxOpacity => LauncherSettings.MaxOpacity;

    public double MinIconSize => LauncherSettings.MinIconSize;

    public double MaxIconSize => LauncherSettings.MaxIconSize;

    public void Save() => _settings.Save();

    /// <summary>
    /// HSV to RGB. WPF has no such conversion, and hue is the only sensible thing to put behind a
    /// single colour slider - stepping through RGB channels gives muddy colours in the middle.
    /// </summary>
    public static Color FromHue(double hue, double saturation, double value)
    {
        double h = LauncherSettings.ClampHue(hue) / 60.0;
        double chroma = value * saturation;
        double second = chroma * (1 - Math.Abs(h % 2 - 1));
        double floor = value - chroma;

        (double r, double g, double b) = (int)h switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second),
        };

        return Color.FromRgb(ToByte(r + floor), ToByte(g + floor), ToByte(b + floor));
    }

    private static byte ToByte(double channel)
        => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool Nearly(double a, double b) => Math.Abs(a - b) < 0.0005;
}
