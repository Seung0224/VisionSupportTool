using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// Opens the first interface (and, only if a binding asks for it, the first camera read-only),
/// reads, and closes everything on Dispose - so VISION opening the board later is never blocked
/// by a handle this tool forgot to let go of.
/// </summary>
internal sealed class GenTLSession : IGenTLSession
{
    private readonly GenTL _gentl;
    private readonly Dictionary<string, GenApiNodeMap> _maps;
    private IntPtr _tl, _iface, _device;

    public GenTLSession(GenTL gentl, Dictionary<string, GenApiNodeMap> nodeMapCache, bool needDevice)
    {
        _gentl = gentl;
        _maps = nodeMapCache;
        try
        {
            _tl = gentl.OpenSystem();
            string ifaceId = gentl.InterfaceIds(_tl).FirstOrDefault()
                ?? throw new InvalidOperationException("GenTL 인터페이스가 없음");
            _iface = gentl.OpenInterface(_tl, ifaceId);
            if (needDevice)
            {
                string deviceId = gentl.DeviceIds(_iface).FirstOrDefault()
                    ?? throw new InvalidOperationException("카메라가 보이지 않음");
                _device = gentl.OpenDeviceReadOnly(_iface, deviceId);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings)
    {
        var readings = new List<NodeReading>(bindings.Count);
        foreach (CxpNodeBinding binding in bindings)
        {
            bool device = binding.Module == "Device";
            IntPtr port = device ? _gentl.DevicePort(_device) : _iface;
            if (!_maps.TryGetValue(binding.Module, out GenApiNodeMap? map))
            {
                string url = _gentl.PortUrls(port).FirstOrDefault()
                    ?? throw new InvalidOperationException($"{binding.Module} XML 위치 없음");
                map = GenApiNodeMap.Parse(GenApiXml.Load(url, (a, l) => _gentl.Read(port, a, l)));
                _maps[binding.Module] = map;
            }
            (long value, string text) = map.Read(binding.Node, (a, l) => _gentl.Read(port, a, l));
            readings.Add(new NodeReading(binding, value, text));
        }
        return readings;
    }

    public void Dispose()
    {
        if (_device != IntPtr.Zero) _gentl.CloseDevice(_device);
        if (_iface != IntPtr.Zero) _gentl.CloseInterface(_iface);
        if (_tl != IntPtr.Zero) _gentl.CloseSystem(_tl);
        _device = _iface = _tl = IntPtr.Zero;
    }
}
