using System.IO;
using System.Windows;
using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Settings a support tool reads at startup have to survive the file being absent, truncated or
/// written by an older build. Failing to start because a preferences file is malformed would be
/// worse than losing the preference.
/// </summary>
public class LauncherSettingsTests
{
    [Fact]
    public void Round_trips_through_disk()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");

        new LauncherSettings { IconLeft = 640, IconTop = 480, IconOpacity = 0.75 }.Save(path);
        LauncherSettings loaded = LauncherSettings.Load(path);

        Assert.Equal(640, loaded.IconLeft);
        Assert.Equal(480, loaded.IconTop);
        Assert.Equal(0.75, loaded.IconOpacity);
    }

    [Fact]
    public void Save_after_loading_writes_back_to_the_loaded_path()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");
        new LauncherSettings { IconLeft = 10 }.Save(path);

        LauncherSettings loaded = LauncherSettings.Load(path);
        loaded.IconLeft = 999;
        loaded.Save();

        Assert.Equal(999, LauncherSettings.Load(path).IconLeft);
    }

    /// <summary>
    /// A ViewModel test that builds a bare LauncherSettings and eventually calls Save() on it
    /// (e.g. via LauncherAppearance) must never reach the real, shared file every running copy of
    /// the app reads - that used to happen on every `dotnet test`, quietly resetting the user's
    /// saved icon appearance and link list.
    /// </summary>
    [Fact]
    public void Save_without_loading_first_does_not_touch_the_shared_default_file()
    {
        bool existedBefore = File.Exists(LauncherSettings.DefaultPath);
        string? contentBefore = existedBefore ? File.ReadAllText(LauncherSettings.DefaultPath) : null;

        new LauncherSettings().Save();

        Assert.Equal(existedBefore, File.Exists(LauncherSettings.DefaultPath));
        if (existedBefore) Assert.Equal(contentBefore, File.ReadAllText(LauncherSettings.DefaultPath));
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var dir = new TempDir();

        LauncherSettings loaded = LauncherSettings.Load(Path.Combine(dir.Path, "nothing.json"));

        Assert.Equal(LauncherSettings.DefaultIconOpacity, loaded.IconOpacity);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_instead_of_throwing()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal(LauncherSettings.DefaultIconOpacity, LauncherSettings.Load(path).IconOpacity);
    }

    [Theory]
    [InlineData(0.0, LauncherSettings.MinOpacity)]
    [InlineData(2.5, LauncherSettings.MaxOpacity)]
    public void Opacity_outside_the_usable_range_is_clamped_on_load(double stored, double expected)
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");
        File.WriteAllText(path, "{\"IconLeft\":0,\"IconTop\":0,\"IconOpacity\":" + stored + "}");

        Assert.Equal(expected, LauncherSettings.Load(path).IconOpacity);
    }

    [Fact]
    public void A_position_on_screen_is_left_alone()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        Point kept = LauncherSettings.ConstrainToScreen(
            new Point(1000, 500), new Size(72, 72), screen, new Point(1, 1));

        Assert.Equal(new Point(1000, 500), kept);
    }

    [Fact]
    public void A_position_on_a_monitor_that_is_gone_falls_back()
    {
        // Remembered on a second monitor to the right; now only the primary is attached.
        var screen = new Rect(0, 0, 1920, 1080);
        var fallback = new Point(1800, 950);

        Point moved = LauncherSettings.ConstrainToScreen(
            new Point(3000, 500), new Size(72, 72), screen, fallback);

        Assert.Equal(fallback, moved);
    }

    [Fact]
    public void A_position_hanging_off_the_bottom_edge_falls_back()
    {
        var screen = new Rect(0, 0, 1920, 1080);
        var fallback = new Point(1800, 950);

        Point moved = LauncherSettings.ConstrainToScreen(
            new Point(900, 1070), new Size(72, 72), screen, fallback);

        Assert.Equal(fallback, moved);
    }
}
