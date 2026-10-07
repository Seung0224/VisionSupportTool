using Microsoft.Diagnostics.Runtime;

namespace VisionSupport.Tests;

/// <summary>
/// Counts, right now, how many live objects of a given type name have a GC root path - i.e. are
/// genuinely reachable, not just sitting in memory awaiting a sweep.
///
/// The automation-disconnect tests run alongside plenty of other tests that legitimately hold
/// their own window of the same type (constructed, never shown, going out of scope on its own
/// schedule) - xunit runs different collections in parallel, so one of those can be mid-flight
/// while this one's snapshot runs. A bare "found == 0" assertion would then fail for a reason that
/// has nothing to do with the window this test itself closed. Comparing a before/after count
/// isolates the count this test's own action is responsible for.
/// </summary>
internal static class GcRootProbe
{
    public static int CountRootPaths(Func<ClrType, bool> matchesType)
    {
        int pid = Environment.ProcessId;
        using DataTarget dt = DataTarget.CreateSnapshotAndAttach(pid);
        ClrInfo clrInfo = dt.ClrVersions[0];
        using ClrRuntime runtime = clrInfo.CreateRuntime();
        ClrHeap heap = runtime.Heap;

        var gcroot = new GCRoot(heap, obj => obj.Type is not null && matchesType(obj.Type));
        return gcroot.EnumerateRootPaths().Count();
    }
}
