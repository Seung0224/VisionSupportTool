using System.IO;
using System.Windows;
using Microsoft.Win32;
using VisionSupport.Launcher;

namespace VisionSupport.Archive;

/// <summary>
/// The drop options, as a dialog rather than a nest of context-menu checkmarks.
///
/// They are the same three switches either way, but two of them are not self-explanatory - what
/// "빠른 압축" trades away, what copy mode stops doing - and a menu has nowhere to say so. It also
/// puts them where the other settings already live: a window shaped like the overview and the
/// links editor.
/// </summary>
public partial class ArchiveSettingsDialog : Window
{
    private readonly LauncherSettings _settings;

    public ArchiveSettingsDialog(LauncherSettings settings)
    {
        InitializeComponent();

        _settings = settings;

        UsePassword.IsChecked = settings.UsePassword;
        FastCompress.IsChecked = settings.FastCompress;
        CopyMode.IsChecked = settings.CopyMode;
        CopyTarget.Text = settings.CopyTargetFolder;

        // Written as they are toggled rather than on OK. There is no OK: the window has one
        // button and it closes, so anything not saved on the spot would be lost by design.
        UsePassword.Click += (_, _) => Store(() => settings.UsePassword = UsePassword.IsChecked == true);
        FastCompress.Click += (_, _) => Store(() => settings.FastCompress = FastCompress.IsChecked == true);
        CopyMode.Click += (_, _) => Store(() => settings.CopyMode = CopyMode.IsChecked == true);

        BrowseButton.Click += (_, _) => PickCopyTarget();
        CloseButton.Click += (_, _) => Close();
    }

    private void PickCopyTarget()
    {
        var picker = new OpenFolderDialog { Title = "복사 대상 폴더" };
        if (Directory.Exists(_settings.CopyTargetFolder)) picker.InitialDirectory = _settings.CopyTargetFolder;

        if (picker.ShowDialog() != true) return;

        CopyTarget.Text = picker.FolderName;
        Store(() => _settings.CopyTargetFolder = picker.FolderName);
    }

    private void Store(Action change)
    {
        change();
        _settings.Save(LauncherSettings.DefaultPath);
    }
}
