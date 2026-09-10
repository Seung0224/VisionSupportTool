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

    public SettingsStore(string filePath) => _filePath = filePath;

    public static SettingsStore Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisionSupport", "ImageConverter", "settings.json"));

    public ImageConverterSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new ImageConverterSettings();
            string json = File.ReadAllText(_filePath);
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
