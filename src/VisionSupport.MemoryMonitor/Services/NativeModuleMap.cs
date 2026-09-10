using System.Diagnostics;
using System.IO;

namespace MemMon.Services;

/// <summary>
/// Maps a return address in the target process to the module it belongs to, and decides which
/// frame on a stack is the one that actually wanted the memory.
///
/// Every native allocation passes through the same plumbing - ntdll, kernelbase - so the
/// innermost frame is always the same and tells you nothing. The first frame above that
/// plumbing is the caller worth naming.
/// </summary>
public sealed class NativeModuleMap
{
    /// <summary>Allocator plumbing: present on every allocation, so never the answer.</summary>
    private static readonly HashSet<string> Plumbing = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntdll.dll", "kernelbase.dll", "kernel32.dll", "msvcrt.dll", "ucrtbase.dll",
    };

    private readonly (ulong Start, ulong End, string Name)[] _modules;

    public NativeModuleMap(IEnumerable<(ulong Start, ulong End, string Name)> modules)
    {
        _modules = modules.Where(m => m.End > m.Start).OrderBy(m => m.Start).ToArray();
    }

    public static NativeModuleMap ForProcess(int pid)
    {
        var modules = new List<(ulong, ulong, string)>();
        try
        {
            using Process p = Process.GetProcessById(pid);
            foreach (ProcessModule module in p.Modules)
            {
                ulong start = (ulong)module.BaseAddress.ToInt64();
                modules.Add((start, start + (ulong)module.ModuleMemorySize,
                    Path.GetFileName(module.FileName ?? "?")));
            }
        }
        catch
        {
            // No module access (target elevated and we are not, or it exited). An empty map
            // still works; allocations simply come back unattributed.
        }
        return new NativeModuleMap(modules);
    }

    public string? Find(ulong address)
    {
        int low = 0, high = _modules.Length - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (address < _modules[mid].Start) high = mid - 1;
            else if (address >= _modules[mid].End) low = mid + 1;
            else return _modules[mid].Name;
        }
        return null;
    }

    /// <summary>The first frame above the allocator plumbing, innermost first.</summary>
    public string? OwnerOf(IReadOnlyList<ulong> stackFrames)
    {
        for (int i = 0; i < stackFrames.Count; i++)
        {
            string? name = Find(stackFrames[i]);
            if (name is null || Plumbing.Contains(name)) continue;
            return name;
        }
        return null;
    }
}
