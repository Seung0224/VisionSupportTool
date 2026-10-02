# VISION 지원툴

VISION 개발 중 상시 띄워두는 보조 도구 모음. 예전에 각각 따로 실행하던 두 프로그램을
**한 프로세스 안의 기능(모듈)** 으로 흡수했다.

```
VisionSupport.sln
└─ src
   ├─ VisionSupport                       셸 (WinExe · net9.0-windows · x64 · 관리자 권한)
   ├─ VisionSupport.MemoryMonitor         구 모니터링/MemMon   (클래스 라이브러리)
   ├─ VisionSupport.PlcServer             구 Server/VirtualPlcServer (클래스 라이브러리)
   ├─ VisionSupport.ImageConverter        이미지 변환기 (클래스 라이브러리)
   └─ VisionSupport.Sam                   누끼 — SAM 2.1 화면 분할 (클래스 라이브러리)
```

## 원본 프로젝트에 대해

`기능성 프로그램 폴더\Server` 와 `기능성 프로그램 폴더\모니터링` 의 소스는 이 솔루션으로
**옮겨온 것**이다. 앞으로 수정은 여기서 한다. 원본 폴더를 고쳐도 이 프로그램에는 반영되지 않는다.

포팅하면서 실제로 바뀐 것:

- VirtualPlcServer 를 `net48` → `net9.0-windows` 로 이식. 한 프로세스는 CLR 하나만 올릴 수
  있어서, 두 기능을 같이 담으려면 피할 수 없는 전제였다.
- `TESTForm/` (주석에 "WinForms 참고용 스크래치"라고 적혀 있고 띄우지 않던 폼)과 그것만
  쓰던 `MaterialSkin.2`, `ReaLTaiizor`, `UseWindowsForms`, `Properties/Resources` 를 제거.
  net9 이식을 막던 의존성이 이 셋뿐이라, 버리는 것으로 이식이 성립했다.
- 두 `MainWindow` 를 `UserControl` 페이지로 전환(`PlcServerView`, `MonitorView`).
- PLC 쪽 `Application.Current.Shutdown()` 제거 — 셸 안에서는 프로세스 전체를 내려버린다.
- 저장 경로는 그대로(`%AppData%\VirtualPlcServer\modules\index.json`). 기존 모듈 설정이
  그대로 이어진다.

## 화면 구조

상시 떠 있는 것은 72×72 원형 아이콘 하나뿐이다. 드래그로 옮길 수 있고, 좌클릭하면 방사형으로
기능 타일이 펼쳐지며, 우클릭에는 전체보기와 종료가 있다. 기능 창은 아이콘에서 그 기능을 눌렀을
때 비로소 만들어진다.

```
LauncherWindow            상시 · Topmost · 작업표시줄 미표시
 ├ 좌클릭 → 방사형 메뉴    도구 · 이미지 변환기 · 누끼 · 링크 · 폴더 · 전체보기
 │  ├ 도구 → 3×3 상자     메모리 모니터 · PLC 서버 · 통신 모니터
 │  └ 누끼 → 화면 막       마우스 아래 대상 강조 · 휠 위/아래 전체/부분 · 좌클릭 따기 · 우클릭/Esc 취소
 └ 우클릭 → 컨텍스트 메뉴  전체보기 · 종료

FeatureWindow × N         기능을 누르면 생성, 닫으면 소멸
CutoutViewer × N          누끼를 딸 때마다 생성 · 우클릭 복사/저장
OverviewDialog × 1        프로세스 자원 · 기능별 상태 · 불투명도
```

누끼는 SAM 3 (tracker ONNX, fp16) 를 ONNX Runtime DirectML 로 GPU 에서 돌린다. 모델(약 900MB)은 처음 쓸 때
`%AppData%\VisionSupport\Sam\` 로 내려받는다. 모드 중 화면은 멈춘 캡처가 아니라 라이브다 — 모니터마다
불투명한 막 창이 캡처 제외(`WDA_EXCLUDEFROMCAPTURE`)로 뜨고, 그 아래 실제 화면을 계속 캡처해 어둡게 다시
그린다(반투명 창은 캡처 제외가 거부된다). 커서 주변 1008×1008 을 원본 해상도로 분석하므로 그보다 큰 대상은
가장자리에서 잘린다. 인코딩(약 0.4초)과 디코딩(약 10ms)은 스레드가 따로라서, 마우스 반응은 인코딩을 기다리지
않는다. 큰 버퍼는 모드 시작 때 한 번만 만들어 재사용하고, 모드가 끝나면 모두 해제한 뒤 바로 수집한다.
모드 중에는 RAM 약 3GB, GPU 약 2.6GB 를 쓴다.
디코더가 내는 마스크 3개 중 무엇을 보여줄지는 모델 점수가 아니라 면적 순으로 고른다 — 점수 순이면 사람 전체보다
상의·얼굴 같은 부분이 뽑힌다. 기본은 가장 큰 마스크이고 휠로 작은 단위로 내려간다(점수 0.5 미만은 제외).
보여주거나 자르기 전에 커서 아래 덩어리만 남기고, 그 면적의 1% 보다 작은 구멍은 메운다. 지금 몇 단계인지는
커서 오른쪽 위에 작게 뜨는 "1/3" 같은 숫자(지수 형태)로 알 수 있다 — 분모는 실제로 신뢰되는 마스크 개수라서
대상에 따라 3보다 작을 수 있고, 휠은 그 범위 안에서만 움직인다. `onnxruntime.dll` 이 System32 의 구형 DirectML 1.0 을 잡으면 GPU 세션이 실패하므로,
세션 전에 앱에 딸린 DirectML.dll 을 먼저 로드한다. 설계와 측정값은
`docs/superpowers/specs/2026-09-15-sam-cutout-design.md`.

이 구조의 핵심은 **"창"과 "실행"이 다른 것**이라는 점이다. 예전 셸은 페이지에 한 번 들어가면
뷰와 ViewModel을 프로세스가 끝날 때까지 들고 있었고, 그래서 실행 여부와 자원 점유가 무관했다.
지금은 창을 닫을 때 기능이 가동 중이 아니면 `StopAsync()`로 전부 해제하고, 가동 중이면
`ReleaseView()`로 뷰와 상세창만 버린다. PLC 서버에 VISION이 붙어 있는 동안 창을 닫아도 소켓이
끊기지 않아야 하기 때문이다. 가동 여부는 셸이 아니라 각 툴의 운전 상태(`IsWorking`)로 판단한다.
백그라운드로 남은 기능은 런처 아이콘의 초록 링으로 드러나고, 내리려면 그 기능 창을 다시 열어
툴에서 정지한 뒤 닫는다.

`Application.ShutdownMode`는 `OnExplicitShutdown`이다. 기능 창이 다 닫혀도 런처는 살아 있어야
하고, 종료는 우클릭 메뉴에서만 일어난다.

앱 위치와 불투명도는 `%AppData%\VisionSupport\launcher.json`에 저장된다.

## 기능 모듈 계약

기능은 `IFeatureModule` 하나로만 셸과 대화한다. 셸은 기능 내부를, 기능은 셸을 모른다.
공통 규약은 `FeatureModule` 추상 클래스가 들고 있다.

실행과 정지는 셸이 아니라 각 툴의 화면에서 한다. 셸이 기능에 대해 판단하는 것은 창을 닫을 때와
프로그램을 종료할 때뿐이다.

| | PLC 서버 | 메모리 모니터 | 이미지 변환기 | 통신 모니터 |
|---|---|---|---|---|
| **가동 기준** (`IsWorking`) | 가동 중인 모듈이 1개 이상 | 대상 프로세스에 attach됨 | 없음 | 이더넷 캡처 또는 CXP 감시가 하나라도 돌고 있음 |
| **창 닫기 — 가동 중** | 서버는 계속 돌고 뷰만 해제 | 수집을 이어가고 뷰만 해제 | 변환 중에는 닫기 거부 | 캡처·판정 계속, 뷰만 해제 |
| **창 닫기 — 가동 아님** | 상태 저장 후 전부 해제 (시나리오 엔진 타이머 포함) | detach + ETW 세션 종료 + 핸들 반납 | 큐 비우기 + 설정 저장 후 전부 해제 | 설정 저장 후 전부 해제 |
| **프로그램 종료** | 상태 저장 후 전부 해제 | detach + ETW 세션 종료 + 핸들 반납 | 변환 중에는 종료 거부 | ETW 세션 → `pktmon stop` → CXP 폴링 정지·GenTL 해제 |

이미지 변환기는 bmp·png·jpg·gif·tiff·jpeg xr 를 WPF 내장 코덱으로 상호 변환한다(압축 가능한
포맷은 품질/압축 조절, 그 외 리사이즈·그레이스케일·비트뎁스). 코그넥스 `.idb` 는
`VisionSupport.ImageConverter.Cognex` 플러그인이 담당하는데, 이 플러그인은 VisionPro가
`C:\Program Files\Cognex\VisionPro\bin` 에 설치된 PC에서만 실제 코덱으로 빌드되고(없으면 스텁),
셸의 `plugins\` 폴더에서 리플렉션으로 로드된다. 없으면 `.idb` 가 메뉴에서 빠질 뿐 나머지는
그대로 동작한다. 저장은 다이얼로그로 폴더를 묻거나, 지정 폴더에 `원본이름.새확장자`(충돌 시
`(1)`, `(2)`)로 바로 저장한다. 설정은 `%AppData%\VisionSupport\ImageConverter\settings.json`.

`IFeatureModule.HasAlert` 는 기능이 사용자에게 알릴 이상을 갖고 있는지다. 하나라도 참이면 런처 아이콘
링이 초록 대신 빨강이 된다(창이 다 닫혀 있어도). 지금은 통신 모니터만 쓴다 — 고정한 PLC/카메라가 응답하지
않거나 끊기거나 프레임을 잃으면 켜지고, 창의 "이상 확인"으로 끈다. 통신 모니터는 Windows 내장 pktmon 을
ETW 로 받아 설치할 것이 없고, CXP 는 Matrox GenTL 프로듀서를 폴링마다 열고 닫아 VISION 의 보드 사용을
막지 않는다. 설정은 `D:\Datas\VisionSupport\Wireshark\settings.json`(옛 `%AppData%` 폴백), CXP 진단
덤프도 같은 폴더. 설계는 `docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md`.

**종료 후 셸에 영향이 없어야 한다**는 것이 이 설계의 핵심 제약이다. 그래서
`FeatureModule.StopAsync` 는 예외를 밖으로 내보내지 않고(`Faulted` 로 표시만 하고),
기능이 연 창을 전부 닫고, 뷰를 버려서 다음 진입 때 새로 만든다. 셸은
`DispatcherUnhandledException` 과 `TaskScheduler.UnobservedTaskException` 도 잡아
원인 기능만 격리한다.

## 테마

셸의 `Theme/Dark.xaml` (구 MemMon `App.xaml` 팔레트)이 앱 전역 테마다.
PLC 기능만 예외로 Material 테마를 쓰는데, 이건 `Application.Resources` 가 아니라
`VisionSupport.PlcServer/Themes/AppTheme.xaml` 을 **해당 뷰에만 머지**해서 넣는다.
WPF 리소스 조회가 비주얼 트리를 타고 올라가므로, 두 컨트롤 테마가 한 프로세스에서
서로를 덮지 않고 공존한다. 별도 창(`PlcMonitorWindow`, `ScenarioEditorWindow`)은
자기 비주얼 트리를 가지므로 같은 사전을 각자 머지한다.

## 불투명도와 둥근 모서리

셸이 소유한 창은 `AllowsTransparency="True"` + `Window.Opacity`로 반투명하다. 창은 **0.95 고정**,
런처 아이콘은 **0.70 기본**이며 아이콘 쪽만 실행 중에 조절된다.

한 번 틀렸다가 되돌린 길이 있으니 적어둔다. 처음에는 Win32 레이어드 윈도우
(`WS_EX_LAYERED` + `SetLayeredWindowAttributes`)로 갔는데, **WPF에서는 동작하지 않는다.** WPF 창은
DWM 리디렉션 표면을 쓰고, 생성된 뒤에는 Windows가 레이어드 비트를 붙여주지 않는다. 같은 핸들에
같은 호출로 `WS_EX_TOOLWINDOW`를 걸면 멀쩡히 붙는데 `WS_EX_LAYERED`만 조용히 사라지고, 이어지는
`SetLayeredWindowAttributes`가 `ERROR_INVALID_PARAMETER`로 실패한다.

`AllowsTransparency`가 WPF가 그 모드로 들어가는 자기 방식이고, **창을 띄우기 전에만** 지정할 수
있다. 그래서 `FeatureWindow.xaml` / `OverviewDialog.xaml`의 마크업에 박혀 있고, 런타임에 바꿀 수
있는 값이 아니다.

여기서 딸려온 이득이 모서리다. 투명한 창은 `Border`의 `CornerRadius`가 그대로 바깥 모서리가
되므로, 예전에 쓰던 `SetWindowRgn` 리전 컷과 그 Win10 계단 현상, Win11/Win10 분기가 전부
사라졌다. 최대화 때만 반경을 0으로 되돌린다.

**기능 프로젝트가 여는 다이얼로그**(`AddModuleDialog`, `PlcMonitorWindow`, `GcReferenceWindow` …)는
불투명하게 남는다. `AllowsTransparency`를 나중에 켤 수 없고, 그 창들은 이 프로젝트가 건드리지
않는 어셈블리 안에 있다.

## 관리자 권한

셸은 `app.manifest` 로 항상 관리자 권한을 요구한다. 권한은 프로세스 단위인데, 메모리
모니터가 ETW 세션 생성과 관리자 권한 프로세스(VISION) attach 에 그 권한을 필요로 한다.

## 빌드 / 실행

```
dotnet build VisionSupport.sln
src\VisionSupport\bin\Debug\net9.0-windows\VisionSupport.exe
```
