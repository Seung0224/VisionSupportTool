namespace MemMon.Models;

/// <summary>How much a value should worry the person reading it.</summary>
public enum Tone
{
    Normal,
    Watch,
    Problem,
}

public sealed record GcTermRow(string Value, string Meaning, string Advice, Tone Tone);

/// <summary>
/// Plain-language glossary for the two enum columns in the GC tab. The values are the full
/// GCReason and GCType sets the CLR can actually emit, ordered by how often they turn up
/// rather than by their numeric value.
/// </summary>
public static class GcReference
{
    public static IReadOnlyList<GcTermRow> Reasons { get; } = new[]
    {
        new GcTermRow("AllocSmall",
            "작은 객체를 할당하다 0세대 할당 예산이 다 찼습니다.",
            "가장 흔하고 정상적인 계기입니다. 이것만 보인다면 건강한 상태입니다.",
            Tone.Normal),
        new GcTermRow("AllocLarge",
            "85KB 이상인 큰 객체 할당이 계기가 됐습니다. 큰 객체는 LOH에 들어갑니다.",
            "자주 보이면 큰 배열이나 이미지 버퍼를 반복 생성하는 중입니다. LOH는 기본적으로 압축되지 않아 조각화가 쌓입니다.",
            Tone.Watch),
        new GcTermRow("Induced",
            "코드가 GC.Collect()를 직접 호출했습니다.",
            "사람이 만든 멈춤입니다. 호출 지점을 찾아 없애는 것이 거의 항상 맞습니다.",
            Tone.Problem),
        new GcTermRow("InducedCompacting",
            "압축까지 강제한 GC.Collect() 호출입니다.",
            "Induced보다 더 오래 걸립니다. 힙 전체를 옮기므로 멈춤이 깁니다.",
            Tone.Problem),
        new GcTermRow("InducedNotForced",
            "GC.Collect() 호출이지만 블로킹을 강제하지 않아 백그라운드로 처리될 수 있습니다.",
            "그래도 코드가 GC를 부르고 있다는 뜻이니 호출 지점을 확인하세요.",
            Tone.Watch),
        new GcTermRow("InducedLowMemory",
            "메모리 부족 때문에 유도된 수집입니다.",
            "머신 또는 프로세스가 메모리 한계에 가까워지고 있습니다.",
            Tone.Problem),
        new GcTermRow("LowMemory",
            "운영체제가 메모리 부족을 알려서 시작됐습니다.",
            "이 프로그램만의 문제가 아닐 수 있습니다. 머신 전체 메모리를 함께 보세요.",
            Tone.Problem),
        new GcTermRow("OutOfSpaceSOH",
            "소형 객체 힙(Gen0~Gen2) 세그먼트에 공간이 부족했습니다.",
            "조각화나 힙 증가를 의심하세요.",
            Tone.Watch),
        new GcTermRow("OutOfSpaceLOH",
            "대형 객체 힙(LOH) 세그먼트에 공간이 부족했습니다.",
            "LOH 조각화의 전형적인 신호입니다. 큰 배열을 재사용하는 쪽으로 바꾸면 줄어듭니다.",
            Tone.Watch),
        new GcTermRow("Empty",
            "이유가 지정되지 않았습니다.",
            "드물게 나타납니다. 단독으로는 판단 근거가 되지 않습니다.",
            Tone.Normal),
        new GcTermRow("Internal",
            "런타임 내부 사정으로 시작됐습니다.",
            "드뭅니다. 애플리케이션 코드가 원인이 아닌 경우가 대부분입니다.",
            Tone.Normal),
        new GcTermRow("PMFullGC",
            "런타임의 provisional 모드에서 일어난 전체 GC입니다.",
            "일반 데스크톱 프로그램에서는 거의 보이지 않습니다.",
            Tone.Normal),
        new GcTermRow("LowMemoryHost",
            "CLR을 품고 있는 호스트(예: SQL Server)가 메모리 부족을 통지했습니다.",
            "호스팅 환경에서만 나타납니다. 일반 WPF/WinForms 프로그램과는 무관합니다.",
            Tone.Normal),
        new GcTermRow("LowMemoryHostBlocking",
            "위와 같으나 블로킹 방식으로 수행됐습니다.",
            "호스팅 환경 전용입니다.",
            Tone.Normal),
    };

    public static IReadOnlyList<GcTermRow> Kinds { get; } = new[]
    {
        new GcTermRow("NonConcurrentGC",
            "프로그램을 완전히 세워두고 한 번에 처리합니다. 수집이 끝날 때까지 모든 스레드가 멈춥니다.",
            "0세대와 1세대는 원래 항상 이 방식이라 정상입니다. 문제는 2세대가 이걸로 나올 때입니다.",
            Tone.Normal),
        new GcTermRow("BackgroundGC",
            "프로그램을 계속 돌리면서 뒤에서 2세대를 수집합니다. 멈춤이 짧게 나뉩니다.",
            "2세대는 이 방식으로 나오는 것이 정상입니다.",
            Tone.Normal),
        new GcTermRow("ForegroundGC",
            "백그라운드 2세대 수집이 도는 도중에 끼어든 0/1세대 수집입니다.",
            "짧습니다. 백그라운드 GC가 동작 중이라는 뜻이기도 합니다.",
            Tone.Normal),
    };

    public const string HowToRead =
        "긴 멈춤을 찾을 때 봐야 할 조합은 '세대 2 + NonConcurrentGC'입니다. " +
        "힙 전체를 세워두고 훑는 방식이라 힙이 클수록 멈춤이 그대로 길어집니다.\n\n" +
        "2세대가 BackgroundGC가 아니라 NonConcurrentGC로 나온다면 대상 프로그램의 " +
        "app.config에 <gcConcurrent enabled=\"false\"/> 가 있는지 확인하세요. " +
        "이 설정이 있으면 모든 2세대 수집이 stop-the-world가 되어, 힙이 수백 MB로 자란 " +
        "프로그램에서는 이것만으로 초 단위 멈춤이 나올 수 있습니다.";
}
