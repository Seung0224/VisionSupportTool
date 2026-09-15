using System.Runtime.InteropServices;

namespace VisionSupport.Sam;

/// <summary>One DXGI adapter. <see cref="Index"/> is the DirectML device id.</summary>
internal sealed record GpuAdapter(int Index, string Name, long DedicatedMemory, bool IsSoftware);

/// <summary>
/// Which GPU DirectML runs on.
///
/// DirectML's device id is the adapter's position in DXGI's list, and that order is not fixed: on a
/// hybrid laptop it depends on which GPU drives the display and on the Windows graphics preference.
/// Picking by dedicated memory lands on the discrete GPU whatever the order happens to be today.
/// </summary>
internal static unsafe class GpuAdapters
{
    private const uint SoftwareFlag = 2;

    /// <summary>The hardware adapter with the most dedicated memory, or null if there is none.
    /// The software renderer is never chosen: DirectML on it is slower than the CPU.</summary>
    internal static GpuAdapter? Pick(IEnumerable<GpuAdapter> adapters)
        => adapters.Where(a => !a.IsSoftware).OrderByDescending(a => a.DedicatedMemory).FirstOrDefault();

    /// <summary>
    /// Every adapter, in DXGI order. The COM calls go through the vtable directly: two methods do
    /// not justify an interop package. Slot numbers are IDXGIFactory1::EnumAdapters1 (12) and
    /// IDXGIAdapter1::GetDesc1 (10).
    /// </summary>
    internal static List<GpuAdapter> Enumerate()
    {
        var adapters = new List<GpuAdapter>();
        Guid factoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");

        int hr = CreateDXGIFactory1(ref factoryId, out IntPtr factory);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);

        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)factory)[12];

            for (uint index = 0; ; index++)
            {
                IntPtr adapter;
                if (enumAdapters1(factory, index, &adapter) != 0) break;

                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, AdapterDesc1*, int>)(*(IntPtr**)adapter)[10];
                    AdapterDesc1 desc;
                    if (getDesc1(adapter, &desc) != 0) continue;

                    adapters.Add(new GpuAdapter((int)index, new string(desc.Description),
                                                (long)desc.DedicatedVideoMemory,
                                                (desc.Flags & SoftwareFlag) != 0));
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        return adapters;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    /// <summary>DXGI_ADAPTER_DESC1.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }
}
