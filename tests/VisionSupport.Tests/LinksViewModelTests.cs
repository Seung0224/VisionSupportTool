using System.Collections.ObjectModel;
using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The links editor keeps two lists because the two kinds live differently on the ring, and only
/// one row is edited at a time. Both of those are easy to get subtly wrong and invisible until
/// someone is mid-edit, so they are pinned here.
/// </summary>
public class LinksViewModelTests
{
    [Fact]
    public void A_web_link_and_a_folder_go_to_their_own_lists()
    {
        LinksViewModel editor = Empty();

        editor.AddWebCommand.Execute(null);
        editor.AddFolderCommand.Execute(null);

        Assert.Single(editor.WebLinks);
        Assert.Single(editor.FolderLinks);
        Assert.Equal(LinkKind.Web, editor.WebLinks[0].Kind);
        Assert.Equal(LinkKind.Folder, editor.FolderLinks[0].Kind);
    }

    [Fact]
    public void A_new_folder_starts_somewhere_real()
    {
        LinksViewModel editor = Empty();

        editor.AddFolderCommand.Execute(null);

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     editor.FolderLinks[0].Url);
    }

    [Fact]
    public void Folders_stop_at_the_limit()
    {
        LinksViewModel editor = Empty();

        for (int i = 0; i < LinksViewModel.MaxFolders + 5; i++) editor.AddFolderCommand.Execute(null);

        Assert.Equal(LinksViewModel.MaxFolders, editor.FolderLinks.Count);
    }

    /// <summary>
    /// Choosing in one list has to clear the other, or the edit fields would show one row while
    /// a different one looks selected.
    /// </summary>
    [Fact]
    public void Choosing_in_one_list_releases_the_other()
    {
        LinksViewModel editor = Empty();
        editor.AddWebCommand.Execute(null);
        editor.AddFolderCommand.Execute(null);

        editor.SelectedWeb = editor.WebLinks[0];

        Assert.Null(editor.SelectedFolder);
        Assert.Same(editor.WebLinks[0], editor.Selected);

        editor.SelectedFolder = editor.FolderLinks[0];

        Assert.Null(editor.SelectedWeb);
        Assert.Same(editor.FolderLinks[0], editor.Selected);
    }

    [Fact]
    public void Removing_takes_the_row_out_of_the_list_it_belongs_to()
    {
        LinksViewModel editor = Empty();
        editor.AddWebCommand.Execute(null);
        editor.AddFolderCommand.Execute(null);
        editor.SelectedFolder = editor.FolderLinks[0];

        editor.RemoveCommand.Execute(null);

        Assert.Single(editor.WebLinks);
        Assert.Empty(editor.FolderLinks);
    }

    private static LinksViewModel Empty()
        => new(new ObservableCollection<LauncherLink>(), new ObservableCollection<LauncherLink>());
}
