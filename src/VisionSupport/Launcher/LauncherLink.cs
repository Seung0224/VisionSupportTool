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

    /// <summary>
    /// What the tile points at: a web address, or a folder to open in Explorer.
    ///
    /// Fixed when the tile is made - the editor has an add button per kind rather than a switch,
    /// because the two have nothing in common to carry across.
    /// </summary>
    [ObservableProperty]
    private LinkKind _kind = LinkKind.Web;

    /// <summary>The address for a web tile, the folder path for a folder one. One field because
    /// it is one idea - where the tile goes - and because renaming it would drop what is already
    /// in everyone's settings file.</summary>
    [ObservableProperty]
    private string _url = string.Empty;

    /// <summary>A Material Design icon name, the same vocabulary the feature tiles use.</summary>
    [ObservableProperty]
    private string _glyph = "OpenInNew";

    [ObservableProperty]
    private int _width = 1100;

    [ObservableProperty]
    private int _height = 800;

    /// <summary>True for a web tile. Drives the editor: a folder has no popup size to set, and
    /// its target is a path rather than an address.</summary>
    public bool IsWeb => Kind == LinkKind.Web;

    /// <summary>Names the target field in the editor, so it asks for the right thing.</summary>
    public string TargetLabel => IsWeb ? "주소" : "폴더 경로";

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
            Kind = LinkKind.Web,
            Glyph = "EmailEditOutline",
            Url = "https://gw.jasrobotics.co.kr/mail2/writeMailView.do?kind=plain",
            Width = 1100,
            Height = 800,
        },
    };

    partial void OnKindChanged(LinkKind value)
    {
        OnPropertyChanged(nameof(IsWeb));
        OnPropertyChanged(nameof(TargetLabel));
    }
}

/// <summary>What a launcher tile opens.</summary>
public enum LinkKind
{
    Web,

    Folder,
}
