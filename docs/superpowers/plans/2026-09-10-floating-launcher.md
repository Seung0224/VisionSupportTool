# 플로팅 런처 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** VISION 지원툴을 "항상 떠 있는 셸 창 + 좌측 네비게이션"에서 "플로팅 아이콘 + 방사형 메뉴 + 필요할 때만 만드는 창" 구조로 바꾸고, 창을 닫으면 그 기능의 UI 자원이 실제로 해제되게 만든다.

**Architecture:** 셸(`src/VisionSupport`)만 바꾼다. 기능 3개 프로젝트는 손대지 않는다. `IFeatureModule`은 계속 `UserControl`을 돌려주고, 새 `FeatureWindow`가 그것을 창에 담아 제목·크기·불투명도·라운드 코너·닫기 정책을 한 곳에서 처리한다. 수명주기는 "창"과 "실행"을 분리한다 — 가동 중이면 창만 닫고(`ReleaseView`), 아니면 전부 해제(`StopAsync`).

**Tech Stack:** .NET 9 (`net9.0-windows`, x64), WPF, CommunityToolkit.Mvvm 8.4.2, xunit 2.9.2. Win32 P/Invoke (user32 레이어드 윈도우, gdi32 리전, dwmapi 라운드 코너).

**Spec:** `docs/superpowers/specs/2026-09-10-floating-launcher-design.md`

## Global Constraints

- 대상 OS는 **Windows 10 build 19045**. Win11 전용 API(`DWMWA_WINDOW_CORNER_PREFERENCE` = 33)는 반드시 `Environment.OSVersion.Version.Build >= 22000` 분기 뒤에 둔다.
- 기능 3개 프로젝트(`VisionSupport.MemoryMonitor`, `VisionSupport.PlcServer`, `VisionSupport.ImageConverter`)와 `VisionSupport.ImageConverter.Cognex`는 **수정 금지**. 기능은 셸을 모른다는 기존 대칭을 유지한다.
- `PlatformTarget`은 모든 프로젝트에서 x64로 고정되어 있다. `GetWindowLongPtrW` / `SetWindowLongPtrW`를 써도 안전하다.
- 불투명도 기본값 **0.92**, 범위 **0.30 ~ 1.00**.
- 방사형 메뉴 반지름 **110**, 타일 **56×56**, 런처 접힘 **72×72**, 펼침 **340×340**.
- 라운드 반경 토큰: `RadiusWindow` 16 · `RadiusCard` 14 · `RadiusTile` 18 · `RadiusControl` 10 · `RadiusPill` 999.
- 저장 파일: `%AppData%\VisionSupport\launcher.json`.
- 주석은 기존 코드베이스 관례를 따른다 — 영어로, "무엇"이 아니라 "왜"를 적는다. 사용자에게 보이는 문자열은 한국어.
- 빌드: `dotnet build VisionSupport.sln`
- 테스트: `dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj`

---

## 파일 구조

**신규**

| 파일 | 책임 |
|---|---|
| `src/VisionSupport/Launcher/RadialLayout.cs` | 방사형 배치 각도 계산 (순수 함수) |
| `src/VisionSupport/Launcher/LauncherSettings.cs` | 위치·불투명도 저장/복원, 화면 밖 좌표 보정 |
| `src/VisionSupport/Launcher/LauncherItem.cs` | 방사형 메뉴 한 항목 |
| `src/VisionSupport/Launcher/LauncherViewModel.cs` | 항목 목록, 창 열기/재사용, 종료 |
| `src/VisionSupport/Launcher/LauncherWindow.xaml(.cs)` | 플로팅 아이콘, 드래그, 펼침/접힘 |
| `src/VisionSupport/Windows/WindowEffects.cs` | 레이어드 알파 + 라운드 코너 P/Invoke |
| `src/VisionSupport/Windows/FeatureWindow.xaml(.cs)` | 기능 창 래퍼, 닫기 정책 |
| `src/VisionSupport/Overview/OverviewViewModel.cs` | 프로세스 자원 샘플링, 기능 행, 불투명도 |
| `src/VisionSupport/Overview/OverviewDialog.xaml(.cs)` | 전체보기 다이얼로그 |

**수정**

| 파일 | 변경 |
|---|---|
| `src/VisionSupport/Features/IFeatureModule.cs` | `Glyph`, `PreferredWindowSize`, `ReleaseView()` 추가 |
| `src/VisionSupport/Features/FeatureModule.cs` | `ReleaseView()` 구현, `SafeTeardown()`이 그것을 호출 |
| `src/VisionSupport/Features/*Feature.cs` | `Glyph`, `PreferredWindowSize` 오버라이드 |
| `src/VisionSupport/Theme/Dark.xaml` | 반경 토큰 + One UI 스타일 |
| `src/VisionSupport/App.xaml(.cs)` | 런처 기동, 전역 알파 클래스 핸들러, `ShutdownMode` |
| `tests/VisionSupport.Tests/VisionSupport.Tests.csproj` | 셸 프로젝트 참조 추가 |

**삭제**

`Shell/ShellWindow.xaml(.cs)` · `Shell/ShellViewModel.cs` · `Shell/NavItem.cs` · `Idle/IdleView.xaml(.cs)` · `Idle/IdleViewModel.cs`

**유지** `Shell/ActivityLog.cs` · `Shell/Converters.cs` · `Features/FeatureState.cs`

---

### Task 0: 버전 관리와 기준선

이 폴더는 아직 git 저장소가 아니다. 이 작업은 셸을 통째로 갈아엎으므로 되돌릴 수단이 있어야 한다.

**Files:**
- Create: `.gitignore`

- [ ] **Step 1: 지금 상태가 초록인지 확인**

```
dotnet build VisionSupport.sln
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj
```

Expected: 빌드 성공, 테스트 8개 통과. 실패하면 여기서 멈추고 보고한다 — 기준선이 빨간 상태에서 시작하면 뒤에서 무엇이 깨진 건지 구분할 수 없다.

- [ ] **Step 2: .gitignore 작성**

```gitignore
bin/
obj/
.vs/
*.user
```

- [ ] **Step 3: 저장소 초기화와 첫 커밋**

```bash
git init
git add -A
git commit -m "chore: baseline before floating launcher rewrite"
```

---

### Task 1: RadialLayout — 방사형 배치 계산

**Files:**
- Create: `src/VisionSupport/Launcher/RadialLayout.cs`
- Modify: `tests/VisionSupport.Tests/VisionSupport.Tests.csproj`
- Test: `tests/VisionSupport.Tests/RadialLayoutTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `VisionSupport.Launcher.RadialLayout.Offsets(int count, double radius) → System.Windows.Point[]`

- [ ] **Step 1: 테스트 프로젝트가 셸을 참조하게 한다**

`tests/VisionSupport.Tests/VisionSupport.Tests.csproj`의 `ProjectReference` `ItemGroup`에 한 줄 추가:

```xml
    <ProjectReference Include="..\..\src\VisionSupport\VisionSupport.csproj" />
```

- [ ] **Step 2: 실패하는 테스트를 쓴다**

`tests/VisionSupport.Tests/RadialLayoutTests.cs`:

```csharp
using System.Windows;
using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The menu's one part with a right answer: n items evenly spaced clockwise from twelve
/// o'clock. Y grows downward in WPF, so three o'clock is +X and six o'clock is +Y.
/// </summary>
public class RadialLayoutTests
{
    [Fact]
    public void Four_items_land_on_the_quarter_hours()
    {
        Point[] offsets = RadialLayout.Offsets(4, 100);

        Assert.Equal(4, offsets.Length);
        AssertPoint(0, -100, offsets[0]);
        AssertPoint(100, 0, offsets[1]);
        AssertPoint(0, 100, offsets[2]);
        AssertPoint(-100, 0, offsets[3]);
    }

    [Fact]
    public void Single_item_sits_at_twelve_oclock()
        => AssertPoint(0, -80, Assert.Single(RadialLayout.Offsets(1, 80)));

    [Fact]
    public void Every_offset_is_exactly_one_radius_from_the_centre()
    {
        foreach (Point p in RadialLayout.Offsets(7, 110))
        {
            Assert.Equal(110, Math.Sqrt(p.X * p.X + p.Y * p.Y), 6);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_counts_produce_nothing(int count)
        => Assert.Empty(RadialLayout.Offsets(count, 100));

    private static void AssertPoint(double x, double y, Point actual)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
    }
}
```

- [ ] **Step 3: 실패를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter RadialLayoutTests
```

Expected: 컴파일 실패 — `RadialLayout` 이름을 찾을 수 없음.

- [ ] **Step 4: 최소 구현**

`src/VisionSupport/Launcher/RadialLayout.cs`:

```csharp
using System.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// Where each item of the radial menu sits, relative to the launcher icon's centre.
///
/// Split out from the window because it is the one part of the menu that has a right answer;
/// everything else about the menu - timing, easing, tile size - is taste, and taste does not
/// belong in a unit test.
/// </summary>
public static class RadialLayout
{
    /// <summary>
    /// Offsets from the centre, in device-independent pixels, for <paramref name="count"/> items
    /// on a circle of <paramref name="radius"/>. Item 0 sits at twelve o'clock and the rest
    /// follow clockwise. An empty array for a count of zero or less.
    /// </summary>
    public static Point[] Offsets(int count, double radius)
    {
        if (count <= 0) return Array.Empty<Point>();

        var offsets = new Point[count];
        double step = 2 * Math.PI / count;

        for (int i = 0; i < count; i++)
        {
            // -PI/2 puts item 0 at twelve o'clock. Y grows downward in WPF, so adding the step
            // walks clockwise on screen without negating anything.
            double angle = -Math.PI / 2 + i * step;
            offsets[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        return offsets;
    }
}
```

- [ ] **Step 5: 통과를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter RadialLayoutTests
```

Expected: 5개 통과.

- [ ] **Step 6: 커밋**

```bash
git add tests/VisionSupport.Tests src/VisionSupport/Launcher
git commit -m "feat: add radial menu layout calculation"
```

---

### Task 2: LauncherSettings — 위치와 불투명도 저장

**Files:**
- Create: `src/VisionSupport/Launcher/LauncherSettings.cs`
- Test: `tests/VisionSupport.Tests/LauncherSettingsTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `LauncherSettings` (속성 `double IconLeft`, `double IconTop`, `double Opacity`)
  - `LauncherSettings.DefaultPath → string`
  - `LauncherSettings.Load(string path) → LauncherSettings`
  - `LauncherSettings.Save(string path)` (인스턴스 메서드)
  - `LauncherSettings.ConstrainToScreen(Point position, Size iconSize, Rect virtualScreen, Point fallback) → Point`
  - 상수 `DefaultOpacity = 0.92`, `MinOpacity = 0.30`, `MaxOpacity = 1.00`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/VisionSupport.Tests/LauncherSettingsTests.cs`:

```csharp
using System.IO;
using System.Windows;
using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Settings a support tool reads at startup have to survive the file being absent, truncated or
/// written by an older build. Failing to start because a preferences file is malformed would be
/// worse than losing the preference.
/// </summary>
public class LauncherSettingsTests
{
    [Fact]
    public void Round_trips_through_disk()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");

        new LauncherSettings { IconLeft = 640, IconTop = 480, Opacity = 0.75 }.Save(path);
        LauncherSettings loaded = LauncherSettings.Load(path);

        Assert.Equal(640, loaded.IconLeft);
        Assert.Equal(480, loaded.IconTop);
        Assert.Equal(0.75, loaded.Opacity);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var dir = new TempDir();

        LauncherSettings loaded = LauncherSettings.Load(Path.Combine(dir.Path, "nothing.json"));

        Assert.Equal(LauncherSettings.DefaultOpacity, loaded.Opacity);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_instead_of_throwing()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal(LauncherSettings.DefaultOpacity, LauncherSettings.Load(path).Opacity);
    }

    [Theory]
    [InlineData(0.0, LauncherSettings.MinOpacity)]
    [InlineData(2.5, LauncherSettings.MaxOpacity)]
    public void Opacity_outside_the_usable_range_is_clamped_on_load(double stored, double expected)
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "launcher.json");
        File.WriteAllText(path, "{\"IconLeft\":0,\"IconTop\":0,\"Opacity\":" + stored + "}");

        Assert.Equal(expected, LauncherSettings.Load(path).Opacity);
    }

    [Fact]
    public void A_position_on_screen_is_left_alone()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        Point kept = LauncherSettings.ConstrainToScreen(
            new Point(1000, 500), new Size(72, 72), screen, new Point(1, 1));

        Assert.Equal(new Point(1000, 500), kept);
    }

    [Fact]
    public void A_position_on_a_monitor_that_is_gone_falls_back()
    {
        // Remembered on a second monitor to the right; now only the primary is attached.
        var screen = new Rect(0, 0, 1920, 1080);
        var fallback = new Point(1800, 950);

        Point moved = LauncherSettings.ConstrainToScreen(
            new Point(3000, 500), new Size(72, 72), screen, fallback);

        Assert.Equal(fallback, moved);
    }

    [Fact]
    public void A_position_hanging_off_the_bottom_edge_falls_back()
    {
        var screen = new Rect(0, 0, 1920, 1080);
        var fallback = new Point(1800, 950);

        Point moved = LauncherSettings.ConstrainToScreen(
            new Point(900, 1070), new Size(72, 72), screen, fallback);

        Assert.Equal(fallback, moved);
    }
}
```

`TempDir`는 `tests/VisionSupport.Tests/TempDir.cs`에 이미 있다. 사용 전에 그 파일을 열어 생성자와 `Path` 속성 이름을 확인하고, 다르면 여기 테스트를 실제 API에 맞춘다.

- [ ] **Step 2: 실패를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter LauncherSettingsTests
```

Expected: 컴파일 실패 — `LauncherSettings` 없음.

- [ ] **Step 3: 구현**

`src/VisionSupport/Launcher/LauncherSettings.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// What the launcher remembers between runs: where the user parked the icon, and how far through
/// the windows they want to see.
///
/// Every read path here falls back to a working default rather than throwing. This file is a
/// convenience; a tool that refuses to start because its preferences are unreadable has turned a
/// convenience into a dependency.
/// </summary>
public sealed class LauncherSettings
{
    public const double DefaultOpacity = 0.92;

    /// <summary>Below this a window is hard to find on a busy desktop, so the slider stops here.</summary>
    public const double MinOpacity = 0.30;

    public const double MaxOpacity = 1.00;

    public double IconLeft { get; set; }

    public double IconTop { get; set; }

    public double Opacity { get; set; } = DefaultOpacity;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisionSupport", "launcher.json");

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new LauncherSettings();

            var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path));
            if (loaded is null) return new LauncherSettings();

            loaded.Opacity = ClampOpacity(loaded.Opacity);
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(
                this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the icon position is not worth failing a shutdown over.
        }
    }

    public static double ClampOpacity(double value)
        => double.IsNaN(value) ? DefaultOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);

    /// <summary>
    /// Pulls a remembered icon position back somewhere reachable.
    ///
    /// Monitor layouts change between runs - a laptop undocked from the monitor the icon was
    /// parked on would otherwise put the launcher at coordinates no screen covers, and the only
    /// way back would be deleting the settings file. The whole icon has to fit, not just its
    /// top-left corner, or it ends up half off the edge.
    /// </summary>
    public static Point ConstrainToScreen(Point position, Size iconSize, Rect virtualScreen, Point fallback)
    {
        var icon = new Rect(position, iconSize);
        return virtualScreen.Contains(icon) ? position : fallback;
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter LauncherSettingsTests
```

Expected: 8개 통과.

- [ ] **Step 5: 커밋**

```bash
git add tests/VisionSupport.Tests src/VisionSupport/Launcher
git commit -m "feat: persist launcher position and opacity"
```

---

### Task 3: 수명주기 분리 — ReleaseView

이 작업이 사유 1("자원이 누적된다")을 실제로 고치는 지점이다.

**Files:**
- Modify: `src/VisionSupport/Features/IFeatureModule.cs`
- Modify: `src/VisionSupport/Features/FeatureModule.cs`
- Modify: `src/VisionSupport/Features/MemoryMonitorFeature.cs`, `PlcServerFeature.cs`, `ImageConverterFeature.cs`
- Test: `tests/VisionSupport.Tests/FeatureModuleLifecycleTests.cs`

**Interfaces:**
- Consumes: `FeatureState` (기존)
- Produces:
  - `IFeatureModule.Glyph → string`
  - `IFeatureModule.PreferredWindowSize → System.Windows.Size`
  - `IFeatureModule.ReleaseView()` (void, 동기)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`tests/VisionSupport.Tests/FeatureModuleLifecycleTests.cs`:

```csharp
using System.Windows.Controls;
using VisionSupport.Features;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Closing a feature's window must not be the same thing as stopping the feature.
///
/// A PLC hub with a VISION client mid-test has to survive its window being closed; an image
/// converter that nobody is looking at has to disappear entirely. That is one decision made in
/// two places - the window asks, the module obeys - and these tests pin the module's half.
/// </summary>
public class FeatureModuleLifecycleTests
{
    [Fact]
    public void Releasing_the_view_builds_a_fresh_one_next_time()
    {
        var module = new FakeFeature();

        UserControl first = OnStaThread(module.GetOrCreateView);
        module.ReleaseView();
        UserControl second = OnStaThread(module.GetOrCreateView);

        Assert.NotSame(first, second);
        Assert.Equal(2, module.ViewsCreated);
    }

    [Fact]
    public void Releasing_the_view_does_not_touch_the_run_state()
    {
        var module = new FakeFeature();
        OnStaThread(module.GetOrCreateView);
        module.StartAsync().GetAwaiter().GetResult();

        module.ReleaseView();

        Assert.Equal(FeatureState.Running, module.State);
        Assert.False(module.WasStopped);
    }

    [Fact]
    public void Stopping_releases_the_view_as_well()
    {
        var module = new FakeFeature();
        UserControl first = OnStaThread(module.GetOrCreateView);
        module.StartAsync().GetAwaiter().GetResult();

        module.StopAsync().GetAwaiter().GetResult();

        Assert.Equal(FeatureState.Stopped, module.State);
        Assert.True(module.WasStopped);
        Assert.NotSame(first, OnStaThread(module.GetOrCreateView));
    }

    /// <summary>WPF controls need an STA thread even when nothing is shown.</summary>
    private static UserControl OnStaThread(Func<UserControl> build)
    {
        UserControl? result = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = build(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result!;
    }

    private sealed class FakeFeature : FeatureModule
    {
        public int ViewsCreated { get; private set; }

        public bool WasStopped { get; private set; }

        public override string Title => "테스트";

        public override string Description => "수명주기 검증용";

        protected override UserControl CreateView()
        {
            ViewsCreated++;
            return new UserControl();
        }

        protected override Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;

        protected override Task OnPauseAsync() => Task.CompletedTask;

        protected override Task OnResumeAsync() => Task.CompletedTask;

        protected override Task OnStopAsync()
        {
            WasStopped = true;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter FeatureModuleLifecycleTests
```

Expected: 컴파일 실패 — `FeatureModule`에 `ReleaseView`가 없음.

- [ ] **Step 3: 인터페이스에 세 멤버를 추가한다**

`src/VisionSupport/Features/IFeatureModule.cs`에 `using System.Windows;`를 더하고, `GetOrCreateView()` 선언 바로 아래에 넣는다:

```csharp
    /// <summary>Radial menu tile icon. One Segoe MDL2 Assets character.</summary>
    string Glyph { get; }

    /// <summary>Opening size of this feature's window.</summary>
    Size PreferredWindowSize { get; }

    /// <summary>
    /// Drops the feature's UI without touching what it is running.
    ///
    /// This is what closing a feature's window does while the feature is still up: servers, ETW
    /// sessions and sampling timers carry on, but every pixel the feature owns goes away and is
    /// rebuilt from scratch on the next visit. Without this split, closing a window would cut a
    /// VISION client off mid-test.
    /// </summary>
    void ReleaseView();
```

- [ ] **Step 4: FeatureModule에 구현을 넣는다**

`src/VisionSupport/Features/FeatureModule.cs`:

(a) `Description` 아래에 기본값 두 개를 더한다.

```csharp
    public virtual string Glyph => "";

    public virtual Size PreferredWindowSize => new(1100, 720);
```

`using System.Windows;`는 이미 있다.

(b) `GetOrCreateView()` 아래에 공개 메서드를 더한다.

```csharp
    /// <summary>
    /// Drops the view and everything hanging off it, leaving the feature running.
    ///
    /// Detail windows go with it: they read live state out of the feature, and one left behind
    /// after its parent window closed is a floating panel with no way back to what opened it.
    /// The view's DataContext is deliberately not disposed here - that is the feature's own
    /// ViewModel, and it is the thing being kept alive.
    /// </summary>
    public void ReleaseView()
    {
        foreach (Window window in _ownedWindows.ToArray())
        {
            try
            {
                window.Close();
            }
            catch
            {
                // A window already closing throws; nothing here is worth failing over.
            }
        }
        _ownedWindows.Clear();

        (_view as IDisposable)?.Dispose();
        _view = null;
    }
```

(c) `SafeTeardown()`의 끝부분에서 창 닫기 루프와 뷰 버리기 블록을 지우고 그 자리에 아래를 넣는다. 즉 다음 세 덩어리를 —

```
        foreach (Window window in _ownedWindows.ToArray()) { ... }
        _ownedWindows.Clear();
        ...
        (_view as IDisposable)?.Dispose();
        (_view?.DataContext as IDisposable)?.Dispose();
        _view = null;
```

— 이렇게 바꾼다 (`_cts` 정리 블록은 그대로 둔다):

```csharp
        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // Disposing an already-disposed source is harmless.
        }
        _cts = null;

        // Dispose the ViewModel first, then hand the rest to ReleaseView - a full stop is a
        // view release plus letting go of what the view was showing.
        (_view?.DataContext as IDisposable)?.Dispose();
        ReleaseView();

        return clean;
```

- [ ] **Step 5: 통과를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter FeatureModuleLifecycleTests
```

Expected: 3개 통과.

- [ ] **Step 6: 기능 3개에 글리프와 창 크기를 준다**

`MemoryMonitorFeature.cs` — `Description` 아래:

```csharp
    public override string Glyph => "";

    public override Size PreferredWindowSize => new(1280, 820);
```

`PlcServerFeature.cs`:

```csharp
    public override string Glyph => "";

    public override Size PreferredWindowSize => new(1180, 760);
```

`ImageConverterFeature.cs`:

```csharp
    public override string Glyph => "";

    public override Size PreferredWindowSize => new(980, 700);
```

세 파일 모두 `using System.Windows;`가 필요하다.

> 글리프 코드포인트는 Segoe MDL2 Assets 기준이다. 실행했을 때 빈 네모로 보이면 `charmap.exe`에서 Segoe MDL2 Assets를 골라 원하는 문자를 찾아 바꾼다. Task 10 스모크 체크리스트에 확인 항목이 있다.

- [ ] **Step 7: 전체 빌드와 테스트**

```
dotnet build VisionSupport.sln
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj
```

Expected: 빌드 성공. 기존 8개 + 새 테스트 16개 전부 통과. `ShellViewModel`은 아직 살아 있고 `IFeatureModule`의 새 멤버를 쓰지 않으므로 컴파일은 깨지지 않는다.

- [ ] **Step 8: 커밋**

```bash
git add src/VisionSupport/Features tests/VisionSupport.Tests
git commit -m "feat: split view release from feature teardown"
```

---

### Task 4: WindowEffects — 레이어드 알파와 라운드 코너

**Files:**
- Create: `src/VisionSupport/Windows/WindowEffects.cs`
- Test: `tests/VisionSupport.Tests/WindowEffectsTests.cs`

**Interfaces:**
- Consumes: `LauncherSettings.ClampOpacity`
- Produces:
  - `WindowEffects.ToAlphaByte(double opacity) → byte`
  - `WindowEffects.ApplyAlpha(Window window, double opacity)`
  - `WindowEffects.ApplyAlphaToAll(double opacity)`
  - `WindowEffects.AttachRoundedCorners(Window window, double radius)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

순수한 부분만 테스트한다. P/Invoke는 실제 HWND가 있어야 하므로 단위 테스트 대상이 아니고, Task 10 스모크에서 눈으로 확인한다.

`tests/VisionSupport.Tests/WindowEffectsTests.cs`:

```csharp
using VisionSupport.Launcher;
using VisionSupport.Windows;
using Xunit;

namespace VisionSupport.Tests;

public class WindowEffectsTests
{
    [Fact]
    public void Full_opacity_is_fully_opaque()
        => Assert.Equal((byte)255, WindowEffects.ToAlphaByte(1.0));

    [Fact]
    public void The_default_opacity_maps_to_the_expected_byte()
        => Assert.Equal((byte)235, WindowEffects.ToAlphaByte(LauncherSettings.DefaultOpacity));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Values_below_the_floor_clamp_to_the_floor(double opacity)
        => Assert.Equal((byte)77, WindowEffects.ToAlphaByte(opacity));

    [Fact]
    public void Values_above_one_clamp_to_opaque()
        => Assert.Equal((byte)255, WindowEffects.ToAlphaByte(3.0));

    [Fact]
    public void NaN_falls_back_to_the_default_rather_than_vanishing()
        => Assert.Equal((byte)235, WindowEffects.ToAlphaByte(double.NaN));
}
```

기대값 근거: `0.92 × 255 = 234.6 → 235`, `0.30 × 255 = 76.5 → 77`(`MidpointRounding.AwayFromZero`).

- [ ] **Step 2: 실패를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter WindowEffectsTests
```

Expected: 컴파일 실패 — `WindowEffects` 없음.

- [ ] **Step 3: 구현**

`src/VisionSupport/Windows/WindowEffects.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VisionSupport.Launcher;

namespace VisionSupport.Windows;

/// <summary>
/// Two pieces of window chrome WPF cannot express on Windows 10: constant alpha for a whole
/// window, and rounded outer corners.
///
/// Alpha goes through the Win32 layered-window API rather than <see cref="Window.Opacity"/>,
/// because Window.Opacity requires AllowsTransparency=true, which drops the window onto a
/// software rendering path. Every window in this process is meant to be see-through - including
/// the feature projects' own dialogs, which this assembly must not modify - so that cost would
/// land everywhere, on the very tool whose job is to watch resource use.
///
/// Corners take two routes. Windows 11 has a DWM attribute that rounds them with proper
/// antialiasing. Windows 10 has nothing, so the HWND itself is clipped to a rounded region;
/// the result is aliased at the corners, which is the price of keeping GPU rendering.
/// </summary>
public static class WindowEffects
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int LwaAlpha = 0x00000002;

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE. Silently ignored before Windows 11.</summary>
    private const int DwmwaWindowCornerPreference = 33;

    private const int DwmwcpRound = 2;

    /// <summary>The build where DWM learned to round corners itself.</summary>
    private const int Windows11Build = 22000;

    // The Ptr variants are the correct 64-bit API and this app pins PlatformTarget to x64, so
    // there is no 32-bit host to fall back for.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, int flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
                                                    int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    /// <summary>Opacity as the 0-255 alpha the layered window API wants, clamped to a usable range.</summary>
    public static byte ToAlphaByte(double opacity)
        => (byte)Math.Round(LauncherSettings.ClampOpacity(opacity) * 255, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Makes one window see-through. Does nothing before the window has an HWND, so callers
    /// should be on Loaded or SourceInitialized.
    /// </summary>
    public static void ApplyAlpha(Window window, double opacity)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExLayered));
        SetLayeredWindowAttributes(hwnd, 0, ToAlphaByte(opacity), LwaAlpha);
    }

    /// <summary>
    /// Re-applies alpha to every open window. The slider changes one number and the whole app
    /// has to follow, including dialogs this assembly never sees the source of.
    /// </summary>
    public static void ApplyAlphaToAll(double opacity)
    {
        if (Application.Current is not { } app) return;

        foreach (Window window in app.Windows)
        {
            // The launcher paints its own shape and needs real per-pixel transparency; layering
            // a constant alpha over it would wash out the icon.
            if (window.AllowsTransparency) continue;
            ApplyAlpha(window, opacity);
        }
    }

    /// <summary>
    /// Rounds a window's outer corners and keeps them rounded.
    ///
    /// On Windows 10 the region is in physical pixels and has to be rebuilt whenever the window
    /// resizes or moves to a monitor with different scaling, so this subscribes rather than
    /// applying once. A maximised window gets no region at all - clipping one leaves four
    /// notches of desktop showing at the screen corners.
    /// </summary>
    public static void AttachRoundedCorners(Window window, double radius)
    {
        void Apply() => ApplyRoundedCorners(window, radius);

        Apply();
        window.SizeChanged += (_, _) => Apply();
        window.StateChanged += (_, _) => Apply();
        window.DpiChanged += (_, _) => Apply();
    }

    private static void ApplyRoundedCorners(Window window, double radius)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (Environment.OSVersion.Version.Build >= Windows11Build)
        {
            int preference = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        double scale = PresentationSource.FromVisual(window) is HwndSource source
            ? source.CompositionTarget.TransformToDevice.M11
            : 1.0;

        int width = (int)Math.Round(window.ActualWidth * scale);
        int height = (int)Math.Round(window.ActualHeight * scale);
        if (width <= 0 || height <= 0) return;

        // CreateRoundRectRgn takes the ellipse's full width and height, not its radius, and its
        // right/bottom bounds are exclusive - hence the doubling and the +1.
        int ellipse = (int)Math.Round(radius * scale) * 2;

        // SetWindowRgn takes ownership of the region; deleting it here would blank the window.
        SetWindowRgn(hwnd, CreateRoundRectRgn(0, 0, width + 1, height + 1, ellipse, ellipse), true);
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj --filter WindowEffectsTests
```

Expected: 6개 통과.

- [ ] **Step 5: 커밋**

```bash
git add src/VisionSupport/Windows tests/VisionSupport.Tests
git commit -m "feat: add layered-window alpha and rounded corners"
```

---

### Task 5: 테마 — 반경 토큰과 One UI 스타일

**Files:**
- Modify: `src/VisionSupport/Theme/Dark.xaml`

**Interfaces:**
- Produces: 리소스 키 `RadiusWindow`, `RadiusCard`, `RadiusTile`, `RadiusControl`, `RadiusPill`, 스타일 `Card`, `AccentButton`, `TitleBarButton`

- [ ] **Step 1: 반경 토큰을 추가한다**

`Dark.xaml`의 `StateFaulted` 브러시 선언 바로 아래에 넣는다:

```xml
    <!-- One UI-flavoured corner radii. Every rounded surface reads one of these rather than
         inventing its own number, so the whole app rounds by the same amount at the same scale. -->
    <CornerRadius x:Key="RadiusWindow">16</CornerRadius>
    <CornerRadius x:Key="RadiusCard">14</CornerRadius>
    <CornerRadius x:Key="RadiusTile">18</CornerRadius>
    <CornerRadius x:Key="RadiusControl">10</CornerRadius>
    <CornerRadius x:Key="RadiusPill">999</CornerRadius>
```

- [ ] **Step 2: 기존 각진 반경을 토큰으로 바꾼다**

세 군데의 하드코딩된 값을 교체한다. **`CornerRadius="3"` → `CornerRadius="{StaticResource RadiusControl}"`**:

- `Style TargetType="Button"`의 템플릿 안 `Border x:Name="Chrome"`
- `Style x:Key="ComboToggle"`의 템플릿 안 `Border x:Name="Chrome"`

**`CornerRadius="4"` → `CornerRadius="{StaticResource RadiusControl}"`**:

- `Style TargetType="ToolTip"`의 템플릿 안 `Border`

`ScrollThumb`의 `CornerRadius="4"`는 그대로 둔다 — 폭 6px 썸에 반경 10을 주면 알약이 아니라 뭉개진 점이 된다.

패딩은 건드리지 않는다. 메모리 모니터 툴바처럼 빽빽한 화면이 이 스타일을 그대로 쓰고 있어, 여백을 키우면 레이아웃이 밀린다.

- [ ] **Step 3: 새 스타일 세 개를 추가한다**

`Dark.xaml`의 `TabItem` 스타일 뒤, `</ResourceDictionary>` 앞에 넣는다:

```xml
    <!-- A rounded surface one step lighter than the page behind it. Sections are separated by
         tone rather than by lines, which is what makes the One UI look read as soft. -->
    <Style x:Key="Card" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource Panel}"/>
        <Setter Property="CornerRadius" Value="{StaticResource RadiusCard}"/>
        <Setter Property="Padding" Value="14,12"/>
    </Style>

    <Style x:Key="AccentButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{StaticResource Accent}"/>
        <Setter Property="BorderBrush" Value="{StaticResource Accent}"/>
        <Setter Property="Foreground" Value="White"/>
    </Style>

    <!-- Caption buttons for the custom title bar. Square, borderless, no margin - they sit flush
         in the corner the way Windows' own do, and only the close button turns red. -->
    <Style x:Key="TitleBarButton" TargetType="Button">
        <Setter Property="Width" Value="46"/>
        <Setter Property="Height" Value="32"/>
        <Setter Property="Margin" Value="0"/>
        <Setter Property="Padding" Value="0"/>
        <Setter Property="Foreground" Value="{StaticResource FgDim}"/>
        <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
        <Setter Property="FontSize" Value="10"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Chrome" Background="Transparent" CornerRadius="{StaticResource RadiusControl}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{StaticResource PanelAlt}"/>
                            <Setter Property="Foreground" Value="{StaticResource Fg}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 4: 빌드로 XAML 파싱을 확인한다**

```
dotnet build VisionSupport.sln
```

Expected: 성공. XAML 오류는 빌드 시점에 잡힌다.

- [ ] **Step 5: 커밋**

```bash
git add src/VisionSupport/Theme/Dark.xaml
git commit -m "feat: add One UI corner radius tokens and card styles"
```

---

### Task 6: FeatureWindow — 기능 창 래퍼

**Files:**
- Create: `src/VisionSupport/Windows/FeatureWindow.xaml`
- Create: `src/VisionSupport/Windows/FeatureWindow.xaml.cs`

**Interfaces:**
- Consumes: `IFeatureModule` (`Title`, `StatusLine`, `State`, `PreferredWindowSize`, `GetOrCreateView()`, `ReleaseView()`, `StopAsync()`, `Changed`), `WindowEffects.AttachRoundedCorners`, 스타일 `TitleBarButton`, 컨버터 `StateBrush`
- Produces:
  - `FeatureWindow(IFeatureModule module)` 생성자
  - `FeatureWindow.Module → IFeatureModule`
  - `FeatureWindow.ForceClose()`

- [ ] **Step 1: XAML을 쓴다**

`src/VisionSupport/Windows/FeatureWindow.xaml`:

```xml
<Window x:Class="VisionSupport.Windows.FeatureWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:chrome="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Background="{StaticResource Bg}"
        WindowStartupLocation="CenterScreen"
        MinWidth="720" MinHeight="480"
        UseLayoutRounding="True">

    <!-- WindowChrome rather than AllowsTransparency: a custom dark caption without dropping the
         window onto WPF's software rendering path. The corners are rounded by WindowEffects,
         which clips the HWND itself. -->
    <chrome:WindowChrome.WindowChrome>
        <chrome:WindowChrome CaptionHeight="40" GlassFrameThickness="0" CornerRadius="0"
                             ResizeBorderThickness="6" UseAeroCaptionButtons="False"/>
    </chrome:WindowChrome.WindowChrome>

    <Border Background="{StaticResource Bg}" CornerRadius="{StaticResource RadiusWindow}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="40"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <Border Grid.Row="0" Background="{StaticResource Panel}"
                    CornerRadius="{StaticResource RadiusWindow}">
                <!-- The caption's bottom corners have to stay square where it meets the content;
                     clipping the whole window is what rounds the outside. -->
                <Border.Clip>
                    <RectangleGeometry x:Name="CaptionClip" Rect="0,0,4000,40"/>
                </Border.Clip>

                <Grid Margin="16,0,4,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <Ellipse x:Name="StateDot" Grid.Column="0" Width="9" Height="9"
                             Margin="0,0,10,0" VerticalAlignment="Center"
                             Fill="{StaticResource StateStopped}"/>

                    <TextBlock x:Name="TitleText" Grid.Column="1" FontSize="13" FontWeight="SemiBold"/>

                    <TextBlock x:Name="StatusText" Grid.Column="2" Margin="14,0,14,0"
                               Foreground="{StaticResource FgDim}" FontSize="12"
                               TextTrimming="CharacterEllipsis"/>

                    <StackPanel Grid.Column="3" Orientation="Horizontal"
                                chrome:WindowChrome.IsHitTestVisibleInChrome="True">
                        <Button x:Name="MinimizeButton" Style="{StaticResource TitleBarButton}"
                                Content="&#xE921;" ToolTip="최소화"/>
                        <Button x:Name="MaximizeButton" Style="{StaticResource TitleBarButton}"
                                Content="&#xE922;" ToolTip="최대화"/>
                        <Button x:Name="CloseButton" Style="{StaticResource TitleBarButton}"
                                Content="&#xE8BB;" ToolTip="닫기"/>
                    </StackPanel>
                </Grid>
            </Border>

            <ContentPresenter x:Name="Host" Grid.Row="1"/>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 2: 코드비하인드를 쓴다**

`src/VisionSupport/Windows/FeatureWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VisionSupport.Features;

namespace VisionSupport.Windows;

/// <summary>
/// One feature's window.
///
/// The shell owns the chrome so the features do not have to: title, caption buttons, rounded
/// corners and - the part that matters - what closing means. Closing decides between letting go
/// of the whole feature and letting go of only its pixels, based on whether the feature is
/// actually running. That is the whole point of the rewrite: a converter nobody is looking at
/// should cost nothing, and a PLC hub with a client attached should survive its window closing.
/// </summary>
public partial class FeatureWindow : Window
{
    /// <summary>Matches Theme/Dark.xaml's RadiusWindow. Kept in sync by hand; it is one number.</summary>
    private const double CornerRadius = 16;

    private bool _readyToClose;
    private bool _closeRequested;

    public FeatureWindow(IFeatureModule module)
    {
        InitializeComponent();

        Module = module;
        Title = module.Title;
        TitleText.Text = module.Title;
        Width = module.PreferredWindowSize.Width;
        Height = module.PreferredWindowSize.Height;
        Host.Content = module.GetOrCreateView();

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();

        module.Changed += OnModuleChanged;

        SourceInitialized += (_, _) => WindowEffects.AttachRoundedCorners(this, CornerRadius);
        SizeChanged += (_, e) => CaptionClip.Rect = new Rect(0, 0, e.NewSize.Width, 40);
        Closing += OnClosing;
        Closed += (_, _) => module.Changed -= OnModuleChanged;

        RefreshCaption();
    }

    public IFeatureModule Module { get; }

    /// <summary>
    /// Closes without running the stop-or-release decision. Used on app exit, where every feature
    /// is being torn down anyway and asking each window again would only race the shutdown.
    /// </summary>
    public void ForceClose()
    {
        _readyToClose = true;
        Close();
    }

    /// <summary>
    /// Decides what closing costs.
    ///
    /// Stopped or faulted, the feature is not doing anything worth keeping, so it goes away
    /// completely - threads, sockets, ETW sessions, the lot. Running or paused, only the view is
    /// dropped and the work carries on in the background, visible on the launcher's ring and
    /// stoppable from the overview dialog.
    ///
    /// The dance with the two flags is the same one the old shell window needed: awaiting the
    /// teardown synchronously deadlocks, because the stop path marshals state changes back to
    /// the UI thread that would be blocked waiting. So the close is cancelled, the teardown is
    /// genuinely awaited, and the window is closed afterwards - and a second Alt+F4 arriving
    /// during that wait must not start the whole thing again.
    /// </summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose) return;

        e.Cancel = true;
        if (_closeRequested) return;
        _closeRequested = true;

        if (Module.State is FeatureState.Running or FeatureState.Paused)
        {
            Module.ReleaseView();
        }
        else
        {
            await Module.StopAsync();
        }

        _readyToClose = true;
        Close();
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        RefreshCaption();

        // A feature that stopped on its own - from a button inside its own view - has dropped
        // the control this window is showing. Rebuild it, or the last chart stays on screen
        // looking live. Not while closing: that would resurrect the view we just released.
        if (!_closeRequested && Module.State is FeatureState.Stopped or FeatureState.Faulted)
        {
            Host.Content = Module.GetOrCreateView();
        }
    }

    private void RefreshCaption()
    {
        StatusText.Text = Module.StatusLine;

        string key = Module.State switch
        {
            FeatureState.Running or FeatureState.Starting => "StateRunning",
            FeatureState.Paused or FeatureState.Stopping => "StatePaused",
            FeatureState.Faulted => "StateFaulted",
            _ => "StateStopped",
        };

        StateDot.Fill = Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
```

- [ ] **Step 3: 빌드**

```
dotnet build VisionSupport.sln
```

Expected: 성공. 아직 아무도 `FeatureWindow`를 만들지 않으므로 실행 동작은 바뀌지 않는다.

- [ ] **Step 4: 커밋**

```bash
git add src/VisionSupport/Windows
git commit -m "feat: add feature window wrapper with close policy"
```

---

### Task 7: 전체보기 다이얼로그

**Files:**
- Create: `src/VisionSupport/Overview/OverviewViewModel.cs`
- Create: `src/VisionSupport/Overview/OverviewDialog.xaml`
- Create: `src/VisionSupport/Overview/OverviewDialog.xaml.cs`

**Interfaces:**
- Consumes: `IFeatureModule`, `ActivityLog`, `LauncherSettings`, `WindowEffects.ApplyAlphaToAll`, `WindowEffects.AttachRoundedCorners`
- Produces:
  - `OverviewViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity, LauncherSettings settings, Action<IFeatureModule> open)`
  - `OverviewViewModel.Activate()` / `Deactivate()`
  - `OverviewViewModel.Rows → ObservableCollection<FeatureRow>`
  - `OverviewDialog(OverviewViewModel viewModel)`

- [ ] **Step 1: ViewModel을 쓴다**

`Idle/IdleViewModel.cs`의 자원 샘플링을 그대로 가져오되, 카드에 정지 기능을 더한다.

`src/VisionSupport/Overview/OverviewViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;
using VisionSupport.Windows;

namespace VisionSupport.Overview;

/// <summary>
/// The small dialog that answers "what is this thing costing me right now, and what is still
/// running?" - the part of the old home screen worth keeping, at a tenth of the screen area.
///
/// The timer only ticks while the dialog is open (Activate/Deactivate come from the view's
/// Loaded/Unloaded). A support tool that burns a wakeup every second while nobody is looking at
/// it is exactly the thing this tool exists to catch.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly DateTime _startedAt;
    private readonly DispatcherTimer _timer;
    private readonly LauncherSettings _settings;

    private TimeSpan _lastCpuTotal;
    private DateTime _lastCpuAt;

    [ObservableProperty]
    private string _uptime = string.Empty;

    [ObservableProperty]
    private string _selfMemory = string.Empty;

    [ObservableProperty]
    private string _selfCpu = string.Empty;

    [ObservableProperty]
    private int _threadCount;

    public OverviewViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity,
                             LauncherSettings settings, DateTime startedAt,
                             Action<IFeatureModule> open)
    {
        _settings = settings;
        _startedAt = startedAt;
        Activity = activity;

        foreach (IFeatureModule module in modules) Rows.Add(new FeatureRow(module, open));

        _lastCpuTotal = _self.TotalProcessorTime;
        _lastCpuAt = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<FeatureRow> Rows { get; } = new();

    public ActivityLog Activity { get; }

    /// <summary>Bound to the slider. Setting it repaints every open window immediately.</summary>
    public double Opacity
    {
        get => _settings.Opacity;
        set
        {
            double clamped = LauncherSettings.ClampOpacity(value);
            if (Math.Abs(_settings.Opacity - clamped) < 0.001) return;

            _settings.Opacity = clamped;
            WindowEffects.ApplyAlphaToAll(clamped);
            OnPropertyChanged();
        }
    }

    public double MinOpacity => LauncherSettings.MinOpacity;

    public double MaxOpacity => LauncherSettings.MaxOpacity;

    public void Activate() => _timer.Start();

    public void Deactivate()
    {
        _timer.Stop();
        _settings.Save(LauncherSettings.DefaultPath);
    }

    private void Refresh()
    {
        TimeSpan up = DateTime.Now - _startedAt;
        Uptime = up.Days > 0
            ? $"{up.Days}일 {up.Hours}시간 {up.Minutes}분"
            : $"{up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}";

        _self.Refresh();
        SelfMemory = FormatBytes(_self.PrivateMemorySize64);
        ThreadCount = _self.Threads.Count;

        // CPU as a share of one core-second per wall-clock second, divided across all cores, so
        // the number means the same thing as Task Manager's column.
        TimeSpan cpuTotal = _self.TotalProcessorTime;
        DateTime cpuAt = DateTime.UtcNow;
        double elapsed = (cpuAt - _lastCpuAt).TotalSeconds;
        if (elapsed > 0)
        {
            double used = (cpuTotal - _lastCpuTotal).TotalSeconds;
            SelfCpu = (used / (elapsed * Environment.ProcessorCount) * 100.0).ToString("F1") + " %";
        }
        _lastCpuTotal = cpuTotal;
        _lastCpuAt = cpuAt;

        foreach (FeatureRow row in Rows) row.Refresh();
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "F0" : "F1") + " " + units[unit];
    }
}

/// <summary>
/// One feature's row. Carries the stop button, which is the only way to bring down a feature
/// that is running with its window closed.
/// </summary>
public sealed partial class FeatureRow : ObservableObject
{
    private readonly Action<IFeatureModule> _open;

    public FeatureRow(IFeatureModule module, Action<IFeatureModule> open)
    {
        Module = module;
        _open = open;
        module.Changed += (_, _) => Refresh();
    }

    public IFeatureModule Module { get; }

    public string Title => Module.Title;

    public FeatureState State => Module.State;

    public string StatusLine => Module.StatusLine;

    public string ToggleLabel => Module.State is FeatureState.Running or FeatureState.Paused
        ? "정지"
        : "실행";

    [RelayCommand]
    private void Open() => _open(Module);

    [RelayCommand]
    private async Task Toggle()
    {
        if (Module.State is FeatureState.Running or FeatureState.Paused) await Module.StopAsync();
        else await Module.StartAsync();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(ToggleLabel));
    }
}
```

- [ ] **Step 2: XAML을 쓴다**

`src/VisionSupport/Overview/OverviewDialog.xaml`:

```xml
<Window x:Class="VisionSupport.Overview.OverviewDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:chrome="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="전체보기" Width="420" Height="380"
        Background="{StaticResource Bg}"
        ResizeMode="NoResize" ShowInTaskbar="False"
        WindowStartupLocation="CenterScreen"
        UseLayoutRounding="True">

    <chrome:WindowChrome.WindowChrome>
        <chrome:WindowChrome CaptionHeight="36" GlassFrameThickness="0" CornerRadius="0"
                             ResizeBorderThickness="0" UseAeroCaptionButtons="False"/>
    </chrome:WindowChrome.WindowChrome>

    <Border Background="{StaticResource Bg}" CornerRadius="{StaticResource RadiusWindow}" Padding="14">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <Grid Grid.Row="0" Margin="2,0,0,10">
                <TextBlock Text="전체보기" FontSize="14" FontWeight="SemiBold"/>
                <Button x:Name="CloseButton" Style="{StaticResource TitleBarButton}"
                        Content="&#xE8BB;" HorizontalAlignment="Right"
                        chrome:WindowChrome.IsHitTestVisibleInChrome="True"/>
            </Grid>

            <Border Grid.Row="1" Style="{StaticResource Card}" Margin="0,0,0,10">
                <UniformGrid Columns="4">
                    <StackPanel>
                        <TextBlock Text="메모리" Foreground="{StaticResource FgDim}" FontSize="11"/>
                        <TextBlock Text="{Binding SelfMemory}" FontSize="15" Margin="0,3,0,0"/>
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="CPU" Foreground="{StaticResource FgDim}" FontSize="11"/>
                        <TextBlock Text="{Binding SelfCpu}" FontSize="15" Margin="0,3,0,0"/>
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="스레드" Foreground="{StaticResource FgDim}" FontSize="11"/>
                        <TextBlock Text="{Binding ThreadCount}" FontSize="15" Margin="0,3,0,0"/>
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="가동시간" Foreground="{StaticResource FgDim}" FontSize="11"/>
                        <TextBlock Text="{Binding Uptime}" FontSize="13" Margin="0,3,0,0"/>
                    </StackPanel>
                </UniformGrid>
            </Border>

            <ItemsControl Grid.Row="2" ItemsSource="{Binding Rows}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource Card}" Margin="0,0,0,6">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>

                                <Ellipse Grid.Column="0" Width="9" Height="9" Margin="0,0,10,0"
                                         Fill="{Binding State, Converter={StaticResource StateBrush}}"/>

                                <StackPanel Grid.Column="1">
                                    <TextBlock Text="{Binding Title}" FontSize="13"/>
                                    <TextBlock Text="{Binding StatusLine}" FontSize="11"
                                               Foreground="{StaticResource FgDim}"
                                               TextTrimming="CharacterEllipsis"
                                               Visibility="{Binding StatusLine,
                                                           Converter={StaticResource EmptyToVis}}"/>
                                </StackPanel>

                                <StackPanel Grid.Column="2" Orientation="Horizontal">
                                    <Button Content="{Binding ToggleLabel}" Command="{Binding ToggleCommand}"
                                            MinWidth="52"/>
                                    <Button Content="열기" Command="{Binding OpenCommand}"
                                            Style="{StaticResource AccentButton}" MinWidth="52" Margin="0"/>
                                </StackPanel>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <Border Grid.Row="3" Style="{StaticResource Card}" Margin="0,4,0,8">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="불투명도" FontSize="12" Margin="0,0,12,0"
                               Foreground="{StaticResource FgDim}"/>
                    <Slider Grid.Column="1" Value="{Binding Opacity, Mode=TwoWay}"
                            Minimum="{Binding MinOpacity}" Maximum="{Binding MaxOpacity}"
                            SmallChange="0.01" LargeChange="0.05" VerticalAlignment="Center"/>
                </Grid>
            </Border>

            <TextBlock Grid.Row="4" Margin="2,0,0,0" FontSize="11"
                       Foreground="{StaticResource FgDim}" TextTrimming="CharacterEllipsis"
                       DataContext="{Binding Activity.Entries[0]}">
                <Run Text="{Binding Time, Mode=OneWay}"/>
                <Run Text="{Binding Source, Mode=OneWay}"/>
                <Run Text="{Binding Message, Mode=OneWay}"/>
            </TextBlock>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 3: 코드비하인드를 쓴다**

`src/VisionSupport/Overview/OverviewDialog.xaml.cs`:

```csharp
using System.Windows;
using VisionSupport.Windows;

namespace VisionSupport.Overview;

public partial class OverviewDialog : Window
{
    /// <summary>Matches Theme/Dark.xaml's RadiusWindow.</summary>
    private const double CornerRadius = 16;

    public OverviewDialog(OverviewViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();

        SourceInitialized += (_, _) => WindowEffects.AttachRoundedCorners(this, CornerRadius);
        Loaded += (_, _) => viewModel.Activate();
        Closed += (_, _) => viewModel.Deactivate();
    }
}
```

- [ ] **Step 4: 빌드**

```
dotnet build VisionSupport.sln
```

Expected: 성공. 실패한다면 대개 `StateBrush` / `EmptyToVis` 컨버터 키다 — `App.xaml`에 이미 선언되어 있으니 키 철자를 확인한다.

- [ ] **Step 5: 커밋**

```bash
git add src/VisionSupport/Overview
git commit -m "feat: add compact overview dialog"
```

---

### Task 8: 런처 — 플로팅 아이콘과 방사형 메뉴

**Files:**
- Create: `src/VisionSupport/Launcher/LauncherItem.cs`
- Create: `src/VisionSupport/Launcher/LauncherViewModel.cs`
- Create: `src/VisionSupport/Launcher/LauncherWindow.xaml`
- Create: `src/VisionSupport/Launcher/LauncherWindow.xaml.cs`

**Interfaces:**
- Consumes: `RadialLayout.Offsets`, `LauncherSettings`, `FeatureWindow`, `OverviewDialog`, `OverviewViewModel`, `ActivityLog`, `IFeatureModule`
- Produces:
  - `LauncherViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity, LauncherSettings settings, DateTime startedAt)`
  - `LauncherViewModel.Items → ObservableCollection<LauncherItem>`
  - `LauncherViewModel.AnyRunning → bool`
  - `LauncherViewModel.ExitAsync() → Task`
  - `LauncherItem` (`Glyph`, `Title`, `X`, `Y`, `State`, `ActivateCommand`)

- [ ] **Step 1: LauncherItem을 쓴다**

`src/VisionSupport/Launcher/LauncherItem.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;

namespace VisionSupport.Launcher;

/// <summary>
/// One tile on the radial menu. Wraps either a feature or a plain action (the overview), so the
/// menu can render both from one list without knowing which is which.
/// </summary>
public sealed partial class LauncherItem : ObservableObject
{
    private readonly Action _activate;

    public LauncherItem(string glyph, string title, double x, double y,
                        Action activate, IFeatureModule? module = null)
    {
        Glyph = glyph;
        Title = title;
        X = x;
        Y = y;
        _activate = activate;
        Module = module;

        if (module is not null)
        {
            module.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsRunning));
            };
        }
    }

    public string Glyph { get; }

    public string Title { get; }

    /// <summary>Canvas position of the tile's top-left inside the expanded launcher window.</summary>
    public double X { get; }

    public double Y { get; }

    /// <summary>Null for the overview tile, which is not a feature.</summary>
    public IFeatureModule? Module { get; }

    public FeatureState State => Module?.State ?? FeatureState.Stopped;

    public bool IsRunning => State is FeatureState.Running or FeatureState.Starting
                                   or FeatureState.Paused;

    [RelayCommand]
    private void Activate() => _activate();
}
```

- [ ] **Step 2: LauncherViewModel을 쓴다**

`src/VisionSupport/Launcher/LauncherViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Overview;
using VisionSupport.Shell;
using VisionSupport.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// What the floating icon opens, and what it remembers about what is already open.
///
/// One window per feature at most: clicking a feature whose window is up brings that window
/// forward rather than building a second one, because two views over one ViewModel would fight
/// over the same chart and the same node table.
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    /// <summary>Tile size and menu radius. The window's expanded size is sized to fit these.</summary>
    public const double TileSize = 56;

    public const double MenuRadius = 110;

    public const double ExpandedSize = 340;

    private readonly IReadOnlyList<IFeatureModule> _modules;
    private readonly ActivityLog _activity;
    private readonly LauncherSettings _settings;
    private readonly DateTime _startedAt;
    private readonly Dictionary<IFeatureModule, FeatureWindow> _open = new();

    private OverviewDialog? _overview;

    [ObservableProperty]
    private bool _isExpanded;

    public LauncherViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity,
                             LauncherSettings settings, DateTime startedAt)
    {
        _modules = modules;
        _activity = activity;
        _settings = settings;
        _startedAt = startedAt;

        Point[] offsets = RadialLayout.Offsets(modules.Count + 1, MenuRadius);
        double centre = ExpandedSize / 2 - TileSize / 2;

        for (int i = 0; i < modules.Count; i++)
        {
            IFeatureModule module = modules[i];
            Items.Add(new LauncherItem(module.Glyph, module.Title,
                centre + offsets[i].X, centre + offsets[i].Y,
                () => Open(module), module));

            module.Changed += OnModuleChanged;
            _activity.Add(module.Title, "준비됨");
        }

        Point last = offsets[^1];
        Items.Add(new LauncherItem("", "전체보기",
            centre + last.X, centre + last.Y, ShowOverview));
    }

    public ObservableCollection<LauncherItem> Items { get; } = new();

    /// <summary>Drives the ring around the icon: something is up even with every window closed.</summary>
    public bool AnyRunning => _modules.Any(m => m.State is FeatureState.Running
                                                       or FeatureState.Starting
                                                       or FeatureState.Paused);

    [RelayCommand]
    public void ShowOverview()
    {
        IsExpanded = false;

        if (_overview is not null)
        {
            _overview.Activate();
            return;
        }

        var viewModel = new OverviewViewModel(_modules, _activity, _settings, _startedAt, Open);
        _overview = new OverviewDialog(viewModel);
        _overview.Closed += (_, _) => _overview = null;
        _overview.Show();
    }

    /// <summary>
    /// Stops every feature and ends the process. Windows are force-closed first: their normal
    /// close path would run the stop-or-release decision again and race this teardown.
    /// </summary>
    public async Task ExitAsync()
    {
        foreach (FeatureWindow window in _open.Values.ToArray()) window.ForceClose();
        _open.Clear();

        _overview?.Close();

        // Sequential, not parallel: teardown touches ETW sessions and sockets, and a failure in
        // one should not be racing another's.
        foreach (IFeatureModule module in _modules) await module.StopAsync();

        _settings.Save(LauncherSettings.DefaultPath);
        Application.Current.Shutdown();
    }

    private void Open(IFeatureModule module)
    {
        IsExpanded = false;

        if (_open.TryGetValue(module, out FeatureWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = new FeatureWindow(module);
        window.Closed += (_, _) => _open.Remove(module);
        _open[module] = window;
        window.Show();
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AnyRunning));

        if (sender is FeatureModule { State: FeatureState.Faulted } faulted)
        {
            _activity.Add(faulted.Title, "오류: " + (faulted.FaultMessage ?? "알 수 없음"));
        }
    }
}
```

- [ ] **Step 3: LauncherWindow XAML을 쓴다**

`src/VisionSupport/Launcher/LauncherWindow.xaml`:

```xml
<Window x:Class="VisionSupport.Launcher.LauncherWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="{x:Null}"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        Width="72" Height="72" UseLayoutRounding="True">

    <!-- Background is {x:Null}, not Transparent: a null background is not hit-testable, so
         clicks in the empty area around the icon reach whatever is behind the launcher. -->
    <Grid Background="{x:Null}">

        <ItemsControl x:Name="Menu" ItemsSource="{Binding Items}" Visibility="Collapsed"
                      RenderTransformOrigin="0.5,0.5">
            <ItemsControl.RenderTransform>
                <ScaleTransform x:Name="MenuScale" ScaleX="0.6" ScaleY="0.6"/>
            </ItemsControl.RenderTransform>
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <Canvas/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemContainerStyle>
                <Style TargetType="ContentPresenter">
                    <Setter Property="Canvas.Left" Value="{Binding X}"/>
                    <Setter Property="Canvas.Top" Value="{Binding Y}"/>
                </Style>
            </ItemsControl.ItemContainerStyle>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Button Command="{Binding ActivateCommand}" ToolTip="{Binding Title}"
                            Width="56" Height="56" Margin="0" Padding="0"
                            Background="{StaticResource PanelAlt}"
                            BorderBrush="{StaticResource Border}">
                        <Button.Template>
                            <ControlTemplate TargetType="Button">
                                <Grid>
                                    <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1"
                                            CornerRadius="{StaticResource RadiusTile}"/>
                                    <!-- The running ring: a feature can be up with no window open,
                                         and this is the only place that says so at a glance. -->
                                    <Border BorderBrush="{StaticResource StateRunning}" BorderThickness="2"
                                            CornerRadius="{StaticResource RadiusTile}"
                                            Visibility="{Binding IsRunning, Converter={StaticResource BoolToVis}}"/>
                                    <TextBlock Text="{Binding Glyph}" FontFamily="Segoe MDL2 Assets"
                                               FontSize="20" HorizontalAlignment="Center"
                                               VerticalAlignment="Center" Foreground="{StaticResource Fg}"/>
                                </Grid>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="Chrome" Property="Background"
                                                Value="{StaticResource AccentHover}"/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Button.Template>
                    </Button>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <Grid x:Name="Fab" Width="56" Height="56" HorizontalAlignment="Center" VerticalAlignment="Center"
              Background="{x:Null}" Cursor="SizeAll">
            <Ellipse Fill="#FF141414" Stroke="{StaticResource Border}" StrokeThickness="1"/>
            <Ellipse x:Name="RunningRing" Stroke="{StaticResource StateRunning}" StrokeThickness="2"
                     Visibility="{Binding AnyRunning, Converter={StaticResource BoolToVis}}"/>
            <TextBlock x:Name="FabGlyph" Text="&#xE710;" FontFamily="Segoe MDL2 Assets" FontSize="20"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Foreground="{StaticResource Fg}"/>
            <Grid.ContextMenu>
                <ContextMenu Background="{StaticResource Panel}" Foreground="{StaticResource Fg}">
                    <MenuItem x:Name="OverviewMenuItem" Header="전체보기"/>
                    <Separator/>
                    <MenuItem x:Name="ExitMenuItem" Header="종료"/>
                </ContextMenu>
            </Grid.ContextMenu>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 4: LauncherWindow 코드비하인드를 쓴다**

`src/VisionSupport/Launcher/LauncherWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace VisionSupport.Launcher;

/// <summary>
/// The floating icon. The only thing on screen when nothing is being used.
///
/// The window grows from 72×72 to 340×340 while the menu is open and shrinks back afterwards. A
/// permanently 340-wide topmost window would sit invisibly over a third of a corner of the
/// screen; clicks pass through its empty area, but OLE drag-and-drop targeting is less
/// forgiving, and the image converter accepts dropped files. Keeping the big window short-lived
/// removes that question entirely.
/// </summary>
public partial class LauncherWindow : Window
{
    private const double CollapsedSize = 72;

    /// <summary>How far the pointer has to move before a press counts as a drag, not a click.</summary>
    private const double DragThreshold = 4;

    private readonly LauncherSettings _settings;

    private Point _pressOrigin;
    private Point _pressWindowOrigin;
    private bool _pressed;
    private bool _dragged;

    public LauncherWindow(LauncherViewModel viewModel, LauncherSettings settings)
    {
        InitializeComponent();

        DataContext = viewModel;
        _settings = settings;

        Left = settings.IconLeft;
        Top = settings.IconTop;

        Fab.MouseLeftButtonDown += OnFabPressed;
        Fab.MouseMove += OnFabMoved;
        Fab.MouseLeftButtonUp += OnFabReleased;

        OverviewMenuItem.Click += (_, _) => viewModel.ShowOverview();
        ExitMenuItem.Click += async (_, _) => await viewModel.ExitAsync();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Collapse();
        };

        Deactivated += (_, _) => Collapse();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.IsExpanded) && !viewModel.IsExpanded)
            {
                Collapse();
            }
        };
    }

    private LauncherViewModel ViewModel => (LauncherViewModel)DataContext;

    private void OnFabPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragged = false;
        _pressOrigin = PointToScreen(e.GetPosition(this));
        _pressWindowOrigin = new Point(Left, Top);
        Fab.CaptureMouse();
    }

    private void OnFabMoved(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;

        Point now = PointToScreen(e.GetPosition(this));
        Vector moved = now - _pressOrigin;
        if (!_dragged && moved.Length < DragThreshold) return;

        _dragged = true;

        // Screen pixels to DIPs: a press on a 150% monitor moves the window 1.5× too far
        // otherwise.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = _pressWindowOrigin.X + moved.X / scale;
        Top = _pressWindowOrigin.Y + moved.Y / scale;
    }

    private void OnFabReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        Fab.ReleaseMouseCapture();

        if (_dragged)
        {
            RememberPosition();
            return;
        }

        if (ViewModel.IsExpanded) Collapse();
        else Expand();
    }

    private void Expand()
    {
        Resize(LauncherViewModel.ExpandedSize);
        NudgeOntoScreen();

        Menu.Visibility = Visibility.Visible;
        Animate(0.6, 1.0);
        ViewModel.IsExpanded = true;
    }

    private void Collapse()
    {
        if (!ViewModel.IsExpanded && Menu.Visibility == Visibility.Collapsed) return;

        ViewModel.IsExpanded = false;
        Menu.Visibility = Visibility.Collapsed;
        Resize(CollapsedSize);
        RememberPosition();
    }

    /// <summary>Resizes around the icon's centre, so growing the window does not move the icon.</summary>
    private void Resize(double size)
    {
        double centreX = Left + Width / 2;
        double centreY = Top + Height / 2;

        Width = size;
        Height = size;
        Left = centreX - size / 2;
        Top = centreY - size / 2;
    }

    /// <summary>
    /// Pulls the expanded window back onto the desktop. An icon parked in a corner would
    /// otherwise open half its menu off the edge of the screen.
    /// </summary>
    private void NudgeOntoScreen()
    {
        double minLeft = SystemParameters.VirtualScreenLeft;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - Width;
        double maxTop = minTop + SystemParameters.VirtualScreenHeight - Height;

        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
    }

    private void Animate(double from, double to)
    {
        var scale = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        MenuScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        MenuScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
    }

    private void RememberPosition()
    {
        _settings.IconLeft = Left;
        _settings.IconTop = Top;
        _settings.Save(LauncherSettings.DefaultPath);
    }
}
```

`using System.Windows.Media;`가 `VisualTreeHelper`와 `ScaleTransform`에 필요하다. 파일 맨 위에 더한다.

- [ ] **Step 5: 빌드**

```
dotnet build VisionSupport.sln
```

Expected: 성공.

- [ ] **Step 6: 커밋**

```bash
git add src/VisionSupport/Launcher
git commit -m "feat: add floating launcher with radial menu"
```

---

### Task 9: 앱 배선과 옛 셸 제거

**Files:**
- Modify: `src/VisionSupport/App.xaml.cs`
- Delete: `src/VisionSupport/Shell/ShellWindow.xaml`, `Shell/ShellWindow.xaml.cs`, `Shell/ShellViewModel.cs`, `Shell/NavItem.cs`, `Idle/IdleView.xaml`, `Idle/IdleView.xaml.cs`, `Idle/IdleViewModel.cs`

- [ ] **Step 1: App.xaml.cs를 다시 배선한다**

`OnStartup`과 필드, 예외 핸들러를 아래로 바꾼다. `EnsureElevated()`와 `CreateModules()`는 그대로 둔다.

```csharp
    private readonly ActivityLog _activity = new();
    private readonly DateTime _startedAt = DateTime.Now;

    private LauncherSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!EnsureElevated()) return;

        // Feature windows come and go; the launcher is the only thing that stays. Without this
        // the process would exit the moment the last feature window closed.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // This process is meant to stay up for days with features starting and stopping under
        // it. A feature that throws on a background task or in an event handler must not be
        // able to take the launcher with it, so those escape hatches are closed here.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = LauncherSettings.Load(LauncherSettings.DefaultPath);
        PlaceIconIfUnset();

        // One registration covers every window in the process, including the dialogs the feature
        // assemblies open. Those assemblies do not reference this one and are not being changed,
        // so a class handler is the only seam that reaches them.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded));

        var launcher = new LauncherViewModel(CreateModules(), _activity, _settings, _startedAt);
        new LauncherWindow(launcher, _settings).Show();
    }

    /// <summary>Parks a first-run icon near the bottom-right, and rescues one left on a monitor
    /// that is no longer attached.</summary>
    private void PlaceIconIfUnset()
    {
        var iconSize = new Size(72, 72);
        var fallback = new Point(
            SystemParameters.WorkArea.Right - 110,
            SystemParameters.WorkArea.Bottom - 130);

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        Point placed = _settings is { IconLeft: 0, IconTop: 0 }
            ? fallback
            : LauncherSettings.ConstrainToScreen(
                new Point(_settings.IconLeft, _settings.IconTop), iconSize, virtualScreen, fallback);

        _settings.IconLeft = placed.X;
        _settings.IconTop = placed.Y;
    }

    private void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        // The launcher paints its own shape through AllowsTransparency and must not be layered
        // on top of that.
        if (sender is Window { AllowsTransparency: false } window)
        {
            WindowEffects.ApplyAlpha(window, _settings.Opacity);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _activity.Add("지원툴", "처리되지 않은 오류: " + e.Exception.Message);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _activity.Add("지원툴", "백그라운드 작업 오류: " + e.Exception.GetBaseException().Message);
        e.SetObserved();
    }
```

`using` 목록에서 `VisionSupport.Shell`을 지우지 말고(`ActivityLog`가 거기 있다), `VisionSupport.Launcher`와 `VisionSupport.Windows`를 더한다. `_shell` 필드는 지운다.

- [ ] **Step 2: 옛 셸 파일을 지운다**

```bash
git rm src/VisionSupport/Shell/ShellWindow.xaml src/VisionSupport/Shell/ShellWindow.xaml.cs \
       src/VisionSupport/Shell/ShellViewModel.cs src/VisionSupport/Shell/NavItem.cs \
       src/VisionSupport/Idle/IdleView.xaml src/VisionSupport/Idle/IdleView.xaml.cs \
       src/VisionSupport/Idle/IdleViewModel.cs
```

`.csproj`는 SDK 스타일이라 파일 목록을 들고 있지 않다. 수정할 것이 없다.

- [ ] **Step 3: 빌드**

```
dotnet build VisionSupport.sln
```

Expected: 성공. 남은 참조 오류가 나면 대개 `App.xaml.cs`에 남은 `_shell` 또는 `IdleViewModel` 참조다.

- [ ] **Step 4: 전체 테스트**

```
dotnet test tests/VisionSupport.Tests/VisionSupport.Tests.csproj
```

Expected: 22개 전부 통과 (기존 8 + RadialLayout 5 + LauncherSettings 8... 실제 개수는 실행 결과로 확인하고, **실패가 0인지**만 판정 기준으로 삼는다).

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat: replace nav-rail shell with floating launcher"
```

---

### Task 10: 실행 스모크 체크

단위 테스트로 덮을 수 없는 것들 — P/Invoke, 창 동작, 글리프 — 을 눈으로 확인한다. 이 작업의 결과물은 코드가 아니라 확인된 목록이다.

**Files:** 없음 (필요한 수정이 나오면 그 자리에서 고치고 커밋)

- [ ] **Step 1: 실행**

```
dotnet build VisionSupport.sln
src\VisionSupport\bin\Debug\net9.0-windows\VisionSupport.exe
```

UAC 프롬프트가 뜬다. 승인한다.

- [ ] **Step 2: 런처 체크리스트**

- [ ] 화면 우하단에 원형 아이콘 하나만 뜬다. 다른 창은 없다.
- [ ] 작업표시줄에 아이콘이 없다.
- [ ] 드래그로 옮겨진다. 놓은 자리에 머문다.
- [ ] 좌클릭하면 방사형으로 타일 4개가 펼쳐진다. 글리프가 빈 네모(□)로 보이면 `charmap.exe`에서 Segoe MDL2 Assets를 열어 대체 문자를 찾아 `*Feature.cs`의 `Glyph`와 `LauncherViewModel`의 전체보기 글리프를 고친다.
- [ ] 화면 구석으로 옮긴 뒤 펼치면 메뉴가 화면 안으로 밀려 들어온다.
- [ ] `Esc`나 다른 창 클릭으로 접힌다.
- [ ] 우클릭하면 "전체보기 / 종료" 메뉴가 뜬다.
- [ ] 앱을 껐다 켜면 아이콘이 마지막 위치에 뜬다.

- [ ] **Step 3: 창과 수명주기 체크리스트**

- [ ] 이미지 변환기를 연다. 창이 뜨고 모서리가 둥글다(Win10에서는 약간 각져 보일 수 있다).
- [ ] 창을 닫는다. 전체보기에서 상태가 "대기"인지 확인한다.
- [ ] 이미지 변환기를 다시 연다. 화면이 초기 상태로 새로 만들어진다.
- [ ] PLC 서버를 연다 → 실행한다 → 창을 닫는다. **런처 아이콘에 초록 링이 남아야 한다.**
- [ ] 전체보기를 연다. PLC 서버 행이 "실행 중"이고 정지 버튼이 있다.
- [ ] PLC 서버를 다시 연다. 서버가 계속 돌고 있던 상태로 보인다.
- [ ] 전체보기에서 정지를 누른다. 링이 사라진다.
- [ ] PLC 서버의 상세창(모니터)을 연 뒤 기능 창을 닫는다. 상세창도 함께 닫힌다.

- [ ] **Step 4: 불투명도 체크리스트**

- [ ] 전체보기의 슬라이더를 움직이면 열려 있는 모든 창이 즉시 반응한다.
- [ ] 런처 아이콘은 슬라이더에 반응하지 않는다(원형 모양이 유지된다).
- [ ] 기능 창 안에서 다이얼로그를 하나 연다(예: PLC 서버 → 모듈 추가). 그 다이얼로그도 반투명하다.
- [ ] 창을 최대화했다가 되돌린다. 반투명과 라운드 코너가 유지된다.
- [ ] 앱을 껐다 켜면 슬라이더 값이 유지된다.

- [ ] **Step 5: 종료 체크리스트**

- [ ] 여러 기능을 열고 실행한 상태에서 우클릭 → 종료.
- [ ] 모든 창이 닫히고 프로세스가 사라진다(작업 관리자에서 `VisionSupport.exe` 확인).
- [ ] 다시 실행해도 ETW 세션 오류 없이 메모리 모니터가 붙는다 — 세션이 새는지 확인하는 실질적 방법이다.

- [ ] **Step 6: 스모크에서 나온 수정이 있으면 커밋**

```bash
git add -A
git commit -m "fix: smoke test corrections for floating launcher"
```

- [ ] **Step 7: README 갱신**

`README.md`의 트리와 "기능 모듈 계약" 표는 여전히 네비게이션 레일 기준으로 쓰여 있다. 다음을 반영한다:

- 셸 설명을 "플로팅 런처 + 기능별 창"으로 고친다.
- 기능 모듈 계약 표에 "창 닫기" 행을 더한다 — 가동 중이면 뷰만 해제, 아니면 전체 정지.
- 불투명도와 라운드 코너 처리 방식을 한 문단으로 적는다.

```bash
git add README.md
git commit -m "docs: describe the floating launcher architecture"
```

---

## Self-Review

**스펙 커버리지**

| 스펙 항목 | 담당 Task |
|---|---|
| 런처 창 구조, 72↔340 리사이즈, 화면 밖 보정 | 8 |
| `ShutdownMode.OnExplicitShutdown` | 9 |
| `launcher.json` 저장/복원 | 2, 9 |
| 창/실행 분리, `ReleaseView` | 3, 6 |
| 상세창을 기능 창과 함께 닫기 | 3 (`ReleaseView`가 `_ownedWindows`를 닫는다) |
| `Glyph`, `PreferredWindowSize` | 3 |
| 반경 토큰과 One UI 스타일 | 5 |
| Win10/Win11 라운드 코너 분기 | 4 |
| 레이어드 알파 + 전역 클래스 핸들러 | 4, 9 |
| 전체보기 4개 덩어리 | 7 |
| 방사형 배치, 활성 링 | 1, 8 |
| 파일 삭제 목록 | 9 |
| 테스트 3종 | 1, 2, 4 (+ 3에서 수명주기 추가) |

**타입 일관성 확인 완료**: `LauncherSettings.ClampOpacity`는 Task 2에서 정의하고 Task 4·7에서 쓴다. `WindowEffects.ApplyAlpha`/`ApplyAlphaToAll`/`AttachRoundedCorners`는 Task 4에서 정의하고 6·7·9에서 쓴다. `IFeatureModule.ReleaseView`는 Task 3에서 정의하고 6에서 쓴다. `OverviewViewModel` 생성자는 Task 7에서 5개 인자(`modules`, `activity`, `settings`, `startedAt`, `open`)로 정의하고 Task 8에서 같은 순서로 호출한다. `FeatureWindow(IFeatureModule)`는 Task 6에서 1개 인자로 정의하고 Task 8에서 그렇게 호출한다.

**스펙과 어긋난 이름 하나**: 스펙은 `Windows/WindowAlpha.cs`로 적었지만 이 계획은 `Windows/WindowEffects.cs`로 간다 — 알파와 라운드 코너를 같이 담으므로 이름이 내용을 반영해야 한다. 스펙 쪽 파일명을 고쳐두었다.
