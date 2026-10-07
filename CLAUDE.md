# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 이 프로젝트

VISION 개발 중 상시 띄워두는 사내 보조 도구. 하나의 WPF 프로세스가 여러 기능을
`IFeatureModule`로 호스팅한다 (PLC 서버, 메모리 모니터, 이미지 변환기, 통신 모니터).
전체 배경·설계 이유·UX 구조는 [README.md](README.md)에 상세히 있으므로 먼저 읽을 것 — 특히
"화면 구조", "기능 모듈 계약", "불투명도와 둥근 모서리" 절은 이 저장소를 건드리기 전에
필수로 이해해야 하는 제약을 담고 있다.

## 빌드 / 실행 / 테스트

```
dotnet build VisionSupport.sln
src\VisionSupport\bin\Debug\net9.0-windows\VisionSupport.exe   # 관리자 권한 필요 (app.manifest)

dotnet test tests\VisionSupport.Tests
dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~RadialLayoutTests"   # 단일 테스트 클래스
dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~RadialLayoutTests.<테스트 메서드명>"  # 단일 테스트

dotnet publish src\VisionSupport -p:PublishProfile=win-x64   # dist\ 에 self-contained 단일 exe
```

- 모든 프로젝트가 `net9.0-windows` / `x64` 고정. 메모리 모니터가 ClrMD로 대상 프로세스에
  attach하는데 리더 비트니스가 대상과 맞아야 해서다.
- `tests/VisionSupport.Tests`는 모든 기능 프로젝트를 직접 참조하는 단일 xunit 프로젝트다.
  별도 테스트 프로젝트가 기능별로 나뉘어 있지 않다.
- WPF 컨트롤을 실제로 생성하는 테스트(`ViewConstructionTests` 등)는 필요할 때 자체적으로
  STA 스레드를 띄워 돈다 — 프로젝트 전역 STA 설정이 아니라 테스트 코드 안의 패턴이다.
- `VisionSupport.csproj`는 `InternalsVisibleTo`로 `VisionSupport.Tests`에 internal을 열어준다
  (레지스트리 커맨드 파싱 등 순수 로직만 대상, 그 때문에 public API를 늘리지 않음).
- `VisionSupport.ImageConverter.Cognex`는 솔루션에 포함되어 있지 않다. Cognex VisionPro SDK가
  설치된 PC에서만 별도로 빌드되어 셸의 `plugins\` 폴더에 놓이고, 셸은 그 코덱을 리플렉션으로
  찾는다(README "기능 모듈 계약" 절 참고) — 이 저장소를 체크아웃한 일반 PC에서는 없는 게 정상이며,
  없어도 `.idb`가 메뉴에서 빠질 뿐 나머지 변환 기능은 그대로 동작한다.

## 아키텍처 핵심

**셸/기능 경계.** `src/VisionSupport`가 셸(런처 아이콘, 방사형 메뉴, 기능 창)이고, 각 기능은
독립된 클래스 라이브러리 프로젝트(`VisionSupport.PlcServer`, `.MemoryMonitor`,
`.ImageConverter`, `.Wireshark`)로 존재한다. 의존 방향은 항상 셸 → 기능 한쪽뿐이다
(`VisionSupport.csproj`에 `ProjectReference`가 몰려 있고, 반대 방향 참조는 없음). 기능은
`FeatureModule`(`src/VisionSupport/Features/FeatureModule.cs`)을 상속해 `IFeatureModule`을
구현하며, 셸과의 접점은 이 인터페이스뿐이다. 새 기능을 추가할 때는 `App.xaml.cs`의
`CreateModules()`에 한 줄 등록하는 식으로 붙인다.

이 계약에서 가장 중요한 건 **"창 닫기"와 "정지"가 다르다**는 점이다 — `ReleaseView()`는 뷰만
버리고 기능은 계속 돌게 두고(PLC 서버가 VISION과 소켓을 물고 있는 동안에도 창을 닫을 수 있어야
함), `StopAsync()`가 완전한 해제다. 기능이 지금 "가동 중"인지는 셸의 `State`가 아니라 각 기능의
`IsWorking`으로 판단한다. `StopAsync`는 절대 예외를 밖으로 던지지 않고 실패 시
`FeatureState.Faulted`로만 표시한다 — 셸은 며칠씩 켜져 있는 게 전제라, 기능 하나가 죽어도
프로세스 전체가 죽으면 안 된다는 게 이 클래스 전체의 설계 이유다.

**PLC 서버 내부.** `VisionSupport.PlcServer`는 이 솔루션으로 포팅되기 전 이름
(`VirtualPlcServer`)의 네임스페이스를 아직 쓴다(`Modules/`, `Protocols/` 아래 다수 파일이
`namespace VirtualPlcServer...`) — grep이나 "정의로 이동"이 프로젝트 이름과 다른 네임스페이스로
튈 수 있다는 점을 염두에 둘 것. 통신 프로토콜(ADS, MC, MC-TCP, OPC UA)은 각각
`Protocols/<이름>/`에 있고, 공통 노드 모델은 `Protocols/Common/`에 있다. 여러 통신 서버·시나리오
모듈을 동시에 올릴 수 있는 구조로, `IHardwareModule`이 그 공용 계약이다(재생/정지/재초기화,
`ModuleRunState`). 이 인터페이스는 카메라 등 다른 가상 하드웨어로 확장하기 위한 자리도 미리
잡아 두고 있다(`ModuleTypeRegistry`의 "Other Hardware / Coming Soon" 항목).

**투명 창.** 셸이 띄우는 창(`FeatureWindow`, `OverviewDialog`)은 `AllowsTransparency`로
반투명하다. 이 속성은 **창을 띄우기 전에만** 지정 가능해서 XAML에 박혀 있고 런타임에 바꿀 수
없다 — Win32 레이어드 윈도우(`WS_EX_LAYERED`)는 WPF에서 동작하지 않아 이미 한 번 시도했다가
되돌린 길이다(README "불투명도와 둥근 모서리" 절에 전말). 기능 프로젝트가 직접 여는 다이얼로그
(`AddModuleDialog`, `PlcMonitorWindow` 등)는 이 저장소가 건드릴 수 없는 어셈블리 안에 있어
불투명하게 남는다 — 의도된 비대칭이다.

**설정 저장 위치.** 기능별 설정은 각자 다른 곳에 저장된다: PLC 서버는
`%AppData%\VirtualPlcServer\modules\index.json`(포팅 전 경로 그대로 유지, 기존 사용자 설정
호환을 위함), 런처 위치/불투명도는 `%AppData%\VisionSupport\launcher.json`, 이미지 변환기는
`%AppData%\VisionSupport\ImageConverter\settings.json`, 통신 모니터는
`D:\Datas\VisionSupport\Wireshark\settings.json`(CXP 진단 덤프도 같은 폴더). 일부 설정은 `D:\Datas`를 우선 읽고
없으면 옛 `%AppData%` 사본으로 폴백한다(커밋 c626971) — 경로를 다룰 때 이 우선순위를 깨지 않을 것.

## 설계 문서 관례

이 저장소는 새 기능을 넣기 전에 `docs/superpowers/specs/`에 설계 문서를, 진행할 때
`docs/superpowers/plans/`에 계획 문서를 남기는 관례를 쓰고 있다(파일명은
`YYYY-MM-DD-주제.md`). 기존 문서를 먼저 확인하고, 이 관례를 따르는 작업(브레인스토밍/계획
수립 스킬 사용 시)은 같은 위치에 문서를 남길 것.
