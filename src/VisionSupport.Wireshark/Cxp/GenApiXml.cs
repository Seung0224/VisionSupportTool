using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>Fetches a module's GenApi XML from the URL its port advertises (GenTL "Local:" or "File:").</summary>
public static class GenApiXml
{
    public static string Load(string url, Func<ulong, int, byte[]> read)
    {
        string location = url.Split('?')[0];
        if (location.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = location["local:".Length..].Split(';');
            if (parts.Length < 3) throw new NotSupportedException($"XML 위치를 해석할 수 없음: {url}");
            string name = parts[0].TrimStart('/');
            ulong address = ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int length = int.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte[] bytes = read(address, length);
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Unzip(bytes)
                : Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        if (location.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            string path = new Uri(location).LocalPath;
            return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Unzip(File.ReadAllBytes(path))
                : File.ReadAllText(path);
        }
        throw new NotSupportedException($"지원하지 않는 XML 위치: {url}");
    }

    private static string Unzip(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException("ZIP 안에 XML이 없음");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
