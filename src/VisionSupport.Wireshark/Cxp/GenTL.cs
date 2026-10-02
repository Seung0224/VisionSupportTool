using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VisionSupport.Wireshark.Cxp;

public sealed class GenTLException : Exception
{
    public GenTLException(int code, string call)
        : base($"{call} 실패 (GC_ERROR {code}{Explain(code)})") => Code = code;

    public int Code { get; }

    private static string Explain(int code) => code switch
    {
        -1004 => ", 다른 프로세스가 사용 중",
        -1005 => ", 접근 거부",
        -1003 => ", 프로듀서가 지원하지 않음",
        _ => string.Empty,
    };
}

/// <summary>
/// The GenTL C entry points this tool needs, bound at run time from the producer (.cti) so the
/// build needs nothing from Matrox. Calls are stdcall per the GenTL headers' GC_CALLTYPE.
/// </summary>
internal sealed class GenTL : IDisposable
{
    public const string MatroxProducer = "Matrox.CoaXPress.cti";
    private const int DeviceAccessReadOnly = 2;
    private const int DeviceInfoAccessStatus = 5;
    private const int UrlInfoUrl = 0;
    private const int ReadChunk = 64 * 1024;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HandleOut(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HandleIn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HandleHandleOut(IntPtr handle, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UpdateList(IntPtr handle, out byte changed, ulong timeoutMs);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(IntPtr handle, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetId(IntPtr handle, uint index, byte[]? buffer, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenById(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string id, out IntPtr opened);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenDeviceFn(IntPtr iface, [MarshalAs(UnmanagedType.LPStr)] string id, int flags, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DeviceInfoFn(IntPtr iface, [MarshalAs(UnmanagedType.LPStr)] string id, int command, out int type, byte[]? buffer, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UrlInfoFn(IntPtr port, uint index, int command, out int type, byte[]? buffer, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadPortFn(IntPtr port, ulong address, byte[] buffer, ref nuint size);

    private delegate int StringCall(byte[]? buffer, ref nuint size);

    private readonly IntPtr _lib;
    private readonly NoArgs _closeLib;
    private readonly HandleOut _tlOpen;
    private readonly HandleIn _tlClose, _ifClose, _devClose;
    private readonly UpdateList _tlUpdate, _ifUpdate;
    private readonly GetCount _tlCount, _ifCount, _urlCount;
    private readonly GetId _tlGetId, _ifGetId;
    private readonly OpenById _tlOpenIf;
    private readonly OpenDeviceFn _ifOpenDev;
    private readonly DeviceInfoFn _ifDevInfo;
    private readonly HandleHandleOut _devGetPort;
    private readonly UrlInfoFn _urlInfo;
    private readonly ReadPortFn _readPort;

    public GenTL(string producerPath)
    {
        _lib = NativeLibrary.Load(producerPath);
        try
        {
            _closeLib = Bind<NoArgs>("GCCloseLib");
            _tlOpen = Bind<HandleOut>("TLOpen");
            _tlClose = Bind<HandleIn>("TLClose");
            _ifClose = Bind<HandleIn>("IFClose");
            _devClose = Bind<HandleIn>("DevClose");
            _tlUpdate = Bind<UpdateList>("TLUpdateInterfaceList");
            _ifUpdate = Bind<UpdateList>("IFUpdateDeviceList");
            _tlCount = Bind<GetCount>("TLGetNumInterfaces");
            _ifCount = Bind<GetCount>("IFGetNumDevices");
            _urlCount = Bind<GetCount>("GCGetNumPortURLs");
            _tlGetId = Bind<GetId>("TLGetInterfaceID");
            _ifGetId = Bind<GetId>("IFGetDeviceID");
            _tlOpenIf = Bind<OpenById>("TLOpenInterface");
            _ifOpenDev = Bind<OpenDeviceFn>("IFOpenDevice");
            _ifDevInfo = Bind<DeviceInfoFn>("IFGetDeviceInfo");
            _devGetPort = Bind<HandleHandleOut>("DevGetPort");
            _urlInfo = Bind<UrlInfoFn>("GCGetPortURLInfo");
            _readPort = Bind<ReadPortFn>("GCReadPort");
            Check(Bind<NoArgs>("GCInitLib")(), "GCInitLib");
        }
        catch
        {
            NativeLibrary.Free(_lib);
            throw;
        }
    }

    /// <summary>The configured path, else the Matrox producer on GENICAM_GENTL64_PATH, else null.</summary>
    public static string? FindProducer(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        string? paths = Environment.GetEnvironmentVariable("GENICAM_GENTL64_PATH");
        return paths?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), MatroxProducer))
            .FirstOrDefault(File.Exists);
    }

    public IntPtr OpenSystem()
    {
        Check(_tlOpen(out IntPtr tl), "TLOpen");
        return tl;
    }

    public IReadOnlyList<string> InterfaceIds(IntPtr tl)
    {
        Check(_tlUpdate(tl, out _, 1000), "TLUpdateInterfaceList");
        Check(_tlCount(tl, out uint count), "TLGetNumInterfaces");
        return Enumerable.Range(0, (int)count)
            .Select(i => ReadString((byte[]? b, ref nuint s) => _tlGetId(tl, (uint)i, b, ref s), "TLGetInterfaceID"))
            .ToList();
    }

    public IntPtr OpenInterface(IntPtr tl, string id)
    {
        Check(_tlOpenIf(tl, id, out IntPtr iface), "TLOpenInterface");
        return iface;
    }

    public IReadOnlyList<string> DeviceIds(IntPtr iface)
    {
        Check(_ifUpdate(iface, out _, 1000), "IFUpdateDeviceList");
        Check(_ifCount(iface, out uint count), "IFGetNumDevices");
        return Enumerable.Range(0, (int)count)
            .Select(i => ReadString((byte[]? b, ref nuint s) => _ifGetId(iface, (uint)i, b, ref s), "IFGetDeviceID"))
            .ToList();
    }

    /// <summary>DEVICE_ACCESS_STATUS: 1 RW, 2 RO, 3 no access, 4 busy, 5 open RW, 6 open RO.</summary>
    public int AccessStatus(IntPtr iface, string deviceId)
    {
        var buffer = new byte[4];
        nuint size = 4;
        Check(_ifDevInfo(iface, deviceId, DeviceInfoAccessStatus, out _, buffer, ref size), "IFGetDeviceInfo");
        return BitConverter.ToInt32(buffer, 0);
    }

    public IntPtr OpenDeviceReadOnly(IntPtr iface, string deviceId)
    {
        Check(_ifOpenDev(iface, deviceId, DeviceAccessReadOnly, out IntPtr device), "IFOpenDevice");
        return device;
    }

    /// <summary>The remote (camera) port of an open device. Not closed separately.</summary>
    public IntPtr DevicePort(IntPtr device)
    {
        Check(_devGetPort(device, out IntPtr port), "DevGetPort");
        return port;
    }

    public IReadOnlyList<string> PortUrls(IntPtr port)
    {
        Check(_urlCount(port, out uint count), "GCGetNumPortURLs");
        return Enumerable.Range(0, (int)count)
            .Select(i => ReadString((byte[]? b, ref nuint s) => _urlInfo(port, (uint)i, UrlInfoUrl, out _, b, ref s), "GCGetPortURLInfo"))
            .ToList();
    }

    public byte[] Read(IntPtr port, ulong address, int length)
    {
        var result = new byte[length];
        for (int done = 0; done < length; done += ReadChunk)
        {
            int count = Math.Min(ReadChunk, length - done);
            var chunk = new byte[count];
            nuint size = (nuint)count;
            Check(_readPort(port, address + (ulong)done, chunk, ref size), "GCReadPort");
            Buffer.BlockCopy(chunk, 0, result, done, count);
        }
        return result;
    }

    public void CloseDevice(IntPtr device) => _devClose(device);

    public void CloseInterface(IntPtr iface) => _ifClose(iface);

    public void CloseSystem(IntPtr tl) => _tlClose(tl);

    public void Dispose()
    {
        _closeLib();
        NativeLibrary.Free(_lib);
    }

    private static void Check(int code, string call)
    {
        if (code != 0) throw new GenTLException(code, call);
    }

    private T Bind<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    private static string ReadString(StringCall call, string name)
    {
        nuint size = 0;
        Check(call(null, ref size), name);
        var buffer = new byte[(int)size];
        Check(call(buffer, ref size), name);
        return Encoding.ASCII.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }
}
