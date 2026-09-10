using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MemMon.Models;

namespace MemMon.Services;

/// <summary>
/// Finds processes worth attaching to.
///
/// The obvious implementation - walking <c>Process.Modules</c> - costs about 17 seconds on a
/// machine with 400 processes, because it materialises a ProcessModule with version info for
/// every module of every process. Enumerating module handles and comparing base names does the
/// same job in under three seconds, with identical results (measured: 16,952 ms vs 2,833 ms,
/// both finding the same 67 processes).
///
/// When the module list is unavailable - an elevated target denies memory-read rights to a
/// normal process - the executable's PE header still says whether the image is managed, so the
/// process is listed with a note about elevation rather than silently hidden.
/// </summary>
public static class ProcessScanner
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ListModulesAll = 0x03;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder buffer, ref int size);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcessModulesEx(
        IntPtr process, [Out] IntPtr[] modules, uint sizeInBytes, out uint neededBytes, uint filter);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleBaseName(IntPtr process, IntPtr module, StringBuilder name, uint size);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameEx(IntPtr process, IntPtr module, StringBuilder name, uint size);

    /// <summary>Blocking and slow (seconds). Callers should run it off the UI thread.</summary>
    public static List<TargetProcessInfo> Scan()
    {
        int self = Environment.ProcessId;
        var found = new List<TargetProcessInfo>();

        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.Id <= 4) continue;
                TargetProcessInfo? info = Describe(p);
                if (info is not null) found.Add(info);
            }
            catch
            {
                // Exited mid-scan. Nothing to offer.
            }
            finally
            {
                p.Dispose();
            }
        }

        found.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
        return found;
    }

    private static TargetProcessInfo? Describe(Process p)
    {
        IntPtr limited = OpenProcess(ProcessQueryLimitedInformation, false, p.Id);
        try
        {
            bool is32Bit = IsWow64(limited);

            if (TryFindClr(p.Id, out string clrLabel))
                return new TargetProcessInfo(p.Id, p.ProcessName, clrLabel, is32Bit, NeedsElevation: false);

            // No module access. Fall back to the image on disk.
            string? imagePath = TryGetImagePath(limited);
            if (!ManagedImage.IsManaged(imagePath)) return null;

            return new TargetProcessInfo(p.Id, p.ProcessName, ".NET (버전 확인 불가)", is32Bit, NeedsElevation: true);
        }
        finally
        {
            if (limited != IntPtr.Zero) CloseHandle(limited);
        }
    }

    /// <summary>
    /// Looks for a CLR among the process's loaded modules, comparing base names only. Version
    /// info is read for the one module that matches, never for the hundreds that do not.
    /// </summary>
    private static bool TryFindClr(int pid, out string label)
    {
        label = "";
        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero) return false;

        try
        {
            var modules = new IntPtr[1024];
            if (!EnumProcessModulesEx(handle, modules, (uint)(modules.Length * IntPtr.Size),
                    out uint neededBytes, ListModulesAll))
            {
                return false;
            }

            int count = Math.Min(modules.Length, (int)(neededBytes / IntPtr.Size));
            var name = new StringBuilder(64);

            for (int i = 0; i < count; i++)
            {
                name.Clear();
                if (GetModuleBaseName(handle, modules[i], name, (uint)name.Capacity) == 0) continue;

                string baseName = name.ToString();
                string? family = baseName.ToLowerInvariant() switch
                {
                    "coreclr.dll" => ".NET",
                    "clr.dll" or "mscorwks.dll" => ".NET Framework",
                    _ => null,
                };
                if (family is null) continue;

                label = $"{family} {ReadVersion(handle, modules[i])}";
                return true;
            }
        }
        finally
        {
            CloseHandle(handle);
        }
        return false;
    }

    private static string ReadVersion(IntPtr process, IntPtr module)
    {
        var path = new StringBuilder(1024);
        if (GetModuleFileNameEx(process, module, path, (uint)path.Capacity) == 0) return "";

        try
        {
            // FileVersion carries a build tag ("4.8.9310.0 built by: NET481REL1LAST_C");
            // only the numeric parts are useful in a picker.
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path.ToString());
            return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsWow64(IntPtr handle)
    {
        if (!Environment.Is64BitOperatingSystem) return true;
        if (handle == IntPtr.Zero) return false;
        return IsWow64Process(handle, out bool wow64) && wow64;
    }

    private static string? TryGetImagePath(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var buffer = new StringBuilder(1024);
        int size = buffer.Capacity;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
    }
}
