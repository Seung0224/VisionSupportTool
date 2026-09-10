using System.IO;
using System.Text.Json;
using System.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// What the launcher remembers between runs: where the user parked the icon, and how the icon
/// looks.
///
/// Window translucency is not here. It is fixed at half in the windows' own markup - it has to be
/// declared before a window is shown (see FeatureWindow.xaml), so it was never something a
/// setting could change at runtime anyway.
///
/// Every read path falls back to a working default rather than throwing. This file is a
/// convenience; a tool that refuses to start because its preferences are unreadable has turned a
/// convenience into a dependency.
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>Below this the icon is hard to find on a busy desktop, so the slider stops here.</summary>
    public const double MinOpacity = 0.30;

    public const double MaxOpacity = 1.00;

    /// <summary>Blue. The icon sits on someone else's desktop all day, so it defaults to a colour
    /// that reads as "a tool", not as an alert.</summary>
    public const double DefaultIconHue = 212;

    public const double DefaultIconOpacity = 0.85;

    public const double DefaultIconSize = 60;

    public const double MinIconSize = 36;

    public const double MaxIconSize = 104;

    public double IconLeft { get; set; }

    public double IconTop { get; set; }

    /// <summary>Icon colour as a hue in degrees, 0-360. Saturation and value are fixed, so any
    /// setting lands on a colour that still reads on both light and dark wallpaper.</summary>
    public double IconHue { get; set; } = DefaultIconHue;

    public double IconOpacity { get; set; } = DefaultIconOpacity;

    public double IconSize { get; set; } = DefaultIconSize;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisionSupport", "launcher.json");

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new LauncherSettings();

            var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path));
            if (loaded is null) return new LauncherSettings();

            loaded.IconHue = ClampHue(loaded.IconHue);
            loaded.IconOpacity = ClampOpacity(loaded.IconOpacity);
            loaded.IconSize = ClampIconSize(loaded.IconSize);
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(
                this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the icon position is not worth failing a shutdown over.
        }
    }

    public static double ClampOpacity(double value)
        => double.IsNaN(value) ? DefaultIconOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);

    /// <summary>Wraps rather than clamps: a hue is a circle, and 370 degrees is 10, not 360.</summary>
    public static double ClampHue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultIconHue;

        double wrapped = value % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }

    public static double ClampIconSize(double value)
        => double.IsNaN(value) ? DefaultIconSize : Math.Clamp(value, MinIconSize, MaxIconSize);

    /// <summary>
    /// Pulls a remembered icon position back somewhere reachable.
    ///
    /// Monitor layouts change between runs - a laptop undocked from the monitor the icon was
    /// parked on would otherwise put the launcher at coordinates no screen covers, and the only
    /// way back would be deleting the settings file. The whole icon has to fit, not just its
    /// top-left corner, or it ends up half off the edge.
    /// </summary>
    public static Point ConstrainToScreen(Point position, Size iconSize, Rect virtualScreen, Point fallback)
    {
        var icon = new Rect(position, iconSize);
        return virtualScreen.Contains(icon) ? position : fallback;
    }
}
