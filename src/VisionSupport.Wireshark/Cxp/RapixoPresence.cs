using System.Management;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>Whether the grabber is in Device Manager and healthy - works no matter who holds the board.</summary>
public static class RapixoPresence
{
    public static (bool Present, string Name) Query(string match)
    {
        string safe = new(match.Where(c => c is not ('\'' or '%' or '_' or '[')).ToArray());
        using var searcher = new ManagementObjectSearcher(
            $"SELECT Name, Status FROM Win32_PnPEntity WHERE Name LIKE '%{safe}%'");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            using (o)
            {
                string name = o["Name"] as string ?? match;
                return (string.Equals(o["Status"] as string, "OK", StringComparison.OrdinalIgnoreCase), name);
            }
        }
        return (false, match);
    }
}
