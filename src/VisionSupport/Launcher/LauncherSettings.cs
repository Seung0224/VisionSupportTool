using System.IO;
using System.Text.Json;
using System.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// What the launcher remembers between runs: where the user parked the icon, and how far through
/// the windows they want to see.
///
/// Every read path here falls back to a working default rather than throwing. This file is a
/// convenience; a tool that refuses to start because its preferences are unreadable has turned a
/// convenience into a dependency.
/// </summary>
public sealed class LauncherSettings
{
    public const double DefaultOpacity = 0.92;

    /// <summary>Below this a window is hard to find on a busy desktop, so the slider stops here.</summary>
    public const double MinOpacity = 0.30;

    public const double MaxOpacity = 1.00;

    public double IconLeft { get; set; }

    public double IconTop { get; set; }

    public double Opacity { get; set; } = DefaultOpacity;

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

            loaded.Opacity = ClampOpacity(loaded.Opacity);
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
        => double.IsNaN(value) ? DefaultOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);

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
