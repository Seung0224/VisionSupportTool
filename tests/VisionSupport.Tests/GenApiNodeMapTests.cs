using System.IO;
using System.IO.Compression;
using System.Text;
using VisionSupport.Wireshark.Cxp;
using Xunit;

namespace VisionSupport.Tests;

public class GenApiNodeMapTests
{
    private const string Xml = """
        <RegisterDescription xmlns="http://www.genicam.org/GenApi/Version_1_1">
          <Integer Name="ErrorCount"><pValue>ErrorCountReg</pValue></Integer>
          <IntReg Name="ErrorCountReg"><Address>0x100</Address><Length>4</Length><AccessMode>RO</AccessMode><pPort>Device</pPort><Sign>Unsigned</Sign><Endianess>BigEndian</Endianess></IntReg>
          <Boolean Name="LinkUp"><pValue>LinkReg</pValue><OnValue>1</OnValue></Boolean>
          <MaskedIntReg Name="LinkReg"><Address>0x200</Address><Length>4</Length><AccessMode>RO</AccessMode><pPort>Device</pPort><Bit>3</Bit><Endianess>LittleEndian</Endianess></MaskedIntReg>
          <Enumeration Name="Speed">
            <EnumEntry Name="CXP6"><Value>0x48</Value></EnumEntry>
            <EnumEntry Name="CXP12"><Value>0x58</Value></EnumEntry>
            <pValue>SpeedReg</pValue>
          </Enumeration>
          <IntReg Name="SpeedReg"><Address>0x300</Address><Address>0x4</Address><Length>4</Length><AccessMode>RO</AccessMode><pPort>Device</pPort><Endianess>BigEndian</Endianess></IntReg>
          <MaskedIntReg Name="TopByte"><Address>0x100</Address><Length>4</Length><pPort>Device</pPort><LSB>7</LSB><MSB>0</MSB><Endianess>BigEndian</Endianess></MaskedIntReg>
          <Integer Name="Fixed"><Value>7</Value></Integer>
          <IntReg Name="Indexed"><pAddress>Somewhere</pAddress><Length>4</Length></IntReg>
          <Integer Name="ViaIndexed"><pValue>Indexed</pValue></Integer>
        </RegisterDescription>
        """;

    private static readonly Dictionary<ulong, byte[]> Memory = new()
    {
        [0x100] = new byte[] { 0x01, 0x00, 0x01, 0x02 },
        [0x200] = new byte[] { 0x08, 0x00, 0x00, 0x00 },
        [0x304] = new byte[] { 0x00, 0x00, 0x00, 0x58 },
    };

    private static byte[] Read(ulong address, int length) => Memory[address].AsSpan(0, length).ToArray();

    private readonly GenApiNodeMap _map = GenApiNodeMap.Parse(Xml);

    [Fact]
    public void An_integer_reads_its_big_endian_register()
        => Assert.Equal(0x01000102, _map.Read("ErrorCount", Read).Value);

    [Fact]
    public void A_boolean_reads_one_bit_of_a_little_endian_register()
        => Assert.Equal((1L, "True"), _map.Read("LinkUp", Read));

    [Fact]
    public void An_enumeration_names_its_value_and_addresses_add_up()
        => Assert.Equal((0x58L, "CXP12"), _map.Read("Speed", Read));

    /// <summary>In a big-endian register GenApi numbers bit 0 as the most significant one.</summary>
    [Fact]
    public void Big_endian_bit_numbering_counts_from_the_top()
        => Assert.Equal(0x01, _map.Read("TopByte", Read).Value);

    [Fact]
    public void A_constant_integer_needs_no_read()
        => Assert.Equal(7, _map.Read("Fixed", (_, _) => throw new InvalidOperationException()).Value);

    [Fact]
    public void Computed_addresses_are_refused_rather_than_guessed()
        => Assert.Throws<NotSupportedException>(() => _map.Read("ViaIndexed", Read));

    [Fact]
    public void Node_names_skip_enum_entries()
    {
        Assert.Contains("Speed", _map.NodeNames);
        Assert.DoesNotContain("CXP12", _map.NodeNames);
    }

    [Fact]
    public void A_local_zipped_url_is_read_from_the_port_and_unzipped()
    {
        var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("map.xml").Open(), Encoding.UTF8))
        {
            writer.Write(Xml);
        }
        byte[] bytes = zipped.ToArray();

        string xml = GenApiXml.Load($"Local:map.zip;1000;{bytes.Length:X}?SchemaVersion=1.1.0",
            (address, length) => address == 0x1000 ? bytes.AsSpan(0, length).ToArray() : throw new InvalidOperationException());

        Assert.Contains("ErrorCountReg", xml);
    }
}
