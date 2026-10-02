using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>
/// The display filter: words separated by spaces, all of which must match. "!word" negates,
/// "id:target" keeps one target's packets, "이상" keeps flagged packets; anything else is a
/// case-insensitive substring of the source, destination, protocol or info column.
/// </summary>
public sealed class PacketFilter
{
    private readonly (string Text, bool Negate)[] _terms;

    public PacketFilter(string text)
        => _terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Length > 1 && t[0] == '!' ? (t[1..], true) : (t, false))
            .ToArray();

    public bool Matches(Packet p) => _terms.All(t => Term(p, t.Text) != t.Negate);

    private static bool Term(Packet p, string term)
    {
        if (term.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(p.TargetId, term[3..], StringComparison.Ordinal);
        }
        if (term == "이상" || term.Equals("anomaly", StringComparison.OrdinalIgnoreCase)) return p.IsAnomalous;
        return Has(p.Source, term) || Has(p.Destination, term) || Has(p.Protocol, term) || Has(p.Info, term);
    }

    private static bool Has(string column, string term) => column.Contains(term, StringComparison.OrdinalIgnoreCase);
}
