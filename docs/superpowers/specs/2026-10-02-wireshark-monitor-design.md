# 통신 모니터(VisionSupport.Wireshark) 기능 모듈 설계

작성일: 2026-10-02
상태: 승인됨 (계획 작성 전)

## 1. 목표

VISION 개발 중 PC에 연결된 통신의 **주고받음과 비정상·끊김 여부**를 상시 확인하는 도구를
도구함(메모리 모니터·PLC 서버와 같은 3×3 상자)에 추가한다. 시중 Wireshark의 패킷 목록/계층
트리/hex 구성을 따르되, 사용자가 패킷을 읽지 않아도 **이상 여부를 바로 알 수 있는 것**이
최우선이다.

확인하고 싶은 장면:

- **A. VISION ↔ PLC 이더넷** (MC 3E/4E 바이너리, ADS/AMS) — 끊김, 응답 지연, 재전송.
- **B. 카메라** — GigE Vision 카메라의 프레임 드롭, 그리고 Hik 계열 CXP 카메라가 물린
  **Matrox Rapixo CXP** 보드의 링크 끊김·에러 카운터·프레임 드롭.

VISION이 영상을 MIL로 받든 Hikrobot MVS로 받든 둘 다 대응한다. 둘 다 같은 Matrox 드라이버와
GenTL 프로듀서(`C:\Program Files\Matrox Imaging\Drivers\GenTLProducer\Win64\Matrox.CoaXPress.cti`,
`GENICAM_GENTL64_PATH`에 등록됨) 위에서 동작하므로, 이 도구는 SDK가 아니라 **GenTL 프로듀서를
직접** 읽는다.

## 2. 비목표 (YAGNI)

- **RS232** — 사용자가 제외. (다른 프로세스가 연 COM 포트 엿보기는 커널 필터 드라이버가
  필요하다.)
- Npcap/WinPcap 기반 캡처 — 드라이버 설치와 상용 재배포 라이선스가 필요하다.
- MIL·MVS SDK 직접 호출 — 코드 두 벌, SDK 의존성. GenTL 한 경로로 대체.
- Wireshark 수준의 범용 해석기, 캡처 필터 언어(BPF), 패킷 송신·재생, IPv6 상세 해석.
- pcapng 읽기(열기) — 내보내기만 한다. 열어 보는 것은 진짜 Wireshark에서.

## 3. 아키텍처

새 프로젝트 `src/VisionSupport.Wireshark` (net9.0-windows, x64, WPF, 네임스페이스
`VisionSupport.Wireshark`). 셸 → 기능 단방향 참조 규칙 그대로.

- `WiresharkFeature : FeatureModule` — `App.CreateTools()`에 한 줄 등록. 타이틀 "통신 모니터",
  Glyph는 `LanConnect` 계열(LauncherIconTests로 검증).
- 패키지: `Microsoft.Diagnostics.Tracing.TraceEvent`, `ScottPlot.WPF`, `CommunityToolkit.Mvvm`
  (메모리 모니터와 같은 버전).

### 3.1 단위

| 단위 | 책임 | 의존 |
|---|---|---|
| `Capture/PktmonSource` | pktmon 필터·캡처 시작/정지, ETW 실시간 세션에서 프레임 수신, NIC 층만 남기고 중복 제거 | TraceEvent, `pktmon.exe` |
| `Capture/LinkWatcher` | NIC 링크 업/다운 (`NetworkChange`, `NetworkInterface`) | BCL |
| `Dissect/*` | 바이트 → 계층 트리. Ethernet, ARP, IPv4, TCP, UDP, MC(3E/4E 바이너리), ADS/AMS, GVCP, GVSP | 없음 (순수) |
| `Health/HealthTracker` | 대상별 상태 기계, 이상 이벤트 생성. 시계는 주입(`TimeProvider`) | 없음 (순수) |
| `Store/PacketStore` | 원형 버퍼 + 이상 전후 보존 | 없음 |
| `Export/PcapngWriter` | pcapng(SHB/IDB/EPB) 쓰기 | 없음 |
| `Cxp/GenTLCxpSource` | GenTL 프로듀서 로드, Interface/Device 노드에서 링크 상태·카운터 1초 폴링 | P/Invoke (`GenTL` C API) |
| `Cxp/PnpWatcher` | Rapixo PCIe 장치 연결/분리 감시 (WMI `Win32_PnPEntity` 이벤트) | System.Management |
| `ViewModels/*`, `Views/*` | 화면 | 위 전부 |

`Dissect`, `Health`, `Store`, `Export`는 WPF·ETW에 의존하지 않아 바이트 배열과 가상 시계만으로
테스트한다.

### 3.2 데이터 흐름

```
PktmonSource ─┐                         ┌→ PacketStore → 패킷 목록 / 계층 트리 / hex
              ├→ Dissector → 이벤트 큐 ──┤
GenTL 폴링 ───┤   (수신 스레드)          └→ HealthTracker → 대상 카드 / 런처 링 / 이상 목록
PnpWatcher ───┤
LinkWatcher ──┘
```

- 수신 스레드는 해석 후 큐에 넣기만 한다. UI는 `DispatcherTimer` 200ms마다 큐를 일괄 반영.
- `HealthTracker`는 패킷 도착과 별개로 **1초 틱**에서 기한 검사를 한다 — 패킷이 아예 안 와도
  "응답 없음/조용함"이 뜬다.

## 4. 화면 (메모리 모니터 레이아웃·색 기준)

위에서 아래로:

1. **툴바** — 인터페이스 선택(NIC 목록 + CXP 보드), 시작/정지, 표시 필터(IP·포트·프로토콜
   텍스트), pcapng 내보내기.
2. **감시 대상 카드 줄** — 이 화면의 주인공. 대상 하나에 카드 하나.
   - 예: `PLC 192.168.0.10:5000 (MC)`, `카메라 GigE 192.168.1.20`, `CXP 보드 0 / 커넥션 0~3`.
   - 색: 초록(정상) / 노랑(주의) / 빨강(이상) / 회색(트래픽 없음·감시 불가).
   - 판정 한 줄을 사람 말로: "응답 없음 4.2초째", "최근 1분 재전송 12회", "프레임 드롭 3장
     (마지막 10:42:15)", "링크 끊김", "상세 불가 — 보드 연결 여부만 감시".
   - 클릭 → 패킷 목록이 그 대상으로 필터되고 가장 최근 이상 패킷으로 스크롤.
3. **실시간 차트**(ScottPlot) — 선택 대상의 송수신량(bytes/s)과 요청-응답 시간(ms).
4. **패킷 목록** — No / 시간 / 출발 / 목적 / 프로토콜 / 길이 / 정보. 이상 행 색 강조.
5. **하단 분할** — 왼쪽 계층 트리, 오른쪽 hex 덤프 (선택 바이트 상호 강조는 비목표).
6. **탭** — 이상 목록(시간·대상·종류·설명, 클릭 시 해당 패킷으로 이동) / CXP 상세(커넥션별
   링크 상태·속도·에러 카운터·프레임 수·드롭 수).

### 4.1 대상 등록

캡처 시작 후 MC(사용자 지정 포트, 기본 5000-5010)·ADS(48898)·GVCP(3956)·GVSP 대화를 자동
감지해 **후보 카드**(점선 테두리)로 띄운다. 사용자가 "고정"하면 다음 실행에도 감시한다.
고정 목록과 임계값은 `D:\Datas\VisionSupport\Wireshark\settings.json`을 우선 읽고 없으면
`%AppData%\VisionSupport\Wireshark\settings.json`으로 폴백한다(c626971의 규칙).

## 5. 이상 판정

| 종류 | 대상 | 기본 임계값 | 카드 색 |
|---|---|---|---|
| 응답 타임아웃 (요청 후 응답 없음) | MC, ADS | 1000ms | 빨강 |
| 응답 지연 | MC, ADS | 200ms 초과가 최근 1분 5회 이상 | 노랑 |
| 조용함 (트래픽 끊김) | 고정 대상 전부 | 5초 | 빨강 |
| TCP 재전송·중복 ACK | TCP | 최근 1분 3회 이상 | 노랑 |
| TCP RST / 비정상 FIN | TCP | 1회 | 빨강 (이후 정상 트래픽 30초면 초록 복귀) |
| 제로 윈도우 | TCP | 1회 | 노랑 |
| GVSP 패킷 ID 누락 / 블록 ID 건너뜀 | GigE | 1회 | 빨강 (드롭 카운트 누적) |
| NIC 링크 다운 | NIC | 즉시 | 빨강 |
| CXP 링크 다운 / 에러 카운터 증가 | CXP | 즉시 / 증가 시 | 빨강 / 노랑 |
| Rapixo 장치 분리 | CXP | 즉시 | 빨강 |

임계값은 설정 파일에서 조정 가능. 모든 판정은 이상 목록에 이벤트로 남는다.

## 6. 셸 계약 변경 — 경고 상태

창을 닫아 두어도 이상을 알 수 있게 **런처 아이콘 링을 빨강**으로 바꾼다.

- `IFeatureModule`에 `bool HasAlert { get; }` 추가. `FeatureModule` 기본 구현은 `false`이고
  변경 시 기존 `Changed` 이벤트를 올린다 — 다른 기능은 수정 불필요.
- 셸의 링 색: 어떤 기능이든 `HasAlert`면 빨강 > `IsWorking`이면 초록 > 없음.
- 통신 모니터의 `HasAlert` = 고정 대상 중 빨강 카드가 하나라도 있음. 사용자가 창에서 "확인"을
  누르면 현재 이상까지는 해제(새 이상이 오면 다시 빨강).
- README "기능 모듈 계약" 표에 통신 모니터 열과 `HasAlert` 설명을 추가.

## 7. 생명주기 (README 계약 표의 새 열)

| | 통신 모니터 |
|---|---|
| **가동 기준** (`IsWorking`) | 이더넷 캡처 또는 CXP 감시가 하나라도 돌고 있음 |
| **창 닫기 — 가동 중** | 캡처·판정 계속, 뷰만 해제 (런처 링으로 상태 노출) |
| **창 닫기 — 가동 아님** | 설정 저장 후 전부 해제 |
| **프로그램 종료** | ETW 세션 정지 → `pktmon stop` → GenTL 핸들 닫기(`DevClose`/`IFClose`/`TLClose`/`GCCloseLib`) → WMI 감시 해제 |

비정상 종료로 남은 ETW 세션(`VisionSupport-Wireshark`)은 다음 시작 때 같은 이름으로 먼저
정리한다. `pktmon`이 이미 다른 용도로 돌고 있으면 건드리지 않고 "pktmon 사용 중"으로 안내한다.

## 8. 메모리 상한 (며칠 상시 실행 전제)

- 패킷 원본은 원형 버퍼, 기본 **200MB 또는 50만 개** 중 먼저 닿는 쪽(설정 가능).
- 이상 이벤트마다 **앞 20개·뒤 20개** 패킷은 별도 보존(이벤트 최대 1000개, 넘으면 오래된 것부터).
- GVSP 영상 페이로드는 저장하지 않는다 — 헤더(블록 ID·패킷 ID·포맷)만 판정에 쓰고 패킷 목록에는
  초당 집계 행으로만 보인다.
- 차트는 최근 10분만 유지.

## 9. 오류 처리

- `pktmon`/ETW 시작 실패 → 카드 줄에 "캡처를 시작할 수 없음: 원인", 기능은 `Faulted` 표시만
  (기존 `FeatureModule` 규약, 예외를 셸로 던지지 않음).
- GenTL 프로듀서 없음 / 열기 거부(VISION 독점) → CXP 카드를 "상세 불가 — 보드 연결 여부만
  감시"로 강등하고 `PnpWatcher`만 유지. 1분마다 재시도.
- 해석기 예외 → 그 패킷만 "해석 실패"로 표시하고 계속.

## 10. 실기 확인이 필요한 가정 (계획의 첫 두 작업)

1. **pktmon 실시간 ETW 수신**: `pktmon start --capture` 상태에서 별도 실시간 ETW 세션으로
   PktMon 프로바이더 이벤트를 받아 원본 프레임을 얻을 수 있는가, 컴포넌트 ID로 NIC 층만 고를 수
   있는가. (이 PC: Windows 10 19045, `pktmon` 존재 확인됨.)
   실패 시 대안: Raw 소켓(`SIO_RCVALL`) — IP 이상만 보임(ARP·링크 층 제외), 화면·판정은 동일.
2. **GenTL 동시 접근**: VISION(MIL 또는 MVS)이 Rapixo로 그랩 중일 때 이 도구가
   `Matrox.CoaXPress.cti`의 TL/Interface를 열고 링크 상태·에러 카운터 노드를 읽을 수 있는가,
   Device를 `DEVICE_ACCESS_READONLY`로 열 수 있는가. **장비 PC에서 확인**.
   실패 시 9절의 강등 모드가 기본 동작이 된다.

결과는 이 문서의 이 절에 기록한다.

## 11. 테스트

- `Dissect` — MC 3E/4E 요청/응답, ADS/AMS, GVCP, GVSP 리더/페이로드/트레일러, TCP 플래그 샘플
  프레임(바이트 배열).
- `HealthTracker` — 가상 시계로 타임아웃·지연·조용함·재전송·RST 복귀·GVSP 드롭 시나리오.
- `PacketStore` — 원형 버퍼 상한, 이상 전후 보존.
- `PcapngWriter` — 블록 구조와 길이 필드 검증(바이트 비교).
- 셸 — `HasAlert` 링 색 우선순위, LauncherIconTests의 Glyph, `ViewConstructionTests` 패턴(STA)으로
  뷰 생성.
- 모든 테스트는 기존 `tests/VisionSupport.Tests` 단일 프로젝트에 추가.
