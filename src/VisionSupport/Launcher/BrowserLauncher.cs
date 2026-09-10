using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VisionSupport.Launcher;

/// <summary>
/// Opens a web page in the user's own browser, in its own window.
///
/// Two things make this more than Process.Start.
///
/// The shell runs elevated. A browser started from here would inherit that, and an elevated
/// browser does not join the ordinary one already running - it starts a separate instance with a
/// separate profile, which means none of the user's cookies. For an intranet page behind SSO that
/// is the difference between the page opening and a login screen. So the process is created with
/// a token borrowed from the desktop shell, which is not elevated.
///
/// And the page should look like the popup the site's own button produces, not a tab in whatever
/// the user was reading. Chromium browsers do that with --app; anything else falls back to a tab,
/// which is worse but still works.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>Chromium builds that understand --app. Firefox has no equivalent.</summary>
    private static readonly string[] AppWindowBrowsers =
        { "msedge.exe", "chrome.exe", "brave.exe", "vivaldi.exe", "opera.exe", "chromium.exe" };

    /// <summary>
    /// Opens <paramref name="url"/> in a browser window of its own, falling back to a tab if
    /// anything about the popup path does not hold on this machine.
    /// </summary>
    public static void OpenPopup(string url, int width, int height)
    {
        try
        {
            string? browser = FindDefaultBrowser();
            if (browser is not null && SupportsAppWindow(browser))
            {
                string command = $"\"{browser}\" {BuildPopupArguments(url, width, height)}";
                if (StartUnelevated(command)) return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Every step below is best-effort: an unreadable registry, a browser that moved, a
            // token that cannot be duplicated. None of them are worth failing the click over
            // when opening a tab still gets the user to the page.
        }

        OpenTab(url);
    }

    /// <summary>
    /// Hands the URL to the desktop shell, which opens it in the default browser at the shell's
    /// own privilege level. The plain fallback: a tab rather than a window.
    /// </summary>
    private static void OpenTab(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", url) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Nothing left to try. A menu tile that quietly does nothing beats one that takes the
            // launcher down with it.
        }
    }

    /// <summary>The command-line tail that turns a Chromium browser into a single popup window.</summary>
    internal static string BuildPopupArguments(string url, int width, int height)
        => $"--app=\"{url}\" --window-size={width},{height}";

    /// <summary>
    /// Pulls the executable out of a registry open command, which looks like
    /// <c>"C:\...\msedge.exe" --single-argument %1</c> - quoted when the path has spaces, bare
    /// when it does not.
    /// </summary>
    internal static string? ExtractExecutable(string? registryCommand)
    {
        if (string.IsNullOrWhiteSpace(registryCommand)) return null;

        string command = registryCommand.TrimStart();

        if (command.StartsWith('"'))
        {
            int closing = command.IndexOf('"', 1);
            return closing > 1 ? command[1..closing] : null;
        }

        int space = command.IndexOf(' ');
        string bare = space < 0 ? command : command[..space];
        return bare.Length == 0 ? null : bare;
    }

    /// <summary>Whether this browser understands --app.</summary>
    internal static bool SupportsAppWindow(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        string name = Path.GetFileName(executablePath);
        return Array.Exists(AppWindowBrowsers,
            candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindDefaultBrowser()
    {
        using RegistryKey? choice = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");

        if (choice?.GetValue("ProgId") is not string progId) return null;

        using RegistryKey? command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        string? executable = ExtractExecutable(command?.GetValue(null) as string);

        return executable is not null && File.Exists(executable) ? executable : null;
    }

    // ---- Starting a process without this one's elevation -------------------------------------

    private const uint ProcessQueryInformation = 0x0400;
    private const uint TokenDuplicate = 0x0002;
    private const uint MaximumAllowed = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit,
                                             uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes,
                                                int impersonationLevel, int tokenType, out IntPtr duplicate);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? applicationName,
                                                       string commandLine, uint creationFlags, IntPtr environment,
                                                       string? currentDirectory, ref StartupInfo startupInfo,
                                                       out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Runs a command line as the logged-on user rather than as this elevated process.
    ///
    /// The desktop shell (explorer) is the borrowed identity: it is the one process guaranteed to
    /// be running as the interactive user at their ordinary privilege level, so a copy of its
    /// token is exactly what a browser needs to find the profile the user is signed in with.
    /// </summary>
    /// <returns>True when the process was started; false to fall back to a tab.</returns>
    private static bool StartUnelevated(string commandLine)
    {
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) return false;

        GetWindowThreadProcessId(shell, out uint shellProcessId);
        if (shellProcessId == 0) return false;

        IntPtr process = IntPtr.Zero;
        IntPtr shellToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;

        try
        {
            process = OpenProcess(ProcessQueryInformation, false, shellProcessId);
            if (process == IntPtr.Zero) return false;

            if (!OpenProcessToken(process, TokenDuplicate, out shellToken)) return false;

            if (!DuplicateTokenEx(shellToken, MaximumAllowed, IntPtr.Zero,
                                  SecurityImpersonation, TokenPrimary, out primaryToken))
            {
                return false;
            }

            var startupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };

            if (!CreateProcessWithTokenW(primaryToken, 0, null, commandLine, 0, IntPtr.Zero, null,
                                         ref startupInfo, out ProcessInformation information))
            {
                return false;
            }

            CloseHandle(information.Process);
            CloseHandle(information.Thread);
            return true;
        }
        finally
        {
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved3;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
