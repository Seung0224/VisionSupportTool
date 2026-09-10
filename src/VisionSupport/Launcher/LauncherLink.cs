using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSupport.Launcher;

/// <summary>
/// A menu tile that opens a web page in its own browser window instead of a feature window.
///
/// These are the user's own, not the shell's: they live in the settings file and are edited from
/// the links dialog. The shell ships one to start with - a compose window for the groupware mail -
/// because a menu whose only answer to "add your own" is an empty list teaches nothing.
/// </summary>
public sealed partial class LauncherLink : ObservableObject
{
    [ObservableProperty]
    private string _title = "새 링크";

    [ObservableProperty]
    private string _url = string.Empty;

    /// <summary>A Material Design icon name, the same vocabulary the feature tiles use.</summary>
    [ObservableProperty]
    private string _glyph = "OpenInNew";

    [ObservableProperty]
    private int _width = 1100;

    [ObservableProperty]
    private int _height = 800;

    /// <summary>
    /// What a fresh install starts with. The URL carries no session token on purpose - the site
    /// mints one from the browser's own cookie, and a token pasted in here would be stale by
    /// tomorrow.
    /// </summary>
    public static List<LauncherLink> Defaults() => new()
    {
        new LauncherLink
        {
            Title = "메일쓰기",
            Glyph = "EmailEditOutline",
            Url = "https://gw.jasrobotics.co.kr/mail2/writeMailView.do?kind=plain",
            Width = 1100,
            Height = 800,
        },
    };
}
