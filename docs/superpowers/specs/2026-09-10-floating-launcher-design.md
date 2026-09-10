# 플로팅 런처 전환 설계

2026-09-10

VISION 지원툴을 "항상 떠 있는 셸 창 + 좌측 네비게이션" 구조에서
"플로팅 아이콘 + 방사형 메뉴 + 필요할 때만 만드는 창" 구조로 바꾼다.

## 왜

세 가지가 관찰됐고, 코드에서 원인이 확인된다.

**자원이 누적된다.** 시작 시 만들어지는 건 `IFeatureModule` 3개(껍데기)뿐이고 무거운
ViewModel은 이미 지연 생성이다(`PlcServerFeature.ViewModel`, `MemoryMonitorFeature.ViewModel`).
문제는 해제 쪽이다. 한 번 페이지에 들어가면 뷰와 ViewModel이 `FeatureModule._view` /
각 feature의 `_viewModel`에 캐시되어, 사용자가 명시적으로 "종료"를 누르기 전까지 프로세스가
끝날 때까지 산다. PLC 서버 페이지를 한 번 열면 저장된 모듈 전부가 객체로 복원된 채 남고,
메모리 모니터는 전체 프로세스 스캔 결과를 들고 있는다. 실행/정지 여부와 자원 점유가
무관해지는 이유가 이것이다.

**안 쓸 때도 창이 자리를 차지한다.** 셸 창은 기능을 하나도 쓰지 않는 동안에도 화면에 펼쳐져
있다. 상시 띄워두는 보조 도구로서는 이게 비용이다.

**페이지 이동이 느리다.** 첫 진입 때 무거운 뷰 트리를 만들면서 `ContentControl.Content`를
통째로 교체한다. 창을 분리하면 이 교체 자체가 사라진다.

## 구조

```
LauncherWindow            상시 · Topmost · 작업표시줄 미표시 · 드래그 이동
 ├ 좌클릭 → 방사형 메뉴    메모리 모니터 · PLC 서버 · 이미지 변환기 · 전체보기
 └ 우클릭 → 컨텍스트 메뉴  전체보기 · 설정 · 종료

FeatureWindow × N         기능 클릭 시 생성, 닫으면 소멸
OverviewDialog × 1        전체보기. 이미 열려 있으면 포커스만 준다
```

`Application.ShutdownMode`를 `OnExplicitShutdown`으로 바꾼다. 지금은 마지막 창이 닫히면
앱이 죽는데, 새 구조에서는 기능 창이 다 닫혀도 런처는 살아 있어야 한다. 종료는 런처
우클릭 메뉴에서만 일어나며, `ShutdownAsync()`로 모든 기능을 순차 정지한 뒤 `Shutdown()`한다.

### 런처 창의 크기

접혔을 때 72×72, 펼치면 340×340으로 커졌다가 접히면 다시 줄어든다. 아이콘은 항상 창 중심에
있고, 크기가 바뀔 때 `Left`/`Top`을 보정해 화면상 위치를 유지한다.

큰 투명 창을 상시 띄우지 않는 이유: `AllowsTransparency` 창의 빈 영역은 클릭이 통과하지만
OLE 드래그앤드롭 대상 판정은 그만큼 확실하지 않다. 이미지 변환기가 드래그앤드롭을 쓰므로,
커져 있는 시간을 메뉴가 펼쳐진 동안으로 한정해 이 위험 자체를 없앤다.

펼칠 때 340×340이 화면(작업 영역) 밖으로 나가면 창을 화면 안으로 밀어 넣는다. 접을 때
원래 위치로 돌아가지 않고, 밀린 위치를 그대로 새 위치로 삼는다.

### 저장

`%AppData%\VisionSupport\launcher.json` — 아이콘 위치(Left/Top), 불투명도.
읽을 때 위치가 현재 연결된 모니터들의 가상 화면 밖이면 주 모니터 우하단으로 되돌린다.
모니터 구성이 바뀌면 창이 보이지 않는 곳에 뜨기 때문이다.

## 수명주기 — "창"과 "실행"의 분리

이 설계의 핵심이다. 지금 `FeatureModule`은 뷰와 ViewModel을 한 몸으로 묶어 `StopAsync`에서
같이 버린다. 창을 닫는 것이 곧 기능을 죽이는 것이 되면 PLC 서버는 쓸 수 없다 — VISION이
접속해 있는 동안 창을 잘못 닫으면 소켓이 끊긴다. 그래서 둘로 가른다.

| 창을 닫을 때의 기능 상태 | 동작 |
|---|---|
| `Stopped` · `Faulted` | `StopAsync()` — 뷰·ViewModel·스레드·소켓·ETW 전부 해제 |
| `Running` · `Paused` | `ReleaseView()` — 뷰와 상세창만 버리고 ViewModel은 유지 |

`FeatureModule`에 추가되는 것은 `ReleaseView()` 하나다. 지금 `SafeTeardown()` 안에 있는
"소유 창 닫기 + 뷰 버리기 + ViewModel 아닌 뷰 쪽 IDisposable 처리" 블록을 그 메서드로
추출하고, `SafeTeardown()`은 그것을 호출하게 바꾼다. 새 로직이 아니라 기존 블록의 이동이다.

`GetOrCreateView()`가 이미 `_view ??= CreateView()`이므로 재진입은 그대로 동작한다.

기능이 열어둔 상세창(`PlcMonitorWindow`, `ScenarioEditorWindow`, `GcReferenceWindow`)은
기능 창과 함께 닫는다. `FeatureWindow`가 그 기능의 유일한 창 계층 루트가 되고, 상세창만
남아 화면을 어지럽히는 일이 없어진다. `TrackWindow`로 등록된 목록이 이미 있으므로 그것을
그대로 쓴다.

효과: 이미지 변환기를 열었다 닫으면 그 자리에서 완전히 사라진다. 지금은 프로세스가 끝날
때까지 남는다. 백그라운드로 남는 것은 사용자가 명시적으로 실행한 기능뿐이고, 그것도 런처
아이콘의 활성 링과 전체보기의 정지 버튼으로 드러난다.

## IFeatureModule 계약

추가 두 개.

```csharp
/// <summary>방사형 메뉴 타일에 그릴 아이콘. Segoe MDL2 Assets 문자 하나.</summary>
string Glyph { get; }

/// <summary>기능 창의 초기 크기.</summary>
Size PreferredWindowSize { get; }
```

`FeatureModule`에 기본값을 둔다(`Glyph => ""`, `PreferredWindowSize => new(1100, 720)`).
제거는 없다. `Description`·`CanPause`·`StatusLine`은 전체보기가 계속 쓴다.

## 디자인 언어

One UI 계열. 각진 사각형 대신 **모서리가 둥근 사각형**이 기본형이다.

`Theme/Dark.xaml`에 반경 토큰을 추가하고 모든 컨테이너가 이것만 참조한다.

| 토큰 | 값 | 쓰이는 곳 |
|---|---|---|
| `RadiusWindow` | 16 | 창·다이얼로그 바깥 껍데기 |
| `RadiusCard` | 14 | 전체보기의 기능 행, 패널 |
| `RadiusTile` | 18 | 방사형 메뉴 타일(56×56) |
| `RadiusControl` | 10 | 버튼, 입력 |
| `RadiusPill` | 999 | 상태 배지, 토글 |

색은 기존 팔레트를 그대로 쓴다(`Bg`, `Panel`, `PanelAlt`, `Border`, `Fg`, `FgDim`, `Accent`,
`State*`). 경계는 선보다 배경 톤 차이로 나눈다.

런처 아이콘 자체는 원형(첨부 이미지의 FAB 형태)을 유지하고, 방사형 메뉴 항목은 둥근
사각형 타일로 그린다.

### 창 모서리를 실제로 둥글게 만드는 법

대상 PC는 Windows 10 19045다. Win11의 `DWMWA_WINDOW_CORNER_PREFERENCE`(attribute 33)는
빌드 22000 미만에서 무시되므로 Win10에서는 창 모서리가 각지게 남는다.

그래서 두 갈래로 간다.

- **Win11 (빌드 22000 이상)**: `DwmSetWindowAttribute`로 `DWMWCP_ROUND`. 안티에일리어싱된
  네이티브 라운드 코너.
- **Win10**: `WindowChrome`(`GlassFrameThickness=0`, `CaptionHeight=36`,
  `ResizeBorderThickness=6`)으로 커스텀 타이틀바를 그리고, `SetWindowRgn` +
  `CreateRoundRectRgn`으로 HWND 자체를 둥글게 자른다. 최대화 시에는 리전을 해제한다.

`WindowChrome`을 쓰는 이유는 `AllowsTransparency=true`와 달리 하드웨어 가속을 유지하기
때문이다. 커스텀 타이틀바가 필요한 것은 어차피 다크 테마를 맞춰야 해서이고, 지금도
`ShellWindow`가 DWM 다크 타이틀바를 따로 요청하고 있다.

`SetWindowRgn` 리전 컷은 안티에일리어싱이 없어 Win10에서 모서리에 계단 현상이 약간 보인다.
이건 Win10에서 GPU 가속을 포기하지 않는 한 피할 수 없는 절충이며, 가속을 포기하면 이
작업의 동기 중 하나였던 "느리다"가 악화된다.

## 불투명도

`App.OnStartup`에서 한 번 등록한다.

```csharp
EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
    new RoutedEventHandler(OnAnyWindowLoaded));
```

핸들러가 해당 창의 HWND에 `WS_EX_LAYERED`를 켜고
`SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA)`를 건다.

이 방식을 고른 이유:

- **기능 프로젝트를 건드리지 않아도 전부 적용된다.** `AddModuleDialog`, `AddNodeDialog`,
  `NodeValueDialog`, `WriteValueDialog`, `ScenarioRuleDialog`, `PlcMonitorWindow`,
  `ScenarioEditorWindow`, `GcReferenceWindow` — 클래스 핸들러는 프로세스 안 모든 `Window`의
  `Loaded`를 잡는다. 기능은 셸을 모른다는 기존 대칭이 유지된다.
- **`Window.Opacity`와 달리 `AllowsTransparency`가 필요 없다.** 하드웨어 가속과 창 크기
  조절이 그대로다.

기본 0.92. 전체보기의 슬라이더가 값을 바꾸면 `Application.Current.Windows`를 돌며 알파만
다시 쓰고 `launcher.json`에 저장한다. 범위는 0.30~1.00으로 클램프한다 — 더 낮으면 창을
찾지 못한다.

런처 창만 예외다. 원형 아이콘이라 진짜 모양 투명(`AllowsTransparency=true`)이 필요하고,
여기에는 레이어드 알파를 걸지 않는다.

레이어드 스타일은 최대화나 DPI 변경 때 풀리는 사례가 있으므로 `StateChanged`와
`DpiChanged`에서 재적용한다.

## 전체보기 다이얼로그

420×380 고정, 리사이즈 없음. 위에서 아래로 네 덩어리.

1. **프로세스 자원** — 메모리·CPU·스레드·가동시간. 기존 `IdleViewModel.Refresh()` 로직을
   그대로 옮긴다. 1초 `DispatcherTimer`는 창이 열려 있는 동안만 돈다(`Loaded`/`Unloaded`).
2. **기능별 행** — 기능마다 둥근 카드 한 장. 상태 점(`State*` 브러시) + `Title` +
   `StatusLine`, 오른쪽에 실행/정지 버튼과 열기 버튼. `CanPause=false`인 이미지 변환기는
   정지 버튼이 비활성. **백그라운드로 남은 기능을 내리는 유일한 통로다.**
3. **설정** — 불투명도 슬라이더.
4. **상태줄** — `ActivityLog` 최신 1줄. 미처리 예외(`DispatcherUnhandledException`,
   `UnobservedTaskException`)가 여기로 들어온다.

## 방사형 메뉴

항목은 기능 3개 + 전체보기 = 4개. 반지름 110px, 타일 56×56.

각도 배치: 항목이 n개일 때 `-90° + i × (360°/n)`, 즉 12시부터 시계 방향 균등 배치.
이 계산은 순수 함수로 분리해 테스트한다.

펼침/접힘은 120ms 스케일 애니메이션 하나로 메뉴 전체를 키우고 줄인다. 항목별 지연(stagger)은
넣지 않는다 — 항목이 4개뿐이라 순차 확산이 눈에 띄지 않고, 항목마다 스토리보드를 따로 거는
비용만 남는다.

기능이 가동 중이면 타일에 `StateRunning` 색 링을 두른다.
창이 이미 열려 있는 기능을 다시 누르면 새로 만들지 않고 그 창을 활성화한다.

메뉴 밖 클릭이나 `Esc`로 접힌다.

## 파일 변경

삭제
```
src/VisionSupport/Shell/ShellWindow.xaml(.cs)
src/VisionSupport/Shell/ShellViewModel.cs
src/VisionSupport/Shell/NavItem.cs
src/VisionSupport/Idle/IdleView.xaml(.cs)
src/VisionSupport/Idle/IdleViewModel.cs
```

신규
```
src/VisionSupport/Launcher/LauncherWindow.xaml(.cs)   플로팅 아이콘 + 방사형 메뉴
src/VisionSupport/Launcher/LauncherViewModel.cs        항목 목록, 창 열기, 종료
src/VisionSupport/Launcher/RadialLayout.cs             각도 배치 (순수 함수)
src/VisionSupport/Launcher/LauncherSettings.cs         위치·불투명도 저장/복원
src/VisionSupport/Windows/FeatureWindow.xaml(.cs)      기능 창 래퍼
src/VisionSupport/Windows/WindowEffects.cs             레이어드 알파 + 라운드 코너
src/VisionSupport/Overview/OverviewDialog.xaml(.cs)
src/VisionSupport/Overview/OverviewViewModel.cs        자원 샘플링 + FeatureCard
```

유지
```
src/VisionSupport/Shell/ActivityLog.cs
src/VisionSupport/Shell/Converters.cs
src/VisionSupport/Theme/Dark.xaml       (반경 토큰과 둥근 스타일 추가)
src/VisionSupport/Features/*            (IFeatureModule에 속성 2개, FeatureModule에 ReleaseView)
```

기능 3개 프로젝트(`MemoryMonitor`, `PlcServer`, `ImageConverter`)는 손대지 않는다.

## 테스트

`tests/VisionSupport.Tests`는 셸을 참조하지 않으므로 기존 테스트 8개는 그대로 통과한다.
셸 프로젝트 참조를 한 줄 추가하고, UI가 아닌 순수 로직만 새로 덮는다.

- `RadialLayout` — n개 항목의 각도가 12시부터 균등 배치되는지, n=1과 n=0에서 깨지지 않는지
- `LauncherSettings` — 저장/복원 라운드트립, 화면 밖 좌표가 기본 위치로 보정되는지,
  파일이 없거나 깨졌을 때 기본값을 주는지
- `WindowEffects.ToAlphaByte` — 0.30~1.00 클램프 후 0~255 알파 변환

`FeatureModule.ReleaseView()`의 상태별 분기는 `FeatureModule`이 추상 클래스라 테스트용 더미
구현으로 검증할 수 있다. Running일 때 뷰만 사라지고 상태가 유지되는지, Stopped일 때
`StopAsync` 경로를 타는지.

## 위험

**PLC 서버가 백그라운드에 남은 걸 잊는다.** 런처 아이콘 자체에도 "가동 중인 기능 있음"
표시를 준다(아이콘 테두리 링). 전체보기에서 한 번에 내릴 수 있다.

**Win10에서 라운드 코너 계단 현상.** 위에 적은 절충. 실물을 보고 심하면 `SetWindowRgn`을
빼고 창 모서리는 각지게, 내부 요소만 둥글게 가는 선택지가 남아 있다.

**레이어드 알파와 최대화/DPI.** `StateChanged`·`DpiChanged`에서 재적용으로 대응.

**런처가 다른 창을 가린다.** Topmost 72×72이고 드래그로 옮길 수 있다. 우클릭 메뉴에
"잠시 숨기기"는 넣지 않는다 — 숨기면 되살릴 경로가 없어진다.
