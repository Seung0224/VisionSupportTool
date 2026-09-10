namespace MemMon.Services;

/// <summary>
/// Explains why the native table is empty.
///
/// The four causes - tracking never started, no snapshots yet, snapshots taken while
/// tracking was off, and a genuinely quiet period - all look identical on screen. Saying
/// nothing makes the tool look broken and sends people hunting the wrong thing.
/// </summary>
public static class NativeTrackingMessage
{
    public static string Explain(
        string? startError, bool hasSnapshots, bool snapshotsCarryNativeData, int rowCount)
    {
        if (!string.IsNullOrEmpty(startError))
            return $"네이티브 추적이 실행 중이 아닙니다 — {startError}";

        if (rowCount > 0) return "";

        if (!hasSnapshots)
            return "비교 탭에서 스냅샷 A와 B를 고르세요. 두 스냅샷 사이의 네이티브 할당이 여기에 나옵니다.";

        if (!snapshotsCarryNativeData)
        {
            return "고른 스냅샷에 네이티브 정보가 없습니다. 스냅샷을 찍을 때 네이티브 추적이 꺼져 있었다는 뜻입니다. " +
                   "감시를 다시 시작한 뒤 스냅샷을 새로 찍으세요.";
        }

        return "두 스냅샷 사이에 네이티브 할당이 없었습니다. 관리 힙 밖에서는 메모리가 늘지 않았다는 뜻입니다.";
    }
}
