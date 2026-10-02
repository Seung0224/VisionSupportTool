using System.Globalization;
using System.Xml.Linq;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// Just enough GenApi to read a status register: a feature that points (pValue) at a register
/// with a fixed address. Anything computed - pAddress, pIndex, SwissKnife - is refused instead of
/// guessed, because a wrong address would read some other register and show it as link state.
/// </summary>
public sealed class GenApiNodeMap
{
    private readonly Dictionary<string, XElement> _nodes;

    private GenApiNodeMap(Dictionary<string, XElement> nodes) => _nodes = nodes;

    public static GenApiNodeMap Parse(string xml)
    {
        XDocument doc = XDocument.Parse(xml);
        var nodes = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (XElement e in doc.Descendants())
        {
            if (e.Name.LocalName == "EnumEntry" || e.Attribute("Name")?.Value is not { } name) continue;
            nodes.TryAdd(name, e);
        }
        return new GenApiNodeMap(nodes);
    }

    public IEnumerable<string> NodeNames => _nodes.Keys;

    public (long Value, string Text) Read(string name, Func<ulong, int, byte[]> read)
    {
        XElement node = Get(name);
        switch (node.Name.LocalName)
        {
            case "Boolean":
            {
                long on = Child(node, "OnValue") is { } o ? ParseLong(o) : 1;
                bool value = ValueOf(node, read) == on;
                return (value ? 1 : 0, value ? "True" : "False");
            }
            case "Enumeration":
            {
                long value = ValueOf(node, read);
                string? entry = node.Elements().Where(e => e.Name.LocalName == "EnumEntry")
                    .FirstOrDefault(e => Child(e, "Value") is { } v && ParseLong(v) == value)
                    ?.Attribute("Name")?.Value;
                return (value, entry ?? value.ToString(CultureInfo.InvariantCulture));
            }
            case "Integer":
            case "IntReg":
            case "MaskedIntReg":
            {
                long value = ValueOf(node, read);
                return (value, value.ToString(CultureInfo.InvariantCulture));
            }
            default:
                throw new NotSupportedException($"{name}: {node.Name.LocalName} 노드는 읽지 않음");
        }
    }

    private long ValueOf(XElement node, Func<ulong, int, byte[]> read)
    {
        if (node.Name.LocalName is "IntReg" or "MaskedIntReg") return ReadRegister(node, read);
        if (Child(node, "Value") is { } constant) return ParseLong(constant);
        if (Child(node, "pValue") is { } pointer) return ValueOf(Get(pointer), read);
        throw new NotSupportedException($"{node.Attribute("Name")?.Value}: 값 위치를 알 수 없음");
    }

    private static long ReadRegister(XElement reg, Func<ulong, int, byte[]> read)
    {
        string name = reg.Attribute("Name")?.Value ?? "?";
        if (reg.Elements().Any(e => e.Name.LocalName is "pAddress" or "pIndex" or "IntSwissKnife"))
        {
            throw new NotSupportedException($"{name}: 계산되는 주소는 지원하지 않음");
        }

        ulong address = reg.Elements().Where(e => e.Name.LocalName == "Address")
            .Aggregate(0UL, (sum, e) => sum + (ulong)ParseLong(e.Value));
        int length = Child(reg, "Length") is { } l ? (int)ParseLong(l) : 4;
        bool little = Child(reg, "Endianess") == "LittleEndian";

        byte[] bytes = read(address, length);
        ulong raw = 0;
        for (int i = 0; i < length; i++)
        {
            raw = little ? raw | (ulong)bytes[i] << (8 * i) : raw << 8 | bytes[i];
        }

        if (reg.Name.LocalName == "MaskedIntReg")
        {
            int lsb, msb;
            if (Child(reg, "Bit") is { } bit) lsb = msb = (int)ParseLong(bit);
            else
            {
                lsb = (int)ParseLong(Child(reg, "LSB") ?? "0");
                msb = (int)ParseLong(Child(reg, "MSB") ?? (length * 8 - 1).ToString(CultureInfo.InvariantCulture));
            }
            // GenApi numbers bits from the most significant end in big-endian registers.
            int width = length * 8;
            int lo = little ? lsb : width - 1 - lsb;
            int hi = little ? msb : width - 1 - msb;
            if (lo > hi) (lo, hi) = (hi, lo);
            int bits = hi - lo + 1;
            raw = (raw >> lo) & (bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1);
        }
        return (long)raw;
    }

    private XElement Get(string name) => _nodes.TryGetValue(name, out XElement? e)
        ? e
        : throw new NotSupportedException($"{name}: 노드맵에 없음");

    private static string? Child(XElement e, string localName)
        => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName)?.Value.Trim();

    private static long ParseLong(string text)
    {
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(text, CultureInfo.InvariantCulture);
    }
}
