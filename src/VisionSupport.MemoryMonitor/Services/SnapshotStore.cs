using System.IO;
using System.Text.Json;
using MemMon.Models;

namespace MemMon.Services;

/// <summary>Saves and loads snapshots as JSON so a run can be compared against an earlier one.</summary>
public static class SnapshotStore
{
    public static void Save(HeapSnapshot snapshot, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(snapshot));

    public static HeapSnapshot Load(string path)
        => JsonSerializer.Deserialize<HeapSnapshot>(File.ReadAllText(path))
           ?? throw new InvalidDataException($"스냅샷 파일을 읽지 못했습니다: {path}");
}
