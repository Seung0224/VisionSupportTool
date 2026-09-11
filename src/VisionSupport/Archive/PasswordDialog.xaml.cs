using System.Windows;

namespace VisionSupport.Archive;

/// <summary>
/// Asks for an archive password.
///
/// Topmost, because the launcher is and a prompt that opened behind it would look like the drop
/// had simply hung. The password is never stored - it lives as long as the job that needed it.
/// </summary>
public partial class PasswordDialog : Window
{
    private PasswordDialog(string prompt, string hint)
    {
        InitializeComponent();

        Prompt.Text = prompt;
        Hint.Text = hint;

        OkButton.Click += (_, _) => { DialogResult = true; };
        CancelButton.Click += (_, _) => { DialogResult = false; };
        Loaded += (_, _) => Entry.Focus();
    }

    /// <summary>The password to lock a new archive with, or null if the user backed out.</summary>
    public static string? AskToLock()
        => Ask("압축에 사용할 비밀번호", "AES-256으로 암호화됩니다. 잊으면 열 수 없습니다.");

    /// <summary>The password to open an archive with, or null if the user backed out.</summary>
    public static string? AskToOpen()
        => Ask("비밀번호가 걸린 압축 파일입니다", "이 압축 파일의 비밀번호를 입력하세요.");

    private static string? Ask(string prompt, string hint)
    {
        var dialog = new PasswordDialog(prompt, hint);
        if (dialog.ShowDialog() != true) return null;

        string password = dialog.Entry.Password;
        return string.IsNullOrEmpty(password) ? null : password;
    }
}
