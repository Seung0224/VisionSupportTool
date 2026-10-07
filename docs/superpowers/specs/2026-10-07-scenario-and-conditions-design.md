# 시나리오 규칙: AND 조건 + 번지 대 번지 비교

## 배경

PLC 서버(MC, UDP)를 PLC 측으로 두고 Hikrobot 스마트카메라와 아래 핸드셰이크를 재현하고 싶다.
카메라가 쓰는 번지는 D30026 STATUS, D30028 COMMAND ACK, D30029 VISION PROCESSING이고,
PLC(이 도구)가 쓰는 번지는 D30036 COMMAND, D30038 STATUS ACK 두 개뿐이다.

1. D30036에 1011을 쓴다.
2. D30028이 D30036과 같은 값이고 D30029가 1이면 D30036을 0으로 Clear한다.
3. D30026이 0이 아니면 D30038에 같은 값을 쓴다.
4. D30026이 0이고 D30029가 0이면 D30038을 0으로 Clear한다.

현재 `ScenarioRule`은 "감시 번지 1개 + 숫자 상수 비교 1개 → 쓰기 1개"라 2번과 4번(AND, 번지 비교)을
표현할 수 없다.

## 범위

- 규칙에 AND로 묶이는 추가 조건 목록을 붙인다.
- 비교값으로 숫자 상수 대신 다른 번지(노드)를 지정할 수 있게 한다.
- 범위 밖: OR 조건, 지연, 단계형 상태머신, 기존 규칙 편집(지금처럼 추가/삭제만).

## 데이터 모델

`ScenarioModels.cs`에 추가한다.

```csharp
public sealed class ScenarioCondition
{
    public TargetRef Target { get; set; }            // 왼쪽 번지
    public CompareOperator Operator { get; set; }
    public double CompareValue { get; set; }         // CompareTarget이 null일 때 숫자 비교
    public string CompareText { get; set; } = "";    // CompareTarget이 null이고 문자열 노드일 때
    public TargetRef CompareTarget { get; set; }     // null이 아니면 이 번지의 현재 값과 비교
}
```

`ScenarioRule`에 두 필드를 더한다.

- `TargetRef CompareTarget` - 주 조건(OnCompare)의 비교 대상 번지. null이면 기존처럼 상수와 비교.
- `List<ScenarioCondition> Conditions` - AND로 붙는 추가 조건. 기본값은 빈 목록.

옛 `index.json`에는 두 필드가 없으므로 null/빈 목록으로 역직렬화되어 기존 규칙의 동작은 그대로다.
역직렬화 결과 `Conditions`가 null이면 엔진과 `Describe`는 빈 목록으로 취급한다.

## 엔진 동작

**구독.** 활성 규칙마다 다음 번지의 `ValueChanged`를 구독한다.

- 주 조건의 `WatchTarget` (OnChange/OnCompare) - 기존과 동일.
- 주 트리거가 OnCompare인 규칙에 한해, 각 추가 조건의 `Target`(왼쪽 번지).

비교 대상 번지(`CompareTarget`, 오른쪽)는 구독하지 않는다. 이 도구가 D30036에 새 커맨드(1011)를 쓴
순간 카메라가 이전 사이클의 ACK(D30028=1011, D30029=1)를 아직 지우지 않았다면, 오른쪽 번지 변경으로
규칙이 실행되어 커맨드가 곧바로 Clear되기 때문이다. 오른쪽 번지는 평가 시점에 읽기만 한다.

**평가.** 구독한 번지에 값이 쓰이면(값이 같아도 쓰기마다 발생하는 기존 이벤트 의미 유지):

1. OnCompare: 주 조건을 평가한다. 이벤트가 주 감시 번지에서 온 것이면 이벤트 값을, 추가 조건 번지에서
   온 것이면 `PlcTargetAccessor.ReadRaw`로 주 감시 번지의 현재 값을 쓴다.
   OnChange: 주 감시 번지의 이벤트에서만 참이다(추가 조건 번지는 구독하지 않으므로 해당 없음).
2. 추가 조건 전부를 각 번지의 현재 값으로 평가한다.
3. 모두 참이면 `Fire(rule, 주 감시 번지 값)` - 수식의 `value`는 기존처럼 주 감시 번지 값이다.

같은 MC 배치 쓰기 안에서 이벤트는 번지 오름차순으로 하나씩 나오고 맵에는 이미 반영된 뒤이므로,
카메라가 D30028/D30029를 어떤 순서로(같은 배치든 따로든) 써도 둘 다 맞춰진 시점에 실행된다.

**타이머.** OnTimer 규칙은 주기가 될 때 추가 조건을 평가해 모두 참일 때만 실행한다(거름 조건).
예: "5초마다, D30036 == 0 이면 D30036에 1011" 로 1단계를 자동 반복할 수 있다.

**비교.** 값 비교는 기존 `CompareNumeric`/`CompareText`를 재사용한다. 비교 대상이 번지면 그 값을 읽어
양쪽이 문자열이면 문자열 비교, 아니면 `PlcTargetAccessor.ToDouble`로 숫자 비교한다.
번지를 읽을 수 없으면(모듈 삭제, 범위 밖 번지, 없는 노드) 그 조건은 거짓이다 - 예외는 밖으로 내지 않는다.

조건 평가 로직은 `ScenarioEngine`에서 떼어 `ConditionEvaluator`(정적 클래스, 모듈 조회 함수를 인자로
받음)로 둔다. 엔진은 구독과 실행만 맡고, 평가는 맵 하나로 단위 테스트할 수 있게 한다.

## UI (규칙 추가 다이얼로그)

- 지금 다이얼로그는 감시 대상과 쓰기 대상에서 "모듈 + D번지 입력 또는 노드 선택 + 배열 인덱스"를 두 벌
  중복으로 갖고 있다. 이를 `TargetPickerViewModel` + `TargetPicker` UserControl 하나로 묶어 감시 대상,
  쓰기 대상, 비교 대상 번지, 추가 조건의 번지에 재사용한다.
- 주 조건의 비교값 칸 앞에 `Value / Address` 선택을 둔다. Address면 같은 모듈 목록에서 고르는
  TargetPicker가 나온다.
- 그 아래 "AND" 섹션: 조건 행 목록(번지 / 연산자 / Value 또는 Address / ✕)과 "+ AND" 버튼.
  주 트리거가 OnChange여도 추가 조건을 붙일 수 있다(거름 조건으로 동작).
- 추가 시 검증: 조건 행의 번지가 비어 있거나, Address 비교인데 비교 번지가 비어 있으면 오류 메시지.
- 목록 설명(`Describe`) 예:
  `WHEN PLC.30028 == PLC.30036 AND PLC.30029 == 1  →  WRITE 0 TO PLC.30036`
  타이머는 `EVERY 5s IF PLC.30036 == 0  →  WRITE 1011 TO PLC.30036`.

## 핸드셰이크를 규칙으로 쓰면

| # | 규칙 |
|---|---|
| 1 | EVERY 5s IF 30036 == 0 → WRITE 1011 TO 30036 (수동으로 할 거면 생략) |
| 2 | WHEN 30028 == [30036] AND 30029 == 1 → WRITE 0 TO 30036 |
| 3 | WHEN 30026 != 0 → WRITE value TO 30038 |
| 4 | WHEN 30026 == 0 AND 30029 == 0 → WRITE 0 TO 30038 |

## 테스트

`tests/VisionSupport.Tests`에 추가. 소켓 없이 `McPlcServer`의 `McMap`에 직접 `WriteWord`한다
(엔진은 DispatcherTimer 때문에 기존 `ScenarioEngineTests`처럼 STA 스레드에서 만든다).

- AND: D30028=1011 → 아직 Clear 안 됨, D30029=1 → D30036이 0. 순서를 바꿔도 동일.
- 번지 비교: D30028 값이 D30036과 다르면 실행 안 됨.
- 오른쪽 번지 무시: D30028=1011, D30029=1 상태에서 D30036에 1011을 쓰면 그대로 1011로 남음.
- 규칙 3/4: D30026=7 → D30038=7, D30026=0이고 D30029=1이면 유지, D30029=0이 되면 0.
- 타이머 거름 조건: `ConditionEvaluator` 단위 테스트로 확인(타이머 tick은 테스트에서 돌리지 않음).
- 호환: `Conditions`/`CompareTarget`이 없는 옛 규칙 JSON이 역직렬화되어 기존대로 동작.
