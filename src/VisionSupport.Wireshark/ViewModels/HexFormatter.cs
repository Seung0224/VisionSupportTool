using System.Text;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>Wireshark's bytes pane: offset, sixteen bytes in two groups of eight, then ASCII.</summary>
public static class HexFormatter
{
    public static string Format(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 4 + 16);
        for (int row = 0; row < data.Length; row += 16)
        {
            int n = Math.Min(16, data.Length - row);
            sb.Append(row.ToString("X4")).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                sb.Append(i < n ? data[row + i].ToString("X2") + " " : "   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int i = 0; i < n; i++)
            {
                byte b = data[row + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
