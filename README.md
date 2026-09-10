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
   └─ VisionSupport.ImageConverter.Cognex 코그넥스 .idb 코덱 (선택 플러그인, 런타임 로딩)
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
 ├ 좌클릭 → 방사형 메뉴    메모리 모니터 · PLC 서버 · 이미지 변환기 · 전체보기
 └ 우클릭 → 컨텍스트 메뉴  전체보기 · 종료

FeatureWindow × N         기능을 누르면 생성, 닫으면 소멸
OverviewDialog × 1        프로세스 자원 · 기능별 상태와 정지 · 불투명도
```

이 구조의 핵심은 **"창"과 "실행"이 다른 것**이라는 점이다. 예전 셸은 페이지에 한 번 들어가면
뷰와 ViewModel을 프로세스가 끝날 때까지 들고 있었고, 그래서 실행 여부와 자원 점유가 무관했다.
지금은 창을 닫을 때 기능이 가동 중이 아니면 `StopAsync()`로 전부 해제하고, 가동 중이면
`ReleaseView()`로 뷰와 상세창만 버린다. PLC 서버에 VISION이 붙어 있는 동안 창을 닫아도 소켓이
끊기지 않아야 하기 때문이다. 백그라운드로 남은 기능은 런처 아이콘의 초록 링으로 드러나고,
전체보기에서 내릴 수 있다.

`Application.ShutdownMode`는 `OnExplicitShutdown`이다. 기능 창이 다 닫혀도 런처는 살아 있어야
하고, 종료는 우클릭 메뉴에서만 일어난다.

앱 위치와 불투명도는 `%AppData%\VisionSupport\launcher.json`에 저장된다.

## 기능 모듈 계약

기능은 `IFeatureModule` 하나로만 셸과 대화한다. 셸은 기능 내부를, 기능은 셸을 모른다.
공통 규약은 `FeatureModule` 추상 클래스가 들고 있다.

| 명령 | PLC 서버 | 메모리 모니터 | 이미지 변환기 |
|---|---|---|---|
| **실행** | 허브의 모든 모듈 기동 | 선택한 프로세스에 attach + 샘플링 시작 | (없음 — 언제나 사용 가능) |
| **정지** | 가동 중이던 모듈만 기억하고 내림 (노드 값 유지) | 샘플링만 멈춤 (attach·ETW 세션 유지) | (일시정지 없음 — 버튼 비활성) |
| **실행**(정지 상태에서) | 기억해 둔 그 모듈들만 다시 기동 | 끊김 없이 이어서 수집 | — |
| **종료** | 상태 저장 후 전부 정지·해제 | detach + ETW 세션 종료 + 핸들 반납 | 진행 중 배치 취소 + 큐 비우기 + 설정 저장 |
| **창 닫기** | 가동 중이면 서버는 계속 돌고 뷰만 해제 | 가동 중이면 수집을 이어가고 뷰만 해제 | 가동 상태가 없으므로 항상 전부 해제 |

이미지 변환기는 bmp·png·jpg·gif·tiff·jpeg xr 를 WPF 내장 코덱으로 상호 변환한다(압축 가능한
포맷은 품질/압축 조절, 그 외 리사이즈·그레이스케일·비트뎁스). 코그넥스 `.idb` 는
`VisionSupport.ImageConverter.Cognex` 플러그인이 담당하는데, 이 플러그인은 VisionPro가
`C:\Program Files\Cognex\VisionPro\bin` 에 설치된 PC에서만 실제 코덱으로 빌드되고(없으면 스텁),
셸의 `plugins\` 폴더에서 리플렉션으로 로드된다. 없으면 `.idb` 가 메뉴에서 빠질 뿐 나머지는
그대로 동작한다. 저장은 다이얼로그로 폴더를 묻거나, 지정 폴더에 `원본이름.새확장자`(충돌 시
`(1)`, `(2)`)로 바로 저장한다. 설정은 `%AppData%\VisionSupport\ImageConverter\settings.json`.

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

모든 창은 반투명하다(기본 0.92, 전체보기의 슬라이더로 0.30~1.00 조절). 구현은 `Window.Opacity`가
아니라 Win32 레이어드 윈도우(`WS_EX_LAYERED` + `SetLayeredWindowAttributes`)다. `Window.Opacity`는
`AllowsTransparency=true`를 요구하고 그러면 소프트웨어 렌더링 경로로 떨어지는데, 그 비용을 모든
창이 치르게 된다.

적용은 `App.OnStartup`의 클래스 핸들러 한 줄이 담당한다.

```csharp
EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, ...)
```

프로세스 안 모든 `Window`의 `Loaded`를 잡으므로 기능 프로젝트의 다이얼로그(`AddModuleDialog`,
`PlcMonitorWindow`, `GcReferenceWindow` …)까지 그 프로젝트를 고치지 않고 전부 적용된다. 기능은
셸을 모른다는 대칭이 유지되는 이유다. 런처 창만 예외로, 원형 모양 때문에 진짜 모양 투명이
필요해서 레이어드 알파를 걸지 않는다.

모서리는 Win11이면 DWM의 `DWMWA_WINDOW_CORNER_PREFERENCE`, Win10이면 `SetWindowRgn`으로 HWND를
직접 둥글게 자른다. Win10 쪽은 안티에일리어싱이 없어 모서리에 약간 계단이 보이는데, 이건 GPU
가속을 지키기 위한 절충이다. 두 경로 다 `Windows/WindowEffects.cs`에 있다.

## 관리자 권한

셸은 `app.manifest` 로 항상 관리자 권한을 요구한다. 권한은 프로세스 단위인데, 메모리
모니터가 ETW 세션 생성과 관리자 권한 프로세스(VISION) attach 에 그 권한을 필요로 한다.

## 빌드 / 실행

```
dotnet build VisionSupport.sln
src\VisionSupport\bin\Debug\net9.0-windows\VisionSupport.exe
```
