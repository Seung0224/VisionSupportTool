using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisionSupport.Wireshark.Settings;

public sealed class WiresharkSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    /// <summary>Where settings would have lived under %AppData%. Read only when the new path has no file.</summary>
    private readonly string? _legacyFilePath;

    public WiresharkSettingsStore(string filePath) : this(filePath, null)
    {
    }

    private WiresharkSettingsStore(string filePath, string? legacyFilePath)
    {
        _filePath = filePath;
        _legacyFilePath = legacyFilePath;
    }

    public static WiresharkSettingsStore Default => new(
        Path.Combine(@"D:\Datas", "VisionSupport", "Wireshark", "settings.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VisionSupport", "Wireshark", "settings.json"));

    /// <summary>Where settings live - and where diagnostics dumps go, next to them.</summary>
    public string DataDirectory => Path.GetDirectoryName(_filePath) ?? ".";

    public WiresharkSettings Load()
    {
        try
        {
            string path = _filePath;
            if (!File.Exists(path) && _legacyFilePath is not null && File.Exists(_legacyFilePath))
            {
                path = _legacyFilePath;
            }

            if (!File.Exists(path)) return new WiresharkSettings();
            return JsonSerializer.Deserialize<WiresharkSettings>(File.ReadAllText(path), JsonOptions)
                ?? new WiresharkSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new WiresharkSettings();
        }
    }

    public void Save(WiresharkSettings settings)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
