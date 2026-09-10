using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace VisionSupport.Launcher;

/// <summary>
/// The links editor. Two lists rather than one, because the two kinds behave differently on the
/// ring: a web link is a tile of its own, folders share one tile and a list.
///
/// It edits the launcher's own collections in place, so a title typed here is the title the tile
/// carries. The ring follows those collections, so an addition shows up at once.
/// </summary>
public sealed partial class LinksViewModel : ObservableObject
{
    /// <summary>
    /// As many folders as the list can show without becoming a scroll exercise. The ring has its
    /// own limit for tiles; this is the list's.
    /// </summary>
    public const int MaxFolders = 12;

    [ObservableProperty]
    private LauncherLink? _selected;

    [ObservableProperty]
    private LauncherLink? _selectedWeb;

    [ObservableProperty]
    private LauncherLink? _selectedFolder;

    public LinksViewModel(ObservableCollection<LauncherLink> webLinks,
                          ObservableCollection<LauncherLink> folderLinks)
    {
        WebLinks = webLinks;
        FolderLinks = folderLinks;

        FolderLinks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FolderCountLabel));
        SelectedWeb = webLinks.FirstOrDefault();
    }

    public ObservableCollection<LauncherLink> WebLinks { get; }

    public ObservableCollection<LauncherLink> FolderLinks { get; }

    /// <summary>Hides the edit fields when nothing is selected, rather than showing dead boxes.</summary>
    public bool HasSelection => Selected is not null;

    public string FolderCountLabel => $"폴더 ({FolderLinks.Count}/{MaxFolders})";

    [RelayCommand]
    private void AddWeb()
    {
        var link = new LauncherLink { Title = "새 링크", Kind = LinkKind.Web, Glyph = "OpenInNew" };
        WebLinks.Add(link);
        SelectedWeb = link;
    }

    /// <summary>
    /// A new folder tile points at Documents rather than nowhere. A tile that does nothing until
    /// it is configured is a tile that looks broken; this one works the moment it appears, and
    /// Browse changes where it goes.
    /// </summary>
    [RelayCommand]
    private void AddFolder()
    {
        if (FolderLinks.Count >= MaxFolders) return;

        var link = new LauncherLink
        {
            Title = "문서",
            Kind = LinkKind.Folder,
            Glyph = "FolderOutline",
            Url = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        FolderLinks.Add(link);
        SelectedFolder = link;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } link) return;

        ObservableCollection<LauncherLink> list = link.Kind == LinkKind.Web ? WebLinks : FolderLinks;
        int index = list.IndexOf(link);
        list.Remove(link);

        LauncherLink? next = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)];
        if (link.Kind == LinkKind.Web) SelectedWeb = next;
        else SelectedFolder = next;

        if (next is null) Selected = null;
    }

    /// <summary>
    /// Picks the folder rather than making the user type a path. Network paths and deep project
    /// folders are exactly where a typo is easy and the failure is silent.
    /// </summary>
    [RelayCommand]
    private void Browse()
    {
        if (Selected is not { Kind: LinkKind.Folder } link) return;

        var dialog = new OpenFolderDialog { Title = "폴더 선택" };
        if (Directory.Exists(link.Url)) dialog.InitialDirectory = link.Url;

        if (dialog.ShowDialog() != true) return;

        link.Url = dialog.FolderName;
        if (link.Title is "문서" or "새 폴더" or "") link.Title = Path.GetFileName(dialog.FolderName);
    }

    // Only one row is edited at a time, so choosing in either list clears the other. The null
    // guard is what stops that clearing from bouncing back and forth.
    partial void OnSelectedWebChanged(LauncherLink? value)
    {
        if (value is null) return;

        SelectedFolder = null;
        Selected = value;
    }

    partial void OnSelectedFolderChanged(LauncherLink? value)
    {
        if (value is null) return;

        SelectedWeb = null;
        Selected = value;
    }

    partial void OnSelectedChanged(LauncherLink? value) => OnPropertyChanged(nameof(HasSelection));
}
