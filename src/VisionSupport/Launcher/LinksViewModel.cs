using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace VisionSupport.Launcher;

/// <summary>
/// The links editor. Edits the launcher's own collection in place rather than a copy, so a title
/// typed here is the title the tile will carry; the ring is rebuilt when the dialog closes,
/// because that is when the count has settled.
/// </summary>
public sealed partial class LinksViewModel : ObservableObject
{
    [ObservableProperty]
    private LauncherLink? _selected;

    public LinksViewModel(ObservableCollection<LauncherLink> links)
    {
        Links = links;
        Selected = links.FirstOrDefault();
    }

    public ObservableCollection<LauncherLink> Links { get; }

    /// <summary>Hides the edit fields when nothing is selected, rather than showing dead boxes.</summary>
    public bool HasSelection => Selected is not null;

    [RelayCommand]
    private void AddWeb() => Append(new LauncherLink
    {
        Title = "새 링크",
        Kind = LinkKind.Web,
        Glyph = "OpenInNew",
    });

    /// <summary>
    /// A new folder tile points at Documents rather than nowhere. A tile that does nothing until
    /// it is configured is a tile that looks broken; this one works the moment it appears, and
    /// Browse changes where it goes.
    /// </summary>
    [RelayCommand]
    private void AddFolder() => Append(new LauncherLink
    {
        Title = "문서",
        Kind = LinkKind.Folder,
        Glyph = "FolderOutline",
        Url = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    });

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
        if (link.Title is "새 폴더" or "") link.Title = Path.GetFileName(dialog.FolderName);
    }

    private void Append(LauncherLink link)
    {
        Links.Add(link);
        Selected = link;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } link) return;

        int index = Links.IndexOf(link);
        Links.Remove(link);
        Selected = Links.Count == 0 ? null : Links[Math.Min(index, Links.Count - 1)];
    }

    partial void OnSelectedChanged(LauncherLink? value) => OnPropertyChanged(nameof(HasSelection));
}
