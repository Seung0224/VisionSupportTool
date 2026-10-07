using System.IO;
using System.Text.Json;
using System.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// What the launcher remembers between runs: where the user parked the icon, and how the icon
/// looks.
///
/// Window translucency is not here. It is fixed at 0.95 in the windows' own markup - it has to be
/// declared before a window is shown (see FeatureWindow.xaml), so it was never something a
/// setting could change at runtime anyway. The icon is the fainter of the two, and the only one
/// that is adjustable.
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

    /// <summary>
    /// The icon sits on top of everything the user is actually working on, so it defaults fainter
    /// than the windows do - present enough to find, faint enough to ignore.
    /// </summary>
    public const double DefaultIconOpacity = 0.70;

    public const double DefaultIconSize = 78;

    public const double MinIconSize = 36;

    public const double MaxIconSize = 104;

    /// <summary>One tile of the opened menu. Fixed rather than a setting: the size was settled
    /// on once and the slider for it was taken out. The ring's radius still follows it.</summary>
    public const double MenuTileSize = 50;

    public double IconLeft { get; set; }

    public double IconTop { get; set; }

    /// <summary>Icon colour as a hue in degrees, 0-360. Saturation and value are fixed, so any
    /// setting lands on a colour that still reads on both light and dark wallpaper.</summary>
    public double IconHue { get; set; } = DefaultIconHue;

    public double IconOpacity { get; set; } = DefaultIconOpacity;

    public double IconSize { get; set; } = DefaultIconSize;

    /// <summary>Ask for a password when packing. The password itself is never stored.</summary>
    public bool UsePassword { get; set; }

    /// <summary>Speed over size. The default, because the usual job is "send this to someone".</summary>
    public bool FastCompress { get; set; } = true;

    /// <summary>File everything dropped into one folder instead of packing it.</summary>
    public bool CopyMode { get; set; }

    public string CopyTargetFolder { get; set; } = string.Empty;

    /// <summary>
    /// The user's own web tiles. Null means "this file has never had them" and gets the shipped
    /// default; an empty list means the user removed them all and is left alone.
    /// </summary>
    public List<LauncherLink>? Links { get; set; }

    public static string DefaultPath => Path.Combine(
        @"D:\Datas", "VisionSupport", "launcher.json");

    /// <summary>
    /// 설정을 %AppData%에 두던 시절의 경로. <see cref="DefaultPath"/>에 파일이 없을 때만 대신 읽는다.
    ///
    /// 이게 없으면 구버전 exe를 쓰다가 새 exe로 넘어오는 순간, 저장해둔 링크와 아이콘 위치가
    /// 통째로 사라진 것처럼 보인다 - 파일이 없다는 것과 처음 실행이라는 것을 구분하지 못하기 때문이다.
    /// 읽기만 여기서 하고 저장은 항상 새 경로로 가므로, 한 번 실행하면 자연스럽게 옮겨진다.
    /// </summary>
    private static string LegacyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisionSupport", "launcher.json");

    /// <summary>
    /// The path this instance was loaded from, remembered so <see cref="Save()"/> always writes
    /// back to where it came from - never a hardcoded path a caller has to get right every time.
    ///
    /// A test building a bare <c>new LauncherSettings()</c> to hand to a ViewModel (never calling
    /// <see cref="Load"/>) leaves this null, so <see cref="Save()"/> becomes a no-op instead of
    /// silently overwriting the real, shared <see cref="DefaultPath"/> file - which is exactly
    /// what happened before this existed: running the test suite quietly wiped the user's saved
    /// icon appearance and link list.
    /// </summary>
    private string? _sourcePath;

    public static LauncherSettings Load(string path)
    {
        LauncherSettings settings = Read(path);

        settings.IconHue = ClampHue(settings.IconHue);
        settings.IconOpacity = ClampOpacity(settings.IconOpacity);
        settings.IconSize = ClampIconSize(settings.IconSize);
        settings.Links ??= LauncherLink.Defaults();
        settings._sourcePath = path;

        return settings;
    }

    /// <summary>Saves back to the path this instance was <see cref="Load">loaded</see> from, or
    /// does nothing if it was never loaded from one.</summary>
    public void Save()
    {
        if (_sourcePath is not null) Save(_sourcePath);
    }

    private static LauncherSettings Read(string path)
    {
        // 새 경로에 없으면 예전 경로의 설정을 이어받는다. 기본 경로로 읽을 때만 해당한다 -
        // 테스트가 임시 파일 경로를 넘길 때까지 %AppData%를 뒤지면 안 된다.
        if (!File.Exists(path) && path == DefaultPath && File.Exists(LegacyPath))
        {
            path = LegacyPath;
        }

        try
        {
            if (!File.Exists(path)) return new LauncherSettings();

            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path))
                   ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 깨진 파일을 그대로 두면 다음 저장 때 조용히 덮여서 복구할 길이 없어진다.
            // 옆으로 치워두면 최소한 되살릴 수는 있다.
            TryPreserveUnreadable(path);
            return new LauncherSettings();
        }
    }

    private static void TryPreserveUnreadable(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 치워두지 못하는 것 자체로 실행을 막을 이유는 없다.
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
