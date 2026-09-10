using System.Net;
using Newtonsoft.Json;
using VirtualPlcServer.Persistence;
using VirtualPlcServer.Protocols.Mc;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Guards the net48 → net9 behaviour change that broke saving the module list.
///
/// On .NET Framework, IPAddress.Any was a plain IPAddress. On .NET Core and later it is
/// System.Net.IPAddress+ReadOnlyIPAddress, an internal subclass. A converter that matched the
/// exact type therefore stopped matching, Newtonsoft fell back to reflecting over the object,
/// and IPAddress.ScopeId threw SocketException(10045) for every IPv4 address - so every attempt
/// to persist a module that had a listen address failed.
/// </summary>
public class IPAddressJsonConverterTests
{
    private static JsonSerializerSettings Settings => new()
    {
        Converters = { new IPAddressJsonConverter() },
    };

    [Fact]
    public void CanConvert_accepts_the_readonly_subclass_returned_by_IPAddress_Any()
    {
        // IPAddress.Any is not typeof(IPAddress) on .NET 9; the converter still has to claim it.
        Assert.True(new IPAddressJsonConverter().CanConvert(IPAddress.Any.GetType()));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.0.10")]
    public void Round_trips_an_address_as_a_plain_string(string address)
    {
        IPAddress original = address switch
        {
            // The well-known statics are the ones that are a different runtime type.
            "0.0.0.0" => IPAddress.Any,
            "127.0.0.1" => IPAddress.Loopback,
            _ => IPAddress.Parse(address),
        };

        string json = JsonConvert.SerializeObject(original, Settings);

        Assert.Equal($"\"{address}\"", json);
        Assert.Equal(original, JsonConvert.DeserializeObject<IPAddress>(json, Settings));
    }

    [Fact]
    public void Serialises_an_MC_config_whose_listen_address_defaults_to_Any()
    {
        // The exact shape that broke ADD MODULE: a config built with an empty IP box, which
        // AddModuleDialogViewModel turns into IPAddress.Any.
        var config = new McServerConfig
        {
            ListenAddress = IPAddress.Any,
            ReadPort = 9003,
            WritePort = 9004,
            StartAddress = 0,
            Size = 100,
        };

        string json = JsonConvert.SerializeObject(config, Settings);
        McServerConfig? restored = JsonConvert.DeserializeObject<McServerConfig>(json, Settings);

        Assert.NotNull(restored);
        Assert.Equal(IPAddress.Any, restored!.ListenAddress);
        Assert.Equal(9003, restored.ReadPort);
        Assert.Equal(9004, restored.WritePort);
    }
}
