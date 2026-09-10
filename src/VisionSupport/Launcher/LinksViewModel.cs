using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    private void Add()
    {
        var link = new LauncherLink();
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
