# 통신 모니터(VisionSupport.Wireshark) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 도구함에 "통신 모니터"를 추가해, VISION↔PLC 이더넷(MC/ADS)과 카메라(GigE Vision, Matrox Rapixo CXP)의 송수신과 끊김·지연·드롭을 Wireshark식 패킷 화면 + 한눈에 보이는 상태 카드 + 런처 빨간 링으로 보여준다.

**Architecture:** 새 클래스 라이브러리 `VisionSupport.Wireshark`가 캡처(pktmon → ETW 실시간), 해석(순수 바이트 파서), 판정(가상 시계로 테스트되는 상태 기계), 저장(원형 버퍼 + pcapng 내보내기), CXP 감시(GenTL 프로듀서 직접 호출, 폴링마다 열고 닫음 + WMI 장치 존재)를 담는다. 셸에는 `WiresharkFeature`와 `IFeatureModule.HasAlert`(런처 빨간 링)만 추가된다.

**Tech Stack:** .NET 9 WPF (net9.0-windows, x64), CommunityToolkit.Mvvm 8.4.2, Microsoft.Diagnostics.Tracing.TraceEvent 3.2.6, ScottPlot.WPF 5.1.59, System.Management 9.0.0, xunit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md`

## Global Constraints

- 모든 프로젝트 `net9.0-windows` / `<PlatformTarget>x64</PlatformTarget>` / `Nullable enable` / `ImplicitUsings enable` / `LangVersion latest`.
- 의존 방향은 셸 → 기능 한쪽뿐. `VisionSupport.Wireshark`는 `VisionSupport`(셸)를 참조하지 않는다.
- 패키지 버전은 메모리 모니터와 동일: CommunityToolkit.Mvvm `8.4.2`, TraceEvent `3.2.6`, ScottPlot.WPF `5.1.59`.
- 사용자에게 보이는 문구는 한국어. 카드 판정 문구는 스펙 표 그대로("응답 없음 4.2초째", "최근 1분 재전송 12회", "프레임 드롭 3장", "링크 끊김", "상세 불가 — 보드 연결 여부만 감시").
- 기본 임계값: 응답 타임아웃 1000ms, 응답 지연 200ms × 1분 5회, 조용함 5초, 재전송 1분 3회, 빨강 복귀 30초.
- 저장 상한: 원형 버퍼 200MB 또는 50만 개, 이상 전후 20개씩, 이상 이벤트 1000개, 차트 10분.
- 설정: `D:\Datas\VisionSupport\Wireshark\settings.json` 우선, 없으면 `%AppData%\VisionSupport\Wireshark\settings.json` 읽기. 저장은 항상 `D:\Datas` 쪽.
- ETW 세션 이름 `VisionSupport-Wireshark`. PktMon 프로바이더 GUID `4d4f80d9-c8bd-4d73-bb5b-19c90402c5ac`.
- `StopAsync`/`OnStopAsync` 경로는 예외를 셸로 내보내지 않는다(기존 `FeatureModule` 규약).
- 작업 트리에 이 기능과 무관한 미커밋 변경이 많다. **커밋은 항상 해당 작업의 파일만 경로를 지정해 `git add`** 한다(`git add -A`/`git commit -a` 금지).
- 테스트는 기존 단일 프로젝트 `tests/VisionSupport.Tests`에 추가한다.

## Review Focus

- **ETW 이벤트 누락이 프레임 드롭으로 오인되는 경우** — GigE 스트림 폭주로 ETW가 이벤트를 잃으면 GVSP 패킷 ID가 비어 보인다. 최근 10초 안에 캡처 누락이 있었다면 드롭은 빨강이 아니라 노랑 "프레임 드롭 의심 N장 (캡처 누락 동반)"이어야 한다. → Task 9 테스트.
- **이 도구가 먼저 떠 있을 때 VISION이 Rapixo를 못 여는 경우** — 감시가 GenTL 핸들을 폴링 사이에 쥐고 있으면 안 된다. 매 `Poll()` 후 세션이 닫혀야 한다. → Task 14 테스트.
- **잘린/깨진 프레임** — pkt-size 256으로 MC/ADS 페이로드가 잘리거나 쓰레기 바이트가 와도 해석기는 절대 던지지 않고, TCP 페이로드 길이는 캡처 길이가 아니라 IP 헤더 길이로 계산돼야 한다. → Task 4 테스트.
- **GVSP 16비트 블록 ID 순환** — 65535 다음 1은 정상이다(0은 무효 값). 드롭으로 세면 안 된다. → Task 9 테스트.
- **설정에서 고정됐지만 아직 한 번도 안 보인 대상** — 프로그램을 막 켰을 때 회색 "트래픽 없음"이어야지 빨강 "끊김"이면 안 된다. → Task 8 테스트.

---

## File Structure

```
src/VisionSupport.Wireshark/
  VisionSupport.Wireshark.csproj
  WiresharkView.xaml(.cs)             화면 (Task 16)
  Converters.cs                       HealthLevel→브러시 등 (Task 16)
  Dissect/
    ProtocolNode.cs  Packet.cs        패킷 모델·계층 트리 (Task 4)
    DissectorContext.cs  FrameDissector.cs   Ethernet/ARP/IPv4/TCP/UDP (Task 4)
    McDissector.cs                    MELSEC MC 3E/4E 바이너리 (Task 5)
    AdsDissector.cs                   ADS/AMS (Task 6)
    GigEDissector.cs                  GVCP/GVSP (Task 7)
  Health/
    HealthModel.cs                    enum·record·임계값 (Task 8)
    HealthTracker.cs                  대상별 상태 기계 (Task 8, 9에서 확장)
    TcpAnalyzer.cs  GvspAnalyzer.cs   (Task 9)
  Store/PacketStore.cs                (Task 10)
  Export/PcapngWriter.cs              (Task 11)
  Settings/WiresharkSettings.cs  WiresharkSettingsStore.cs   (Task 12)
  Capture/
    RawFrame.cs  Pktmon.cs  NicCatalog.cs  RecentPacketIds.cs  PktmonSource.cs   (Task 13)
  Cxp/
    CxpReport.cs                      (Task 9)
    GenApiNodeMap.cs                  최소 GenApi 레지스터 해석 (Task 14)
    GenApiXml.cs  GenTL.cs  IGenTLSession.cs  GenTLSession.cs
    RapixoPresence.cs  CxpMonitor.cs  CxpDiagnostics.cs   (Task 14)
  ViewModels/
    PacketFilter.cs  HexFormatter.cs  ChartHistory.cs  TargetCardViewModel.cs   (Task 15)
    WiresharkViewModel.cs             (Task 3 골격 → Task 15 완성)
src/VisionSupport/
  Features/IFeatureModule.cs  FeatureModule.cs   HasAlert (Task 2)
  Launcher/LauncherViewModel.cs  LauncherWindow.xaml   빨간 링 (Task 2)
  Features/WiresharkFeature.cs  App.xaml.cs  VisionSupport.csproj   (Task 3)
tests/VisionSupport.Tests/
  LauncherAlertTests.cs (2)  TestFrames.cs (4,5,6,7)  FrameDissectorTests.cs (4)
  McDissectorTests.cs (5)  AdsDissectorTests.cs (6)  GigEDissectorTests.cs (7)
  ManualClock.cs  HealthTrackerTests.cs (8)  HealthTrackerLinkTests.cs (9)
  PacketStoreTests.cs (10)  PcapngWriterTests.cs (11)  WiresharkSettingsTests.cs (12)
  PktmonParsingTests.cs (13)  GenApiNodeMapTests.cs  CxpMonitorTests.cs (14)
  WiresharkViewModelPartsTests.cs  WiresharkViewModelTests.cs (15)  WiresharkViewTests.cs (16)
```

---

### Task 1: pktmon 실시간 ETW 수신 실기 확인 (스파이크, 코드 버림)

스펙 10절 가정 1을 확인한다. 여기서 확인한 이벤트 필드 이름이 Task 13의 `PktmonSource.OnEvent`에 그대로 들어간다. **관리자 권한 PowerShell에서** 수행한다.

**Files:**
- Create (버림): `<scratchpad>/pktmon-probe/` 콘솔 프로젝트
- Modify: `docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md` 10절에 결과 기록

- [ ] **Step 1: 프로브 프로젝트 만들기**

```powershell
cd $env:TEMP; dotnet new console -n pktmon-probe -f net9.0; cd pktmon-probe
dotnet add package Microsoft.Diagnostics.Tracing.TraceEvent --version 3.2.6
```

`Program.cs`:

```csharp
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

var provider = new Guid("4d4f80d9-c8bd-4d73-bb5b-19c90402c5ac");
int shown = 0;
using var session = new TraceEventSession("pktmon-probe") { StopOnDispose = true };
session.EnableProvider(provider, TraceEventLevel.Verbose, ulong.MaxValue);
session.Source.Dynamic.All += e =>
{
    if (shown++ > 40) return;
    Console.WriteLine($"ID={(int)e.ID} Name={e.EventName} Fields=[{string.Join(", ", e.PayloadNames)}]");
    foreach (string name in e.PayloadNames)
    {
        object v = e.PayloadByName(name);
        Console.WriteLine($"   {name} = {(v is byte[] b ? $"byte[{b.Length}] {Convert.ToHexString(b.AsSpan(0, Math.Min(16, b.Length)))}" : v)}");
    }
};
var pump = new Thread(() => session.Source.Process()) { IsBackground = true };
pump.Start();
Console.WriteLine("Enter로 종료"); Console.ReadLine();
Console.WriteLine($"EventsLost={session.EventsLost}");
```

- [ ] **Step 2: 실행 순서대로 확인**

```powershell
pktmon comp list                       # 출력 원문을 저장 (Task 13 파서 테스트 입력)
pktmon filter remove
pktmon start --capture --comp nics --pkt-size 256 -m memory -s 16
dotnet run                             # 다른 창에서 ping 192.168.x.x 몇 번
pktmon stop
```

확인할 것:
1. 패킷 이벤트의 ID와 필드 이름이 `PktGroupId, PktNumber, AppearanceCount, DirTag, PacketType, ComponentId, EdgeId, FilterId, DropReason, DropLocation, OriginalPayloadSize, LoggedPayloadSize, Payload`인지. `PacketType` 이더넷 값이 1인지.
2. `--comp nics`에서 같은 패킷(같은 `PktGroupId`+`PktNumber`)이 몇 번 나오는지 (중복 제거 필요 여부).
3. `ComponentId`가 `pktmon comp list`의 NIC Id와 같은지.
4. `-m memory`로 시작해도 실시간 세션에 이벤트가 오는지. 안 오면 `-m real-time`을 시도하고, 그것도 안 되면 기본(circular) 모드로 시도.
5. GigE 카메라 PC라면: Matrox GigE 필터 드라이버(`mtxgigefilter`)가 있는 NIC에서 GVSP 패킷이 보이는지.

- [ ] **Step 3: 결과를 스펙 10절에 기록**

10절 항목 1 아래에 `**결과(YYYY-MM-DD):**` 줄을 추가하고 위 5개 질문의 답, 실제 필드 이름, 사용할 `pktmon start` 인자를 적는다. 필드 이름이나 인자가 다르면 **Task 13의 코드 블록을 그 값으로 고친 뒤** 진행한다. 2번이 "한 번만"이면 Task 13의 `RecentPacketIds`는 그대로 둔다(무해).

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md
git commit -m "docs: record what pktmon's live ETW stream actually delivers"
```

---

### Task 2: 셸 계약 — `HasAlert`와 런처 빨간 링

**Files:**
- Modify: `src/VisionSupport/Features/IFeatureModule.cs` (`IsWorking` 바로 아래)
- Modify: `src/VisionSupport/Features/FeatureModule.cs` (`IsWorking` 선언 아래)
- Modify: `src/VisionSupport/Launcher/LauncherViewModel.cs:195` 부근, `OnModuleChanged`
- Modify: `src/VisionSupport/Launcher/LauncherWindow.xaml:311-312` (초록 링 바로 아래)
- Test: `tests/VisionSupport.Tests/LauncherAlertTests.cs`

**Interfaces:**
- Produces: `bool IFeatureModule.HasAlert { get; }`, `public virtual bool FeatureModule.HasAlert => false`, `bool LauncherViewModel.AnyAlert`

- [ ] **Step 1: 실패하는 테스트**

```csharp
using System.Windows.Controls;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;
using Xunit;

namespace VisionSupport.Tests;

public class LauncherAlertTests
{
    private sealed class AlertingFeature : FeatureModule
    {
        public bool Alert { get; set; }
        public override string Title => "t";
        public override string Description => "d";
        public override bool IsWorking => false;
        public override bool HasAlert => Alert;
        public void Raise() => RaiseChanged();
        protected override UserControl CreateView() => throw new NotSupportedException();
        protected override Task OnStopAsync() => Task.CompletedTask;
    }

    private static LauncherViewModel LauncherWith(IFeatureModule tool)
    {
        var settings = new LauncherSettings();
        return new LauncherViewModel(Array.Empty<IFeatureModule>(), new[] { tool },
            new ActivityLog(), settings, new LauncherAppearance(settings), DateTime.Now);
    }

    [Fact]
    public void A_tool_raising_an_alert_turns_the_launcher_ring_red()
    {
        var tool = new AlertingFeature();
        LauncherViewModel launcher = LauncherWith(tool);
        var raised = new List<string?>();
        launcher.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(launcher.AnyAlert);
        tool.Alert = true;
        tool.Raise();

        Assert.True(launcher.AnyAlert);
        Assert.Contains(nameof(LauncherViewModel.AnyAlert), raised);
    }

    [Fact]
    public void Features_that_never_alert_default_to_false()
        => Assert.False(new MemoryMonitorFeature().HasAlert);
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~LauncherAlertTests"`
Expected: 빌드 실패 — `HasAlert`/`AnyAlert` 없음.

- [ ] **Step 3: 구현**

`IFeatureModule.cs`, `bool IsWorking { get; }` 블록 바로 아래:

```csharp
    /// <summary>
    /// Whether the feature has seen something the user should look at - a PLC that stopped
    /// answering, a camera dropping frames. Drives the red ring on the launcher icon, which has to
    /// say so even with every window closed. Changes are announced through <see cref="Changed"/>.
    /// </summary>
    bool HasAlert { get; }
```

`FeatureModule.cs`, `public abstract bool IsWorking { get; }` 아래:

```csharp
    public virtual bool HasAlert => false;
```

`LauncherViewModel.cs`, `AnyRunning` 아래:

```csharp
    /// <summary>Drives the red ring: some feature wants attention, even with every window closed.</summary>
    public bool AnyAlert => _modules.Any(m => m.HasAlert);
```

같은 파일 `OnModuleChanged`의 `OnPropertyChanged(nameof(AnyRunning));` 다음 줄:

```csharp
        OnPropertyChanged(nameof(AnyAlert));
```

`LauncherWindow.xaml`, 초록 링 `<Ellipse ... Visibility="{Binding AnyRunning, ...}"/>` 바로 아래:

```xml
            <!-- Drawn over the green ring, so "something is wrong" wins over "something is up". -->
            <Ellipse Stroke="{StaticResource StateFaulted}" StrokeThickness="2.5" Margin="1"
                     Visibility="{Binding AnyAlert, Converter={StaticResource BoolToVis}}"/>
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~LauncherAlertTests|FullyQualifiedName~LauncherIconTests|FullyQualifiedName~LauncherMenuTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/VisionSupport/Features/IFeatureModule.cs src/VisionSupport/Features/FeatureModule.cs src/VisionSupport/Launcher/LauncherViewModel.cs src/VisionSupport/Launcher/LauncherWindow.xaml tests/VisionSupport.Tests/LauncherAlertTests.cs
git commit -m "feat: let a feature turn the launcher ring red when it needs attention"
```

> 주의: `LauncherViewModel.cs`, `LauncherWindow.xaml.cs`에는 이 작업과 무관한 미커밋 변경이 이미 있다. `git add -p`로 이 작업의 hunk만 스테이징할 것(`LauncherWindow.xaml`은 미변경 상태이므로 그대로 add 가능).

---

### Task 3: 프로젝트 골격과 도구함 등록

**Files:**
- Create: `src/VisionSupport.Wireshark/VisionSupport.Wireshark.csproj`
- Create: `src/VisionSupport.Wireshark/ViewModels/WiresharkViewModel.cs` (골격, Task 15에서 전체 교체)
- Create: `src/VisionSupport.Wireshark/WiresharkView.xaml`, `WiresharkView.xaml.cs` (골격, Task 16에서 전체 교체)
- Create: `src/VisionSupport/Features/WiresharkFeature.cs`
- Modify: `src/VisionSupport/VisionSupport.csproj` (기능 ProjectReference 묶음)
- Modify: `src/VisionSupport/App.xaml.cs:127-131` (`CreateTools`)
- Modify: `VisionSupport.sln` (`dotnet sln add`)
- Modify: `tests/VisionSupport.Tests/VisionSupport.Tests.csproj`
- Modify: `tests/VisionSupport.Tests/LauncherIconTests.cs:26`

**Interfaces:**
- Produces: `VisionSupport.Wireshark.ViewModels.WiresharkViewModel` (`Status`, `IsWorking`, `HasAlert`, `Dispose()`), `VisionSupport.Wireshark.WiresharkView(WiresharkViewModel)`, `VisionSupport.Features.WiresharkFeature`

- [ ] **Step 1: 실패하는 테스트** — `LauncherIconTests.cs`의 도구 목록에 새 기능 추가

```csharp
            new IFeatureModule[] { new MemoryMonitorFeature(), new PlcServerFeature(), new WiresharkFeature() },
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~LauncherIconTests"`
Expected: 빌드 실패 — `WiresharkFeature` 없음.

- [ ] **Step 2: csproj**

`src/VisionSupport.Wireshark/VisionSupport.Wireshark.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <!-- Same pin as every other project: the shell is x64-only, and the GenTL producer this loads
         is the 64-bit one (GENICAM_GENTL64_PATH). -->
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>VisionSupport.Wireshark</AssemblyName>
    <RootNamespace>VisionSupport.Wireshark</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <!-- pktmon's packets arrive as ETW events; same package and version as the memory monitor. -->
    <PackageReference Include="Microsoft.Diagnostics.Tracing.TraceEvent" Version="3.2.6" />
    <PackageReference Include="ScottPlot.WPF" Version="5.1.59" />
    <!-- Whether the Rapixo board is present, from Win32_PnPEntity. -->
    <PackageReference Include="System.Management" Version="9.0.0" />
  </ItemGroup>

</Project>
```

```powershell
dotnet sln VisionSupport.sln add src\VisionSupport.Wireshark\VisionSupport.Wireshark.csproj
```

`VisionSupport.csproj`의 기능 ProjectReference 묶음 끝에:

```xml
    <ProjectReference Include="..\VisionSupport.Wireshark\VisionSupport.Wireshark.csproj" />
```

`tests/VisionSupport.Tests/VisionSupport.Tests.csproj`의 ProjectReference 묶음에:

```xml
    <ProjectReference Include="..\..\src\VisionSupport.Wireshark\VisionSupport.Wireshark.csproj" />
```

- [ ] **Step 3: 골격 ViewModel / View**

`ViewModels/WiresharkViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>Skeleton so the feature can be registered; Task 15 replaces this file.</summary>
public sealed partial class WiresharkViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _status = string.Empty;

    public bool IsWorking => false;

    public bool HasAlert => false;

    public void Dispose()
    {
    }
}
```

`WiresharkView.xaml`:

```xml
<UserControl x:Class="VisionSupport.Wireshark.WiresharkView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Bg}">
    <TextBlock Text="통신 모니터" Foreground="{StaticResource Fg}" Margin="16"/>
</UserControl>
```

`WiresharkView.xaml.cs`:

```csharp
using System.Windows.Controls;
using VisionSupport.Wireshark.ViewModels;

namespace VisionSupport.Wireshark;

public partial class WiresharkView : UserControl
{
    public WiresharkView(WiresharkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

- [ ] **Step 4: 셸 기능 클래스와 등록**

`src/VisionSupport/Features/WiresharkFeature.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using VisionSupport.Wireshark;
using VisionSupport.Wireshark.ViewModels;

namespace VisionSupport.Features;

/// <summary>
/// The communication monitor as a hosted feature.
///
/// Capture and CXP watching start and stop from the monitor's own toolbar; the shell only asks
/// whether either is running and whether anything is wrong. Stop is what matters for the shell's
/// health: it ends the ETW session and pktmon, because both outlive the process that started
/// them and a leaked capture keeps costing the machine until someone runs `pktmon stop`.
/// </summary>
public sealed class WiresharkFeature : FeatureModule
{
    private WiresharkViewModel? _viewModel;

    public override string Title => "통신 모니터";

    public override string Description
        => "PLC(MC·ADS)·GigE 이더넷과 CXP 카메라의 송수신과 끊김·지연·드롭을 감시합니다.";

    public override string Glyph => "LanConnect";

    public override Size PreferredWindowSize => new(1280, 860);

    public override string StatusLine => _viewModel?.Status ?? string.Empty;

    public override bool IsWorking => _viewModel is { IsWorking: true };

    public override bool HasAlert => _viewModel is { HasAlert: true };

    private WiresharkViewModel ViewModel
    {
        get
        {
            if (_viewModel is not null) return _viewModel;

            _viewModel = new WiresharkViewModel();
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(WiresharkViewModel.Status)
                    or nameof(WiresharkViewModel.IsWorking)
                    or nameof(WiresharkViewModel.HasAlert))
                {
                    RaiseChanged();
                }
            };
            return _viewModel;
        }
    }

    protected override UserControl CreateView() => new WiresharkView(ViewModel);

    protected override Task OnStopAsync()
    {
        // Dispose stops the capture (ETW session, then `pktmon stop`) and the CXP poll loop, and
        // saves settings. Safe to call twice.
        _viewModel?.Dispose();
        _viewModel = null;
        return Task.CompletedTask;
    }
}
```

`App.xaml.cs`의 `CreateTools()`:

```csharp
    private static IReadOnlyList<IFeatureModule> CreateTools() => new List<IFeatureModule>
    {
        new MemoryMonitorFeature(),
        new PlcServerFeature(),
        new WiresharkFeature(),
    };
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet build VisionSupport.sln` → 경고 외 성공
Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~LauncherIconTests"`
Expected: PASS (`LanConnect`가 `PackIconMaterialKind`에 있음)

- [ ] **Step 6: Commit**

```bash
git add src/VisionSupport.Wireshark VisionSupport.sln src/VisionSupport/VisionSupport.csproj src/VisionSupport/App.xaml.cs src/VisionSupport/Features/WiresharkFeature.cs tests/VisionSupport.Tests/VisionSupport.Tests.csproj tests/VisionSupport.Tests/LauncherIconTests.cs
git commit -m "feat: add the communication monitor to the tools box"
```

---

### Task 4: 패킷 모델과 Ethernet/ARP/IPv4/TCP/UDP 해석

**Files:**
- Create: `src/VisionSupport.Wireshark/Dissect/ProtocolNode.cs`
- Create: `src/VisionSupport.Wireshark/Dissect/Packet.cs`
- Create: `src/VisionSupport.Wireshark/Dissect/DissectorContext.cs`
- Create: `src/VisionSupport.Wireshark/Dissect/FrameDissector.cs`
- Test: `tests/VisionSupport.Tests/TestFrames.cs`, `tests/VisionSupport.Tests/FrameDissectorTests.cs`

**Interfaces:**
- Produces:
  - `ProtocolNode(string text, int offset, int length)`; `Text`, `Offset`, `Length`, `List<ProtocolNode> Children`, `ProtocolNode Add(string text, int offset, int length)`
  - `Packet(long number, DateTime time, byte[] data, int originalLength)` with settable `Source, Destination, Protocol, Info, SrcIp, DstIp, Tcp, Udp, App, Gvsp, TargetId, IsAnomalous`, `List<ProtocolNode> Layers`
  - `enum TcpFlags : byte { Fin=1, Syn=2, Rst=4, Psh=8, Ack=16, Urg=32 }`
  - `record struct TcpInfo(ushort SrcPort, ushort DstPort, uint Seq, uint Ack, TcpFlags Flags, ushort Window, int PayloadLength)`
  - `record struct UdpInfo(ushort SrcPort, ushort DstPort)`
  - `enum AppKind { Mc, Ads, Gvcp }`, `enum MessageRole { Request, Response, Unsolicited }`
  - `record AppMessage(AppKind Kind, MessageRole Role, uint? CorrelationId, string Summary, bool IsError)`
  - `enum GvspFormat : byte { Unknown=0, Leader=1, Trailer=2, Payload=3, AllIn=4, H264=5, MultiZone=6 }`
  - `record GvspHeader(ulong BlockId, uint PacketId, GvspFormat Format, ushort Status, bool ExtendedId)`
  - `DissectorContext { int McPortMin=5000; int McPortMax=5010; int AdsPort=48898; bool IsMcPort(int); void LearnCamera(IPAddress); bool IsCamera(IPAddress) }`
  - `static Packet FrameDissector.Dissect(long number, DateTime time, byte[] frame, int originalLength, DissectorContext context)`
  - Task 5/6/7이 채울 훅: `McDissector.TryDissect`, `AdsDissector.TryDissect`, `GigEDissector.TryDissectGvcp/TryDissectGvsp` — 이 작업에서는 FrameDissector가 호출하지 않는다(각 작업에서 한 줄씩 연결).

- [ ] **Step 1: 테스트용 프레임 빌더**

`tests/VisionSupport.Tests/TestFrames.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Tests;

/// <summary>Builds Ethernet/IPv4 frames byte by byte, so dissector tests read like the wire.</summary>
internal static class TestFrames
{
    public static readonly DateTime T0 = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    public static byte[] Tcp(string src, int srcPort, string dst, int dstPort, byte[]? payload = null,
        uint seq = 1000, uint ack = 1, TcpFlags flags = TcpFlags.Ack | TcpFlags.Psh, ushort window = 8192)
    {
        payload ??= Array.Empty<byte>();
        var tcp = new byte[20 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(0), (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.AsSpan(4), seq);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.AsSpan(8), ack);
        tcp[12] = 5 << 4;
        tcp[13] = (byte)flags;
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(14), window);
        payload.CopyTo(tcp, 20);
        return Ip(src, dst, 6, tcp);
    }

    public static byte[] Udp(string src, int srcPort, string dst, int dstPort, byte[] payload)
    {
        var udp = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(0), (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(4), (ushort)udp.Length);
        payload.CopyTo(udp, 8);
        return Ip(src, dst, 17, udp);
    }

    public static byte[] Arp(string senderIp, string targetIp)
    {
        var frame = new byte[14 + 28];
        for (int i = 0; i < 6; i++) { frame[i] = 0xFF; frame[6 + i] = 0x22; }
        frame[12] = 0x08; frame[13] = 0x06;
        Span<byte> arp = frame.AsSpan(14);
        arp[1] = 1; arp[2] = 0x08; arp[4] = 6; arp[5] = 4; arp[7] = 1; // request
        IPAddress.Parse(senderIp).GetAddressBytes().CopyTo(arp[14..]);
        IPAddress.Parse(targetIp).GetAddressBytes().CopyTo(arp[24..]);
        return frame;
    }

    private static byte[] Ip(string src, string dst, byte protocol, byte[] l4)
    {
        var frame = new byte[14 + 20 + l4.Length];
        for (int i = 0; i < 6; i++) { frame[i] = 0x11; frame[6 + i] = 0x22; }
        frame[12] = 0x08; frame[13] = 0x00;
        Span<byte> ip = frame.AsSpan(14);
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip[2..], (ushort)(20 + l4.Length));
        ip[8] = 64;
        ip[9] = protocol;
        IPAddress.Parse(src).GetAddressBytes().CopyTo(ip[12..]);
        IPAddress.Parse(dst).GetAddressBytes().CopyTo(ip[16..]);
        l4.CopyTo(ip[20..]);
        return frame;
    }

    public static Packet Dissect(byte[] frame, DissectorContext? context = null, long number = 1,
        DateTime? time = null, int? originalLength = null)
        => FrameDissector.Dissect(number, time ?? T0, frame, originalLength ?? frame.Length,
            context ?? new DissectorContext());
}
```

- [ ] **Step 2: 실패하는 테스트**

`tests/VisionSupport.Tests/FrameDissectorTests.cs`:

```csharp
using System.Net;
using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class FrameDissectorTests
{
    [Fact]
    public void A_tcp_segment_yields_endpoints_flags_and_payload_length()
    {
        byte[] frame = TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 8080, new byte[] { 1, 2, 3 },
            seq: 77, flags: TcpFlags.Ack | TcpFlags.Psh, window: 512);

        Packet p = TestFrames.Dissect(frame);

        Assert.Equal("TCP", p.Protocol);
        Assert.Equal("192.168.0.2:50000", p.Source);
        Assert.Equal("192.168.0.10:8080", p.Destination);
        Assert.Equal(IPAddress.Parse("192.168.0.10"), p.DstIp);
        Assert.Equal(new TcpInfo(50000, 8080, 77, 1, TcpFlags.Ack | TcpFlags.Psh, 512, 3), p.Tcp);
        Assert.Equal(new[] { "Ethernet II", "IPv4", "TCP" }, p.Layers.Select(l => l.Text.Split(',')[0]));
    }

    [Fact]
    public void A_udp_datagram_yields_ports()
    {
        Packet p = TestFrames.Dissect(TestFrames.Udp("10.0.0.1", 1234, "10.0.0.2", 9999, new byte[4]));

        Assert.Equal("UDP", p.Protocol);
        Assert.Equal(new UdpInfo(1234, 9999), p.Udp);
    }

    [Fact]
    public void An_arp_request_reads_as_a_question()
    {
        Packet p = TestFrames.Dissect(TestFrames.Arp("192.168.0.2", "192.168.0.10"));

        Assert.Equal("ARP", p.Protocol);
        Assert.Equal("192.168.0.10 은(는) 누구? 192.168.0.2 에게 알려줘", p.Info);
    }

    /// <summary>pktmon cuts each packet at --pkt-size. The TCP payload length must still be the
    /// real one, from the IP header, or every truncated segment would look like a retransmission.</summary>
    [Fact]
    public void Payload_length_comes_from_the_ip_header_when_the_capture_is_truncated()
    {
        byte[] full = TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 8080, new byte[1000]);
        byte[] cut = full.AsSpan(0, 60).ToArray();

        Packet p = TestFrames.Dissect(cut, originalLength: full.Length);

        Assert.Equal(1000, p.Tcp!.Value.PayloadLength);
        Assert.Equal(full.Length, p.OriginalLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(20)]
    [InlineData(40)]
    public void Short_or_garbage_frames_never_throw(int length)
    {
        var junk = new byte[length];
        new Random(length).NextBytes(junk);
        if (length >= 14) { junk[12] = 0x08; junk[13] = 0x00; junk[14 % length] = 0x4F; }

        Packet p = TestFrames.Dissect(junk);

        Assert.NotNull(p.Protocol);
    }
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~FrameDissectorTests"`
Expected: 빌드 실패 — 타입 없음.

- [ ] **Step 3: 모델 구현**

`Dissect/ProtocolNode.cs`:

```csharp
namespace VisionSupport.Wireshark.Dissect;

/// <summary>One line of the protocol tree, with the bytes it covers so the hex view can point at them.</summary>
public sealed class ProtocolNode
{
    public ProtocolNode(string text, int offset, int length)
    {
        Text = text;
        Offset = offset;
        Length = length;
    }

    public string Text { get; }

    public int Offset { get; }

    public int Length { get; }

    public List<ProtocolNode> Children { get; } = new();

    public ProtocolNode Add(string text, int offset, int length)
    {
        var child = new ProtocolNode(text, offset, length);
        Children.Add(child);
        return child;
    }
}
```

`Dissect/Packet.cs`:

```csharp
using System.Net;

namespace VisionSupport.Wireshark.Dissect;

[Flags]
public enum TcpFlags : byte { Fin = 1, Syn = 2, Rst = 4, Psh = 8, Ack = 16, Urg = 32 }

public readonly record struct TcpInfo(ushort SrcPort, ushort DstPort, uint Seq, uint Ack,
    TcpFlags Flags, ushort Window, int PayloadLength);

public readonly record struct UdpInfo(ushort SrcPort, ushort DstPort);

public enum AppKind { Mc, Ads, Gvcp }

public enum MessageRole { Request, Response, Unsolicited }

/// <summary>What the application layer said, reduced to what the health tracker needs.</summary>
public sealed record AppMessage(AppKind Kind, MessageRole Role, uint? CorrelationId, string Summary, bool IsError);

public enum GvspFormat : byte { Unknown = 0, Leader = 1, Trailer = 2, Payload = 3, AllIn = 4, H264 = 5, MultiZone = 6 }

public sealed record GvspHeader(ulong BlockId, uint PacketId, GvspFormat Format, ushort Status, bool ExtendedId);

/// <summary>
/// One captured frame and everything read out of it. The list columns are plain strings so the
/// view never re-parses; the typed parts are what the health tracker reads.
/// </summary>
public sealed class Packet
{
    public Packet(long number, DateTime time, byte[] data, int originalLength)
    {
        Number = number;
        Time = time;
        Data = data;
        OriginalLength = originalLength;
    }

    public long Number { get; }

    public DateTime Time { get; }

    /// <summary>The captured bytes - possibly cut short at pktmon's --pkt-size.</summary>
    public byte[] Data { get; }

    /// <summary>The frame's length on the wire.</summary>
    public int OriginalLength { get; }

    public string Source { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string Protocol { get; set; } = "Ethernet";

    public string Info { get; set; } = string.Empty;

    public IPAddress? SrcIp { get; set; }

    public IPAddress? DstIp { get; set; }

    public TcpInfo? Tcp { get; set; }

    public UdpInfo? Udp { get; set; }

    public AppMessage? App { get; set; }

    public GvspHeader? Gvsp { get; set; }

    public List<ProtocolNode> Layers { get; } = new();

    /// <summary>Which watched target this belongs to; set by the health tracker.</summary>
    public string? TargetId { get; set; }

    /// <summary>Set by the health tracker when this packet is evidence of a problem.</summary>
    public bool IsAnomalous { get; set; }
}
```

`Dissect/DissectorContext.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// What the dissector needs to know beyond the bytes: which ports mean MC, and which hosts are
/// GigE cameras. GVSP has no fixed port, so a UDP datagram is only read as video once its sender
/// has answered on GVCP - that is how cameras get learned.
/// </summary>
public sealed class DissectorContext
{
    public const int GvcpPort = 3956;

    private readonly ConcurrentDictionary<IPAddress, byte> _cameras = new();

    public int McPortMin { get; init; } = 5000;

    public int McPortMax { get; init; } = 5010;

    public int AdsPort { get; init; } = 48898;

    public bool IsMcPort(int port) => port >= McPortMin && port <= McPortMax;

    public void LearnCamera(IPAddress address) => _cameras.TryAdd(address, 0);

    public bool IsCamera(IPAddress address) => _cameras.ContainsKey(address);
}
```

- [ ] **Step 4: FrameDissector 구현**

`Dissect/FrameDissector.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// Bytes to <see cref="Packet"/>. Runs on the capture thread for every frame, so it never throws:
/// a frame it cannot read becomes a row that says so.
/// </summary>
public static class FrameDissector
{
    public static Packet Dissect(long number, DateTime time, byte[] frame, int originalLength, DissectorContext context)
    {
        var packet = new Packet(number, time, frame, originalLength);
        try
        {
            DissectEthernet(packet, context);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            packet.Info = "해석 실패: " + ex.GetType().Name;
        }
        return packet;
    }

    private static void DissectEthernet(Packet packet, DissectorContext context)
    {
        byte[] f = packet.Data;
        if (f.Length < 14)
        {
            packet.Info = $"잘린 프레임 ({f.Length} 바이트)";
            return;
        }

        int offset = 12;
        ushort type = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(offset));
        if (type == 0x8100 && f.Length >= 18)
        {
            offset += 4;
            type = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(offset));
        }
        int l3 = offset + 2;

        string dstMac = Mac(f.AsSpan(0, 6)), srcMac = Mac(f.AsSpan(6, 6));
        packet.Source = srcMac;
        packet.Destination = dstMac;
        var eth = new ProtocolNode($"Ethernet II, {srcMac} → {dstMac}", 0, l3);
        eth.Add($"유형: 0x{type:X4}", offset, 2);
        packet.Layers.Add(eth);

        switch (type)
        {
            case 0x0806: DissectArp(packet, l3); break;
            case 0x0800: DissectIPv4(packet, l3, context); break;
            default: packet.Info = $"EtherType 0x{type:X4}"; break;
        }
    }

    private static void DissectArp(Packet packet, int at)
    {
        byte[] f = packet.Data;
        packet.Protocol = "ARP";
        if (f.Length < at + 28)
        {
            packet.Info = "ARP (잘림)";
            return;
        }

        ushort op = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 6));
        var sender = new IPAddress(f.AsSpan(at + 14, 4));
        var target = new IPAddress(f.AsSpan(at + 24, 4));
        packet.Info = op == 1
            ? $"{target} 은(는) 누구? {sender} 에게 알려줘"
            : $"{sender} 은(는) {Mac(f.AsSpan(at + 8, 6))}";
        var node = new ProtocolNode($"ARP, {(op == 1 ? "요청" : "응답")}", at, 28);
        node.Add($"보낸 쪽: {sender}", at + 14, 4);
        node.Add($"대상: {target}", at + 24, 4);
        packet.Layers.Add(node);
    }

    private static void DissectIPv4(Packet packet, int at, DissectorContext context)
    {
        byte[] f = packet.Data;
        packet.Protocol = "IPv4";
        if (f.Length < at + 20)
        {
            packet.Info = "IPv4 (잘림)";
            return;
        }

        int headerLength = (f[at] & 0x0F) * 4;
        int totalLength = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 2));
        ushort fragment = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 6));
        byte protocol = f[at + 9];
        var src = new IPAddress(f.AsSpan(at + 12, 4));
        var dst = new IPAddress(f.AsSpan(at + 16, 4));
        packet.SrcIp = src;
        packet.DstIp = dst;
        packet.Source = src.ToString();
        packet.Destination = dst.ToString();

        var node = new ProtocolNode($"IPv4, {src} → {dst}", at, headerLength);
        node.Add($"전체 길이: {totalLength}", at + 2, 2);
        node.Add($"프로토콜: {protocol}", at + 9, 1);
        packet.Layers.Add(node);

        if ((fragment & 0x1FFF) != 0 || headerLength < 20)
        {
            packet.Info = "IPv4 조각";
            return;
        }

        int l4 = at + headerLength;
        // What the sender put on the wire after the IP header, not what pktmon kept of it.
        int l4Length = Math.Max(0, totalLength - headerLength);
        switch (protocol)
        {
            case 6: DissectTcp(packet, l4, l4Length, context); break;
            case 17: DissectUdp(packet, l4, context); break;
            default: packet.Info = $"IP 프로토콜 {protocol}"; break;
        }
    }

    private static void DissectTcp(Packet packet, int at, int segmentLength, DissectorContext context)
    {
        byte[] f = packet.Data;
        packet.Protocol = "TCP";
        if (f.Length < at + 20)
        {
            packet.Info = "TCP (잘림)";
            return;
        }

        ushort sp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at));
        ushort dp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 2));
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at + 4));
        uint ack = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at + 8));
        int headerLength = (f[at + 12] >> 4) * 4;
        var flags = (TcpFlags)(f[at + 13] & 0x3F);
        ushort window = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 14));
        int payloadLength = Math.Max(0, segmentLength - headerLength);

        packet.Tcp = new TcpInfo(sp, dp, seq, ack, flags, window, payloadLength);
        packet.Source = $"{packet.SrcIp}:{sp}";
        packet.Destination = $"{packet.DstIp}:{dp}";
        packet.Info = $"{sp} → {dp} [{FlagText(flags)}] Seq={seq} Ack={ack} Win={window} Len={payloadLength}";

        var node = new ProtocolNode($"TCP, {sp} → {dp}, Len={payloadLength}", at, headerLength);
        node.Add($"순서 번호: {seq}", at + 4, 4);
        node.Add($"확인 번호: {ack}", at + 8, 4);
        node.Add($"플래그: {FlagText(flags)}", at + 13, 1);
        node.Add($"윈도우: {window}", at + 14, 2);
        packet.Layers.Add(node);

        int payloadAt = at + headerLength;
        if (payloadLength == 0 || payloadAt >= f.Length) return;
        ReadOnlySpan<byte> payload = f.AsSpan(payloadAt);
        // Application dissectors are wired in by later tasks:
        // MC (Task 5), ADS (Task 6).
        _ = payload;
        _ = context;
    }

    private static void DissectUdp(Packet packet, int at, DissectorContext context)
    {
        byte[] f = packet.Data;
        packet.Protocol = "UDP";
        if (f.Length < at + 8)
        {
            packet.Info = "UDP (잘림)";
            return;
        }

        ushort sp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at));
        ushort dp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 2));
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 4));
        packet.Udp = new UdpInfo(sp, dp);
        packet.Source = $"{packet.SrcIp}:{sp}";
        packet.Destination = $"{packet.DstIp}:{dp}";
        packet.Info = $"{sp} → {dp} Len={Math.Max(0, length - 8)}";
        packet.Layers.Add(new ProtocolNode($"UDP, {sp} → {dp}", at, 8));

        int payloadAt = at + 8;
        if (payloadAt >= f.Length) return;
        ReadOnlySpan<byte> payload = f.AsSpan(payloadAt);
        // GVCP/GVSP are wired in by Task 7.
        _ = payload;
        _ = context;
    }

    private static string FlagText(TcpFlags flags)
    {
        var names = new List<string>(6);
        if (flags.HasFlag(TcpFlags.Syn)) names.Add("SYN");
        if (flags.HasFlag(TcpFlags.Fin)) names.Add("FIN");
        if (flags.HasFlag(TcpFlags.Rst)) names.Add("RST");
        if (flags.HasFlag(TcpFlags.Psh)) names.Add("PSH");
        if (flags.HasFlag(TcpFlags.Ack)) names.Add("ACK");
        if (flags.HasFlag(TcpFlags.Urg)) names.Add("URG");
        return string.Join(", ", names);
    }

    private static string Mac(ReadOnlySpan<byte> bytes)
        => string.Join(":", bytes.ToArray().Select(b => b.ToString("X2")));
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~FrameDissectorTests"`
Expected: PASS (7 tests)

- [ ] **Step 6: Commit**

```bash
git add src/VisionSupport.Wireshark/Dissect tests/VisionSupport.Tests/TestFrames.cs tests/VisionSupport.Tests/FrameDissectorTests.cs
git commit -m "feat(wireshark): dissect Ethernet, ARP, IPv4, TCP and UDP"
```

---

### Task 5: MELSEC MC 3E/4E 바이너리 해석

**Files:**
- Create: `src/VisionSupport.Wireshark/Dissect/McDissector.cs`
- Modify: `src/VisionSupport.Wireshark/Dissect/FrameDissector.cs` (`DissectTcp`의 훅 주석 자리)
- Modify: `tests/VisionSupport.Tests/TestFrames.cs` (MC 샘플 추가)
- Test: `tests/VisionSupport.Tests/McDissectorTests.cs`

**Interfaces:**
- Consumes: Task 4 `Packet`, `ProtocolNode`, `AppMessage`, `DissectorContext.IsMcPort`
- Produces: `static bool McDissector.TryDissect(ReadOnlySpan<byte> payload, int offset, Packet packet)`; `TestFrames.McReadRequest3E`, `McOkResponse3E`, `McErrorResponse3E`, `McReadRequest4E(ushort serial)`, `McOkResponse4E(ushort serial)`
- 역할 규약: 3E는 `CorrelationId = null`(같은 연결 안에서 FIFO로 짝지음), 4E는 `CorrelationId = 시리얼 번호`.

- [ ] **Step 1: 샘플 추가 (TestFrames 클래스 안)**

```csharp
    // D100부터 10워드 일괄 읽기 (0401/0000), 3E 바이너리.
    public static readonly byte[] McReadRequest3E =
    {
        0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0C, 0x00, 0x10, 0x00,
        0x01, 0x04, 0x00, 0x00, 0x64, 0x00, 0x00, 0xA8, 0x0A, 0x00,
    };

    public static readonly byte[] McOkResponse3E =
        { 0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00 };

    public static readonly byte[] McErrorResponse3E =
        { 0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x02, 0x00, 0x59, 0xC0 };

    public static byte[] McReadRequest4E(ushort serial)
        => new byte[] { 0x54, 0x00, (byte)serial, (byte)(serial >> 8), 0x00, 0x00 }
            .Concat(McReadRequest3E.Skip(2)).ToArray();

    public static byte[] McOkResponse4E(ushort serial)
        => new byte[] { 0xD4, 0x00, (byte)serial, (byte)(serial >> 8), 0x00, 0x00 }
            .Concat(McOkResponse3E.Skip(2)).ToArray();
```

- [ ] **Step 2: 실패하는 테스트**

`tests/VisionSupport.Tests/McDissectorTests.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class McDissectorTests
{
    private static Packet Over5000(byte[] payload, bool toPlc = true) => TestFrames.Dissect(toPlc
        ? TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, payload)
        : TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, payload));

    [Fact]
    public void A_3E_batch_read_is_a_request_without_a_serial()
    {
        Packet p = Over5000(TestFrames.McReadRequest3E);

        Assert.Equal("MC", p.Protocol);
        Assert.Equal(new AppMessage(AppKind.Mc, MessageRole.Request, null, "MC 3E 요청 0401 일괄 읽기", false), p.App);
        Assert.Contains(p.Layers, l => l.Text.StartsWith("MELSEC MC 3E"));
    }

    [Fact]
    public void A_3E_response_with_end_code_zero_is_normal()
    {
        Packet p = Over5000(TestFrames.McOkResponse3E, toPlc: false);

        Assert.Equal(MessageRole.Response, p.App!.Role);
        Assert.False(p.App.IsError);
        Assert.Equal("MC 3E 응답 정상", p.Info);
    }

    [Fact]
    public void A_nonzero_end_code_is_an_error()
    {
        Packet p = Over5000(TestFrames.McErrorResponse3E, toPlc: false);

        Assert.True(p.App!.IsError);
        Assert.Equal("MC 3E 응답 이상 종료코드 C059", p.Info);
    }

    [Fact]
    public void A_4E_frame_carries_its_serial_as_the_correlation_id()
    {
        Assert.Equal(0x1234u, Over5000(TestFrames.McReadRequest4E(0x1234)).App!.CorrelationId);
        Assert.Equal(0x1234u, Over5000(TestFrames.McOkResponse4E(0x1234), toPlc: false).App!.CorrelationId);
    }

    [Fact]
    public void Other_bytes_on_an_mc_port_stay_plain_tcp()
    {
        Packet p = Over5000(new byte[] { 0x01, 0x02, 0x03 });

        Assert.Equal("TCP", p.Protocol);
        Assert.Null(p.App);
    }

    [Fact]
    public void Mc_bytes_on_a_port_outside_the_range_are_not_read_as_mc()
        => Assert.Null(TestFrames.Dissect(
            TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 8080, TestFrames.McReadRequest3E)).App);
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~McDissectorTests"`
Expected: 빌드 실패 — `McDissector` 없음 → 구현 후 테스트 실패 순서.

- [ ] **Step 3: 구현**

`Dissect/McDissector.cs`:

```csharp
using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// MELSEC MC protocol, 3E and 4E binary frames. Only the header is read: who asked what, and
/// whether the answer's end code was zero. Device addresses and data stay in the hex view.
/// </summary>
public static class McDissector
{
    public static bool TryDissect(ReadOnlySpan<byte> p, int offset, Packet packet)
    {
        if (p.Length < 2) return false;

        bool is4E, request;
        switch ((p[0], p[1]))
        {
            case (0x50, 0x00): is4E = false; request = true; break;
            case (0xD0, 0x00): is4E = false; request = false; break;
            case (0x54, 0x00): is4E = true; request = true; break;
            case (0xD4, 0x00): is4E = true; request = false; break;
            default: return false;
        }

        // b is where the access route (network no.) starts.
        int b = is4E ? 6 : 2;
        int needed = request ? b + 11 : b + 9;
        if (p.Length < needed) return false;

        string frame = is4E ? "4E" : "3E";
        uint? serial = is4E ? BinaryPrimitives.ReadUInt16LittleEndian(p[2..]) : null;
        var node = new ProtocolNode($"MELSEC MC {frame} 바이너리 {(request ? "요청" : "응답")}", offset, p.Length);
        if (is4E) node.Add($"시리얼 번호: {serial}", offset + 2, 2);
        node.Add($"네트워크 {p[b]} / PC {p[b + 1]} / 국번 {p[b + 4]}", offset + b, 5);
        node.Add($"데이터 길이: {BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 5)..])}", offset + b + 5, 2);

        string summary;
        bool isError;
        if (request)
        {
            ushort command = BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 9)..]);
            string name = CommandName(command);
            node.Add($"감시 타이머: {BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 7)..])}", offset + b + 7, 2);
            node.Add($"커맨드: {command:X4} {name}", offset + b + 9, 2);
            if (p.Length >= b + 13)
            {
                node.Add($"서브커맨드: {BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 11)..]):X4}", offset + b + 11, 2);
            }
            summary = $"MC {frame} 요청 {command:X4} {name}".TrimEnd();
            isError = false;
        }
        else
        {
            ushort endCode = BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 7)..]);
            isError = endCode != 0;
            node.Add($"종료 코드: {endCode:X4} {(isError ? "(이상)" : "(정상)")}", offset + b + 7, 2);
            summary = isError ? $"MC {frame} 응답 이상 종료코드 {endCode:X4}" : $"MC {frame} 응답 정상";
        }

        packet.Layers.Add(node);
        packet.Protocol = "MC";
        packet.Info = summary;
        packet.App = new AppMessage(AppKind.Mc, request ? MessageRole.Request : MessageRole.Response,
            serial, summary, isError);
        return true;
    }

    private static string CommandName(ushort command) => command switch
    {
        0x0401 => "일괄 읽기",
        0x1401 => "일괄 쓰기",
        0x0403 => "랜덤 읽기",
        0x1402 => "랜덤 쓰기",
        0x0406 => "블록 읽기",
        0x1406 => "블록 쓰기",
        0x0619 => "루프백",
        _ => string.Empty,
    };
}
```

`FrameDissector.DissectTcp`의 훅 주석 3줄(`// Application dissectors are wired in...` ~ `_ = context;`)을 다음으로 교체:

```csharp
        if (context.IsMcPort(dp) || context.IsMcPort(sp))
        {
            McDissector.TryDissect(payload, payloadAt, packet);
        }
        // ADS is wired in by Task 6.
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~McDissectorTests|FullyQualifiedName~FrameDissectorTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/VisionSupport.Wireshark/Dissect tests/VisionSupport.Tests/TestFrames.cs tests/VisionSupport.Tests/McDissectorTests.cs
git commit -m "feat(wireshark): read MC 3E/4E binary request and response headers"
```

---

### Task 6: ADS/AMS 해석

**Files:**
- Create: `src/VisionSupport.Wireshark/Dissect/AdsDissector.cs`
- Modify: `src/VisionSupport.Wireshark/Dissect/FrameDissector.cs` (`// ADS is wired in by Task 6.` 줄)
- Modify: `tests/VisionSupport.Tests/TestFrames.cs` (`Ads(...)` 빌더 추가)
- Test: `tests/VisionSupport.Tests/AdsDissectorTests.cs`

**Interfaces:**
- Produces: `static bool AdsDissector.TryDissect(ReadOnlySpan<byte> payload, int offset, Packet packet)`; `TestFrames.Ads(ushort command, bool response, uint invokeId, uint error = 0, uint result = 0)`
- 역할 규약: 명령 8(Device Notification)은 `Unsolicited`/`CorrelationId=null`, 그 외 `CorrelationId = invoke id`.

- [ ] **Step 1: 빌더 추가 (TestFrames 클래스 안)**

```csharp
    /// <summary>AMS/TCP header + AMS header (+ 4-byte ADS result on responses).</summary>
    public static byte[] Ads(ushort command, bool response, uint invokeId, uint error = 0, uint result = 0)
    {
        int data = response ? 4 : 0;
        var p = new byte[6 + 32 + data];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), (uint)(32 + data));
        byte[] target = { 5, 1, 2, 3, 1, 1 }, source = { 192, 168, 0, 2, 1, 1 };
        (response ? source : target).CopyTo(p, 6);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12), (ushort)(response ? 30000 : 851));
        (response ? target : source).CopyTo(p, 14);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(20), (ushort)(response ? 851 : 30000));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(22), command);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(24), (ushort)(response ? 0x0005 : 0x0004));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(26), (uint)data);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(30), error);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(34), invokeId);
        if (response) BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(38), result);
        return p;
    }
```

- [ ] **Step 2: 실패하는 테스트**

`tests/VisionSupport.Tests/AdsDissectorTests.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class AdsDissectorTests
{
    private static Packet Ads(byte[] payload, bool toPlc) => TestFrames.Dissect(toPlc
        ? TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.20", 48898, payload)
        : TestFrames.Tcp("192.168.0.20", 48898, "192.168.0.2", 50000, payload));

    [Fact]
    public void A_read_request_is_correlated_by_invoke_id()
    {
        Packet p = Ads(TestFrames.Ads(2, response: false, invokeId: 7), toPlc: true);

        Assert.Equal("ADS", p.Protocol);
        Assert.Equal(new AppMessage(AppKind.Ads, MessageRole.Request, 7u, "ADS Read 요청 #7", false), p.App);
    }

    [Fact]
    public void A_response_with_a_nonzero_result_is_an_error()
    {
        Packet p = Ads(TestFrames.Ads(2, response: true, invokeId: 7, result: 0x710), toPlc: false);

        Assert.Equal(MessageRole.Response, p.App!.Role);
        Assert.True(p.App.IsError);
        Assert.Equal("ADS Read 응답 오류 0x710", p.Info);
    }

    [Fact]
    public void A_device_notification_is_unsolicited()
    {
        Packet p = Ads(TestFrames.Ads(8, response: false, invokeId: 0), toPlc: false);

        Assert.Equal(MessageRole.Unsolicited, p.App!.Role);
        Assert.Null(p.App.CorrelationId);
    }

    [Fact]
    public void A_truncated_ams_header_stays_plain_tcp()
        => Assert.Null(Ads(TestFrames.Ads(2, false, 1).AsSpan(0, 20).ToArray(), toPlc: true).App);
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~AdsDissectorTests"` → 실패 확인.

- [ ] **Step 3: 구현**

`Dissect/AdsDissector.cs`:

```csharp
using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// Beckhoff ADS over AMS/TCP (port 48898): a 6-byte AMS/TCP header, then the 32-byte AMS header.
/// The invoke id pairs a response with its request; a device notification answers nothing.
/// </summary>
public static class AdsDissector
{
    private const int AmsHeader = 6;

    public static bool TryDissect(ReadOnlySpan<byte> p, int offset, Packet packet)
    {
        if (p.Length < AmsHeader + 32 || p[0] != 0 || p[1] != 0) return false;

        ReadOnlySpan<byte> h = p[AmsHeader..];
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(h[16..]);
        ushort stateFlags = BinaryPrimitives.ReadUInt16LittleEndian(h[18..]);
        uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(h[20..]);
        uint error = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
        uint invokeId = BinaryPrimitives.ReadUInt32LittleEndian(h[28..]);
        bool response = (stateFlags & 0x0001) != 0;

        MessageRole role = command == 8 ? MessageRole.Unsolicited
            : response ? MessageRole.Response : MessageRole.Request;
        uint result = response && command != 8 && dataLength >= 4 && h.Length >= 36
            ? BinaryPrimitives.ReadUInt32LittleEndian(h[32..])
            : 0;
        bool isError = error != 0 || result != 0;
        string name = CommandName(command);

        var node = new ProtocolNode($"ADS/AMS, {name} {(role == MessageRole.Request ? "요청" : role == MessageRole.Response ? "응답" : "알림")}",
            offset, Math.Min(p.Length, AmsHeader + 32 + (int)Math.Min(dataLength, int.MaxValue - 64)));
        node.Add($"대상: {NetId(h[..6])}:{BinaryPrimitives.ReadUInt16LittleEndian(h[6..])}", offset + AmsHeader, 8);
        node.Add($"출발: {NetId(h[8..14])}:{BinaryPrimitives.ReadUInt16LittleEndian(h[14..])}", offset + AmsHeader + 8, 8);
        node.Add($"명령: {command} {name}", offset + AmsHeader + 16, 2);
        node.Add($"오류 코드: 0x{error:X}", offset + AmsHeader + 24, 4);
        node.Add($"Invoke ID: {invokeId}", offset + AmsHeader + 28, 4);

        string summary = role switch
        {
            MessageRole.Request => $"ADS {name} 요청 #{invokeId}",
            MessageRole.Response when isError => $"ADS {name} 응답 오류 0x{(error != 0 ? error : result):X}",
            MessageRole.Response => $"ADS {name} 응답 #{invokeId}",
            _ => "ADS 알림(Device Notification)",
        };

        packet.Layers.Add(node);
        packet.Protocol = "ADS";
        packet.Info = summary;
        packet.App = new AppMessage(AppKind.Ads, role, role == MessageRole.Unsolicited ? null : invokeId,
            summary, isError);
        return true;
    }

    private static string NetId(ReadOnlySpan<byte> b) => $"{b[0]}.{b[1]}.{b[2]}.{b[3]}.{b[4]}.{b[5]}";

    private static string CommandName(ushort command) => command switch
    {
        1 => "ReadDeviceInfo",
        2 => "Read",
        3 => "Write",
        4 => "ReadState",
        5 => "WriteControl",
        6 => "AddNotification",
        7 => "DeleteNotification",
        8 => "Notification",
        9 => "ReadWrite",
        _ => $"명령{command}",
    };
}
```

`FrameDissector.DissectTcp`의 `// ADS is wired in by Task 6.` 줄을 교체:

```csharp
        else if (dp == context.AdsPort || sp == context.AdsPort)
        {
            AdsDissector.TryDissect(payload, payloadAt, packet);
        }
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~AdsDissectorTests|FullyQualifiedName~McDissectorTests|FullyQualifiedName~FrameDissectorTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/VisionSupport.Wireshark/Dissect tests/VisionSupport.Tests/TestFrames.cs tests/VisionSupport.Tests/AdsDissectorTests.cs
git commit -m "feat(wireshark): read ADS/AMS headers and pair responses by invoke id"
```

---

### Task 7: GigE Vision — GVCP/GVSP 해석

**Files:**
- Create: `src/VisionSupport.Wireshark/Dissect/GigEDissector.cs`
- Modify: `src/VisionSupport.Wireshark/Dissect/FrameDissector.cs` (`DissectUdp`의 훅 주석 자리)
- Modify: `tests/VisionSupport.Tests/TestFrames.cs` (GigE 빌더 추가)
- Test: `tests/VisionSupport.Tests/GigEDissectorTests.cs`

**Interfaces:**
- Consumes: `DissectorContext.LearnCamera/IsCamera`, `GvspHeader`, `GvspFormat`
- Produces: `GigEDissector.TryDissectGvcp(ReadOnlySpan<byte>, int offset, Packet, DissectorContext, bool fromCamera)`, `GigEDissector.TryDissectGvsp(ReadOnlySpan<byte>, int offset, Packet)`; `TestFrames.GvcpReadRegCmd(ushort reqId)`, `GvcpReadRegAck(ushort ackId, ushort status = 0)`, `Gvsp(ushort block, uint packetId, GvspFormat format)`, `GvspExtended(ulong block, uint packetId, GvspFormat format)`, 상수 `Camera = "192.168.1.20"`, `Host = "192.168.1.2"`
- 규약: GVCP 응답이 오면 그 출발 IP를 카메라로 학습한다. 학습된 카메라에서 온 UDP(출발·도착 포트가 3956이 아님)만 GVSP로 읽는다. GVSP 패킷은 `App`이 없고 `Gvsp`만 채운다.

- [ ] **Step 1: 빌더 추가 (TestFrames 클래스 안)**

```csharp
    public const string Camera = "192.168.1.20";
    public const string Host = "192.168.1.2";

    public static byte[] GvcpReadRegCmd(ushort reqId)
    {
        var p = new byte[12];
        p[0] = 0x42; p[1] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), 0x0080);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), 4);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), reqId);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(8), 0x0A00);
        return Udp(Host, 50001, Camera, 3956, p);
    }

    public static byte[] GvcpReadRegAck(ushort ackId, ushort status = 0)
    {
        var p = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0), status);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), 0x0081);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), 4);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), ackId);
        return Udp(Camera, 3956, Host, 50001, p);
    }

    public static byte[] Gvsp(ushort block, uint packetId, GvspFormat format)
    {
        var p = new byte[8 + 16];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), block);
        p[4] = (byte)format;
        p[5] = (byte)(packetId >> 16); p[6] = (byte)(packetId >> 8); p[7] = (byte)packetId;
        return Udp(Camera, 20202, Host, 50010, p);
    }

    public static byte[] GvspExtended(ulong block, uint packetId, GvspFormat format)
    {
        var p = new byte[20 + 16];
        p[4] = (byte)(0x80 | (byte)format);
        BinaryPrimitives.WriteUInt64BigEndian(p.AsSpan(8), block);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(16), packetId);
        return Udp(Camera, 20202, Host, 50010, p);
    }
```

- [ ] **Step 2: 실패하는 테스트**

`tests/VisionSupport.Tests/GigEDissectorTests.cs`:

```csharp
using System.Net;
using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class GigEDissectorTests
{
    private readonly DissectorContext _context = new();

    [Fact]
    public void A_gvcp_command_is_a_request_keyed_by_req_id()
    {
        Packet p = TestFrames.Dissect(TestFrames.GvcpReadRegCmd(42), _context);

        Assert.Equal("GVCP", p.Protocol);
        Assert.Equal(new AppMessage(AppKind.Gvcp, MessageRole.Request, 42u, "GVCP READREG 요청 #42", false), p.App);
    }

    [Fact]
    public void A_gvcp_ack_teaches_the_context_which_host_is_a_camera()
    {
        Packet p = TestFrames.Dissect(TestFrames.GvcpReadRegAck(42), _context);

        Assert.Equal(MessageRole.Response, p.App!.Role);
        Assert.True(_context.IsCamera(IPAddress.Parse(TestFrames.Camera)));
    }

    [Fact]
    public void A_gvcp_ack_with_a_nonzero_status_is_an_error()
        => Assert.True(TestFrames.Dissect(TestFrames.GvcpReadRegAck(1, status: 0x8006), _context).App!.IsError);

    [Fact]
    public void Udp_from_an_unknown_host_is_not_read_as_video()
        => Assert.Null(TestFrames.Dissect(TestFrames.Gvsp(1, 0, GvspFormat.Leader), _context).Gvsp);

    [Fact]
    public void Gvsp_from_a_learned_camera_yields_block_and_packet_ids()
    {
        TestFrames.Dissect(TestFrames.GvcpReadRegAck(1), _context);

        Packet p = TestFrames.Dissect(TestFrames.Gvsp(513, 0x010203, GvspFormat.Payload), _context);

        Assert.Equal("GVSP", p.Protocol);
        Assert.Equal(new GvspHeader(513, 0x010203, GvspFormat.Payload, 0, false), p.Gvsp);
        Assert.Null(p.App);
    }

    [Fact]
    public void Extended_id_gvsp_reads_64_bit_blocks()
    {
        TestFrames.Dissect(TestFrames.GvcpReadRegAck(1), _context);

        Packet p = TestFrames.Dissect(TestFrames.GvspExtended(0x1_0000_0001, 7, GvspFormat.Trailer), _context);

        Assert.Equal(new GvspHeader(0x1_0000_0001, 7, GvspFormat.Trailer, 0, true), p.Gvsp);
    }
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~GigEDissectorTests"` → 실패 확인.

- [ ] **Step 3: 구현**

`Dissect/GigEDissector.cs`:

```csharp
using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// GigE Vision: GVCP (control, UDP 3956, request/ack pairs) and GVSP (the image stream, from the
/// camera to a port the host chose). Only headers are read - GVSP payload is never looked at.
/// </summary>
public static class GigEDissector
{
    public static bool TryDissectGvcp(ReadOnlySpan<byte> p, int offset, Packet packet,
        DissectorContext context, bool fromCamera)
    {
        if (p.Length < 8) return false;

        AppMessage message;
        if (!fromCamera)
        {
            if (p[0] != 0x42) return false;
            ushort command = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
            ushort reqId = BinaryPrimitives.ReadUInt16BigEndian(p[6..]);
            string summary = $"GVCP {Name(command)} 요청 #{reqId}";
            message = new AppMessage(AppKind.Gvcp, MessageRole.Request, reqId, summary, false);
            var node = new ProtocolNode($"GVCP 명령, {Name(command)}", offset, p.Length);
            node.Add($"명령: 0x{command:X4}", offset + 2, 2);
            node.Add($"Req ID: {reqId}", offset + 6, 2);
            packet.Layers.Add(node);
        }
        else
        {
            ushort status = BinaryPrimitives.ReadUInt16BigEndian(p);
            ushort answer = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
            ushort ackId = BinaryPrimitives.ReadUInt16BigEndian(p[6..]);
            bool isError = status != 0;
            if (packet.SrcIp is not null) context.LearnCamera(packet.SrcIp);
            MessageRole role = answer == 0x0089 ? MessageRole.Unsolicited : MessageRole.Response;
            string summary = $"GVCP {Name(answer)} 응답 #{ackId}" + (isError ? $" 상태 0x{status:X4}" : string.Empty);
            message = new AppMessage(AppKind.Gvcp, role, role == MessageRole.Response ? ackId : null, summary, isError);
            var node = new ProtocolNode($"GVCP 응답, {Name(answer)}", offset, p.Length);
            node.Add($"상태: 0x{status:X4}", offset, 2);
            node.Add($"Ack ID: {ackId}", offset + 6, 2);
            packet.Layers.Add(node);
        }

        packet.Protocol = "GVCP";
        packet.Info = message.Summary;
        packet.App = message;
        return true;
    }

    public static bool TryDissectGvsp(ReadOnlySpan<byte> p, int offset, Packet packet)
    {
        if (p.Length < 8) return false;

        ushort status = BinaryPrimitives.ReadUInt16BigEndian(p);
        bool extended = (p[4] & 0x80) != 0;
        var format = (GvspFormat)(p[4] & 0x0F);
        ulong block;
        uint packetId;
        if (extended)
        {
            if (p.Length < 20) return false;
            block = BinaryPrimitives.ReadUInt64BigEndian(p[8..]);
            packetId = BinaryPrimitives.ReadUInt32BigEndian(p[16..]);
        }
        else
        {
            block = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
            packetId = (uint)(p[5] << 16 | p[6] << 8 | p[7]);
        }

        packet.Gvsp = new GvspHeader(block, packetId, format, status, extended);
        packet.Protocol = "GVSP";
        packet.Info = $"GVSP 블록 {block} {FormatName(format)} #{packetId}"
            + (status != 0 ? $" 상태 0x{status:X4}" : string.Empty);
        var node = new ProtocolNode($"GVSP, {FormatName(format)}", offset, extended ? 20 : 8);
        node.Add($"블록 ID: {block}", offset + (extended ? 8 : 2), extended ? 8 : 2);
        node.Add($"패킷 ID: {packetId}", offset + (extended ? 16 : 5), extended ? 4 : 3);
        packet.Layers.Add(node);
        return true;
    }

    private static string FormatName(GvspFormat format) => format switch
    {
        GvspFormat.Leader => "리더",
        GvspFormat.Trailer => "트레일러",
        GvspFormat.Payload => "페이로드",
        GvspFormat.AllIn => "All-in",
        _ => format.ToString(),
    };

    private static string Name(ushort code) => code switch
    {
        0x0089 => "PENDING",
        0x0040 => "PACKETRESEND",
        _ => (code & 0xFFFE) switch
        {
            0x0002 => "DISCOVERY",
            0x0004 => "FORCEIP",
            0x0080 => "READREG",
            0x0082 => "WRITEREG",
            0x0084 => "READMEM",
            0x0086 => "WRITEMEM",
            0x00C0 => "EVENT",
            0x00C2 => "EVENTDATA",
            0x0100 => "ACTION",
            _ => $"0x{code:X4}",
        },
    };
}
```

`FrameDissector.DissectUdp`의 `// GVCP/GVSP are wired in by Task 7.` 줄과 `_ = payload; _ = context;` 두 줄을 교체:

```csharp
        if (dp == DissectorContext.GvcpPort || sp == DissectorContext.GvcpPort)
        {
            GigEDissector.TryDissectGvcp(payload, payloadAt, packet, context, fromCamera: sp == DissectorContext.GvcpPort);
        }
        else if (packet.SrcIp is not null && context.IsCamera(packet.SrcIp))
        {
            GigEDissector.TryDissectGvsp(payload, payloadAt, packet);
        }
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~Dissector"`
Expected: PASS (Frame/Mc/Ads/GigE 전부)

- [ ] **Step 5: Commit**

```bash
git add src/VisionSupport.Wireshark/Dissect tests/VisionSupport.Tests/TestFrames.cs tests/VisionSupport.Tests/GigEDissectorTests.cs
git commit -m "feat(wireshark): read GVCP and GVSP headers, learning cameras from GVCP acks"
```

---

### Task 8: 판정기 핵심 — 대상, 응답 타임아웃·지연, 조용함, 경고 확인

**Files:**
- Create: `src/VisionSupport.Wireshark/Health/HealthModel.cs`
- Create: `src/VisionSupport.Wireshark/Health/HealthTracker.cs`
- Test: `tests/VisionSupport.Tests/ManualClock.cs`, `tests/VisionSupport.Tests/HealthTrackerTests.cs`

**Interfaces:**
- Consumes: Task 4-7 `Packet`, `AppMessage`, `MessageRole`, `AppKind`
- Produces:
  - `enum HealthLevel { Idle, Ok, Warn, Bad }`
  - `enum TargetKind { Mc, Ads, GigE, Nic, Cxp }`
  - `enum AnomalyKind { Timeout, SlowResponse, ErrorResponse, Silence, Retransmit, ConnectionClosed, ZeroWindow, FrameDrop, LinkDown, CxpErrors, DeviceRemoved }`
  - `record Anomaly(DateTime Time, string TargetId, string TargetName, AnomalyKind Kind, HealthLevel Severity, string Text, long? PacketNumber)`
  - `record TargetSnapshot(string Id, TargetKind Kind, string Name, bool Pinned, HealthLevel Level, string Summary, long DropCount, double? LastResponseMs, long Bytes)`
  - `class HealthThresholds { ResponseTimeoutMs=1000; SlowResponseMs=200; SlowPerMinute=5; SilenceSeconds=5; RetransmitsPerMinute=3; RecoverySeconds=30 }` (모두 `int`, get/set)
  - `public sealed partial class HealthTracker(HealthThresholds thresholds, TimeProvider clock)`: `Pin(string id, TargetKind kind, string name)`, `Unpin(string id)`, `Observe(Packet)`, `Tick()`, `IReadOnlyList<TargetSnapshot> Snapshot()`, `IReadOnlyList<Anomaly> DrainAnomalies()`, `bool HasAlert`, `Acknowledge()`, `static string KindLabel(TargetKind)`
  - Task 9용 내부 훅: `partial void ObserveTransportCore(TargetState target, Packet packet)` — Task 9가 구현할 때까지는 호출이 컴파일에서 사라진다.
- 대상 ID 규칙: MC/ADS = PLC 쪽 `"ip:port"`, GigE = 카메라 `"ip"`, NIC = MAC, CXP = 보드 ID. 이름 기본값 `"{KindLabel} {id}"`.
- 모든 공개 메서드는 내부 lock으로 스레드 안전(캡처 스레드가 `Observe`, UI 타이머가 나머지).

- [ ] **Step 1: 테스트용 시계**

`tests/VisionSupport.Tests/ManualClock.cs`:

```csharp
namespace VisionSupport.Tests;

/// <summary>A clock that only moves when told to. Local time is UTC so packet times built from
/// <see cref="Now"/> and the tracker's own "now" are on the same scale.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(TestFrames.T0);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public DateTime Now => _now.UtcDateTime;

    public void Advance(double seconds) => _now = _now.AddSeconds(seconds);
}
```

- [ ] **Step 2: 실패하는 테스트**

`tests/VisionSupport.Tests/HealthTrackerTests.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using Xunit;

namespace VisionSupport.Tests;

public class HealthTrackerTests
{
    private const string Plc = "192.168.0.10:5000";
    private readonly ManualClock _clock = new();
    private readonly HealthTracker _tracker;
    private long _number;

    public HealthTrackerTests() => _tracker = new HealthTracker(new HealthThresholds(), _clock);

    private Packet Send(byte[] frame)
    {
        Packet p = TestFrames.Dissect(frame, number: ++_number, time: _clock.Now);
        _tracker.Observe(p);
        return p;
    }

    private Packet Request(int clientPort = 50000)
        => Send(TestFrames.Tcp("192.168.0.2", clientPort, "192.168.0.10", 5000, TestFrames.McReadRequest3E));

    private Packet Response(int clientPort = 50000)
        => Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", clientPort, TestFrames.McOkResponse3E));

    private TargetSnapshot Plc0() => _tracker.Snapshot().Single(t => t.Id == Plc);

    [Fact]
    public void Mc_traffic_creates_an_unpinned_candidate_named_after_the_plc()
    {
        Request();
        _tracker.Tick();

        TargetSnapshot t = Plc0();
        Assert.Equal(TargetKind.Mc, t.Kind);
        Assert.False(t.Pinned);
        Assert.Equal("PLC(MC) 192.168.0.10:5000", t.Name);
    }

    [Fact]
    public void A_prompt_answer_is_ok_and_shows_the_latency()
    {
        Request();
        _clock.Advance(0.05);
        Response();
        _tracker.Tick();

        Assert.Equal(HealthLevel.Ok, Plc0().Level);
        Assert.Equal("정상 · 응답 50ms", Plc0().Summary);
    }

    [Fact]
    public void No_answer_turns_the_target_bad_and_raises_one_timeout()
    {
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Plc0().Level);
        Assert.Equal("응답 없음 1.5초째", Plc0().Summary);
        Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Timeout);
    }

    [Fact]
    public void A_late_answer_stays_bad_for_the_recovery_window_then_clears()
    {
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        Response();
        _tracker.Tick();
        Assert.Equal(HealthLevel.Bad, Plc0().Level);

        _clock.Advance(31);
        Request();
        Response();
        _tracker.Tick();
        Assert.Equal(HealthLevel.Ok, Plc0().Level);
    }

    [Fact]
    public void Mc_3E_answers_pair_within_their_own_connection()
    {
        Request(50000);
        Request(50001);
        Response(50001);
        _clock.Advance(1.5);
        _tracker.Tick();

        Anomaly timeout = Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Timeout);
        Assert.Equal(1, timeout.PacketNumber);
    }

    [Fact]
    public void Five_slow_answers_in_a_minute_warn()
    {
        for (int i = 0; i < 5; i++)
        {
            Request();
            _clock.Advance(0.25);
            Response();
        }
        _tracker.Tick();

        Assert.Equal(HealthLevel.Warn, Plc0().Level);
        Assert.Equal("최근 1분 응답 지연 5회", Plc0().Summary);
    }

    [Fact]
    public void An_error_end_code_warns()
    {
        Request();
        Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, TestFrames.McErrorResponse3E));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Warn, Plc0().Level);
        Assert.Contains(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.ErrorResponse);
    }

    /// <summary>Right after start-up nothing has been heard yet. That is "no traffic", not "cut off".</summary>
    [Fact]
    public void A_pinned_target_never_seen_is_idle_not_silent()
    {
        _tracker.Pin(Plc, TargetKind.Mc, "검사기 PLC");
        _clock.Advance(60);
        _tracker.Tick();

        TargetSnapshot t = Plc0();
        Assert.Equal(HealthLevel.Idle, t.Level);
        Assert.Equal("트래픽 없음", t.Summary);
        Assert.Equal("검사기 PLC", t.Name);
        Assert.Empty(_tracker.DrainAnomalies());
        Assert.False(_tracker.HasAlert);
    }

    [Fact]
    public void A_pinned_target_that_goes_quiet_is_bad_once()
    {
        _tracker.Pin(Plc, TargetKind.Mc, "검사기 PLC");
        Request();
        Response();
        _clock.Advance(6);
        _tracker.Tick();
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Plc0().Level);
        Assert.Equal("트래픽 끊김 6초째", Plc0().Summary);
        Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Silence);
    }

    [Fact]
    public void An_unpinned_candidate_is_never_reported_silent()
    {
        Request();
        Response();
        _clock.Advance(60);
        _tracker.Tick();

        Assert.DoesNotContain(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Silence);
    }

    [Fact]
    public void Only_pinned_targets_raise_the_alert_and_acknowledging_clears_it_until_the_next_problem()
    {
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        Assert.False(_tracker.HasAlert);

        _tracker.Pin(Plc, TargetKind.Mc, "검사기 PLC");
        _tracker.Tick();
        Assert.True(_tracker.HasAlert);

        _tracker.Acknowledge();
        Assert.False(_tracker.HasAlert);

        _clock.Advance(1);
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        Assert.True(_tracker.HasAlert);
    }

    [Fact]
    public void Unpinning_a_target_never_seen_removes_it()
    {
        _tracker.Pin("10.0.0.1:5000", TargetKind.Mc, "x");
        _tracker.Unpin("10.0.0.1:5000");

        Assert.Empty(_tracker.Snapshot());
    }
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~HealthTrackerTests"` → 빌드 실패 확인.

- [ ] **Step 3: 모델 구현**

`Health/HealthModel.cs`:

```csharp
namespace VisionSupport.Wireshark.Health;

/// <summary>Card colour: grey, green, yellow, red.</summary>
public enum HealthLevel { Idle, Ok, Warn, Bad }

public enum TargetKind { Mc, Ads, GigE, Nic, Cxp }

public enum AnomalyKind
{
    Timeout, SlowResponse, ErrorResponse, Silence, Retransmit, ConnectionClosed, ZeroWindow,
    FrameDrop, LinkDown, CxpErrors, DeviceRemoved,
}

public sealed record Anomaly(DateTime Time, string TargetId, string TargetName, AnomalyKind Kind,
    HealthLevel Severity, string Text, long? PacketNumber);

public sealed record TargetSnapshot(string Id, TargetKind Kind, string Name, bool Pinned, HealthLevel Level,
    string Summary, long DropCount, double? LastResponseMs, long Bytes);

/// <summary>Spec §5 defaults. Settable so settings.json can tune them per line.</summary>
public sealed class HealthThresholds
{
    public int ResponseTimeoutMs { get; set; } = 1000;
    public int SlowResponseMs { get; set; } = 200;
    public int SlowPerMinute { get; set; } = 5;
    public int SilenceSeconds { get; set; } = 5;
    public int RetransmitsPerMinute { get; set; } = 3;
    /// <summary>How long a target stays red after a one-off problem (RST, timeout, drop).</summary>
    public int RecoverySeconds { get; set; } = 30;
}
```

- [ ] **Step 4: 판정기 구현**

`Health/HealthTracker.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

/// <summary>
/// Turns packets into a verdict per target. Packet arrival updates counters; <see cref="Tick"/>,
/// once a second, does everything that depends on time passing - so "no answer" and "gone quiet"
/// show up even when no packet arrives at all.
///
/// Thread-safe: the capture thread calls <see cref="Observe"/>, the UI timer everything else.
/// </summary>
public sealed partial class HealthTracker
{
    private const int MaxPending = 256;

    private readonly object _gate = new();
    private readonly Dictionary<string, TargetState> _targets = new();
    private readonly List<Anomaly> _raised = new();
    private readonly HealthThresholds _t;
    private readonly TimeProvider _clock;
    private DateTime _acknowledgedAt = DateTime.MinValue;

    public HealthTracker(HealthThresholds thresholds, TimeProvider clock)
    {
        _t = thresholds;
        _clock = clock;
    }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    public static string KindLabel(TargetKind kind) => kind switch
    {
        TargetKind.Mc => "PLC(MC)",
        TargetKind.Ads => "PLC(ADS)",
        TargetKind.GigE => "카메라(GigE)",
        TargetKind.Nic => "네트워크",
        TargetKind.Cxp => "카메라(CXP)",
        _ => kind.ToString(),
    };

    public void Pin(string id, TargetKind kind, string name)
    {
        lock (_gate)
        {
            TargetState t = GetOrAdd(id, kind);
            t.Pinned = true;
            t.Name = name;
        }
    }

    public void Unpin(string id)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(id, out TargetState? t)) return;
            if (t.LastSeen is null) _targets.Remove(id);
            else t.Pinned = false;
        }
    }

    public void Observe(Packet packet)
    {
        lock (_gate)
        {
            TargetState? target = Resolve(packet);
            if (target is null) return;

            packet.TargetId = target.Id;
            target.LastSeen = packet.Time;
            target.LastPacket = packet.Number;
            target.Bytes += packet.OriginalLength;
            target.SilenceReported = false;

            if (packet.App is { Kind: AppKind.Mc or AppKind.Ads } app)
            {
                TrackMessage(target, packet, app);
            }
            else if (packet.App is { IsError: true } other)
            {
                packet.IsAnomalous = true;
                Raise(target, AnomalyKind.ErrorResponse, HealthLevel.Warn, other.Summary, packet.Number);
            }

            ObserveTransportCore(target, packet);
        }
    }

    /// <summary>TCP and GVSP analysis, implemented by Task 9 in HealthTracker.Transport.cs. Called under the lock.</summary>
    partial void ObserveTransportCore(TargetState target, Packet packet);

    public void Tick()
    {
        lock (_gate)
        {
            DateTime now = Now;
            foreach (TargetState t in _targets.Values)
            {
                CheckTimeouts(t, now);
                CheckSilence(t, now);
                Prune(t.Slow, now);
                Prune(t.Retransmits, now);
                Evaluate(t, now);
            }
        }
    }

    public IReadOnlyList<TargetSnapshot> Snapshot()
    {
        lock (_gate)
        {
            return _targets.Values
                .OrderByDescending(t => t.Pinned).ThenBy(t => t.Kind).ThenBy(t => t.Id, StringComparer.Ordinal)
                .Select(t => new TargetSnapshot(t.Id, t.Kind, t.Name, t.Pinned, t.Level, t.Summary,
                    t.DropCount, t.LastResponseMs, t.Bytes))
                .ToList();
        }
    }

    public IReadOnlyList<Anomaly> DrainAnomalies()
    {
        lock (_gate)
        {
            List<Anomaly> drained = _raised.ToList();
            _raised.Clear();
            return drained;
        }
    }

    /// <summary>A watched target is red, and went red (or got a new problem) after the last acknowledge.</summary>
    public bool HasAlert
    {
        get
        {
            lock (_gate)
            {
                return _targets.Values.Any(t => t.Watched && t.Level == HealthLevel.Bad
                    && Later(t.BadSince, t.LastBadEvent) > _acknowledgedAt);
            }
        }
    }

    public void Acknowledge()
    {
        lock (_gate) _acknowledgedAt = Now;
    }

    private static DateTime Later(DateTime? a, DateTime? b)
    {
        DateTime x = a ?? DateTime.MinValue, y = b ?? DateTime.MinValue;
        return x > y ? x : y;
    }

    private TargetState GetOrAdd(string id, TargetKind kind)
    {
        if (!_targets.TryGetValue(id, out TargetState? t))
        {
            t = new TargetState(id, kind, $"{KindLabel(kind)} {id}");
            _targets.Add(id, t);
        }
        return t;
    }

    private TargetState? Find(string id) => _targets.GetValueOrDefault(id);

    private TargetState? Resolve(Packet p)
    {
        if (p.App is { Kind: AppKind.Mc or AppKind.Ads } app && p.Tcp is { } tcp)
        {
            string id = app.Role == MessageRole.Request ? $"{p.DstIp}:{tcp.DstPort}" : $"{p.SrcIp}:{tcp.SrcPort}";
            return GetOrAdd(id, app.Kind == AppKind.Mc ? TargetKind.Mc : TargetKind.Ads);
        }
        if (p.App is { Kind: AppKind.Gvcp } gvcp)
        {
            var camera = gvcp.Role == MessageRole.Request ? p.DstIp : p.SrcIp;
            return camera is null ? null : GetOrAdd(camera.ToString(), TargetKind.GigE);
        }
        if (p.Gvsp is not null && p.SrcIp is not null) return GetOrAdd(p.SrcIp.ToString(), TargetKind.GigE);
        if (p.Tcp is { } t && p.SrcIp is not null && p.DstIp is not null)
        {
            return Find($"{p.DstIp}:{t.DstPort}") ?? Find($"{p.SrcIp}:{t.SrcPort}");
        }
        return null;
    }

    private void TrackMessage(TargetState target, Packet packet, AppMessage app)
    {
        // Answers pair within one TCP connection: VISION may hold several to the same PLC port,
        // and MC 3E has no serial, so pairing across connections would match the wrong request.
        string client = app.Role == MessageRole.Request ? packet.Source : packet.Destination;
        switch (app.Role)
        {
            case MessageRole.Request:
            {
                var pending = new Pending(packet.Time, packet.Number);
                if (app.CorrelationId is uint id) target.Correlated[$"{client}#{id}"] = pending;
                else target.FifoFor(client).Enqueue(pending);
                target.TrimPending(MaxPending);
                break;
            }
            case MessageRole.Response:
            {
                Pending? matched = null;
                if (app.CorrelationId is uint id) target.Correlated.Remove($"{client}#{id}", out matched);
                else if (target.Fifo.TryGetValue(client, out Queue<Pending>? queue) && queue.Count > 0) matched = queue.Dequeue();

                if (matched is not null)
                {
                    double ms = (packet.Time - matched.Sent).TotalMilliseconds;
                    target.LastResponseMs = ms;
                    if (ms > _t.SlowResponseMs)
                    {
                        packet.IsAnomalous = true;
                        target.Slow.Enqueue(packet.Time);
                        if (target.Slow.Count == _t.SlowPerMinute)
                        {
                            Raise(target, AnomalyKind.SlowResponse, HealthLevel.Warn,
                                $"응답 지연 {target.Slow.Count}회 (최근 1분, 마지막 {ms:0}ms)", packet.Number);
                        }
                    }
                }
                if (app.IsError)
                {
                    packet.IsAnomalous = true;
                    Raise(target, AnomalyKind.ErrorResponse, HealthLevel.Warn, app.Summary, packet.Number);
                }
                break;
            }
        }
    }

    private void CheckTimeouts(TargetState t, DateTime now)
    {
        foreach (Pending p in t.AllPending())
        {
            if (p.Reported || (now - p.Sent).TotalMilliseconds < _t.ResponseTimeoutMs) continue;
            p.Reported = true;
            Raise(t, AnomalyKind.Timeout, HealthLevel.Bad, $"응답 없음 (요청 #{p.Packet})", p.Packet);
        }
    }

    private void CheckSilence(TargetState t, DateTime now)
    {
        if (!t.Pinned || t.Kind is TargetKind.Nic or TargetKind.Cxp) return;
        if (t.LastSeen is not { } seen || t.SilenceReported) return;
        if ((now - seen).TotalSeconds < _t.SilenceSeconds) return;
        t.SilenceReported = true;
        Raise(t, AnomalyKind.Silence, HealthLevel.Bad, $"트래픽 끊김 ({_t.SilenceSeconds}초 이상)", t.LastPacket);
    }

    private static void Prune(Queue<DateTime> times, DateTime now)
    {
        while (times.Count > 0 && (now - times.Peek()).TotalSeconds > 60) times.Dequeue();
    }

    private void Evaluate(TargetState t, DateTime now)
    {
        TimeSpan recovery = TimeSpan.FromSeconds(_t.RecoverySeconds);
        TimeSpan unanswered = t.AllPending().Where(p => p.Reported)
            .Select(p => now - p.Sent).DefaultIfEmpty(TimeSpan.Zero).Max();

        (HealthLevel level, string summary) = true switch
        {
            _ when t.LinkDown => (HealthLevel.Bad, t.LinkDownText),
            _ when unanswered > TimeSpan.Zero => (HealthLevel.Bad, $"응답 없음 {unanswered.TotalSeconds:0.0}초째"),
            _ when t.SilenceReported && t.LastSeen is { } s => (HealthLevel.Bad, $"트래픽 끊김 {(now - s).TotalSeconds:0}초째"),
            _ when t.LastBadEvent is { } b && now - b < recovery => (HealthLevel.Bad, t.LastBadText),
            _ when t.Slow.Count >= _t.SlowPerMinute => (HealthLevel.Warn, $"최근 1분 응답 지연 {t.Slow.Count}회"),
            _ when t.Retransmits.Count >= _t.RetransmitsPerMinute => (HealthLevel.Warn, $"최근 1분 재전송 {t.Retransmits.Count}회"),
            _ when t.LastWarnEvent is { } w && now - w < recovery => (HealthLevel.Warn, t.LastWarnText),
            _ when t.StatusOverride is { } o => o,
            _ when t.LastSeen is null => (HealthLevel.Idle, "트래픽 없음"),
            _ => (HealthLevel.Ok, t.LastResponseMs is { } ms ? $"정상 · 응답 {ms:0}ms" : "정상"),
        };

        if (level == HealthLevel.Bad && t.Level != HealthLevel.Bad) t.BadSince = now;
        if (level != HealthLevel.Bad) t.BadSince = null;
        t.Level = level;
        t.Summary = summary;
    }

    private void Raise(TargetState t, AnomalyKind kind, HealthLevel severity, string text, long? packetNumber)
    {
        DateTime now = Now;
        _raised.Add(new Anomaly(now, t.Id, t.Name, kind, severity, text, packetNumber));
        if (severity == HealthLevel.Bad)
        {
            t.LastBadEvent = now;
            t.LastBadText = text;
        }
        else
        {
            t.LastWarnEvent = now;
            t.LastWarnText = text;
        }
    }

    private sealed class Pending
    {
        public Pending(DateTime sent, long packet)
        {
            Sent = sent;
            Packet = packet;
        }

        public DateTime Sent { get; }
        public long Packet { get; }
        public bool Reported { get; set; }
    }

    private sealed class TargetState
    {
        public TargetState(string id, TargetKind kind, string name)
        {
            Id = id;
            Kind = kind;
            Name = name;
        }

        public string Id { get; }
        public TargetKind Kind { get; }
        public string Name { get; set; }
        public bool Pinned { get; set; }
        /// <summary>Counts toward the launcher alert: pinned, or the machine's own NIC/board.</summary>
        public bool Watched => Pinned || Kind is TargetKind.Nic or TargetKind.Cxp;

        public DateTime? LastSeen { get; set; }
        public long LastPacket { get; set; }
        public long Bytes { get; set; }
        public double? LastResponseMs { get; set; }
        public long DropCount { get; set; }

        public Dictionary<string, Pending> Correlated { get; } = new();
        public Dictionary<string, Queue<Pending>> Fifo { get; } = new();
        public Queue<DateTime> Slow { get; } = new();
        public Queue<DateTime> Retransmits { get; } = new();

        public bool SilenceReported { get; set; }
        public bool LinkDown { get; set; }
        public string LinkDownText { get; set; } = "링크 끊김";
        public DateTime? LastBadEvent { get; set; }
        public string LastBadText { get; set; } = string.Empty;
        public DateTime? LastWarnEvent { get; set; }
        public string LastWarnText { get; set; } = string.Empty;
        /// <summary>Set by CXP presence-only mode: grey card with the reason instead of "트래픽 없음".</summary>
        public (HealthLevel, string)? StatusOverride { get; set; }

        public HealthLevel Level { get; set; } = HealthLevel.Idle;
        public string Summary { get; set; } = "트래픽 없음";
        public DateTime? BadSince { get; set; }

        public Queue<Pending> FifoFor(string client)
        {
            if (!Fifo.TryGetValue(client, out Queue<Pending>? queue))
            {
                queue = new Queue<Pending>();
                Fifo.Add(client, queue);
            }
            return queue;
        }

        public IEnumerable<Pending> AllPending() => Correlated.Values.Concat(Fifo.Values.SelectMany(q => q));

        /// <summary>Requests that never get an answer must not pile up for days.</summary>
        public void TrimPending(int max)
        {
            while (Correlated.Count > max)
            {
                Correlated.Remove(Correlated.MinBy(kv => kv.Value.Sent).Key);
            }
            foreach (Queue<Pending> queue in Fifo.Values)
            {
                while (queue.Count > max) queue.Dequeue();
            }
            if (Fifo.Count > max) Fifo.Clear();
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~HealthTrackerTests"`
Expected: PASS (12 tests)

- [ ] **Step 6: Commit**

```bash
git add src/VisionSupport.Wireshark/Health tests/VisionSupport.Tests/ManualClock.cs tests/VisionSupport.Tests/HealthTrackerTests.cs
git commit -m "feat(wireshark): judge each target - timeouts, slow answers, silence, alert"
```

---

### Task 9: 판정기 확장 — TCP 이상, GVSP 드롭, 캡처 누락, NIC 링크, CXP 보고

**Files:**
- Create: `src/VisionSupport.Wireshark/Health/TcpAnalyzer.cs`
- Create: `src/VisionSupport.Wireshark/Health/GvspAnalyzer.cs`
- Create: `src/VisionSupport.Wireshark/Health/HealthTracker.Transport.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/CxpReport.cs`
- Test: `tests/VisionSupport.Tests/HealthTrackerLinkTests.cs`

**Interfaces:**
- Consumes: Task 8 `HealthTracker`(partial) 내부 — `_gate`, `_t`, `Now`, `GetOrAdd`, `Raise(TargetState, AnomalyKind, HealthLevel, string, long?)`, `TargetState`(Retransmits, DropCount, LinkDown, LinkDownText, StatusOverride, LastSeen, Name, Id)
- Produces:
  - `[Flags] internal enum TcpVerdict { None=0, Retransmission=1, Reset=2, Fin=4, ZeroWindow=8 }`, `internal TcpAnalyzer.Inspect(Packet) : TcpVerdict`
  - `internal GvspAnalyzer.Inspect(string streamId, GvspHeader header) : int` (이 패킷으로 드러난 잃은 프레임 수)
  - `HealthTracker.ReportCaptureLoss(long totalLost)`, `HealthTracker.ReportLink(string nicId, string name, bool up)`, `HealthTracker.ReportCxp(CxpReport report)`
  - 네임스페이스 `VisionSupport.Wireshark.Cxp`: `enum CxpMode { Full, PresenceOnly, Absent }`, `record CxpConnectionStatus(int Index, bool? LinkUp, string Speed, long? ErrorCount, long? FrameCount, long? DropCount)`, `record CxpReport(string BoardId, string BoardName, CxpMode Mode, IReadOnlyList<CxpConnectionStatus> Connections, string? Message)`

- [ ] **Step 1: 실패하는 테스트**

`tests/VisionSupport.Tests/HealthTrackerLinkTests.cs`:

```csharp
using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using Xunit;

namespace VisionSupport.Tests;

public class HealthTrackerLinkTests
{
    private const string Plc = "192.168.0.10:5000";
    private readonly ManualClock _clock = new();
    private readonly HealthTracker _tracker;
    private readonly DissectorContext _context = new();
    private long _number;

    public HealthTrackerLinkTests() => _tracker = new HealthTracker(new HealthThresholds(), _clock);

    private Packet Send(byte[] frame)
    {
        Packet p = TestFrames.Dissect(frame, _context, ++_number, _clock.Now);
        _tracker.Observe(p);
        return p;
    }

    private TargetSnapshot Target(string id) => _tracker.Snapshot().Single(t => t.Id == id);

    // MC request, seq 100..121 (the 3E sample is 21 bytes).
    private Packet McFromVision(uint seq = 100)
        => Send(TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, TestFrames.McReadRequest3E, seq: seq));

    [Fact]
    public void Three_resent_segments_in_a_minute_warn()
    {
        McFromVision();
        Packet last = null!;
        for (int i = 0; i < 3; i++) last = McFromVision();
        _tracker.Tick();

        Assert.True(last.IsAnomalous);
        Assert.Equal(HealthLevel.Warn, Target(Plc).Level);
        Assert.Equal("최근 1분 재전송 3회", Target(Plc).Summary);
    }

    [Fact]
    public void A_keepalive_is_not_a_retransmission()
    {
        McFromVision();
        Packet keepalive = Send(TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, new byte[1], seq: 120));

        Assert.False(keepalive.IsAnomalous);
    }

    [Fact]
    public void A_reset_turns_the_target_bad()
    {
        McFromVision();
        Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, flags: TcpFlags.Rst | TcpFlags.Ack));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Target(Plc).Level);
        Assert.Equal("연결 리셋(RST)", Target(Plc).Summary);
    }

    [Fact]
    public void A_zero_window_warns()
    {
        McFromVision();
        Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, window: 0));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Warn, Target(Plc).Level);
    }

    private void LearnCamera() => Send(TestFrames.GvcpReadRegAck(1));

    private void Frame(ushort block, params uint[] payloadIds)
    {
        Send(TestFrames.Gvsp(block, 0, GvspFormat.Leader));
        foreach (uint id in payloadIds) Send(TestFrames.Gvsp(block, id, GvspFormat.Payload));
        Send(TestFrames.Gvsp(block, (uint)payloadIds.Length + 1, GvspFormat.Trailer));
    }

    [Fact]
    public void A_skipped_block_id_is_a_dropped_frame()
    {
        LearnCamera();
        Frame(1, 1, 2, 3);
        Frame(3, 1, 2, 3);
        _tracker.Tick();

        TargetSnapshot cam = Target(TestFrames.Camera);
        Assert.Equal(1, cam.DropCount);
        Assert.Equal(HealthLevel.Bad, cam.Level);
        Assert.Equal("프레임 드롭 1장 (누적 1)", cam.Summary);
    }

    [Fact]
    public void A_missing_packet_inside_a_block_is_one_dropped_frame()
    {
        LearnCamera();
        Send(TestFrames.Gvsp(1, 0, GvspFormat.Leader));
        Send(TestFrames.Gvsp(1, 1, GvspFormat.Payload));
        Send(TestFrames.Gvsp(1, 3, GvspFormat.Payload));
        Send(TestFrames.Gvsp(1, 5, GvspFormat.Payload));
        Send(TestFrames.Gvsp(1, 6, GvspFormat.Trailer));

        Assert.Equal(1, Target(TestFrames.Camera).DropCount);
    }

    /// <summary>16-bit block ids run 1..65535 and skip 0. Wrapping is not a drop.</summary>
    [Fact]
    public void The_16_bit_block_id_wrapping_past_65535_is_not_a_drop()
    {
        LearnCamera();
        Frame(65534, 1);
        Frame(65535, 1);
        Frame(1, 1);

        Assert.Equal(0, Target(TestFrames.Camera).DropCount);
    }

    /// <summary>When ETW itself lost events, a gap in the stream may be ours, not the camera's.</summary>
    [Fact]
    public void A_drop_seen_while_the_capture_was_losing_events_is_only_a_warning()
    {
        LearnCamera();
        _tracker.ReportCaptureLoss(10);
        Frame(1, 1);
        Frame(3, 1);
        _tracker.Tick();

        Anomaly drop = Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.FrameDrop);
        Assert.Equal(HealthLevel.Warn, drop.Severity);
        Assert.Equal("프레임 드롭 의심 1장 (캡처 누락 동반)", drop.Text);
        Assert.Equal(HealthLevel.Warn, Target(TestFrames.Camera).Level);
    }

    [Fact]
    public void A_nic_going_down_is_bad_and_alerts_without_pinning()
    {
        _tracker.ReportLink("00-11-22-33-44-55", "이더넷 2", up: true);
        _tracker.ReportLink("00-11-22-33-44-55", "이더넷 2", up: false);
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Target("00-11-22-33-44-55").Level);
        Assert.True(_tracker.HasAlert);
        Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.LinkDown);
    }

    private static CxpReport Full(long errors, long drops, bool linkUp = true) => new("Rapixo#0", "Rapixo CXP", CxpMode.Full,
        new[] { new CxpConnectionStatus(0, linkUp, "CXP-12", errors, 100, drops) }, null);

    [Fact]
    public void A_missing_board_is_bad()
    {
        _tracker.ReportCxp(new CxpReport("Rapixo#0", "Rapixo CXP", CxpMode.Absent, Array.Empty<CxpConnectionStatus>(), null));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Target("Rapixo#0").Level);
        Assert.Equal("CXP 보드가 보이지 않음(분리됨)", Target("Rapixo#0").Summary);
    }

    [Fact]
    public void Presence_only_is_grey_with_the_reason()
    {
        _tracker.ReportCxp(new CxpReport("Rapixo#0", "Rapixo CXP", CxpMode.PresenceOnly, Array.Empty<CxpConnectionStatus>(), null));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Idle, Target("Rapixo#0").Level);
        Assert.Equal("상세 불가 — 보드 연결 여부만 감시", Target("Rapixo#0").Summary);
    }

    [Fact]
    public void Rising_cxp_counters_warn_on_errors_and_count_drops()
    {
        _tracker.ReportCxp(Full(errors: 0, drops: 0));
        _tracker.ReportCxp(Full(errors: 2, drops: 0));
        _tracker.Tick();
        Assert.Equal(HealthLevel.Warn, Target("Rapixo#0").Level);

        _tracker.ReportCxp(Full(errors: 2, drops: 3));
        _tracker.Tick();
        Assert.Equal(HealthLevel.Bad, Target("Rapixo#0").Level);
        Assert.Equal(3, Target("Rapixo#0").DropCount);
    }

    [Fact]
    public void A_cxp_link_going_down_is_bad()
    {
        _tracker.ReportCxp(Full(0, 0));
        _tracker.ReportCxp(Full(0, 0, linkUp: false));
        _tracker.Tick();

        Assert.Equal("CXP 링크 끊김 (커넥션 0)", Target("Rapixo#0").Summary);
    }
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~HealthTrackerLinkTests"` → 빌드 실패 확인.

- [ ] **Step 2: CXP 보고 레코드**

`Cxp/CxpReport.cs`:

```csharp
namespace VisionSupport.Wireshark.Cxp;

/// <summary>How much the monitor could see of the board this poll.</summary>
public enum CxpMode
{
    /// <summary>Link state and counters were read through GenTL.</summary>
    Full,
    /// <summary>The board is there, but its registers could not be read (VISION holds it, no producer, ...).</summary>
    PresenceOnly,
    /// <summary>The board is not in the device list at all.</summary>
    Absent,
}

/// <summary>One CXP connection. Null means "not readable on this board", not zero.</summary>
public sealed record CxpConnectionStatus(int Index, bool? LinkUp, string Speed, long? ErrorCount,
    long? FrameCount, long? DropCount);

public sealed record CxpReport(string BoardId, string BoardName, CxpMode Mode,
    IReadOnlyList<CxpConnectionStatus> Connections, string? Message);
```

- [ ] **Step 3: 분석기**

`Health/TcpAnalyzer.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

[Flags]
internal enum TcpVerdict { None = 0, Retransmission = 1, Reset = 2, Fin = 4, ZeroWindow = 8 }

/// <summary>
/// Per-direction sequence tracking, the way Wireshark's "TCP Retransmission" works at its
/// simplest: data that ends at or before the highest byte already seen was sent before.
/// </summary>
internal sealed class TcpAnalyzer
{
    private const int MaxFlows = 4096;
    private readonly Dictionary<string, uint> _highestEnd = new();

    public TcpVerdict Inspect(Packet p)
    {
        if (p.Tcp is not { } t) return TcpVerdict.None;

        var verdict = TcpVerdict.None;
        if (t.Flags.HasFlag(TcpFlags.Rst)) verdict |= TcpVerdict.Reset;
        if (t.Flags.HasFlag(TcpFlags.Fin)) verdict |= TcpVerdict.Fin;
        if (t.Window == 0 && (t.Flags & (TcpFlags.Rst | TcpFlags.Syn)) == 0) verdict |= TcpVerdict.ZeroWindow;

        string flow = $"{p.Source}>{p.Destination}";
        if (t.Flags.HasFlag(TcpFlags.Syn)) _highestEnd.Remove(flow);
        if (t.PayloadLength == 0) return verdict;

        uint end = unchecked(t.Seq + (uint)t.PayloadLength);
        if (_highestEnd.TryGetValue(flow, out uint highest))
        {
            // A keep-alive re-sends the last byte on purpose.
            bool keepAlive = t.PayloadLength <= 1 && t.Seq == unchecked(highest - 1);
            if (unchecked((int)(end - highest)) > 0) _highestEnd[flow] = end;
            else if (!keepAlive) verdict |= TcpVerdict.Retransmission;
        }
        else
        {
            if (_highestEnd.Count >= MaxFlows) _highestEnd.Clear();
            _highestEnd[flow] = end;
        }
        return verdict;
    }
}
```

`Health/GvspAnalyzer.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

/// <summary>
/// Frame-drop detection from GVSP headers alone: block ids should step by one, and packet ids
/// inside a block should step by one from the leader's 0. Each broken block counts once.
/// </summary>
internal sealed class GvspAnalyzer
{
    private sealed class Stream
    {
        public ulong? LastBlock;
        public uint NextPacket;
        public bool Broken;
        public bool InBlock;
    }

    private readonly Dictionary<string, Stream> _streams = new();

    /// <returns>Frames this packet shows were lost.</returns>
    public int Inspect(string streamId, GvspHeader h)
    {
        if (!_streams.TryGetValue(streamId, out Stream? s))
        {
            s = new Stream();
            _streams.Add(streamId, s);
        }

        int lost = 0;
        if (s.LastBlock != h.BlockId)
        {
            if (s.LastBlock is { } last) lost += Gap(last, h.BlockId, h.ExtendedId);
            s.LastBlock = h.BlockId;
            s.InBlock = true;
            s.Broken = false;
            if (h.Format != GvspFormat.Leader && h.PacketId != 0)
            {
                // This block's leader never arrived.
                s.Broken = true;
                lost += 1;
            }
            s.NextPacket = h.PacketId + 1;
        }
        else if (s.InBlock)
        {
            if (h.PacketId != s.NextPacket && !s.Broken)
            {
                s.Broken = true;
                lost += 1;
            }
            s.NextPacket = h.PacketId + 1;
        }

        if (h.Format == GvspFormat.Trailer) s.InBlock = false;
        return lost;
    }

    /// <summary>Whole frames skipped between two block ids. 16-bit ids wrap 65535 → 1 (0 is never used).</summary>
    private static int Gap(ulong last, ulong current, bool extended)
    {
        ulong step = extended
            ? (current > last ? current - last : 1)
            : (current > last ? current - last : current + 65535 - last);
        return step > 1 ? (int)Math.Min(step - 1, int.MaxValue) : 0;
    }
}
```

- [ ] **Step 4: 판정기에 연결**

`Health/HealthTracker.Transport.cs`:

```csharp
using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

public sealed partial class HealthTracker
{
    private readonly TcpAnalyzer _tcp = new();
    private readonly GvspAnalyzer _gvsp = new();
    private readonly Dictionary<string, Dictionary<int, CxpConnectionStatus>> _cxpPrevious = new();
    private long _captureLostTotal;
    private DateTime? _lastCaptureLoss;

    /// <summary>ETW's cumulative lost-event count. A gap in a video stream within 10 s of a loss
    /// may be ours rather than the camera's, so it is reported as a suspicion, not a drop.</summary>
    public void ReportCaptureLoss(long totalLost)
    {
        lock (_gate)
        {
            if (totalLost > _captureLostTotal) _lastCaptureLoss = Now;
            _captureLostTotal = totalLost;
        }
    }

    public void ReportLink(string nicId, string name, bool up)
    {
        lock (_gate)
        {
            TargetState t = GetOrAdd(nicId, TargetKind.Nic);
            t.Name = $"{KindLabel(TargetKind.Nic)} {name}";
            t.LastSeen = Now;
            if (!up && !t.LinkDown)
            {
                t.LinkDownText = "링크 끊김";
                Raise(t, AnomalyKind.LinkDown, HealthLevel.Bad, $"{name} 링크 끊김", null);
            }
            t.LinkDown = !up;
        }
    }

    public void ReportCxp(CxpReport report)
    {
        lock (_gate)
        {
            TargetState t = GetOrAdd(report.BoardId, TargetKind.Cxp);
            t.Name = $"{KindLabel(TargetKind.Cxp)} {report.BoardName}";
            t.LastSeen = Now;

            switch (report.Mode)
            {
                case CxpMode.Absent:
                    t.StatusOverride = null;
                    if (!t.LinkDown)
                    {
                        Raise(t, AnomalyKind.DeviceRemoved, HealthLevel.Bad, "CXP 보드가 보이지 않음(분리됨)", null);
                    }
                    t.LinkDown = true;
                    t.LinkDownText = "CXP 보드가 보이지 않음(분리됨)";
                    _cxpPrevious.Remove(report.BoardId);
                    return;
                case CxpMode.PresenceOnly:
                    t.LinkDown = false;
                    t.StatusOverride = (HealthLevel.Idle, report.Message ?? "상세 불가 — 보드 연결 여부만 감시");
                    return;
            }

            t.StatusOverride = null;
            int[] down = report.Connections.Where(c => c.LinkUp == false).Select(c => c.Index).ToArray();
            string downText = $"CXP 링크 끊김 (커넥션 {string.Join(",", down)})";
            if (down.Length > 0 && !t.LinkDown) Raise(t, AnomalyKind.LinkDown, HealthLevel.Bad, downText, null);
            t.LinkDown = down.Length > 0;
            t.LinkDownText = downText;

            _cxpPrevious.TryGetValue(report.BoardId, out Dictionary<int, CxpConnectionStatus>? previous);
            foreach (CxpConnectionStatus c in report.Connections)
            {
                if (previous?.GetValueOrDefault(c.Index) is not { } before) continue;
                if (c.ErrorCount > before.ErrorCount)
                {
                    Raise(t, AnomalyKind.CxpErrors, HealthLevel.Warn,
                        $"CXP 커넥션 {c.Index} 에러 +{c.ErrorCount - before.ErrorCount}", null);
                }
                if (c.DropCount > before.DropCount)
                {
                    long delta = c.DropCount!.Value - (before.DropCount ?? 0);
                    t.DropCount += delta;
                    Raise(t, AnomalyKind.FrameDrop, HealthLevel.Bad, $"프레임 드롭 {delta}장 (CXP, 누적 {t.DropCount})", null);
                }
            }
            _cxpPrevious[report.BoardId] = report.Connections.ToDictionary(c => c.Index);
        }
    }

    partial void ObserveTransportCore(TargetState target, Packet packet)
    {
        TcpVerdict verdict = _tcp.Inspect(packet);
        if (verdict.HasFlag(TcpVerdict.Retransmission))
        {
            packet.IsAnomalous = true;
            target.Retransmits.Enqueue(packet.Time);
            if (target.Retransmits.Count == _t.RetransmitsPerMinute)
            {
                Raise(target, AnomalyKind.Retransmit, HealthLevel.Warn, $"재전송 {target.Retransmits.Count}회 (최근 1분)", packet.Number);
            }
        }
        if ((verdict & (TcpVerdict.Reset | TcpVerdict.Fin)) != 0)
        {
            packet.IsAnomalous = true;
            Raise(target, AnomalyKind.ConnectionClosed, HealthLevel.Bad,
                verdict.HasFlag(TcpVerdict.Reset) ? "연결 리셋(RST)" : "연결 종료(FIN)", packet.Number);
        }
        if (verdict.HasFlag(TcpVerdict.ZeroWindow))
        {
            packet.IsAnomalous = true;
            Raise(target, AnomalyKind.ZeroWindow, HealthLevel.Warn, "수신 버퍼 가득 참(제로 윈도우)", packet.Number);
        }

        if (packet.Gvsp is { } gvsp)
        {
            int lost = _gvsp.Inspect(target.Id, gvsp);
            if (lost == 0) return;

            packet.IsAnomalous = true;
            bool uncertain = _lastCaptureLoss is { } loss && (Now - loss).TotalSeconds < 10;
            if (uncertain)
            {
                Raise(target, AnomalyKind.FrameDrop, HealthLevel.Warn, $"프레임 드롭 의심 {lost}장 (캡처 누락 동반)", packet.Number);
            }
            else
            {
                target.DropCount += lost;
                Raise(target, AnomalyKind.FrameDrop, HealthLevel.Bad, $"프레임 드롭 {lost}장 (누적 {target.DropCount})", packet.Number);
            }
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~HealthTracker"`
Expected: PASS (Task 8 + Task 9 전부)

- [ ] **Step 6: Commit**

```bash
git add src/VisionSupport.Wireshark/Health src/VisionSupport.Wireshark/Cxp/CxpReport.cs tests/VisionSupport.Tests/HealthTrackerLinkTests.cs
git commit -m "feat(wireshark): flag retransmits, resets, GVSP and CXP drops, and link loss"
```

---

### Task 10: 패킷 저장소 — 원형 버퍼와 이상 전후 보존

**Files:**
- Create: `src/VisionSupport.Wireshark/Store/PacketStore.cs`
- Test: `tests/VisionSupport.Tests/PacketStoreTests.cs`

**Interfaces:**
- Produces: `PacketStore(long maxBytes, int maxCount, int keepBefore = 20, int keepAfter = 20, int maxKeptEvents = 1000)`; `Add(Packet)`, `KeepAround(long packetNumber)`, `IReadOnlyList<Packet> Snapshot()`, `int Count`, `long Bytes`, `Clear()`. 스레드 안전.
- `KeepAround(n)`은 링에 남아 있는 n 이하의 최근 `keepBefore`+1개(그 패킷 자신 포함)와 이후 들어올 `keepAfter`개를 보존한다.

- [ ] **Step 1: 실패하는 테스트**

`tests/VisionSupport.Tests/PacketStoreTests.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Store;
using Xunit;

namespace VisionSupport.Tests;

public class PacketStoreTests
{
    private static Packet P(long n, int size = 10) => new(n, TestFrames.T0, new byte[size], size);

    private static long[] Numbers(PacketStore s) => s.Snapshot().Select(p => p.Number).ToArray();

    [Fact]
    public void The_ring_drops_the_oldest_past_its_count()
    {
        var store = new PacketStore(maxBytes: long.MaxValue, maxCount: 3);
        for (int i = 1; i <= 5; i++) store.Add(P(i));

        Assert.Equal(new long[] { 3, 4, 5 }, Numbers(store));
    }

    [Fact]
    public void The_ring_drops_the_oldest_past_its_bytes()
    {
        var store = new PacketStore(maxBytes: 25, maxCount: 100);
        for (int i = 1; i <= 4; i++) store.Add(P(i));

        Assert.Equal(new long[] { 3, 4 }, Numbers(store));
        Assert.Equal(20, store.Bytes);
    }

    [Fact]
    public void Packets_around_an_anomaly_outlive_the_ring()
    {
        var store = new PacketStore(long.MaxValue, maxCount: 3, keepBefore: 1, keepAfter: 1);
        for (int i = 1; i <= 4; i++) store.Add(P(i));
        store.KeepAround(4);
        for (int i = 5; i <= 10; i++) store.Add(P(i));

        Assert.Equal(new long[] { 3, 4, 5, 8, 9, 10 }, Numbers(store));
    }

    [Fact]
    public void Kept_events_are_capped_oldest_first()
    {
        var store = new PacketStore(long.MaxValue, maxCount: 1, keepBefore: 0, keepAfter: 0, maxKeptEvents: 2);
        for (int i = 1; i <= 3; i++)
        {
            store.Add(P(i));
            store.KeepAround(i);
        }
        store.Add(P(4));

        Assert.Equal(new long[] { 2, 3, 4 }, Numbers(store));
    }

    [Fact]
    public void A_packet_kept_by_two_events_survives_the_first_being_dropped()
    {
        var store = new PacketStore(long.MaxValue, maxCount: 2, keepBefore: 1, keepAfter: 0, maxKeptEvents: 1);
        store.Add(P(1));
        store.Add(P(2));
        store.KeepAround(2); // keeps 1, 2
        store.KeepAround(2); // keeps 1, 2 again; the first event is dropped
        store.Add(P(3));

        Assert.Equal(new long[] { 1, 2, 3 }, Numbers(store));
    }
}
```

Run → 빌드 실패 확인.

- [ ] **Step 2: 구현**

`Store/PacketStore.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Store;

/// <summary>
/// Recent packets in a ring capped by count and bytes, plus the packets around every anomaly,
/// kept after the ring has moved on - the evidence is what you want when you open the window an
/// hour after the PLC hiccupped. Kept packets are reference-counted because two anomalies close
/// together share their neighbours.
/// </summary>
public sealed class PacketStore
{
    private readonly object _gate = new();
    private readonly LinkedList<Packet> _ring = new();
    private readonly Dictionary<long, (Packet Packet, int Refs)> _kept = new();
    private readonly Queue<List<long>> _events = new();
    private readonly long _maxBytes;
    private readonly int _maxCount, _keepBefore, _keepAfter, _maxKeptEvents;
    private List<long>? _collecting;
    private int _afterRemaining;
    private long _ringBytes;

    public PacketStore(long maxBytes, int maxCount, int keepBefore = 20, int keepAfter = 20, int maxKeptEvents = 1000)
    {
        _maxBytes = maxBytes;
        _maxCount = maxCount;
        _keepBefore = keepBefore;
        _keepAfter = keepAfter;
        _maxKeptEvents = maxKeptEvents;
    }

    public int Count
    {
        get { lock (_gate) return _ring.Count + _kept.Values.Count(k => !InRing(k.Packet)); }
    }

    public long Bytes
    {
        get { lock (_gate) return _ringBytes; }
    }

    public void Add(Packet packet)
    {
        lock (_gate)
        {
            _ring.AddLast(packet);
            _ringBytes += packet.Data.Length;

            if (_afterRemaining > 0 && _collecting is not null)
            {
                Keep(packet, _collecting);
                _afterRemaining--;
            }

            while ((_ringBytes > _maxBytes || _ring.Count > _maxCount) && _ring.First is { } first)
            {
                _ringBytes -= first.Value.Data.Length;
                _ring.RemoveFirst();
            }
        }
    }

    public void KeepAround(long packetNumber)
    {
        lock (_gate)
        {
            var ids = new List<long>();
            int taken = 0;
            for (LinkedListNode<Packet>? n = _ring.Last; n is not null && taken <= _keepBefore; n = n.Previous)
            {
                if (n.Value.Number > packetNumber) continue;
                Keep(n.Value, ids);
                taken++;
            }

            _events.Enqueue(ids);
            _collecting = ids;
            _afterRemaining = _keepAfter;

            while (_events.Count > _maxKeptEvents)
            {
                foreach (long id in _events.Dequeue()) Release(id);
            }
        }
    }

    public IReadOnlyList<Packet> Snapshot()
    {
        lock (_gate)
        {
            return _kept.Values.Select(k => k.Packet).Where(p => !InRing(p))
                .OrderBy(p => p.Number)
                .Concat(_ring)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _ring.Clear();
            _kept.Clear();
            _events.Clear();
            _collecting = null;
            _afterRemaining = 0;
            _ringBytes = 0;
        }
    }

    private bool InRing(Packet p) => _ring.First is { } first && p.Number >= first.Value.Number;

    private void Keep(Packet packet, List<long> owner)
    {
        owner.Add(packet.Number);
        _kept[packet.Number] = _kept.TryGetValue(packet.Number, out var k) ? (k.Packet, k.Refs + 1) : (packet, 1);
    }

    private void Release(long id)
    {
        if (!_kept.TryGetValue(id, out var k)) return;
        if (k.Refs <= 1) _kept.Remove(id);
        else _kept[id] = (k.Packet, k.Refs - 1);
    }
}
```

- [ ] **Step 3: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~PacketStoreTests"`
Expected: PASS (5 tests)

- [ ] **Step 4: Commit**

```bash
git add src/VisionSupport.Wireshark/Store tests/VisionSupport.Tests/PacketStoreTests.cs
git commit -m "feat(wireshark): keep recent packets in a bounded ring and the ones around anomalies"
```

---

### Task 11: pcapng 내보내기

**Files:**
- Create: `src/VisionSupport.Wireshark/Export/PcapngWriter.cs`
- Test: `tests/VisionSupport.Tests/PcapngWriterTests.cs`

**Interfaces:**
- Produces: `static void PcapngWriter.Write(Stream stream, IEnumerable<Packet> packets)` — SHB + IDB(LINKTYPE_ETHERNET=1, snaplen 0) + 패킷마다 EPB(마이크로초, UTC). 스트림은 닫지 않는다.

- [ ] **Step 1: 실패하는 테스트**

`tests/VisionSupport.Tests/PcapngWriterTests.cs`:

```csharp
using System.Buffers.Binary;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Export;
using Xunit;

namespace VisionSupport.Tests;

public class PcapngWriterTests
{
    [Fact]
    public void Writes_a_section_an_ethernet_interface_and_one_block_per_packet()
    {
        var time = new DateTime(2026, 10, 2, 1, 2, 3, DateTimeKind.Utc).AddTicks(4560);
        var packet = new Packet(1, time, new byte[] { 1, 2, 3, 4, 5 }, 60);
        var stream = new MemoryStream();

        PcapngWriter.Write(stream, new[] { packet });
        byte[] b = stream.ToArray();

        // Section Header Block
        Assert.Equal(0x0A0D0D0Au, U32(b, 0));
        Assert.Equal(28u, U32(b, 4));
        Assert.Equal(0x1A2B3C4Du, U32(b, 8));
        Assert.Equal(28u, U32(b, 24));
        // Interface Description Block, Ethernet
        Assert.Equal(1u, U32(b, 28));
        Assert.Equal(20u, U32(b, 32));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(36)));
        // Enhanced Packet Block: 32 header/trailer bytes + 5 data bytes padded to 8
        const int epb = 48;
        Assert.Equal(6u, U32(b, epb));
        Assert.Equal(40u, U32(b, epb + 4));
        ulong micros = (ulong)U32(b, epb + 12) << 32 | U32(b, epb + 16);
        Assert.Equal((ulong)((time - DateTime.UnixEpoch).Ticks / 10), micros);
        Assert.Equal(5u, U32(b, epb + 20));
        Assert.Equal(60u, U32(b, epb + 24));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 0, 0, 0 }, b.AsSpan(epb + 28, 8).ToArray());
        Assert.Equal(40u, U32(b, epb + 36));
        Assert.Equal(epb + 40, b.Length);
    }

    [Fact]
    public void Leaves_the_stream_open()
    {
        var stream = new MemoryStream();
        PcapngWriter.Write(stream, Array.Empty<Packet>());
        Assert.True(stream.CanWrite);
    }

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
```

Run → 빌드 실패 확인.

- [ ] **Step 2: 구현**

`Export/PcapngWriter.cs`:

```csharp
using System.Text;
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Export;

/// <summary>
/// Saves captured packets as pcapng, so anything found here can be opened in the real Wireshark.
/// One section, one Ethernet interface, one Enhanced Packet Block per packet (microsecond times).
/// </summary>
public static class PcapngWriter
{
    public static void Write(Stream stream, IEnumerable<Packet> packets)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Section Header Block
        w.Write(0x0A0D0D0Au);
        w.Write(28u);
        w.Write(0x1A2B3C4Du);
        w.Write((ushort)1);
        w.Write((ushort)0);
        w.Write(-1L);
        w.Write(28u);

        // Interface Description Block: LINKTYPE_ETHERNET, no snap length
        w.Write(1u);
        w.Write(20u);
        w.Write((ushort)1);
        w.Write((ushort)0);
        w.Write(0u);
        w.Write(20u);

        foreach (Packet p in packets)
        {
            int padded = (p.Data.Length + 3) & ~3;
            uint total = (uint)(32 + padded);
            ulong micros = (ulong)((p.Time.ToUniversalTime() - DateTime.UnixEpoch).Ticks / 10);

            w.Write(6u);
            w.Write(total);
            w.Write(0u);
            w.Write((uint)(micros >> 32));
            w.Write((uint)micros);
            w.Write((uint)p.Data.Length);
            w.Write((uint)p.OriginalLength);
            w.Write(p.Data);
            w.Write(new byte[padded - p.Data.Length]);
            w.Write(total);
        }
    }
}
```

- [ ] **Step 3: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~PcapngWriterTests"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/VisionSupport.Wireshark/Export tests/VisionSupport.Tests/PcapngWriterTests.cs
git commit -m "feat(wireshark): export captures as pcapng"
```

---

### Task 12: 설정 저장

**Files:**
- Create: `src/VisionSupport.Wireshark/Settings/WiresharkSettings.cs`
- Create: `src/VisionSupport.Wireshark/Settings/WiresharkSettingsStore.cs`
- Test: `tests/VisionSupport.Tests/WiresharkSettingsTests.cs` (기존 `TempDir` 사용: `using var dir = new TempDir(); dir.Path`)

**Interfaces:**
- Consumes: `HealthThresholds`, `TargetKind`
- Produces:
  - `PinnedTarget { string Id; TargetKind Kind; string Name }`
  - `enum CxpNodeRole { LinkUp, Speed, ErrorCount, FrameCount, DropCount }`
  - `CxpNodeBinding { string Module = "Interface"; string Node = ""; CxpNodeRole Role; int Connection }` (Module은 `"Interface"` 또는 `"Device"`)
  - `WiresharkSettings { List<PinnedTarget> Pinned; HealthThresholds Thresholds; int McPortMin=5000; int McPortMax=5010; int CapturePacketSize=256; long StoreMaxBytes=200MB; int StoreMaxCount=500000; int? LastNicComponentId; string CxpDeviceNameMatch="Rapixo"; string? GenTLProducerPath; List<CxpNodeBinding> CxpNodes }`
  - `WiresharkSettingsStore(string filePath)`, `static WiresharkSettingsStore Default`, `string DataDirectory`, `WiresharkSettings Load()`, `void Save(WiresharkSettings)`

- [ ] **Step 1: 실패하는 테스트**

`tests/VisionSupport.Tests/WiresharkSettingsTests.cs`:

```csharp
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.Settings;
using Xunit;

namespace VisionSupport.Tests;

public class WiresharkSettingsTests
{
    [Fact]
    public void A_missing_file_gives_the_spec_defaults()
    {
        using var dir = new TempDir();
        WiresharkSettings s = new WiresharkSettingsStore(Path.Combine(dir.Path, "settings.json")).Load();

        Assert.Equal(1000, s.Thresholds.ResponseTimeoutMs);
        Assert.Equal(200L * 1024 * 1024, s.StoreMaxBytes);
        Assert.Equal(500_000, s.StoreMaxCount);
        Assert.Equal("Rapixo", s.CxpDeviceNameMatch);
    }

    [Fact]
    public void Pinned_targets_thresholds_and_cxp_nodes_round_trip()
    {
        using var dir = new TempDir();
        var store = new WiresharkSettingsStore(Path.Combine(dir.Path, "sub", "settings.json"));
        var s = new WiresharkSettings();
        s.Pinned.Add(new PinnedTarget { Id = "192.168.0.10:5000", Kind = TargetKind.Mc, Name = "검사기 PLC" });
        s.Thresholds.SilenceSeconds = 9;
        s.CxpNodes.Add(new CxpNodeBinding { Module = "Interface", Node = "LinkStatus", Role = CxpNodeRole.LinkUp, Connection = 1 });

        store.Save(s);
        WiresharkSettings back = store.Load();

        Assert.Equal("검사기 PLC", Assert.Single(back.Pinned).Name);
        Assert.Equal(TargetKind.Mc, back.Pinned[0].Kind);
        Assert.Equal(9, back.Thresholds.SilenceSeconds);
        Assert.Equal(CxpNodeRole.LinkUp, Assert.Single(back.CxpNodes).Role);
    }

    [Fact]
    public void A_corrupt_file_gives_defaults_instead_of_throwing()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Empty(new WiresharkSettingsStore(path).Load().Pinned);
    }
}
```

Run → 빌드 실패 확인.

- [ ] **Step 2: 구현**

`Settings/WiresharkSettings.cs`:

```csharp
using VisionSupport.Wireshark.Health;

namespace VisionSupport.Wireshark.Settings;

public sealed class PinnedTarget
{
    public string Id { get; set; } = string.Empty;
    public TargetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
}

public enum CxpNodeRole { LinkUp, Speed, ErrorCount, FrameCount, DropCount }

/// <summary>One GenICam feature to read each poll, and what it means. Which names exist depends on
/// the board and driver, so they come from the diagnostics dump (Task 17), not from code.</summary>
public sealed class CxpNodeBinding
{
    /// <summary>"Interface" (the grabber's node map) or "Device" (the camera's).</summary>
    public string Module { get; set; } = "Interface";
    public string Node { get; set; } = string.Empty;
    public CxpNodeRole Role { get; set; }
    public int Connection { get; set; }
}

public sealed class WiresharkSettings
{
    public List<PinnedTarget> Pinned { get; set; } = new();
    public HealthThresholds Thresholds { get; set; } = new();
    public int McPortMin { get; set; } = 5000;
    public int McPortMax { get; set; } = 5010;
    /// <summary>Bytes pktmon keeps of each packet. Enough for every header this tool reads.</summary>
    public int CapturePacketSize { get; set; } = 256;
    public long StoreMaxBytes { get; set; } = 200L * 1024 * 1024;
    public int StoreMaxCount { get; set; } = 500_000;
    public int? LastNicComponentId { get; set; }
    public string CxpDeviceNameMatch { get; set; } = "Rapixo";
    /// <summary>Null: look for Matrox.CoaXPress.cti on GENICAM_GENTL64_PATH.</summary>
    public string? GenTLProducerPath { get; set; }
    public List<CxpNodeBinding> CxpNodes { get; set; } = new();
}
```

`Settings/WiresharkSettingsStore.cs` (이미지 변환기 `SettingsStore`와 같은 규칙 — D:\Datas 우선, 옛 %AppData% 폴백, 저장은 새 경로):

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisionSupport.Wireshark.Settings;

public sealed class WiresharkSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    /// <summary>Where settings would have lived under %AppData%. Read only when the new path has no file.</summary>
    private readonly string? _legacyFilePath;

    public WiresharkSettingsStore(string filePath) : this(filePath, null)
    {
    }

    private WiresharkSettingsStore(string filePath, string? legacyFilePath)
    {
        _filePath = filePath;
        _legacyFilePath = legacyFilePath;
    }

    public static WiresharkSettingsStore Default => new(
        Path.Combine(@"D:\Datas", "VisionSupport", "Wireshark", "settings.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VisionSupport", "Wireshark", "settings.json"));

    /// <summary>Where settings live - and where diagnostics dumps go, next to them.</summary>
    public string DataDirectory => Path.GetDirectoryName(_filePath) ?? ".";

    public WiresharkSettings Load()
    {
        try
        {
            string path = _filePath;
            if (!File.Exists(path) && _legacyFilePath is not null && File.Exists(_legacyFilePath))
            {
                path = _legacyFilePath;
            }

            if (!File.Exists(path)) return new WiresharkSettings();
            return JsonSerializer.Deserialize<WiresharkSettings>(File.ReadAllText(path), JsonOptions)
                ?? new WiresharkSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new WiresharkSettings();
        }
    }

    public void Save(WiresharkSettings settings)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
```

- [ ] **Step 3: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~WiresharkSettingsTests"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/VisionSupport.Wireshark/Settings tests/VisionSupport.Tests/WiresharkSettingsTests.cs
git commit -m "feat(wireshark): persist pinned targets and thresholds under D:\Datas"
```

---

### Task 13: pktmon 캡처 소스와 NIC 목록

**Files:**
- Create: `src/VisionSupport.Wireshark/Capture/RawFrame.cs`
- Create: `src/VisionSupport.Wireshark/Capture/Pktmon.cs`
- Create: `src/VisionSupport.Wireshark/Capture/NicCatalog.cs`
- Create: `src/VisionSupport.Wireshark/Capture/RecentPacketIds.cs`
- Create: `src/VisionSupport.Wireshark/Capture/PktmonSource.cs`
- Modify: `src/VisionSupport.Wireshark/VisionSupport.Wireshark.csproj` (`InternalsVisibleTo`)
- Test: `tests/VisionSupport.Tests/PktmonParsingTests.cs`

**Interfaces:**
- Consumes: Task 1에서 확인한 이벤트 필드 이름과 `pktmon start` 인자 (다르면 아래 코드를 그 값으로 고칠 것)
- Produces:
  - `readonly record struct RawFrame(DateTime Time, byte[] Data, int OriginalLength, int ComponentId)`
  - `record NicInfo(int ComponentId, string Mac, string Name, bool IsUp)` (`ToString()` = `"{Name} ({Mac})"`)
  - `static class NicCatalog`: `ParseComponentList(string pktmonOutput) : IReadOnlyList<(int Id, string Mac, string Name)>`, `Query() : IReadOnlyList<NicInfo>`, `IsUp(string mac) : bool?`
  - `internal sealed class RecentPacketIds(int capacity)`: `bool Add(ulong group, uint number)` (처음이면 true)
  - `sealed class PktmonSource(Action<RawFrame> onFrame) : IDisposable`: `const string SessionName`, `Start(int packetSize)`(실패 시 `InvalidOperationException`), `Stop()`, `long EventsLost`, `event Action<string>? Faulted`
- `onFrame`은 ETW 펌프 스레드에서 호출된다.

- [ ] **Step 1: 실패하는 테스트**

Task 1에서 저장한 `pktmon comp list` 원문이 아래 샘플과 모양이 다르면 그 원문으로 샘플을 바꾼다.

`tests/VisionSupport.Tests/PktmonParsingTests.cs`:

```csharp
using VisionSupport.Wireshark.Capture;
using Xunit;

namespace VisionSupport.Tests;

public class PktmonParsingTests
{
    private const string English = """
        Network Adapters:
          Id  MAC Address        Name
          --  -----------        ----
           9  00-15-5D-01-02-03  Intel(R) Ethernet Connection I219-V
          12  A0-B1-C2-D3-E4-F5  Realtek PCIe GbE Family Controller #2

        Virtual Switches:
          Id  Name
          --  ----
          20  Default Switch
        """;

    private const string Korean = """
        네트워크 어댑터:
          ID  MAC 주소           이름
          --  -----------        ----
           9  00-15-5D-01-02-03  Intel(R) Ethernet Connection I219-V
        """;

    [Fact]
    public void Adapter_rows_are_read_and_other_sections_ignored()
    {
        var rows = NicCatalog.ParseComponentList(English);

        Assert.Equal(new[] { 9, 12 }, rows.Select(r => r.Id));
        Assert.Equal("A0-B1-C2-D3-E4-F5", rows[1].Mac);
        Assert.Equal("Realtek PCIe GbE Family Controller #2", rows[1].Name);
    }

    [Fact]
    public void Localised_headers_do_not_matter()
        => Assert.Equal(9, Assert.Single(NicCatalog.ParseComponentList(Korean)).Id);

    [Fact]
    public void Recent_packet_ids_report_a_repeat_once()
    {
        var ids = new RecentPacketIds(capacity: 2);

        Assert.True(ids.Add(1, 1));
        Assert.False(ids.Add(1, 1));
        Assert.True(ids.Add(1, 2));
        Assert.True(ids.Add(1, 3));
        Assert.True(ids.Add(1, 1)); // forgotten: capacity 2
    }
}
```

Run → 빌드 실패 확인.

- [ ] **Step 2: csproj에 테스트 공개**

`VisionSupport.Wireshark.csproj`의 `</PropertyGroup>` 다음:

```xml
  <!-- Pure helpers (pktmon output parsing, dedupe) are tested without growing the public surface. -->
  <ItemGroup>
    <InternalsVisibleTo Include="VisionSupport.Tests" />
  </ItemGroup>
```

- [ ] **Step 3: 구현**

`Capture/RawFrame.cs`:

```csharp
namespace VisionSupport.Wireshark.Capture;

/// <summary>One frame as pktmon delivered it: possibly cut at --pkt-size, tagged with the NIC component.</summary>
public readonly record struct RawFrame(DateTime Time, byte[] Data, int OriginalLength, int ComponentId);
```

`Capture/Pktmon.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VisionSupport.Wireshark.Capture;

/// <summary>Runs pktmon.exe and returns what it printed. Its console output is in the OEM code page.</summary>
internal static class Pktmon
{
    public static (int ExitCode, string Output) Run(string arguments)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var info = new ProcessStartInfo("pktmon.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };

        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException("pktmon.exe를 실행할 수 없음");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new InvalidOperationException($"pktmon {arguments} 응답 없음");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
```

`Capture/NicCatalog.cs`:

```csharp
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace VisionSupport.Wireshark.Capture;

public sealed record NicInfo(int ComponentId, string Mac, string Name, bool IsUp)
{
    public override string ToString() => $"{Name} ({Mac})";
}

/// <summary>
/// pktmon's NIC component ids, joined to Windows' own adapter names by MAC. pktmon tags each
/// packet with its component id; that is the only way to tell which NIC a frame came through.
/// </summary>
public static class NicCatalog
{
    // "  9  00-15-5D-01-02-03  Intel(R) ..." - headers are localised, so only the row shape is matched.
    private static readonly Regex Row = new(
        @"^\s*(\d+)\s+((?:[0-9A-Fa-f]{2}-){5}[0-9A-Fa-f]{2})\s+(.+?)\s*$", RegexOptions.Multiline);

    public static IReadOnlyList<(int Id, string Mac, string Name)> ParseComponentList(string output)
        => Row.Matches(output)
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value))
            .ToList();

    public static IReadOnlyList<NicInfo> Query()
    {
        (int code, string output) = Pktmon.Run("comp list");
        if (code != 0) throw new InvalidOperationException(output.Trim());

        Dictionary<string, NetworkInterface> byMac = Adapters();
        return ParseComponentList(output)
            .Select(r => byMac.TryGetValue(r.Mac, out NetworkInterface? n)
                ? new NicInfo(r.Id, r.Mac, n.Name, n.OperationalStatus == OperationalStatus.Up)
                : new NicInfo(r.Id, r.Mac, r.Name, false))
            .ToList();
    }

    public static bool? IsUp(string mac)
        => Adapters().TryGetValue(mac, out NetworkInterface? n) ? n.OperationalStatus == OperationalStatus.Up : null;

    private static Dictionary<string, NetworkInterface> Adapters()
    {
        var map = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
        {
            string mac = string.Join("-", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            if (mac.Length == 17) map.TryAdd(mac, n);
        }
        return map;
    }
}
```

`Capture/RecentPacketIds.cs`:

```csharp
namespace VisionSupport.Wireshark.Capture;

/// <summary>pktmon reports one packet at every component edge it crosses; this keeps the first sighting.</summary>
internal sealed class RecentPacketIds
{
    private readonly HashSet<(ulong, uint)> _seen = new();
    private readonly Queue<(ulong, uint)> _order = new();
    private readonly int _capacity;

    public RecentPacketIds(int capacity) => _capacity = capacity;

    public bool Add(ulong group, uint number)
    {
        var key = (group, number);
        if (!_seen.Add(key)) return false;
        _order.Enqueue(key);
        if (_order.Count > _capacity) _seen.Remove(_order.Dequeue());
        return true;
    }
}
```

`Capture/PktmonSource.cs`:

```csharp
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace VisionSupport.Wireshark.Capture;

/// <summary>
/// Live packets from Windows' built-in packet monitor, with nothing to install: `pktmon start`
/// makes the driver emit one ETW event per packet, and a real-time session of ours receives them.
///
/// Both halves outlive this process if it dies - the ETW session and the pktmon capture - so
/// <see cref="Start"/> first clears up after a previous run that never reached <see cref="Stop"/>.
/// </summary>
public sealed class PktmonSource : IDisposable
{
    public const string SessionName = "VisionSupport-Wireshark";

    private static readonly Guid PktMonProvider = new("4d4f80d9-c8bd-4d73-bb5b-19c90402c5ac");
    private const int EthernetPacketType = 1;

    private readonly Action<RawFrame> _onFrame;
    private readonly RecentPacketIds _seen = new(8192);
    private TraceEventSession? _session;
    private Thread? _pump;
    private bool _pktmonStarted;

    public PktmonSource(Action<RawFrame> onFrame) => _onFrame = onFrame;

    /// <summary>The ETW pump died. Raised on the pump thread.</summary>
    public event Action<string>? Faulted;

    /// <summary>Events ETW dropped because we could not keep up. Cumulative.</summary>
    public long EventsLost => _session?.EventsLost ?? 0;

    public void Start(int packetSize)
    {
        if (_session is not null) return;

        // A leftover session means a previous run of ours crashed mid-capture, so the pktmon
        // capture it started is ours to stop too.
        if (StopLeftoverSession()) Pktmon.Run("stop");

        Pktmon.Run("filter remove");
        (int code, string output) = Pktmon.Run($"start --capture --comp nics --pkt-size {packetSize} -m memory -s 16");
        if (code != 0)
        {
            string reason = output.Trim();
            throw new InvalidOperationException(reason.Length > 0
                ? reason + " (다른 pktmon 캡처가 돌고 있다면 'pktmon stop' 후 다시 시도)"
                : $"pktmon 종료 코드 {code}");
        }
        _pktmonStarted = true;

        try
        {
            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(PktMonProvider, TraceEventLevel.Verbose, ulong.MaxValue);
            session.Source.Dynamic.All += OnEvent;
            _session = session;
            _pump = new Thread(Pump) { IsBackground = true, Name = "pktmon ETW" };
            _pump.Start();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        TraceEventSession? session = _session;
        _session = null;
        session?.Dispose();
        _pump?.Join(2000);
        _pump = null;

        if (!_pktmonStarted) return;
        _pktmonStarted = false;
        try
        {
            Pktmon.Run("stop");
        }
        catch (InvalidOperationException)
        {
            // Nothing more to do from here; the next Start cleans up.
        }
    }

    public void Dispose() => Stop();

    private void Pump()
    {
        try
        {
            _session?.Source.Process();
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex.Message);
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if (e.PayloadIndex("Payload") < 0) return;
        if (Convert.ToInt32(e.PayloadByName("PacketType")) != EthernetPacketType) return;
        if (!_seen.Add(Convert.ToUInt64(e.PayloadByName("PktGroupId")), Convert.ToUInt32(e.PayloadByName("PktNumber")))) return;
        if (e.PayloadByName("Payload") is not byte[] { Length: > 0 } data) return;

        _onFrame(new RawFrame(e.TimeStamp, data,
            Convert.ToInt32(e.PayloadByName("OriginalPayloadSize")),
            Convert.ToInt32(e.PayloadByName("ComponentId"))));
    }

    private static bool StopLeftoverSession()
    {
        if (!TraceEventSession.GetActiveSessionNames().Contains(SessionName)) return false;
        using var leftover = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
        leftover.Stop();
        return true;
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet build VisionSupport.sln` → 성공
Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~PktmonParsingTests"` → PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/VisionSupport.Wireshark/Capture src/VisionSupport.Wireshark/VisionSupport.Wireshark.csproj tests/VisionSupport.Tests/PktmonParsingTests.cs
git commit -m "feat(wireshark): receive live packets from pktmon over ETW"
```

---

### Task 14: CXP 감시 — GenTL 호출, 최소 GenApi 해석, 장치 존재, 진단 덤프

**Files:**
- Create: `src/VisionSupport.Wireshark/Cxp/GenApiNodeMap.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/GenApiXml.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/GenTL.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/IGenTLSession.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/GenTLSession.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/RapixoPresence.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/CxpMonitor.cs`
- Create: `src/VisionSupport.Wireshark/Cxp/CxpDiagnostics.cs`
- Test: `tests/VisionSupport.Tests/GenApiNodeMapTests.cs`, `tests/VisionSupport.Tests/CxpMonitorTests.cs`

**Interfaces:**
- Consumes: Task 9 `CxpReport`, `CxpMode`, `CxpConnectionStatus`; Task 12 `WiresharkSettings`, `CxpNodeBinding`, `CxpNodeRole`
- Produces:
  - `GenApiNodeMap.Parse(string xml)`, `IEnumerable<string> NodeNames`, `(long Value, string Text) Read(string name, Func<ulong, int, byte[]> read)` — 상수 `<Address>`만 있는 `IntReg`/`MaskedIntReg`와 그것을 `pValue`로 가리키는 `Integer`/`Boolean`/`Enumeration`, 그리고 `<Value>` 상수 `Integer`. 그 외(pAddress, SwissKnife 등)는 `NotSupportedException`.
  - `GenApiXml.Load(string url, Func<ulong, int, byte[]> read) : string` — `Local:이름(.zip);주소hex;길이hex` 와 `File:` URL
  - `record NodeReading(CxpNodeBinding Binding, long Value, string Text)`
  - `interface IGenTLSession : IDisposable { IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings); }`
  - `sealed class CxpMonitor : IDisposable`: `CxpMonitor(Func<(bool Present, string Name)> presence, Func<IGenTLSession>? openSession, IReadOnlyList<CxpNodeBinding> bindings, TimeProvider clock)`, `static CxpMonitor Create(WiresharkSettings, TimeProvider)`, `CxpReport Poll()`, `const string BoardId = "Rapixo#0"`
  - `static class CxpDiagnostics { string Run(WiresharkSettings) }`
  - `static class RapixoPresence { (bool Present, string Name) Query(string match) }`
- **불변식:** `Poll()`이 반환할 때 GenTL TL/Interface/Device 핸들은 모두 닫혀 있다(Review Focus). 실패 후 1분 동안은 다시 열지 않는다.

- [ ] **Step 1: 실패하는 테스트 — GenApi**

`tests/VisionSupport.Tests/GenApiNodeMapTests.cs`:

```csharp
using System.IO.Compression;
using System.Text;
using VisionSupport.Wireshark.Cxp;
using Xunit;

namespace VisionSupport.Tests;

public class GenApiNodeMapTests
{
    private const string Xml = """
        <RegisterDescription xmlns="http://www.genicam.org/GenApi/Version_1_1">
          <Integer Name="ErrorCount"><pValue>ErrorCountReg</pValue></Integer>
          <IntReg Name="ErrorCountReg"><Address>0x100</Address><Length>4</Length><AccessMode>RO</AccessMode><pPort>Device</pPort><Sign>Unsigned</Sign><Endianess>BigEndian</Endianess></IntReg>
          <Boolean Name="LinkUp"><pValue>LinkReg</pValue><OnValue>1</OnValue></Boolean>
          <MaskedIntReg Name="LinkReg"><Address>0x200</Address><Length>4</Length><AccessMode>RO</AccessMode><pPort>Device</pPort><Bit>3</Bit><Endianess>LittleEndian</Endianess></MaskedIntReg>
          <Enumeration Name="Speed">
            <EnumEntry Name="CXP6"><Value>0x48</Value></EnumEntry>
            <EnumEntry Name="CXP12"><Value>0x58</Value></EnumEntry>
            <pValue>SpeedReg</pValue>
          </Enumeration>
          <IntReg Name="SpeedReg"><Address>0x300</Address><Address>0x4</Address><Length>4</Length><AccessMode>RO</AccessMode><pPort>Device</pPort><Endianess>BigEndian</Endianess></IntReg>
          <MaskedIntReg Name="TopByte"><Address>0x100</Address><Length>4</Length><pPort>Device</pPort><LSB>7</LSB><MSB>0</MSB><Endianess>BigEndian</Endianess></MaskedIntReg>
          <Integer Name="Fixed"><Value>7</Value></Integer>
          <IntReg Name="Indexed"><pAddress>Somewhere</pAddress><Length>4</Length></IntReg>
          <Integer Name="ViaIndexed"><pValue>Indexed</pValue></Integer>
        </RegisterDescription>
        """;

    private static readonly Dictionary<ulong, byte[]> Memory = new()
    {
        [0x100] = new byte[] { 0x01, 0x00, 0x01, 0x02 },
        [0x200] = new byte[] { 0x08, 0x00, 0x00, 0x00 },
        [0x304] = new byte[] { 0x00, 0x00, 0x00, 0x58 },
    };

    private static byte[] Read(ulong address, int length) => Memory[address].AsSpan(0, length).ToArray();

    private readonly GenApiNodeMap _map = GenApiNodeMap.Parse(Xml);

    [Fact]
    public void An_integer_reads_its_big_endian_register()
        => Assert.Equal(0x01000102, _map.Read("ErrorCount", Read).Value);

    [Fact]
    public void A_boolean_reads_one_bit_of_a_little_endian_register()
        => Assert.Equal((1L, "True"), _map.Read("LinkUp", Read));

    [Fact]
    public void An_enumeration_names_its_value_and_addresses_add_up()
        => Assert.Equal((0x58L, "CXP12"), _map.Read("Speed", Read));

    /// <summary>In a big-endian register GenApi numbers bit 0 as the most significant one.</summary>
    [Fact]
    public void Big_endian_bit_numbering_counts_from_the_top()
        => Assert.Equal(0x01, _map.Read("TopByte", Read).Value);

    [Fact]
    public void A_constant_integer_needs_no_read()
        => Assert.Equal(7, _map.Read("Fixed", (_, _) => throw new InvalidOperationException()).Value);

    [Fact]
    public void Computed_addresses_are_refused_rather_than_guessed()
        => Assert.Throws<NotSupportedException>(() => _map.Read("ViaIndexed", Read));

    [Fact]
    public void Node_names_skip_enum_entries()
    {
        Assert.Contains("Speed", _map.NodeNames);
        Assert.DoesNotContain("CXP12", _map.NodeNames);
    }

    [Fact]
    public void A_local_zipped_url_is_read_from_the_port_and_unzipped()
    {
        var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("map.xml").Open(), Encoding.UTF8))
        {
            writer.Write(Xml);
        }
        byte[] bytes = zipped.ToArray();

        string xml = GenApiXml.Load($"Local:map.zip;1000;{bytes.Length:X}?SchemaVersion=1.1.0",
            (address, length) => address == 0x1000 ? bytes.AsSpan(0, length).ToArray() : throw new InvalidOperationException());

        Assert.Contains("ErrorCountReg", xml);
    }
}
```

- [ ] **Step 2: 실패하는 테스트 — CxpMonitor**

`tests/VisionSupport.Tests/CxpMonitorTests.cs`:

```csharp
using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Settings;
using Xunit;

namespace VisionSupport.Tests;

public class CxpMonitorTests
{
    private sealed class FakeSession : IGenTLSession
    {
        private readonly Func<IReadOnlyList<CxpNodeBinding>, IReadOnlyList<NodeReading>> _read;
        public FakeSession(Func<IReadOnlyList<CxpNodeBinding>, IReadOnlyList<NodeReading>> read) => _read = read;
        public bool Disposed { get; private set; }
        public IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings) => _read(bindings);
        public void Dispose() => Disposed = true;
    }

    private static readonly CxpNodeBinding Link0 = new() { Node = "Link0", Role = CxpNodeRole.LinkUp, Connection = 0 };
    private static readonly CxpNodeBinding Errors0 = new() { Node = "Err0", Role = CxpNodeRole.ErrorCount, Connection = 0 };
    private static readonly CxpNodeBinding Speed0 = new() { Node = "Speed0", Role = CxpNodeRole.Speed, Connection = 0 };
    private static readonly CxpNodeBinding Link1 = new() { Node = "Link1", Role = CxpNodeRole.LinkUp, Connection = 1 };

    private readonly ManualClock _clock = new();
    private readonly List<FakeSession> _opened = new();

    private CxpMonitor Monitor(bool present = true, Func<IReadOnlyList<CxpNodeBinding>, IReadOnlyList<NodeReading>>? read = null,
        bool producer = true, params CxpNodeBinding[] bindings)
    {
        Func<IGenTLSession>? open = producer
            ? () =>
            {
                var s = new FakeSession(read ?? (b => b.Select(x => new NodeReading(x, 1, "1")).ToList()));
                _opened.Add(s);
                return s;
            }
            : null;
        return new CxpMonitor(() => (present, "Matrox Rapixo CXP"), open, bindings, _clock);
    }

    [Fact]
    public void No_board_in_the_device_list_is_absent()
        => Assert.Equal(CxpMode.Absent, Monitor(present: false, bindings: Link0).Poll().Mode);

    [Fact]
    public void Without_a_producer_only_presence_is_watched()
    {
        CxpReport r = Monitor(producer: false, bindings: Link0).Poll();

        Assert.Equal(CxpMode.PresenceOnly, r.Mode);
        Assert.Contains("GenTL 프로듀서를 찾을 수 없음", r.Message);
    }

    [Fact]
    public void Without_bindings_only_presence_is_watched()
        => Assert.Contains("노드가 설정되지 않음", Monitor().Poll().Message);

    [Fact]
    public void Readings_are_grouped_per_connection()
    {
        CxpReport r = Monitor(read: b => new List<NodeReading>
        {
            new(Link0, 1, "True"), new(Errors0, 5, "5"), new(Speed0, 0x58, "CXP12"), new(Link1, 0, "False"),
        }, bindings: new[] { Link0, Errors0, Speed0, Link1 }).Poll();

        Assert.Equal(CxpMode.Full, r.Mode);
        Assert.Equal(new CxpConnectionStatus(0, true, "CXP12", 5, null, null), r.Connections[0]);
        Assert.Equal(new CxpConnectionStatus(1, false, "", null, null, null), r.Connections[1]);
    }

    /// <summary>VISION started after this tool must still be able to open the board.</summary>
    [Fact]
    public void Every_handle_is_released_before_poll_returns()
    {
        CxpMonitor monitor = Monitor(bindings: Link0);
        monitor.Poll();
        monitor.Poll();

        Assert.Equal(2, _opened.Count);
        Assert.All(_opened, s => Assert.True(s.Disposed));
    }

    [Fact]
    public void A_refused_open_degrades_to_presence_and_waits_a_minute_before_retrying()
    {
        int attempts = 0;
        var monitor = new CxpMonitor(() => (true, "Rapixo"),
            () => { attempts++; throw new GenTLException(-1004, "IFOpenDevice"); },
            new[] { Link0 }, _clock);

        CxpReport first = monitor.Poll();
        _clock.Advance(30);
        monitor.Poll();
        _clock.Advance(31);
        monitor.Poll();

        Assert.Equal(CxpMode.PresenceOnly, first.Mode);
        Assert.Contains("다른 프로세스가 사용 중", first.Message);
        Assert.Equal(2, attempts);
    }
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~GenApiNodeMapTests|FullyQualifiedName~CxpMonitorTests"` → 빌드 실패 확인.

- [ ] **Step 3: GenApi 해석기**

`Cxp/GenApiNodeMap.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// Just enough GenApi to read a status register: a feature that points (pValue) at a register
/// with a fixed address. Anything computed - pAddress, pIndex, SwissKnife - is refused instead of
/// guessed, because a wrong address would read some other register and show it as link state.
/// </summary>
public sealed class GenApiNodeMap
{
    private readonly Dictionary<string, XElement> _nodes;

    private GenApiNodeMap(Dictionary<string, XElement> nodes) => _nodes = nodes;

    public static GenApiNodeMap Parse(string xml)
    {
        XDocument doc = XDocument.Parse(xml);
        var nodes = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (XElement e in doc.Descendants())
        {
            if (e.Name.LocalName == "EnumEntry" || e.Attribute("Name")?.Value is not { } name) continue;
            nodes.TryAdd(name, e);
        }
        return new GenApiNodeMap(nodes);
    }

    public IEnumerable<string> NodeNames => _nodes.Keys;

    public (long Value, string Text) Read(string name, Func<ulong, int, byte[]> read)
    {
        XElement node = Get(name);
        switch (node.Name.LocalName)
        {
            case "Boolean":
            {
                long on = Child(node, "OnValue") is { } o ? ParseLong(o) : 1;
                bool value = ValueOf(node, read) == on;
                return (value ? 1 : 0, value ? "True" : "False");
            }
            case "Enumeration":
            {
                long value = ValueOf(node, read);
                string? entry = node.Elements().Where(e => e.Name.LocalName == "EnumEntry")
                    .FirstOrDefault(e => Child(e, "Value") is { } v && ParseLong(v) == value)
                    ?.Attribute("Name")?.Value;
                return (value, entry ?? value.ToString(CultureInfo.InvariantCulture));
            }
            case "Integer":
            case "IntReg":
            case "MaskedIntReg":
            {
                long value = ValueOf(node, read);
                return (value, value.ToString(CultureInfo.InvariantCulture));
            }
            default:
                throw new NotSupportedException($"{name}: {node.Name.LocalName} 노드는 읽지 않음");
        }
    }

    private long ValueOf(XElement node, Func<ulong, int, byte[]> read)
    {
        if (node.Name.LocalName is "IntReg" or "MaskedIntReg") return ReadRegister(node, read);
        if (Child(node, "Value") is { } constant) return ParseLong(constant);
        if (Child(node, "pValue") is { } pointer) return ValueOf(Get(pointer), read);
        throw new NotSupportedException($"{node.Attribute("Name")?.Value}: 값 위치를 알 수 없음");
    }

    private static long ReadRegister(XElement reg, Func<ulong, int, byte[]> read)
    {
        string name = reg.Attribute("Name")?.Value ?? "?";
        if (reg.Elements().Any(e => e.Name.LocalName is "pAddress" or "pIndex" or "IntSwissKnife"))
        {
            throw new NotSupportedException($"{name}: 계산되는 주소는 지원하지 않음");
        }

        ulong address = reg.Elements().Where(e => e.Name.LocalName == "Address")
            .Aggregate(0UL, (sum, e) => sum + (ulong)ParseLong(e.Value));
        int length = Child(reg, "Length") is { } l ? (int)ParseLong(l) : 4;
        bool little = Child(reg, "Endianess") == "LittleEndian";

        byte[] bytes = read(address, length);
        ulong raw = 0;
        for (int i = 0; i < length; i++)
        {
            raw = little ? raw | (ulong)bytes[i] << (8 * i) : raw << 8 | bytes[i];
        }

        if (reg.Name.LocalName == "MaskedIntReg")
        {
            int lsb, msb;
            if (Child(reg, "Bit") is { } bit) lsb = msb = (int)ParseLong(bit);
            else
            {
                lsb = (int)ParseLong(Child(reg, "LSB") ?? "0");
                msb = (int)ParseLong(Child(reg, "MSB") ?? (length * 8 - 1).ToString(CultureInfo.InvariantCulture));
            }
            // GenApi numbers bits from the most significant end in big-endian registers.
            int width = length * 8;
            int lo = little ? lsb : width - 1 - lsb;
            int hi = little ? msb : width - 1 - msb;
            if (lo > hi) (lo, hi) = (hi, lo);
            int bits = hi - lo + 1;
            raw = (raw >> lo) & (bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1);
        }
        return (long)raw;
    }

    private XElement Get(string name) => _nodes.TryGetValue(name, out XElement? e)
        ? e
        : throw new NotSupportedException($"{name}: 노드맵에 없음");

    private static string? Child(XElement e, string localName)
        => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName)?.Value.Trim();

    private static long ParseLong(string text)
    {
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(text, CultureInfo.InvariantCulture);
    }
}
```

`Cxp/GenApiXml.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>Fetches a module's GenApi XML from the URL its port advertises (GenTL "Local:" or "File:").</summary>
public static class GenApiXml
{
    public static string Load(string url, Func<ulong, int, byte[]> read)
    {
        string location = url.Split('?')[0];
        if (location.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = location["local:".Length..].Split(';');
            if (parts.Length < 3) throw new NotSupportedException($"XML 위치를 해석할 수 없음: {url}");
            string name = parts[0].TrimStart('/');
            ulong address = ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int length = int.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte[] bytes = read(address, length);
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Unzip(bytes)
                : Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        if (location.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            string path = new Uri(location).LocalPath;
            return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Unzip(File.ReadAllBytes(path))
                : File.ReadAllText(path);
        }
        throw new NotSupportedException($"지원하지 않는 XML 위치: {url}");
    }

    private static string Unzip(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException("ZIP 안에 XML이 없음");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 4: GenTL 바인딩**

`Cxp/GenTL.cs`:

```csharp
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VisionSupport.Wireshark.Cxp;

public sealed class GenTLException : Exception
{
    public GenTLException(int code, string call)
        : base($"{call} 실패 (GC_ERROR {code}{Explain(code)})") => Code = code;

    public int Code { get; }

    private static string Explain(int code) => code switch
    {
        -1004 => ", 다른 프로세스가 사용 중",
        -1005 => ", 접근 거부",
        -1003 => ", 프로듀서가 지원하지 않음",
        _ => string.Empty,
    };
}

/// <summary>
/// The GenTL C entry points this tool needs, bound at run time from the producer (.cti) so the
/// build needs nothing from Matrox. Calls are stdcall per the GenTL headers' GC_CALLTYPE.
/// </summary>
internal sealed class GenTL : IDisposable
{
    public const string MatroxProducer = "Matrox.CoaXPress.cti";
    private const int DeviceAccessReadOnly = 2;
    private const int DeviceInfoAccessStatus = 5;
    private const int UrlInfoUrl = 0;
    private const int ReadChunk = 64 * 1024;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HandleOut(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HandleIn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HandleHandleOut(IntPtr handle, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UpdateList(IntPtr handle, out byte changed, ulong timeoutMs);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(IntPtr handle, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetId(IntPtr handle, uint index, byte[]? buffer, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenById(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string id, out IntPtr opened);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenDeviceFn(IntPtr iface, [MarshalAs(UnmanagedType.LPStr)] string id, int flags, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DeviceInfoFn(IntPtr iface, [MarshalAs(UnmanagedType.LPStr)] string id, int command, out int type, byte[]? buffer, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UrlInfoFn(IntPtr port, uint index, int command, out int type, byte[]? buffer, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadPortFn(IntPtr port, ulong address, byte[] buffer, ref nuint size);

    private delegate int StringCall(byte[]? buffer, ref nuint size);

    private readonly IntPtr _lib;
    private readonly NoArgs _closeLib;
    private readonly HandleOut _tlOpen;
    private readonly HandleIn _tlClose, _ifClose, _devClose;
    private readonly UpdateList _tlUpdate, _ifUpdate;
    private readonly GetCount _tlCount, _ifCount, _urlCount;
    private readonly GetId _tlGetId, _ifGetId;
    private readonly OpenById _tlOpenIf;
    private readonly OpenDeviceFn _ifOpenDev;
    private readonly DeviceInfoFn _ifDevInfo;
    private readonly HandleHandleOut _devGetPort;
    private readonly UrlInfoFn _urlInfo;
    private readonly ReadPortFn _readPort;

    public GenTL(string producerPath)
    {
        _lib = NativeLibrary.Load(producerPath);
        try
        {
            _closeLib = Bind<NoArgs>("GCCloseLib");
            _tlOpen = Bind<HandleOut>("TLOpen");
            _tlClose = Bind<HandleIn>("TLClose");
            _ifClose = Bind<HandleIn>("IFClose");
            _devClose = Bind<HandleIn>("DevClose");
            _tlUpdate = Bind<UpdateList>("TLUpdateInterfaceList");
            _ifUpdate = Bind<UpdateList>("IFUpdateDeviceList");
            _tlCount = Bind<GetCount>("TLGetNumInterfaces");
            _ifCount = Bind<GetCount>("IFGetNumDevices");
            _urlCount = Bind<GetCount>("GCGetNumPortURLs");
            _tlGetId = Bind<GetId>("TLGetInterfaceID");
            _ifGetId = Bind<GetId>("IFGetDeviceID");
            _tlOpenIf = Bind<OpenById>("TLOpenInterface");
            _ifOpenDev = Bind<OpenDeviceFn>("IFOpenDevice");
            _ifDevInfo = Bind<DeviceInfoFn>("IFGetDeviceInfo");
            _devGetPort = Bind<HandleHandleOut>("DevGetPort");
            _urlInfo = Bind<UrlInfoFn>("GCGetPortURLInfo");
            _readPort = Bind<ReadPortFn>("GCReadPort");
            Check(Bind<NoArgs>("GCInitLib")(), "GCInitLib");
        }
        catch
        {
            NativeLibrary.Free(_lib);
            throw;
        }
    }

    /// <summary>The configured path, else the Matrox producer on GENICAM_GENTL64_PATH, else null.</summary>
    public static string? FindProducer(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        string? paths = Environment.GetEnvironmentVariable("GENICAM_GENTL64_PATH");
        return paths?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), MatroxProducer))
            .FirstOrDefault(File.Exists);
    }

    public IntPtr OpenSystem()
    {
        Check(_tlOpen(out IntPtr tl), "TLOpen");
        return tl;
    }

    public IReadOnlyList<string> InterfaceIds(IntPtr tl)
    {
        Check(_tlUpdate(tl, out _, 1000), "TLUpdateInterfaceList");
        Check(_tlCount(tl, out uint count), "TLGetNumInterfaces");
        return Enumerable.Range(0, (int)count)
            .Select(i => ReadString((byte[]? b, ref nuint s) => _tlGetId(tl, (uint)i, b, ref s), "TLGetInterfaceID"))
            .ToList();
    }

    public IntPtr OpenInterface(IntPtr tl, string id)
    {
        Check(_tlOpenIf(tl, id, out IntPtr iface), "TLOpenInterface");
        return iface;
    }

    public IReadOnlyList<string> DeviceIds(IntPtr iface)
    {
        Check(_ifUpdate(iface, out _, 1000), "IFUpdateDeviceList");
        Check(_ifCount(iface, out uint count), "IFGetNumDevices");
        return Enumerable.Range(0, (int)count)
            .Select(i => ReadString((byte[]? b, ref nuint s) => _ifGetId(iface, (uint)i, b, ref s), "IFGetDeviceID"))
            .ToList();
    }

    /// <summary>DEVICE_ACCESS_STATUS: 1 RW, 2 RO, 3 no access, 4 busy, 5 open RW, 6 open RO.</summary>
    public int AccessStatus(IntPtr iface, string deviceId)
    {
        var buffer = new byte[4];
        nuint size = 4;
        Check(_ifDevInfo(iface, deviceId, DeviceInfoAccessStatus, out _, buffer, ref size), "IFGetDeviceInfo");
        return BitConverter.ToInt32(buffer, 0);
    }

    public IntPtr OpenDeviceReadOnly(IntPtr iface, string deviceId)
    {
        Check(_ifOpenDev(iface, deviceId, DeviceAccessReadOnly, out IntPtr device), "IFOpenDevice");
        return device;
    }

    /// <summary>The remote (camera) port of an open device. Not closed separately.</summary>
    public IntPtr DevicePort(IntPtr device)
    {
        Check(_devGetPort(device, out IntPtr port), "DevGetPort");
        return port;
    }

    public IReadOnlyList<string> PortUrls(IntPtr port)
    {
        Check(_urlCount(port, out uint count), "GCGetNumPortURLs");
        return Enumerable.Range(0, (int)count)
            .Select(i => ReadString((byte[]? b, ref nuint s) => _urlInfo(port, (uint)i, UrlInfoUrl, out _, b, ref s), "GCGetPortURLInfo"))
            .ToList();
    }

    public byte[] Read(IntPtr port, ulong address, int length)
    {
        var result = new byte[length];
        for (int done = 0; done < length; done += ReadChunk)
        {
            int count = Math.Min(ReadChunk, length - done);
            var chunk = new byte[count];
            nuint size = (nuint)count;
            Check(_readPort(port, address + (ulong)done, chunk, ref size), "GCReadPort");
            Buffer.BlockCopy(chunk, 0, result, done, count);
        }
        return result;
    }

    public void CloseDevice(IntPtr device) => _devClose(device);

    public void CloseInterface(IntPtr iface) => _ifClose(iface);

    public void CloseSystem(IntPtr tl) => _tlClose(tl);

    public void Dispose()
    {
        _closeLib();
        NativeLibrary.Free(_lib);
    }

    private static void Check(int code, string call)
    {
        if (code != 0) throw new GenTLException(code, call);
    }

    private T Bind<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    private static string ReadString(StringCall call, string name)
    {
        nuint size = 0;
        Check(call(null, ref size), name);
        var buffer = new byte[(int)size];
        Check(call(buffer, ref size), name);
        return Encoding.ASCII.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }
}
```

`Cxp/IGenTLSession.cs`:

```csharp
using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

public sealed record NodeReading(CxpNodeBinding Binding, long Value, string Text);

/// <summary>GenTL handles opened for one poll and closed by Dispose. Never kept between polls.</summary>
public interface IGenTLSession : IDisposable
{
    IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings);
}
```

`Cxp/GenTLSession.cs`:

```csharp
using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// Opens the first interface (and, only if a binding asks for it, the first camera read-only),
/// reads, and closes everything on Dispose - so VISION opening the board later is never blocked
/// by a handle this tool forgot to let go of.
/// </summary>
internal sealed class GenTLSession : IGenTLSession
{
    private readonly GenTL _gentl;
    private readonly Dictionary<string, GenApiNodeMap> _maps;
    private IntPtr _tl, _iface, _device;

    public GenTLSession(GenTL gentl, Dictionary<string, GenApiNodeMap> nodeMapCache, bool needDevice)
    {
        _gentl = gentl;
        _maps = nodeMapCache;
        try
        {
            _tl = gentl.OpenSystem();
            string ifaceId = gentl.InterfaceIds(_tl).FirstOrDefault()
                ?? throw new InvalidOperationException("GenTL 인터페이스가 없음");
            _iface = gentl.OpenInterface(_tl, ifaceId);
            if (needDevice)
            {
                string deviceId = gentl.DeviceIds(_iface).FirstOrDefault()
                    ?? throw new InvalidOperationException("카메라가 보이지 않음");
                _device = gentl.OpenDeviceReadOnly(_iface, deviceId);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings)
    {
        var readings = new List<NodeReading>(bindings.Count);
        foreach (CxpNodeBinding binding in bindings)
        {
            bool device = binding.Module == "Device";
            IntPtr port = device ? _gentl.DevicePort(_device) : _iface;
            if (!_maps.TryGetValue(binding.Module, out GenApiNodeMap? map))
            {
                string url = _gentl.PortUrls(port).FirstOrDefault()
                    ?? throw new InvalidOperationException($"{binding.Module} XML 위치 없음");
                map = GenApiNodeMap.Parse(GenApiXml.Load(url, (a, l) => _gentl.Read(port, a, l)));
                _maps[binding.Module] = map;
            }
            (long value, string text) = map.Read(binding.Node, (a, l) => _gentl.Read(port, a, l));
            readings.Add(new NodeReading(binding, value, text));
        }
        return readings;
    }

    public void Dispose()
    {
        if (_device != IntPtr.Zero) _gentl.CloseDevice(_device);
        if (_iface != IntPtr.Zero) _gentl.CloseInterface(_iface);
        if (_tl != IntPtr.Zero) _gentl.CloseSystem(_tl);
        _device = _iface = _tl = IntPtr.Zero;
    }
}
```

`Cxp/RapixoPresence.cs`:

```csharp
using System.Management;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>Whether the grabber is in Device Manager and healthy - works no matter who holds the board.</summary>
public static class RapixoPresence
{
    public static (bool Present, string Name) Query(string match)
    {
        string safe = new(match.Where(c => c is not ('\'' or '%' or '_' or '[')).ToArray());
        using var searcher = new ManagementObjectSearcher(
            $"SELECT Name, Status FROM Win32_PnPEntity WHERE Name LIKE '%{safe}%'");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            using (o)
            {
                string name = o["Name"] as string ?? match;
                return (string.Equals(o["Status"] as string, "OK", StringComparison.OrdinalIgnoreCase), name);
            }
        }
        return (false, match);
    }
}
```

- [ ] **Step 5: 모니터와 진단**

`Cxp/CxpMonitor.cs`:

```csharp
using System.IO;
using System.Xml;
using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// One poll = is the board there, and if it can be read, what do its link/counter nodes say.
/// Every failure to read degrades to presence-only rather than failing: VISION holding the board
/// is the normal case on a running line, not an error.
/// </summary>
public sealed class CxpMonitor : IDisposable
{
    public const string BoardId = "Rapixo#0";
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly Func<(bool Present, string Name)> _presence;
    private readonly Func<IGenTLSession>? _openSession;
    private readonly IReadOnlyList<CxpNodeBinding> _bindings;
    private readonly TimeProvider _clock;
    private Action? _dispose;
    private DateTimeOffset? _failedAt;
    private string _failure = string.Empty;

    public CxpMonitor(Func<(bool Present, string Name)> presence, Func<IGenTLSession>? openSession,
        IReadOnlyList<CxpNodeBinding> bindings, TimeProvider clock)
    {
        _presence = presence;
        _openSession = openSession;
        _bindings = bindings;
        _clock = clock;
    }

    public static CxpMonitor Create(WiresharkSettings settings, TimeProvider clock)
    {
        string? producer = GenTL.FindProducer(settings.GenTLProducerPath);
        GenTL? gentl = null;
        var cache = new Dictionary<string, GenApiNodeMap>();
        bool needDevice = settings.CxpNodes.Any(b => b.Module == "Device");
        Func<IGenTLSession>? open = producer is null
            ? null
            : () => new GenTLSession(gentl ??= new GenTL(producer), cache, needDevice);

        return new CxpMonitor(() => RapixoPresence.Query(settings.CxpDeviceNameMatch), open,
            settings.CxpNodes.ToList(), clock) { _dispose = () => gentl?.Dispose() };
    }

    public CxpReport Poll()
    {
        (bool present, string name) = _presence();
        if (!present) return new CxpReport(BoardId, name, CxpMode.Absent, Array.Empty<CxpConnectionStatus>(), null);
        if (_openSession is null) return PresenceOnly(name, "상세 불가 — GenTL 프로듀서를 찾을 수 없음 (보드 연결 여부만 감시)");
        if (_bindings.Count == 0) return PresenceOnly(name, "상세 불가 — 읽을 노드가 설정되지 않음 (진단 덤프 후 설정)");
        if (_failedAt is { } failed && _clock.GetUtcNow() - failed < RetryAfter) return PresenceOnly(name, _failure);

        try
        {
            IReadOnlyList<NodeReading> readings;
            using (IGenTLSession session = _openSession())
            {
                readings = session.ReadAll(_bindings);
            }
            _failedAt = null;
            return new CxpReport(BoardId, name, CxpMode.Full, Group(readings), null);
        }
        catch (Exception ex) when (ex is GenTLException or InvalidOperationException or NotSupportedException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or IOException or XmlException)
        {
            _failedAt = _clock.GetUtcNow();
            _failure = $"상세 불가 — {ex.Message} (보드 연결 여부만 감시)";
            return PresenceOnly(name, _failure);
        }
    }

    public void Dispose() => _dispose?.Invoke();

    private static CxpReport PresenceOnly(string name, string message)
        => new(BoardId, name, CxpMode.PresenceOnly, Array.Empty<CxpConnectionStatus>(), message);

    private static IReadOnlyList<CxpConnectionStatus> Group(IReadOnlyList<NodeReading> readings)
        => readings.GroupBy(r => r.Binding.Connection).OrderBy(g => g.Key).Select(g =>
        {
            NodeReading? Find(CxpNodeRole role) => g.FirstOrDefault(r => r.Binding.Role == role);
            return new CxpConnectionStatus(g.Key,
                Find(CxpNodeRole.LinkUp) is { } link ? link.Value != 0 : null,
                Find(CxpNodeRole.Speed)?.Text ?? string.Empty,
                Find(CxpNodeRole.ErrorCount)?.Value,
                Find(CxpNodeRole.FrameCount)?.Value,
                Find(CxpNodeRole.DropCount)?.Value);
        }).ToList();
}
```

`Cxp/CxpDiagnostics.cs`:

```csharp
using System.Text;
using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// Everything needed to decide which nodes to bind, as text: is the board there, can the
/// interface and camera be opened right now (run it while VISION is grabbing), and every
/// link/error/frame-looking node with its current value.
/// </summary>
public static class CxpDiagnostics
{
    private static readonly string[] Interesting =
        { "Link", "Cxp", "Error", "Crc", "Frame", "Drop", "Lost", "Speed", "Connection", "Packet", "Status" };

    public static string Run(WiresharkSettings settings)
    {
        var o = new StringBuilder();
        o.AppendLine($"통신 모니터 CXP 진단 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        (bool present, string name) = RapixoPresence.Query(settings.CxpDeviceNameMatch);
        o.AppendLine($"장치 관리자 '{settings.CxpDeviceNameMatch}': {(present ? name + " (정상)" : "없음 또는 오류")}");
        string? producer = GenTL.FindProducer(settings.GenTLProducerPath);
        o.AppendLine($"GenTL 프로듀서: {producer ?? "없음"}");
        if (producer is null) return o.ToString();

        try
        {
            using var gentl = new GenTL(producer);
            IntPtr tl = gentl.OpenSystem();
            try
            {
                foreach (string ifaceId in gentl.InterfaceIds(tl)) DumpInterface(o, gentl, tl, ifaceId);
            }
            finally
            {
                gentl.CloseSystem(tl);
            }
        }
        catch (Exception ex)
        {
            o.AppendLine($"GenTL 오류: {ex.Message}");
        }
        return o.ToString();
    }

    private static void DumpInterface(StringBuilder o, GenTL gentl, IntPtr tl, string ifaceId)
    {
        o.AppendLine($"인터페이스 {ifaceId}");
        IntPtr iface = IntPtr.Zero;
        try
        {
            iface = gentl.OpenInterface(tl, ifaceId);
            o.AppendLine("  열기: 성공");
            DumpNodeMap(o, "  [Interface]", gentl, iface);
            foreach (string deviceId in gentl.DeviceIds(iface))
            {
                o.AppendLine($"  장치 {deviceId}, 접근 상태 {Try(() => gentl.AccessStatus(iface, deviceId).ToString())}");
                IntPtr device = IntPtr.Zero;
                try
                {
                    device = gentl.OpenDeviceReadOnly(iface, deviceId);
                    o.AppendLine("    읽기 전용 열기: 성공");
                    DumpNodeMap(o, "    [Device]", gentl, gentl.DevicePort(device));
                }
                catch (Exception ex)
                {
                    o.AppendLine($"    읽기 전용 열기: 실패 — {ex.Message}");
                }
                finally
                {
                    if (device != IntPtr.Zero) gentl.CloseDevice(device);
                }
            }
        }
        catch (Exception ex)
        {
            o.AppendLine($"  실패 — {ex.Message}");
        }
        finally
        {
            if (iface != IntPtr.Zero) gentl.CloseInterface(iface);
        }
    }

    private static void DumpNodeMap(StringBuilder o, string prefix, GenTL gentl, IntPtr port)
    {
        try
        {
            string url = gentl.PortUrls(port).FirstOrDefault() ?? throw new InvalidOperationException("XML 위치 없음");
            o.AppendLine($"{prefix} XML {url}");
            GenApiNodeMap map = GenApiNodeMap.Parse(GenApiXml.Load(url, (a, l) => gentl.Read(port, a, l)));
            foreach (string node in map.NodeNames
                .Where(n => Interesting.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n, StringComparer.Ordinal))
            {
                o.AppendLine($"{prefix}   {node} = {Try(() => map.Read(node, (a, l) => gentl.Read(port, a, l)).Text)}");
            }
        }
        catch (Exception ex)
        {
            o.AppendLine($"{prefix} 노드맵 실패 — {ex.Message}");
        }
    }

    private static string Try(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return "읽기 불가: " + ex.Message;
        }
    }
}
```

- [ ] **Step 6: 통과 확인**

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~GenApiNodeMapTests|FullyQualifiedName~CxpMonitorTests"`
Expected: PASS (GenApi 8, CxpMonitor 6)

- [ ] **Step 7: Commit**

```bash
git add src/VisionSupport.Wireshark/Cxp tests/VisionSupport.Tests/GenApiNodeMapTests.cs tests/VisionSupport.Tests/CxpMonitorTests.cs
git commit -m "feat(wireshark): watch the Rapixo board through GenTL, holding no handle between polls"
```

---

### Task 15: 화면 ViewModel — 캡처 파이프라인, 카드, 필터, 내보내기

**Files:**
- Create: `src/VisionSupport.Wireshark/ViewModels/PacketFilter.cs`
- Create: `src/VisionSupport.Wireshark/ViewModels/HexFormatter.cs`
- Create: `src/VisionSupport.Wireshark/ViewModels/ChartHistory.cs`
- Create: `src/VisionSupport.Wireshark/ViewModels/TargetCardViewModel.cs`
- Modify (전체 교체): `src/VisionSupport.Wireshark/ViewModels/WiresharkViewModel.cs`
- Test: `tests/VisionSupport.Tests/WiresharkViewModelPartsTests.cs`, `tests/VisionSupport.Tests/WiresharkViewModelTests.cs`

**Interfaces:**
- Consumes: Task 4-14 전부
- Produces (Task 16의 XAML이 바인딩):
  - `PacketFilter(string text)`, `bool Matches(Packet)` — 공백으로 나눈 항목 AND, `!` 접두 부정, `id:<대상ID>`, `이상`/`anomaly`, 그 외 출발·목적·프로토콜·정보 부분 일치(대소문자 무시)
  - `HexFormatter.Format(byte[]) : string`
  - `ChartHistory(int capacity)`: `List<DateTime> Times`, `List<double> BytesPerSecond`, `List<double> ResponseMs`, `Add(DateTime, double bytes, double? responseMs)`
  - `TargetCardViewModel(string id, TargetKind kind)`: `Id, Kind, CanPin, Name, Level, Summary, Pinned, DropCount`, `Update(TargetSnapshot)`
  - `WiresharkViewModel()` / `WiresharkViewModel(WiresharkSettingsStore store, TimeProvider clock)`:
    - 컬렉션: `Nics`, `Cards`, `Rows`(교체될 수 있음 → 바인딩은 프로퍼티로), `Anomalies`, `CxpConnections`
    - 프로퍼티: `SelectedNic`, `FilterText`, `SelectedPacket`, `SelectedLayers`, `HexDump`, `SelectedAnomaly`, `FocusedCard`, `FocusedHistory`, `IsCapturing`, `IsCxpWatching`, `IsWorking`, `HasAlert`, `Status`, `CaptureError`, `CxpMessage`
    - 명령: `RefreshNicsCommand`, `StartCaptureCommand`, `StopCaptureCommand`, `StartCxpCommand`, `StopCxpCommand`, `AcknowledgeCommand`, `TogglePinCommand(TargetCardViewModel)`, `FocusCardCommand(TargetCardViewModel)`, `ClearFilterCommand`, `ClearPacketsCommand`
    - 메서드: `int ExportTo(string path)`, `string WriteCxpDiagnostics()`; 이벤트 `ChartUpdated`
- 생성자는 프로세스(pktmon)를 띄우지 않는다. NIC 목록은 뷰가 `Loaded`에서 `RefreshNicsCommand`로 채운다.

- [ ] **Step 1: 실패하는 테스트 — 순수 부품**

`tests/VisionSupport.Tests/WiresharkViewModelPartsTests.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

public class WiresharkViewModelPartsTests
{
    private static Packet Mc() => TestFrames.Dissect(
        TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, TestFrames.McReadRequest3E));

    private static Packet Arp() => TestFrames.Dissect(TestFrames.Arp("192.168.0.2", "192.168.0.10"));

    [Theory]
    [InlineData("", true, true)]
    [InlineData("mc", true, false)]
    [InlineData("!ARP", true, false)]
    [InlineData("192.168.0.10 일괄", true, false)]
    [InlineData("192.168.0.10 RST", false, false)]
    public void Filter_terms_are_and_ed_case_insensitive_and_negatable(string filter, bool mc, bool arp)
    {
        var f = new PacketFilter(filter);

        Assert.Equal(mc, f.Matches(Mc()));
        Assert.Equal(arp, f.Matches(Arp()));
    }

    [Fact]
    public void Filter_by_target_and_by_anomaly()
    {
        Packet p = Mc();
        p.TargetId = "192.168.0.10:5000";
        p.IsAnomalous = true;

        Assert.True(new PacketFilter("id:192.168.0.10:5000").Matches(p));
        Assert.False(new PacketFilter("id:192.168.0.11:5000").Matches(p));
        Assert.True(new PacketFilter("이상").Matches(p));
        Assert.False(new PacketFilter("이상").Matches(Arp()));
    }

    [Fact]
    public void Hex_rows_have_offset_two_groups_of_eight_and_ascii()
    {
        byte[] data = Enumerable.Range(0x41, 18).Select(i => (byte)i).ToArray();

        string[] lines = HexFormatter.Format(data).TrimEnd().Split(Environment.NewLine);

        Assert.Equal("0000  41 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50  ABCDEFGHIJKLMNOP", lines[0]);
        Assert.StartsWith("0010  51 52 ", lines[1]);
        Assert.EndsWith("QR", lines[1]);
    }

    [Fact]
    public void Chart_history_is_capped_and_carries_the_last_response_time_forward()
    {
        var h = new ChartHistory(capacity: 2);
        h.Add(TestFrames.T0, 10, 5);
        h.Add(TestFrames.T0.AddSeconds(1), 20, null);
        h.Add(TestFrames.T0.AddSeconds(2), 30, 7);

        Assert.Equal(new double[] { 20, 30 }, h.BytesPerSecond);
        Assert.Equal(new double[] { 5, 7 }, h.ResponseMs);
    }

    [Fact]
    public void A_card_follows_its_snapshot_and_machine_targets_cannot_be_pinned()
    {
        var card = new TargetCardViewModel("x", TargetKind.Mc);
        card.Update(new TargetSnapshot("x", TargetKind.Mc, "PLC", true, HealthLevel.Bad, "응답 없음 1.0초째", 0, null, 0));

        Assert.Equal(HealthLevel.Bad, card.Level);
        Assert.True(card.Pinned);
        Assert.True(card.CanPin);
        Assert.False(new TargetCardViewModel("nic", TargetKind.Nic).CanPin);
    }
}
```

Run → 빌드 실패 확인.

- [ ] **Step 2: 순수 부품 구현**

`ViewModels/PacketFilter.cs`:

```csharp
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>
/// The display filter: words separated by spaces, all of which must match. "!word" negates,
/// "id:target" keeps one target's packets, "이상" keeps flagged packets; anything else is a
/// case-insensitive substring of the source, destination, protocol or info column.
/// </summary>
public sealed class PacketFilter
{
    private readonly (string Text, bool Negate)[] _terms;

    public PacketFilter(string text)
        => _terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Length > 1 && t[0] == '!' ? (t[1..], true) : (t, false))
            .ToArray();

    public bool Matches(Packet p) => _terms.All(t => Term(p, t.Text) != t.Negate);

    private static bool Term(Packet p, string term)
    {
        if (term.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(p.TargetId, term[3..], StringComparison.Ordinal);
        }
        if (term == "이상" || term.Equals("anomaly", StringComparison.OrdinalIgnoreCase)) return p.IsAnomalous;
        return Has(p.Source, term) || Has(p.Destination, term) || Has(p.Protocol, term) || Has(p.Info, term);
    }

    private static bool Has(string column, string term) => column.Contains(term, StringComparison.OrdinalIgnoreCase);
}
```

`ViewModels/HexFormatter.cs`:

```csharp
using System.Text;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>Wireshark's bytes pane: offset, sixteen bytes in two groups of eight, then ASCII.</summary>
public static class HexFormatter
{
    public static string Format(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 4 + 16);
        for (int row = 0; row < data.Length; row += 16)
        {
            int n = Math.Min(16, data.Length - row);
            sb.Append(row.ToString("X4")).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                sb.Append(i < n ? data[row + i].ToString("X2") + " " : "   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int i = 0; i < n; i++)
            {
                byte b = data[row + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

`ViewModels/ChartHistory.cs`:

```csharp
namespace VisionSupport.Wireshark.ViewModels;

/// <summary>One target's last ten minutes at one sample a second: bytes/s and response time.</summary>
public sealed class ChartHistory
{
    private readonly int _capacity;

    public ChartHistory(int capacity) => _capacity = capacity;

    public List<DateTime> Times { get; } = new();

    public List<double> BytesPerSecond { get; } = new();

    public List<double> ResponseMs { get; } = new();

    public void Add(DateTime time, double bytes, double? responseMs)
    {
        Times.Add(time);
        BytesPerSecond.Add(bytes);
        // A second with no answer keeps the last response time rather than dropping to zero.
        ResponseMs.Add(responseMs ?? (ResponseMs.Count > 0 ? ResponseMs[^1] : 0));
        if (Times.Count <= _capacity) return;
        Times.RemoveAt(0);
        BytesPerSecond.RemoveAt(0);
        ResponseMs.RemoveAt(0);
    }
}
```

`ViewModels/TargetCardViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using VisionSupport.Wireshark.Health;

namespace VisionSupport.Wireshark.ViewModels;

public sealed partial class TargetCardViewModel : ObservableObject
{
    public TargetCardViewModel(string id, TargetKind kind)
    {
        Id = id;
        Kind = kind;
    }

    public string Id { get; }

    public TargetKind Kind { get; }

    /// <summary>The machine's own NIC and CXP board are always watched; there is nothing to pin.</summary>
    public bool CanPin => Kind is not (TargetKind.Nic or TargetKind.Cxp);

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private HealthLevel _level;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _pinned;
    [ObservableProperty] private long _dropCount;

    public void Update(TargetSnapshot s)
    {
        Name = s.Name;
        Level = s.Level;
        Summary = s.Summary;
        Pinned = s.Pinned;
        DropCount = s.DropCount;
    }
}
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~WiresharkViewModelPartsTests"` → PASS

- [ ] **Step 3: 실패하는 테스트 — ViewModel**

`tests/VisionSupport.Tests/WiresharkViewModelTests.cs`:

```csharp
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.Settings;
using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

[Collection(StaAppCollection.Name)]
public class WiresharkViewModelTests
{
    private readonly StaAppFixture _app;

    public WiresharkViewModelTests(StaAppFixture app) => _app = app;

    private static WiresharkSettingsStore StoreWithPinnedPlc(TempDir dir)
    {
        var store = new WiresharkSettingsStore(Path.Combine(dir.Path, "settings.json"));
        var settings = new WiresharkSettings();
        settings.Pinned.Add(new PinnedTarget { Id = "192.168.0.10:5000", Kind = TargetKind.Mc, Name = "검사기 PLC" });
        store.Save(settings);
        return store;
    }

    [Fact]
    public void Pinned_targets_show_as_grey_cards_before_anything_runs() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());

        TargetCardViewModel card = Assert.Single(vm.Cards);
        Assert.Equal("검사기 PLC", card.Name);
        Assert.Equal(HealthLevel.Idle, card.Level);
        Assert.Equal("트래픽 없음", card.Summary);
        Assert.False(vm.IsWorking);
        Assert.False(vm.HasAlert);
    });

    [Fact]
    public void Unpinning_is_saved_and_drops_a_card_never_seen() => _app.Run(() =>
    {
        using var dir = new TempDir();
        WiresharkSettingsStore store = StoreWithPinnedPlc(dir);
        using (var vm = new WiresharkViewModel(store, new ManualClock()))
        {
            vm.TogglePinCommand.Execute(vm.Cards[0]);
            Assert.Empty(vm.Cards);
        }

        Assert.Empty(store.Load().Pinned);
    });

    [Fact]
    public void Focusing_a_card_filters_the_list_to_it() => _app.Run(() =>
    {
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());

        vm.FocusCardCommand.Execute(vm.Cards[0]);

        Assert.Equal("id:192.168.0.10:5000", vm.FilterText);
        Assert.Same(vm.Cards[0], vm.FocusedCard);
    });

    [Fact]
    public void Disposing_twice_is_harmless() => _app.Run(() =>
    {
        using var dir = new TempDir();
        var vm = new WiresharkViewModel(StoreWithPinnedPlc(dir), new ManualClock());
        vm.Dispose();
        vm.Dispose();
    });
}
```

Run → 빌드 실패(골격 VM에 멤버 없음) 확인.

- [ ] **Step 4: ViewModel 구현 (파일 전체 교체)**

`ViewModels/WiresharkViewModel.cs`:

```csharp
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Wireshark.Capture;
using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Export;
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.Settings;
using VisionSupport.Wireshark.Store;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>
/// Wires capture → dissect → judge → store on the capture thread, and shows the result on two UI
/// timers: 200 ms for new rows (so a burst never floods the dispatcher), 1 s for everything that
/// depends on time passing (cards, alert, chart). Lives on after the window closes while capture
/// or CXP watching is running - the feature keeps it, the view is rebuilt.
/// </summary>
public sealed partial class WiresharkViewModel : ObservableObject, IDisposable
{
    private const int MaxRows = 20_000;
    private const int MaxAnomalies = 1000;
    private const int MaxDrainPerTick = 5_000;
    private const int MaxQueued = 100_000;
    private const int ChartSeconds = 600;

    private readonly WiresharkSettingsStore _store;
    private readonly WiresharkSettings _settings;
    private readonly TimeProvider _clock;
    private readonly HealthTracker _health;
    private readonly PacketStore _packets;
    private readonly ConcurrentQueue<Packet> _incoming = new();
    private readonly Dictionary<string, TargetCardViewModel> _cards = new();
    private readonly Dictionary<string, ChartHistory> _history = new();
    private readonly Dictionary<string, long> _lastBytes = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rowTimer;
    private readonly DispatcherTimer _tickTimer;
    private DissectorContext _context = new();
    private PktmonSource? _capture;
    private CxpMonitor? _cxp;
    private CancellationTokenSource? _cxpStop;
    private Task? _cxpLoop;
    private volatile int _nicFilter = -1;
    private long _nextNumber;
    private bool _disposed;

    public WiresharkViewModel() : this(WiresharkSettingsStore.Default, TimeProvider.System)
    {
    }

    public WiresharkViewModel(WiresharkSettingsStore store, TimeProvider clock)
    {
        _store = store;
        _settings = store.Load();
        _clock = clock;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _health = new HealthTracker(_settings.Thresholds, clock);
        foreach (PinnedTarget p in _settings.Pinned) _health.Pin(p.Id, p.Kind, p.Name);
        _packets = new PacketStore(_settings.StoreMaxBytes, _settings.StoreMaxCount);

        _rowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => DrainIncoming(), _dispatcher);
        _tickTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), _dispatcher);
        OnTick();
    }

    public ObservableCollection<NicInfo> Nics { get; } = new();

    public ObservableCollection<TargetCardViewModel> Cards { get; } = new();

    public ObservableCollection<Anomaly> Anomalies { get; } = new();

    public ObservableCollection<CxpConnectionStatus> CxpConnections { get; } = new();

    /// <summary>Replaced wholesale when trimmed: removing from the front one by one is O(n) each.</summary>
    [ObservableProperty] private ObservableCollection<Packet> _rows = new();

    [ObservableProperty] private NicInfo? _selectedNic;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private Packet? _selectedPacket;
    [ObservableProperty] private IReadOnlyList<ProtocolNode>? _selectedLayers;
    [ObservableProperty] private string _hexDump = string.Empty;
    [ObservableProperty] private Anomaly? _selectedAnomaly;
    [ObservableProperty] private TargetCardViewModel? _focusedCard;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWorking))] private bool _isCapturing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWorking))] private bool _isCxpWatching;
    [ObservableProperty] private bool _hasAlert;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _captureError = string.Empty;
    [ObservableProperty] private string _cxpMessage = string.Empty;

    public bool IsWorking => IsCapturing || IsCxpWatching;

    public ChartHistory? FocusedHistory => FocusedCard is { } c ? _history.GetValueOrDefault(c.Id) : null;

    /// <summary>Raised once a second after the chart data moved.</summary>
    public event EventHandler? ChartUpdated;

    partial void OnSelectedNicChanged(NicInfo? value)
    {
        _nicFilter = value?.ComponentId ?? -1;
        _settings.LastNicComponentId = value?.ComponentId;
    }

    partial void OnFilterTextChanged(string value) => RebuildRows();

    partial void OnSelectedPacketChanged(Packet? value)
    {
        SelectedLayers = value?.Layers;
        HexDump = value is null ? string.Empty : HexFormatter.Format(value.Data);
    }

    partial void OnSelectedAnomalyChanged(Anomaly? value)
    {
        if (value?.PacketNumber is not long number) return;
        Packet? packet = Rows.FirstOrDefault(p => p.Number == number)
            ?? _packets.Snapshot().FirstOrDefault(p => p.Number == number);
        if (packet is null) return;
        if (!Rows.Contains(packet))
        {
            FilterText = string.Empty;
        }
        SelectedPacket = Rows.FirstOrDefault(p => p.Number == number);
    }

    partial void OnFocusedCardChanged(TargetCardViewModel? value) => OnPropertyChanged(nameof(FocusedHistory));

    [RelayCommand]
    private void RefreshNics()
    {
        try
        {
            Nics.Clear();
            foreach (NicInfo nic in NicCatalog.Query()) Nics.Add(nic);
            SelectedNic = Nics.FirstOrDefault(n => n.ComponentId == _settings.LastNicComponentId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            CaptureError = "NIC 목록을 읽을 수 없음: " + ex.Message;
        }
    }

    [RelayCommand]
    private void StartCapture()
    {
        if (IsCapturing) return;
        CaptureError = string.Empty;
        _context = new DissectorContext { McPortMin = _settings.McPortMin, McPortMax = _settings.McPortMax };

        var source = new PktmonSource(OnFrame);
        source.Faulted += message => _dispatcher.BeginInvoke(() =>
        {
            CaptureError = "캡처 중단: " + message;
            StopCapture();
        });
        try
        {
            source.Start(_settings.CapturePacketSize);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            source.Dispose();
            CaptureError = "캡처를 시작할 수 없음: " + ex.Message;
            return;
        }
        _capture = source;
        IsCapturing = true;
    }

    [RelayCommand]
    private void StopCapture()
    {
        _capture?.Dispose();
        _capture = null;
        IsCapturing = false;
    }

    [RelayCommand]
    private void StartCxp()
    {
        if (IsCxpWatching) return;
        CxpMonitor monitor = CxpMonitor.Create(_settings, _clock);
        var stop = new CancellationTokenSource();
        _cxp = monitor;
        _cxpStop = stop;
        IsCxpWatching = true;
        _cxpLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                do
                {
                    CxpReport report = monitor.Poll();
                    _health.ReportCxp(report);
                    _ = _dispatcher.BeginInvoke(() => ShowCxp(report));
                }
                while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    [RelayCommand]
    private void StopCxp()
    {
        _cxpStop?.Cancel();
        try
        {
            _cxpLoop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        _cxpStop?.Dispose();
        _cxpStop = null;
        _cxpLoop = null;
        _cxp?.Dispose();
        _cxp = null;
        IsCxpWatching = false;
        CxpConnections.Clear();
    }

    [RelayCommand]
    private void Acknowledge()
    {
        _health.Acknowledge();
        HasAlert = false;
    }

    [RelayCommand]
    private void TogglePin(TargetCardViewModel card)
    {
        if (!card.CanPin) return;
        if (card.Pinned)
        {
            _health.Unpin(card.Id);
            _settings.Pinned.RemoveAll(p => p.Id == card.Id);
        }
        else
        {
            _health.Pin(card.Id, card.Kind, card.Name);
            _settings.Pinned.Add(new PinnedTarget { Id = card.Id, Kind = card.Kind, Name = card.Name });
        }
        SaveSettings();
        OnTick();
    }

    [RelayCommand]
    private void FocusCard(TargetCardViewModel card)
    {
        FocusedCard = card;
        FilterText = "id:" + card.Id;
        SelectedPacket = Rows.LastOrDefault(p => p.IsAnomalous) ?? Rows.LastOrDefault();
        ChartUpdated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FocusedCard = null;
        FilterText = string.Empty;
    }

    [RelayCommand]
    private void ClearPackets()
    {
        _packets.Clear();
        Rows = new ObservableCollection<Packet>();
        SelectedPacket = null;
    }

    /// <returns>How many packets were written.</returns>
    public int ExportTo(string path)
    {
        IReadOnlyList<Packet> all = _packets.Snapshot();
        using FileStream file = File.Create(path);
        PcapngWriter.Write(file, all);
        return all.Count;
    }

    /// <returns>The path of the written report.</returns>
    public string WriteCxpDiagnostics()
    {
        string text = CxpDiagnostics.Run(_settings);
        Directory.CreateDirectory(_store.DataDirectory);
        string path = Path.Combine(_store.DataDirectory, $"cxp-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rowTimer.Stop();
        _tickTimer.Stop();
        StopCapture();
        StopCxp();
        SaveSettings();
    }

    /// <summary>Capture thread. Dissect and judge here so the UI only ever sees finished packets.</summary>
    private void OnFrame(RawFrame frame)
    {
        int nic = _nicFilter;
        if (nic >= 0 && frame.ComponentId != nic) return;

        long number = Interlocked.Increment(ref _nextNumber);
        Packet packet = FrameDissector.Dissect(number, frame.Time, frame.Data, frame.OriginalLength, _context);
        _health.Observe(packet);

        // Spec §8: video payload is judged, not kept - except the packets that showed a problem.
        if (packet.Gvsp is { Format: GvspFormat.Payload } && !packet.IsAnomalous) return;

        _packets.Add(packet);
        if (_incoming.Count < MaxQueued) _incoming.Enqueue(packet);
    }

    private void DrainIncoming()
    {
        var filter = new PacketFilter(FilterText);
        for (int i = 0; i < MaxDrainPerTick && _incoming.TryDequeue(out Packet? p); i++)
        {
            if (filter.Matches(p)) Rows.Add(p);
        }
        if (Rows.Count > MaxRows + MaxRows / 10)
        {
            Rows = new ObservableCollection<Packet>(Rows.Skip(Rows.Count - MaxRows));
        }

        foreach (Anomaly a in _health.DrainAnomalies())
        {
            if (a.PacketNumber is long n) _packets.KeepAround(n);
            Anomalies.Insert(0, a);
        }
        while (Anomalies.Count > MaxAnomalies) Anomalies.RemoveAt(Anomalies.Count - 1);
    }

    private void RebuildRows()
    {
        var filter = new PacketFilter(FilterText);
        List<Packet> matching = _packets.Snapshot().Where(filter.Matches).ToList();
        Rows = new ObservableCollection<Packet>(matching.Skip(Math.Max(0, matching.Count - MaxRows)));
    }

    private void OnTick()
    {
        if (_capture is { } capture)
        {
            _health.ReportCaptureLoss(capture.EventsLost);
            if (SelectedNic is { } nic) _health.ReportLink(nic.Mac, nic.Name, NicCatalog.IsUp(nic.Mac) ?? false);
        }
        _health.Tick();
        UpdateCards();
        DrainIncoming();

        HasAlert = _health.HasAlert;
        Status = !IsWorking ? string.Empty
            : $"패킷 {Interlocked.Read(ref _nextNumber):N0} · 보관 {_packets.Count:N0}"
              + (_capture is { EventsLost: > 0 } c ? $" · 캡처 누락 {c.EventsLost:N0}" : string.Empty)
              + (HasAlert ? " · 이상 있음" : string.Empty);
        ChartUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCards()
    {
        DateTime now = _clock.GetLocalNow().DateTime;
        IReadOnlyList<TargetSnapshot> snapshots = _health.Snapshot();
        var seen = new HashSet<string>();

        for (int i = 0; i < snapshots.Count; i++)
        {
            TargetSnapshot s = snapshots[i];
            seen.Add(s.Id);
            if (!_cards.TryGetValue(s.Id, out TargetCardViewModel? card))
            {
                card = new TargetCardViewModel(s.Id, s.Kind);
                _cards.Add(s.Id, card);
                Cards.Insert(Math.Min(i, Cards.Count), card);
            }
            card.Update(s);
            int at = Cards.IndexOf(card);
            if (at != i && i < Cards.Count) Cards.Move(at, i);

            if (!_history.TryGetValue(s.Id, out ChartHistory? history))
            {
                history = new ChartHistory(ChartSeconds);
                _history.Add(s.Id, history);
            }
            long previous = _lastBytes.GetValueOrDefault(s.Id, s.Bytes);
            history.Add(now, s.Bytes - previous, s.LastResponseMs);
            _lastBytes[s.Id] = s.Bytes;
        }

        foreach (string gone in _cards.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Cards.Remove(_cards[gone]);
            _cards.Remove(gone);
            _history.Remove(gone);
            _lastBytes.Remove(gone);
            if (FocusedCard?.Id == gone) FocusedCard = null;
        }
    }

    private void ShowCxp(CxpReport report)
    {
        if (!IsCxpWatching) return;
        CxpMessage = report.Mode switch
        {
            CxpMode.Full => $"{report.BoardName}: 상세 감시 중",
            CxpMode.PresenceOnly => report.Message ?? string.Empty,
            _ => "CXP 보드가 보이지 않음",
        };
        CxpConnections.Clear();
        foreach (CxpConnectionStatus c in report.Connections) CxpConnections.Add(c);
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CaptureError = "설정 저장 실패: " + ex.Message;
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet build VisionSupport.sln`
Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~WiresharkViewModel"`
Expected: PASS (Parts 9 + VM 4)

- [ ] **Step 6: Commit**

```bash
git add src/VisionSupport.Wireshark/ViewModels tests/VisionSupport.Tests/WiresharkViewModelPartsTests.cs tests/VisionSupport.Tests/WiresharkViewModelTests.cs
git commit -m "feat(wireshark): drive capture, cards, filter and export from one view model"
```

---

### Task 16: 화면 — 상태 카드, 차트, 패킷 목록, 계층 트리/hex, 이상·CXP 탭

**Files:**
- Create: `src/VisionSupport.Wireshark/Converters.cs`
- Modify (전체 교체): `src/VisionSupport.Wireshark/WiresharkView.xaml`, `src/VisionSupport.Wireshark/WiresharkView.xaml.cs`
- Test: `tests/VisionSupport.Tests/WiresharkViewTests.cs`

**Interfaces:**
- Consumes: Task 15 `WiresharkViewModel` 전체, 셸 테마 키(`Bg, Panel, PanelAlt, Border, Fg, FgDim, Accent, StateStopped, StateRunning, StatePaused, StateFaulted`, 스타일 `AccentButton`, `HeaderTip`) — 메모리 모니터 뷰와 같이 셸의 `Application.Resources`(Dark.xaml)에서 찾는다.
- Produces: `HealthBrushConverter` (`HealthLevel` → 상태 브러시), `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`

- [ ] **Step 1: 실패하는 테스트**

`tests/VisionSupport.Tests/WiresharkViewTests.cs`:

```csharp
using VisionSupport.Wireshark;
using VisionSupport.Wireshark.Settings;
using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>The view resolves the shell's theme keys at construction; a typo fails here, not on the line PC.</summary>
[Collection(StaAppCollection.Name)]
public class WiresharkViewTests
{
    private readonly StaAppFixture _app;

    public WiresharkViewTests(StaAppFixture app) => _app = app;

    [Fact]
    public void The_view_builds_against_the_shell_theme() => _app.Run(() =>
    {
        OverviewDialogAutomationTests.EnsureShellResourcesMerged();
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());

        var view = new WiresharkView(vm);

        Assert.Same(vm, view.DataContext);
    });
}
```

Run: 현재 골격 뷰로는 통과할 수 있다 — 이 테스트는 Step 3 이후 회귀 방지용이다. 그대로 두고 진행.

- [ ] **Step 2: 변환기**

`Converters.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VisionSupport.Wireshark.Health;

namespace VisionSupport.Wireshark;

/// <summary>Card colour from the shell's run-state brushes, so "red" here is the launcher ring's red.</summary>
public sealed class HealthBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value switch
        {
            HealthLevel.Ok => "StateRunning",
            HealthLevel.Warn => "StatePaused",
            HealthLevel.Bad => "StateFaulted",
            _ => "StateStopped",
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 3: XAML (전체 교체)**

`WiresharkView.xaml`:

```xml
<!--
    Laid out like the memory monitor: toolbar, then what matters most, then detail. The cards are
    the answer to "is anything wrong?"; everything under them is for finding out why.
-->
<UserControl x:Class="VisionSupport.Wireshark.WiresharkView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:VisionSupport.Wireshark"
             xmlns:sp="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"
             Background="{StaticResource Bg}"
             TextOptions.TextFormattingMode="Display">

    <UserControl.Resources>
        <local:HealthBrushConverter x:Key="HealthBrush"/>
        <local:BoolToVisibilityConverter x:Key="BoolToVis"/>
        <local:InverseBoolToVisibilityConverter x:Key="NotBoolToVis"/>
    </UserControl.Resources>

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>   <!-- toolbar -->
            <RowDefinition Height="Auto"/>   <!-- errors -->
            <RowDefinition Height="Auto"/>   <!-- cards -->
            <RowDefinition Height="150"/>    <!-- chart -->
            <RowDefinition Height="*"/>      <!-- packets -->
            <RowDefinition Height="5"/>
            <RowDefinition Height="220"/>    <!-- detail + tabs -->
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <DockPanel Grid.Row="0" Margin="0,0,0,8" LastChildFill="True">
            <TextBlock Text="인터페이스" Foreground="{StaticResource FgDim}" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <ComboBox Width="260" ItemsSource="{Binding Nics}" SelectedItem="{Binding SelectedNic}"
                      ToolTip="비워 두면 모든 NIC를 봅니다. 캡처 중에 바꾸면 그 NIC 것만 이어서 받습니다."/>
            <Button Content="↻" Width="28" Margin="4,0" Command="{Binding RefreshNicsCommand}" ToolTip="NIC 목록 새로 고침"/>
            <Button Content="캡처 시작" Style="{StaticResource AccentButton}" Margin="8,0,0,0" Padding="12,3"
                    Command="{Binding StartCaptureCommand}"
                    Visibility="{Binding IsCapturing, Converter={StaticResource NotBoolToVis}}"/>
            <Button Content="캡처 정지" Margin="8,0,0,0" Padding="12,3" Command="{Binding StopCaptureCommand}"
                    Visibility="{Binding IsCapturing, Converter={StaticResource BoolToVis}}"/>
            <Button Content="CXP 감시 시작" Margin="8,0,0,0" Padding="12,3" Command="{Binding StartCxpCommand}"
                    Visibility="{Binding IsCxpWatching, Converter={StaticResource NotBoolToVis}}"/>
            <Button Content="CXP 감시 정지" Margin="8,0,0,0" Padding="12,3" Command="{Binding StopCxpCommand}"
                    Visibility="{Binding IsCxpWatching, Converter={StaticResource BoolToVis}}"/>
            <Button Content="이상 확인" Margin="8,0,0,0" Padding="12,3" Command="{Binding AcknowledgeCommand}"
                    Visibility="{Binding HasAlert, Converter={StaticResource BoolToVis}}"
                    ToolTip="런처의 빨간 링을 끕니다. 새 이상이 생기면 다시 켜집니다."/>
            <Button Content="pcapng 저장" Margin="8,0,0,0" Padding="12,3" Click="OnExport"/>
            <Button Content="목록 비우기" Margin="8,0,0,0" Padding="12,3" Command="{Binding ClearPacketsCommand}"/>
            <TextBox Margin="12,0,0,0" Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged, Delay=300}"
                     ToolTip="공백으로 나눈 단어 모두 일치 · !단어 제외 · id:대상 · 이상"/>
        </DockPanel>

        <TextBlock Grid.Row="1" Text="{Binding CaptureError}" Foreground="{StaticResource StateFaulted}"
                   TextWrapping="Wrap" Margin="0,0,0,6">
            <TextBlock.Style>
                <Style TargetType="TextBlock">
                    <Style.Triggers>
                        <Trigger Property="Text" Value="">
                            <Setter Property="Visibility" Value="Collapsed"/>
                        </Trigger>
                    </Style.Triggers>
                </Style>
            </TextBlock.Style>
        </TextBlock>

        <!-- Cards: one per watched target. Click to focus, pin to keep across restarts. -->
        <ItemsControl Grid.Row="2" ItemsSource="{Binding Cards}" Margin="0,0,0,8">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Width="250" Margin="0,0,8,8" Cursor="Hand"
                          MouseLeftButtonUp="OnCardClicked">
                        <Border Background="{StaticResource Panel}" CornerRadius="6" BorderThickness="0,0,0,0"/>
                        <!-- Candidates (not pinned yet) get a dashed outline. -->
                        <Rectangle RadiusX="6" RadiusY="6" StrokeThickness="1" StrokeDashArray="4 3"
                                   Stroke="{StaticResource Border}"
                                   Visibility="{Binding Pinned, Converter={StaticResource NotBoolToVis}}"/>
                        <Border BorderBrush="{Binding Level, Converter={StaticResource HealthBrush}}"
                                BorderThickness="5,0,0,0" CornerRadius="6,0,0,6" HorizontalAlignment="Left" Width="5"/>
                        <StackPanel Margin="14,8,8,8">
                            <DockPanel>
                                <Button DockPanel.Dock="Right" Padding="6,0" FontSize="11"
                                        Command="{Binding DataContext.TogglePinCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                        CommandParameter="{Binding}"
                                        Visibility="{Binding CanPin, Converter={StaticResource BoolToVis}}">
                                    <Button.Style>
                                        <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
                                            <Setter Property="Content" Value="고정"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Pinned}" Value="True">
                                                    <Setter Property="Content" Value="해제"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Button.Style>
                                </Button>
                                <TextBlock Text="{Binding Name}" Foreground="{StaticResource Fg}" FontWeight="SemiBold"
                                           TextTrimming="CharacterEllipsis" ToolTip="{Binding Id}"/>
                            </DockPanel>
                            <TextBlock Text="{Binding Summary}" Margin="0,4,0,0" FontSize="13"
                                       Foreground="{Binding Level, Converter={StaticResource HealthBrush}}"
                                       TextTrimming="CharacterEllipsis" ToolTip="{Binding Summary}"/>
                        </StackPanel>
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- Chart for the focused card -->
        <Border Grid.Row="3" Background="{StaticResource Panel}" CornerRadius="6" Margin="0,0,0,8">
            <Grid>
                <sp:WpfPlot x:Name="Chart"/>
                <TextBlock x:Name="ChartHint" Text="카드를 누르면 그 대상의 송수신량과 응답 시간이 여기 그려집니다."
                           Foreground="{StaticResource FgDim}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Grid>
        </Border>

        <!-- Packet list -->
        <ListView Grid.Row="4" x:Name="PacketList" ItemsSource="{Binding Rows}" SelectedItem="{Binding SelectedPacket}"
                  VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                  Background="{StaticResource Panel}" BorderBrush="{StaticResource Border}">
            <ListView.ItemContainerStyle>
                <Style TargetType="ListViewItem" BasedOn="{StaticResource {x:Type ListViewItem}}">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsAnomalous}" Value="True">
                            <Setter Property="Background" Value="#40F48771"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </ListView.ItemContainerStyle>
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="No" Width="70" DisplayMemberBinding="{Binding Number}"/>
                    <GridViewColumn Header="시간" Width="110" DisplayMemberBinding="{Binding Time, StringFormat=HH:mm:ss.fff}"/>
                    <GridViewColumn Header="출발" Width="170" DisplayMemberBinding="{Binding Source}"/>
                    <GridViewColumn Header="목적" Width="170" DisplayMemberBinding="{Binding Destination}"/>
                    <GridViewColumn Header="프로토콜" Width="70" DisplayMemberBinding="{Binding Protocol}"/>
                    <GridViewColumn Header="길이" Width="60" DisplayMemberBinding="{Binding OriginalLength}"/>
                    <GridViewColumn Header="정보" Width="520" DisplayMemberBinding="{Binding Info}"/>
                </GridView>
            </ListView.View>
        </ListView>

        <GridSplitter Grid.Row="5" HorizontalAlignment="Stretch" Background="{StaticResource Border}"/>

        <!-- Detail and tabs -->
        <Grid Grid.Row="6">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="5"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <TabControl Grid.Column="0">
                <TabItem Header="패킷 상세">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="*"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <TreeView ItemsSource="{Binding SelectedLayers}" Background="{StaticResource Panel}">
                            <TreeView.ItemTemplate>
                                <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                                    <TextBlock Text="{Binding Text}" Foreground="{StaticResource Fg}"/>
                                </HierarchicalDataTemplate>
                            </TreeView.ItemTemplate>
                        </TreeView>
                        <TextBox Grid.Row="1" Text="{Binding HexDump, Mode=OneWay}" IsReadOnly="True"
                                 FontFamily="Consolas" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"/>
                    </Grid>
                </TabItem>
            </TabControl>

            <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" Background="{StaticResource Border}"/>

            <TabControl Grid.Column="2">
                <TabItem Header="이상 목록">
                    <ListView ItemsSource="{Binding Anomalies}" SelectedItem="{Binding SelectedAnomaly}"
                              Background="{StaticResource Panel}">
                        <ListView.View>
                            <GridView>
                                <GridViewColumn Header="시간" Width="80" DisplayMemberBinding="{Binding Time, StringFormat=HH:mm:ss}"/>
                                <GridViewColumn Header="대상" Width="170" DisplayMemberBinding="{Binding TargetName}"/>
                                <GridViewColumn Header="내용" Width="320">
                                    <GridViewColumn.CellTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding Text}"
                                                       Foreground="{Binding Severity, Converter={StaticResource HealthBrush}}"/>
                                        </DataTemplate>
                                    </GridViewColumn.CellTemplate>
                                </GridViewColumn>
                            </GridView>
                        </ListView.View>
                    </ListView>
                </TabItem>
                <TabItem Header="CXP">
                    <DockPanel Margin="6">
                        <DockPanel DockPanel.Dock="Top" Margin="0,0,0,6">
                            <Button DockPanel.Dock="Right" Content="진단 덤프" Padding="10,2" Click="OnCxpDiagnostics"
                                    ToolTip="VISION이 영상을 받는 중에 눌러야 실제 조건(동시 접근 가능 여부)을 볼 수 있습니다."/>
                            <TextBlock Text="{Binding CxpMessage}" Foreground="{StaticResource FgDim}"
                                       VerticalAlignment="Center" TextWrapping="Wrap"/>
                        </DockPanel>
                        <DataGrid ItemsSource="{Binding CxpConnections}" AutoGenerateColumns="False" IsReadOnly="True"
                                  HeadersVisibility="Column">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="커넥션" Binding="{Binding Index}" Width="60"/>
                                <DataGridTextColumn Header="링크" Binding="{Binding LinkUp}" Width="60"/>
                                <DataGridTextColumn Header="속도" Binding="{Binding Speed}" Width="90"/>
                                <DataGridTextColumn Header="에러" Binding="{Binding ErrorCount}" Width="70"/>
                                <DataGridTextColumn Header="프레임" Binding="{Binding FrameCount}" Width="80"/>
                                <DataGridTextColumn Header="드롭" Binding="{Binding DropCount}" Width="70"/>
                            </DataGrid.Columns>
                        </DataGrid>
                    </DockPanel>
                </TabItem>
            </TabControl>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 4: 코드 비하인드 (전체 교체)**

`WiresharkView.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ScottPlot;
using VisionSupport.Wireshark.ViewModels;
using PlotColor = ScottPlot.Color;
using PlotFonts = ScottPlot.Fonts;

namespace VisionSupport.Wireshark;

public partial class WiresharkView : UserControl, IDisposable
{
    private const string HangulFontAlias = "VisionSupport Hangul";
    private readonly WiresharkViewModel _viewModel;
    private DateTime _timeOrigin;

    public WiresharkView(WiresharkViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        StyleChart();
        viewModel.ChartUpdated += OnChartUpdated;
        Loaded += (_, _) =>
        {
            if (_viewModel.Nics.Count == 0) _viewModel.RefreshNicsCommand.Execute(null);
        };
    }

    /// <summary>Called by the feature when the window closes: the view model outlives this view.</summary>
    public void Dispose() => _viewModel.ChartUpdated -= OnChartUpdated;

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        if ((sender as FrameworkElement)?.DataContext is TargetCardViewModel card)
        {
            _viewModel.FocusCardCommand.Execute(card);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "pcapng (*.pcapng)|*.pcapng",
            FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.pcapng",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            int count = _viewModel.ExportTo(dialog.FileName);
            MessageBox.Show($"{count:N0}개 패킷을 저장했습니다.", "통신 모니터");
        }
        catch (IOException ex)
        {
            MessageBox.Show("저장 실패: " + ex.Message, "통신 모니터", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCxpDiagnostics(object sender, RoutedEventArgs e)
    {
        Cursor = Cursors.Wait;
        try
        {
            string path = _viewModel.WriteCxpDiagnostics();
            MessageBox.Show($"진단 결과를 저장했습니다.\n{path}", "통신 모니터");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("진단 저장 실패: " + ex.Message, "통신 모니터", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void StyleChart()
    {
        PlotFonts.Default = RegisterHangulFont();
        Plot plot = Chart.Plot;
        plot.FigureBackground.Color = PlotColor.FromHex("#252526");
        plot.DataBackground.Color = PlotColor.FromHex("#252526");
        plot.Axes.Color(PlotColor.FromHex("#9D9D9D"));
        plot.Grid.MajorLineColor = PlotColor.FromHex("#2F2F2F");
        plot.Axes.Left.Label.Text = "B/s";
        plot.Axes.Right.Label.Text = "응답 ms";
        foreach (var axis in new IAxis[] { plot.Axes.Bottom, plot.Axes.Left, plot.Axes.Right })
        {
            axis.Label.FontName = PlotFonts.Default;
            axis.TickLabelStyle.FontName = PlotFonts.Default;
        }
        if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic ticks)
        {
            ticks.LabelFormatter = seconds => _timeOrigin.AddSeconds(seconds).ToString("HH:mm:ss");
        }
        Chart.UserInputProcessor.Disable();
        Chart.Refresh();
    }

    private void OnChartUpdated(object? sender, EventArgs e)
    {
        ChartHistory? history = _viewModel.FocusedHistory;
        ChartHint.Visibility = history is { Times.Count: > 1 } ? Visibility.Collapsed : Visibility.Visible;
        if (history is not { Times.Count: > 1 }) return;

        Plot plot = Chart.Plot;
        plot.Clear();
        _timeOrigin = history.Times[0];
        double[] xs = history.Times.Select(t => (t - _timeOrigin).TotalSeconds).ToArray();

        var traffic = plot.Add.Scatter(xs, history.BytesPerSecond.ToArray());
        traffic.MarkerSize = 0;
        traffic.Color = PlotColor.FromHex("#1F9CF0");

        var response = plot.Add.Scatter(xs, history.ResponseMs.ToArray());
        response.MarkerSize = 0;
        response.Color = PlotColor.FromHex("#DCDCAA");
        response.Axes.YAxis = plot.Axes.Right;

        plot.Axes.AutoScale();
        Chart.Refresh();
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (DependencyObject? n = node; n is not null; n = System.Windows.Media.VisualTreeHelper.GetParent(n))
        {
            if (n is T match) return match;
        }
        return null;
    }

    /// <summary>
    /// Same reason as the memory monitor's copy: ScottPlot draws through SkiaSharp, which cannot
    /// find Malgun Gothic by name here, so the font files are registered directly (bold too, for titles).
    /// </summary>
    private static string RegisterHangulFont()
    {
        string fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string regular = Path.Combine(fontDir, "malgun.ttf");
        string bold = Path.Combine(fontDir, "malgunbd.ttf");
        if (!File.Exists(regular)) return PlotFonts.Detect("응답");
        PlotFonts.AddFontFile(HangulFontAlias, regular, bold: false, italic: false);
        PlotFonts.AddFontFile(HangulFontAlias, File.Exists(bold) ? bold : regular, bold: true, italic: false);
        return HangulFontAlias;
    }
}
```

> `FeatureModule.ReleaseView()`는 `(_view as IDisposable)?.Dispose()`를 호출하므로, 뷰가 `IDisposable`이면 창을 닫을 때 `ChartUpdated` 구독이 풀린다(뷰가 VM에 잡혀 누수되지 않음). ScottPlot 5.1의 `IAxis`/`Axes.Right`/`scatter.Axes.YAxis` 이름이 컴파일 오류를 내면 메모리 모니터의 `MonitorView.xaml.cs`에서 같은 버전으로 쓰는 API에 맞춘다.

- [ ] **Step 5: 통과 확인**

Run: `dotnet build VisionSupport.sln`
Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~WiresharkViewTests|FullyQualifiedName~ViewConstructionTests|FullyQualifiedName~LauncherIconTests"`
Expected: PASS

- [ ] **Step 6: 실행해서 눈으로 확인 (관리자)**

`src\VisionSupport\bin\Debug\net9.0-windows\VisionSupport.exe` 실행 → 런처 좌클릭 → 도구 → 통신 모니터.
1. 창이 메모리 모니터와 같은 어두운 테마로 뜨고, NIC 목록이 채워진다.
2. 캡처 시작 → `ping <PLC IP>` 또는 PLC 서버 기능으로 MC 서버를 띄우고 VISION/테스트 클라이언트로 접속 → 패킷 목록에 MC 행과 후보 카드(점선)가 생긴다.
3. 카드 "고정" → 클라이언트를 끊는다 → 5초 뒤 카드가 빨강 "트래픽 끊김 N초째", 런처 아이콘 링이 빨강이 된다.
4. "이상 확인" → 링이 꺼진다. 창을 닫아도 캡처가 계속되고 링(초록)이 남는다.
5. 캡처 정지 → 창 닫기 → `pktmon status`가 캡처 중이 아님을, `logman query -ets`에 `VisionSupport-Wireshark`가 없음을 확인.
6. pcapng 저장 → Wireshark에서 열린다(설치되어 있다면).

- [ ] **Step 7: Commit**

```bash
git add src/VisionSupport.Wireshark/Converters.cs src/VisionSupport.Wireshark/WiresharkView.xaml src/VisionSupport.Wireshark/WiresharkView.xaml.cs tests/VisionSupport.Tests/WiresharkViewTests.cs
git commit -m "feat(wireshark): status cards, chart, packet list, tree and hex, anomaly and CXP tabs"
```

---

### Task 17: CXP 동시 접근 실기 확인 (장비 PC) 과 기본 노드 설정

스펙 10절 가정 2. **Rapixo CXP + Hik 카메라가 물린 장비 PC에서, VISION이 영상을 받는 중에** 수행한다.

**Files:**
- Modify: `docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md` (10절 결과)
- Modify: `src/VisionSupport.Wireshark/Settings/WiresharkSettings.cs` (`CxpNodes` 기본값 — 확인된 경우에만)
- Modify: `tests/VisionSupport.Tests/WiresharkSettingsTests.cs` (기본값 테스트 — 확인된 경우에만)

- [ ] **Step 1: 장비 PC에 배포**

```powershell
dotnet publish src\VisionSupport -p:PublishProfile=win-x64
```
`dist\` 의 exe를 장비 PC로 복사해 관리자 권한으로 실행.

- [ ] **Step 2: VISION 그랩 중 진단 덤프**

통신 모니터 → CXP 탭 → "진단 덤프". `D:\Datas\VisionSupport\Wireshark\cxp-diagnostics-*.txt`를 가져온다. 이어서 **VISION을 끈 상태에서** 한 번 더 덤프한다(비교용).

- [ ] **Step 3: 판단**

| 덤프 결과 (VISION 그랩 중) | 결정 |
|---|---|
| 인터페이스 열기 성공 + `[Interface]` 노드에 링크/에러/프레임 값이 읽힘 | 그 노드들을 `Module="Interface"` 바인딩으로 기본값에 넣는다 |
| 인터페이스는 되지만 링크 정보가 `[Device]`에만 있고 장치 읽기 전용 열기 성공 | `Module="Device"` 바인딩. **그 상태로 VISION을 재시작해 그랩이 정상인지 반드시 확인** — 안 되면 Device 바인딩은 쓰지 않는다 |
| 인터페이스 열기부터 실패(사용 중/접근 거부) | 기본값은 빈 목록 유지 = 존재 감시만. 코드 변경 없음 |

- [ ] **Step 4: 결과 기록과 기본값 반영**

스펙 10절 항목 2 아래에 `**결과(YYYY-MM-DD, 장비명):**`으로 위 표의 어느 경우였는지, 사용한 노드 이름, VISION 재시작 확인 여부를 적는다.

바인딩을 넣는 경우 `WiresharkSettings.CxpNodes` 초기값을 덤프에서 확인한 이름으로 채운다. 형식 예(이름은 덤프에 나온 실제 값으로):

```csharp
    public List<CxpNodeBinding> CxpNodes { get; set; } = new()
    {
        new CxpNodeBinding { Module = "Interface", Node = "<덤프의 커넥션0 링크 노드>", Role = CxpNodeRole.LinkUp, Connection = 0 },
        new CxpNodeBinding { Module = "Interface", Node = "<덤프의 커넥션0 에러 카운터 노드>", Role = CxpNodeRole.ErrorCount, Connection = 0 },
    };
```

그리고 `WiresharkSettingsTests`에 기본값 테스트를 추가한다:

```csharp
    [Fact]
    public void Default_cxp_bindings_match_what_the_line_pc_dump_showed()
    {
        var s = new WiresharkSettings();
        Assert.Contains(s.CxpNodes, b => b.Role == CxpNodeRole.LinkUp && b.Connection == 0);
    }
```

Run: `dotnet test tests\VisionSupport.Tests --filter "FullyQualifiedName~WiresharkSettingsTests"` → PASS

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md src/VisionSupport.Wireshark/Settings/WiresharkSettings.cs tests/VisionSupport.Tests/WiresharkSettingsTests.cs
git commit -m "docs: record what the Rapixo board allows while VISION grabs, and bind those nodes"
```

---

### Task 18: 문서 정리와 전체 검증

**Files:**
- Modify: `README.md` ("화면 구조" 트리, "기능 모듈 계약" 표와 `HasAlert` 설명)
- Modify: `CLAUDE.md` ("이 프로젝트"의 기능 나열, 설정 저장 위치)

- [ ] **Step 1: README**

"화면 구조" 트리의 도구 상자 줄을 다음으로:

```
 │  ├ 도구 → 3×3 상자     메모리 모니터 · PLC 서버 · 통신 모니터
```

"기능 모듈 계약" 표에 열 추가(기존 열 뒤):

| | 통신 모니터 |
|---|---|
| **가동 기준** (`IsWorking`) | 이더넷 캡처 또는 CXP 감시가 하나라도 돌고 있음 |
| **창 닫기 — 가동 중** | 캡처·판정 계속, 뷰만 해제 |
| **창 닫기 — 가동 아님** | 설정 저장 후 전부 해제 |
| **프로그램 종료** | ETW 세션 → `pktmon stop` → CXP 폴링 정지·GenTL 해제 |

표 아래에 한 문단:

```markdown
`IFeatureModule.HasAlert` 는 기능이 사용자에게 알릴 이상을 갖고 있는지다. 하나라도 참이면 런처 아이콘
링이 초록 대신 빨강이 된다(창이 다 닫혀 있어도). 지금은 통신 모니터만 쓴다 — 고정한 PLC/카메라가 응답하지
않거나 끊기거나 프레임을 잃으면 켜지고, 창의 "이상 확인"으로 끈다. 통신 모니터는 Windows 내장 pktmon 을
ETW 로 받아 설치할 것이 없고, CXP 는 Matrox GenTL 프로듀서를 폴링마다 열고 닫아 VISION 의 보드 사용을
막지 않는다. 설계는 `docs/superpowers/specs/2026-10-02-wireshark-monitor-design.md`.
```

- [ ] **Step 2: CLAUDE.md**

"이 프로젝트" 첫 문단의 기능 나열에 "통신 모니터"를 추가하고, "셸/기능 경계"의 프로젝트 나열에 `.Wireshark`를 추가한다. "설정 저장 위치"에 `통신 모니터는 D:\Datas\VisionSupport\Wireshark\settings.json(옛 %AppData% 폴백), CXP 진단 덤프도 같은 폴더` 한 줄을 추가한다.

- [ ] **Step 3: 전체 빌드·테스트**

Run: `dotnet build VisionSupport.sln` → 경고 외 성공
Run: `dotnet test tests\VisionSupport.Tests` → 전부 PASS (실패가 있으면 출력 그대로 보고)

- [ ] **Step 4: 장시간 실행 점검 (관리자, 사무실 PC)**

1. 캡처를 켜고 30분 둔다. 작업 관리자에서 메모리가 200MB 상한 근처에서 멈추는지 확인한다.
2. 앱을 작업 관리자에서 강제 종료 → 다시 실행 → 캡처 시작이 성공하는지(남은 세션 정리) 확인한다.

- [ ] **Step 5: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "docs: describe the communication monitor and the launcher's red ring"
```
