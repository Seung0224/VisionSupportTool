using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// Loads and saves <see cref="ImageConverterSettings"/> as JSON. A missing or unreadable file is
/// not an error - it just means "first run", so <see cref="Load"/> hands back defaults. The path
/// is injected so tests can point it at a temp file; <see cref="Default"/> is the real location
/// under %AppData%, matching the PLC server's own store.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    /// <summary>설정을 %AppData%에 두던 시절의 경로. 새 경로에 파일이 없을 때만 대신 읽는다.
    /// 테스트가 임시 경로를 주입할 때는 null이라 예전 경로를 건드리지 않는다.</summary>
    private readonly string? _legacyFilePath;

    public SettingsStore(string filePath) : this(filePath, null)
    {
    }

    private SettingsStore(string filePath, string? legacyFilePath)
    {
        _filePath = filePath;
        _legacyFilePath = legacyFilePath;
    }

    public static SettingsStore Default => new(
        Path.Combine(@"D:\Datas", "VisionSupport", "ImageConverter", "settings.json"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VisionSupport", "ImageConverter", "settings.json"));

    public ImageConverterSettings Load()
    {
        try
        {
            string path = _filePath;
            if (!File.Exists(path) && _legacyFilePath is not null && File.Exists(_legacyFilePath))
            {
                // 예전 경로에 남아있는 설정을 이어받는다. 저장은 항상 새 경로로 간다.
                path = _legacyFilePath;
            }

            if (!File.Exists(path)) return new ImageConverterSettings();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ImageConverterSettings>(json, JsonOptions)
                ?? new ImageConverterSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ImageConverterSettings();
        }
    }

    public void Save(ImageConverterSettings settings)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
