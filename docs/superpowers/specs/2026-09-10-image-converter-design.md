# 이미지 변환기 기능 모듈 설계

작성일: 2026-09-10
상태: 승인됨 (구현 진행)

## 1. 목표

VISION 지원툴 셸에 세 번째 기능 모듈 **이미지 변환기**를 추가한다. 메모리 모니터·PLC 서버와
동일하게 독립 프로젝트 + `IFeatureModule` 구현 + `App.CreateModules()` 한 줄 등록으로 붙는다.

기능 요구:

- 대부분의 래스터 포맷 입출력: bmp, png, jpg/jpeg, gif, tiff, jpeg xr(jxr/wdp), 그리고
  코그넥스 `.idb`.
- 압축 가능한 포맷은 압축률/품질을 설정.
- 저장 시 두 방식: (a) 저장 다이얼로그로 경로를 묻는다, (b) 미리 지정한 폴더에 자동 이름으로
  바로 저장한다. 방식은 설정에 영속.
- 드래그앤드롭으로 변환할 이미지(파일·폴더)를 큐에 올린다.
- 변환 옵션: 리사이즈, 그레이스케일, 비트뎁스 변경.

## 2. 비목표 (YAGNI)

- WebP/HEIC/AVIF 인코딩 — WPF 내장 코덱 범위 밖. 입력은 OS 코덱이 있으면 디코드되지만
  출력 대상에서는 제외.
- PNG 압축 레벨 조절 — WPF `PngBitmapEncoder`는 무손실 deflate 고정. UI에서 "조절 불가"로
  안내만 한다. (별도 인코더 도입은 이번 범위 밖.)
- 이미지 편집(크롭, 회전, 필터), 배치 병렬 변환, 변환 프로파일 저장, 별도 미리보기 창.
- 코그넥스 `.idb` 실제 코덱의 CI 테스트 — 빌드 서버에 VisionPro가 없다.

## 3. 아키텍처

### 3.1 새 프로젝트

```
src/VisionSupport.ImageConverter/            클래스 라이브러리
  - net9.0-windows, UseWPF, x64, Nullable enable, ImplicitUsings enable
  - RootNamespace / AssemblyName = VisionSupport.ImageConverter
  - PackageReference 없음 (System.Text.Json 은 net9 내장)

src/VisionSupport.ImageConverter.Cognex/     선택 플러그인 (Exists() 가드)
  - net9.0-windows, UseWPF, x64
  - ProjectReference → VisionSupport.ImageConverter  (IImageDbCodec 계약만 사용)
  - VisionPro DLL 이 표준 경로에 있으면 실제 코덱으로, 없으면 스텁으로 컴파일
```

### 3.2 기존 파일 변경점

| 파일 | 변경 |
|---|---|
| `VisionSupport.sln` | 두 프로젝트 추가 (기존과 동일하게 모든 플랫폼 → `Debug/Release\|Any CPU` 매핑), `src` 솔루션 폴더에 중첩 |
| `src/VisionSupport/VisionSupport.csproj` | `ProjectReference` → `VisionSupport.ImageConverter` (일반 참조). `ProjectReference` → `VisionSupport.ImageConverter.Cognex` 는 `ReferenceOutputAssembly=false` `Private=false` (빌드 순서만, 코드 의존 없음). 조건부 `<None>` 로 플러그인 DLL 을 `plugins\` 로 복사 |
| `src/VisionSupport/App.xaml.cs` | `CreateModules()` 에 `new ImageConverterFeature()` 추가 (PLC 서버 다음) |
| `tests/VisionSupport.Tests/VisionSupport.Tests.csproj` | `ProjectReference` → `VisionSupport.ImageConverter` |
| `README.md` | 구조도·기능 모듈 계약 표에 이미지 변환기 행 추가 |

셸은 `.Cognex` 를 **코드로 참조하지 않는다**. 빌드 순서 참조(`ReferenceOutputAssembly=false`)와
런타임 리플렉션 로딩만으로 연결된다.

## 4. 컴포넌트

### 4.1 `ImageConverterFeature : FeatureModule`

메모리 모니터·PLC 서버 패턴을 그대로 따른다.

- `Title` = `"이미지 변환기"`
- `Description` = `"bmp·png·jpg·tiff·코그넥스 idb 등을 상호 변환합니다. 압축률·리사이즈·그레이스케일 설정과 드래그앤드롭 일괄 변환을 지원합니다."`
- `CanPause => false` — 일시정지는 의미 없음. 셸이 정지 버튼을 비활성화한다.
- `StatusLine` — ViewModel 상태를 요약: 대기 중이면 `"대기 N개"`, 변환 중이면 `"변환 중 k/N"`,
  끝났으면 `"완료 c · 실패 f"`.
- `OnStartAsync(ct)` — no-op. 기능이 Running 이면 변환을 받을 수 있다는 뜻.
- `OnPauseAsync` / `OnResumeAsync` — no-op (`CanPause=false` 라 호출되지 않음).
- `OnStopAsync` — ViewModel 의 취소 토큰을 통해 진행 중 배치를 중단하고, 큐를 비우고,
  ViewModel 을 `Dispose`. 예외를 밖으로 내지 않는다 (`FeatureModule.SafeTeardown` 이 감싼다).
- `CreateView()` — `new ImageConverterView(viewModel)`. 별도 창을 열지 않으므로 `TrackWindow`
  사용 없음.
- ViewModel 은 첫 사용 시 생성 (다른 기능과 동일). 생성 시 설정 파일을 읽는다.

### 4.2 ViewModels

**`ConversionOptions`** (ObservableObject) — 변환 파라미터 한 벌.

- `TargetFormat : ImageFormat` (enum: Bmp, Png, Jpeg, Gif, Tiff, JpegXr, Idb)
- `JpegQuality : int` (1–100, 기본 90)
- `TiffCompression : TiffCompressOption` (None/Lzw/Zip/Rle/Ccitt4, 기본 Lzw)
- `JpegXrQuality : int` (0–100, 기본 90)
- `Resize : ResizeMode` (None / Pixels(w,h,keepAspect) / Percent(p))
- `Grayscale : bool`
- `BitDepth : BitDepthOption` (Source / Gray8 / Gray16 / Bgr24 / Bgra32 / Rgb48)
- `SaveMode : SaveMode` (Ask / DirectToFolder)
- `OutputFolder : string`

**`ConversionItem`** (ObservableObject) — 큐 1건.

- `SourcePath : string` (읽기 전용)
- `FileName`, `SourceFormatLabel` (표시용)
- `Status : ItemStatus` (Pending / Converting / Done / Failed / Skipped)
- `Message : string` (실패 사유, 출력 경로 등)
- `OutputPath : string?`

**`ImageConverterViewModel`** (ObservableObject, IDisposable)

- `ObservableCollection<ConversionItem> Queue`
- `ConversionOptions Options`
- `ConversionItem? SelectedItem` → 선택 시 `PreviewImage : BitmapSource?` 갱신 (썸네일 디코드)
- `bool IsConverting`, `int DoneCount`, `int FailedCount`, `string Summary`
- `bool RecurseFolders` (기본 true) — 폴더 드롭 시 하위 폴더 포함 여부
- `bool IdbAvailable` — `.idb` 코덱 로드 성공 여부. false 면 타깃 포맷 목록에서 idb 를
  비활성/숨김하고 안내 문구 표시.
- Commands: `AddFilesCommand`, `AddFolderCommand`, `RemoveSelectedCommand`, `ClearQueueCommand`,
  `ConvertAllCommand`, `CancelCommand`, `PickOutputFolderCommand`, `SaveSettingsCommand`(옵션 변경 시 디바운스 저장)
- 드롭 진입점: `AddPaths(IEnumerable<string> paths)` — 뷰의 Drop 핸들러가 호출. 디렉터리는
  `RecurseFolders` 에 따라 펼치고, `FormatCatalog.IsSupportedInput(ext)` 로 필터, 중복 경로 제거.
- `ConvertAllAsync(CancellationToken ct)`:
  - `Task.Run` 위에서 큐를 **순차** 처리.
  - 항목마다 상태를 UI 스레드로 마샬링해 갱신 (`Application.Current.Dispatcher`).
  - 항목별 try/catch — 실패는 `Failed` + 사유 기록 후 다음으로 계속.
  - `ct` 취소 시 남은 항목은 `Skipped`.
  - 기능 정지(`OnStopAsync`)가 이 `ct` 의 소스를 취소한다.

### 4.3 Services

**`ImageFormat` / `FormatCatalog`**

- 입력 지원 판정: `BitmapDecoder` 가 다루는 확장자 화이트리스트 + `.idb`(코덱 로드 시).
- 출력 포맷별 허용 옵션 매트릭스:

| 포맷 | 품질/압축 컨트롤 | 확장자 |
|---|---|---|
| Jpeg | `JpegQuality` 슬라이더 | .jpg |
| Tiff | `TiffCompression` 드롭다운 | .tif |
| JpegXr | `JpegXrQuality` 슬라이더 | .jxr |
| Png | 없음 (안내 문구) | .png |
| Bmp / Gif | 없음 | .bmp / .gif |
| Idb | 코덱이 정의 (Cognex 압축 옵션) | .idb |

- 리사이즈·그레이스케일·비트뎁스는 전 포맷 공통.

**`IImageCodec` / `WpfImageCodec`**

```
BitmapSource Decode(string path);
void Encode(BitmapSource image, string path, ImageFormat format, ConversionOptions options);
```

- `Decode` — `BitmapDecoder.Create(uri, None, OnLoad).Frames[0]`.
- `Encode` — 포맷별 `BitmapEncoder` 선택, `JpegBitmapEncoder.QualityLevel` /
  `TiffBitmapEncoder.Compression` / `WmpBitmapEncoder.ImageQualityLevel` 적용,
  `encoder.Frames.Add(BitmapFrame.Create(image))`, 파일 스트림에 `Save`.

**`ConversionPipeline`**

```
void Convert(string sourcePath, string outputPath, ImageFormat target, ConversionOptions o,
             IImageCodec wpfCodec, IImageDbCodec? idbCodec);
```

- 입력이 `.idb` 면 `idbCodec.Decode`, 아니면 `wpfCodec.Decode`.
- 변환 순서: **decode → resize → grayscale → bitdepth → encode**.
  - resize: `TransformedBitmap` + `ScaleTransform`. Percent 는 배율 그대로. Pixels+keepAspect 는
    목표 상자에 들어가도록 `min(targetW/srcW, targetH/srcH)` 배율(fit inside). Pixels+!keepAspect 는
    축별 배율을 따로 적용. 확대 필요 시에도 그대로 적용(업스케일 허용).
  - grayscale: `FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0)`.
  - bitdepth: `Source` 가 아니면 `FormatConvertedBitmap` 으로 목표 `PixelFormat` 변환.
    타깃 인코더가 못 받는 포맷 조합은 `FormatCatalog` 이 사전 차단.
- 출력이 `.idb` 면 `idbCodec.Encode`, 아니면 `wpfCodec.Encode`.
- 각 `BitmapSource` 는 `Freeze()` 해서 백그라운드 스레드에서 다룬다.

**`OutputPathResolver`**

```
string Resolve(string sourcePath, ImageFormat target, SaveMode mode, string outputFolder,
               Func<string> askDialog);   // Ask 모드에서 다이얼로그 호출자
```

- `DirectToFolder`: `outputFolder / (원본파일명 + 새 확장자)`. 존재하면 `이름 (1).ext`,
  `이름 (2).ext` … 로 회피 (덮어쓰지 않음).
- `Ask` (단건): `SaveFileDialog` 결과. 취소면 항목 `Skipped`.
- `Ask` (배치): 배치 시작 시 폴더 1회 선택 → 그 폴더에 대해 `DirectToFolder` 규칙 적용.
- 원본과 출력 경로가 같으면 (같은 포맷·같은 폴더) 항상 `이름 (1).ext` 로 분기해 원본 보호.

**`ImageConverterSettings` / `SettingsStore`**

- 경로: `%AppData%\VisionSupport\ImageConverter\settings.json` (PLC 의 `%AppData%\VirtualPlcServer`
  패턴과 동일한 계열).
- 직렬화: `System.Text.Json` (`JsonSerializerOptions { WriteIndented = true }`).
- 필드: `TargetFormat`, `JpegQuality`, `TiffCompression`, `JpegXrQuality`, `SaveMode`,
  `OutputFolder`, `Resize*`, `Grayscale`, `BitDepth`, `RecurseFolders`, `CognexBinPath`.
- 로드 실패(파일 없음·손상)면 기본값. 저장은 옵션 변경 250ms 디바운스.

### 4.4 코그넥스 `.idb` 플러그인

**계약 (`IImageDbCodec`, 메인 프로젝트에 정의)**

```csharp
public interface IImageDbCodec
{
    bool CanDecode(string extension);   // ".idb"
    bool CanEncode(string extension);
    BitmapSource Decode(string path);
    void Encode(BitmapSource image, string path);  // 압축 옵션은 구현 내부 규약
}
```

**로더 (`CognexCodecLoader`, 메인 프로젝트)**

- 후보 경로: `AppContext.BaseDirectory / plugins / VisionSupport.ImageConverter.Cognex.dll`.
- 없으면 `null` 반환 → `IdbAvailable = false`.
- 있으면:
  1. `AppDomain.CurrentDomain.AssemblyResolve` 에 핸들러 등록 — `Cognex.VisionPro.*` 요청을
     `settings.CognexBinPath`(기본 `C:\Program Files\Cognex\VisionPro\bin`) 에서 `LoadFrom`.
  2. `Assembly.LoadFrom(pluginPath)` → `IImageDbCodec` 구현 타입을 `Activator.CreateInstance`.
  3. 인스턴스 생성/첫 호출에서 예외(VisionPro 미설치·라이선스 없음)면 잡아서 `null`,
     활동 로그에 사유 표시.

**구현 (`CognexImageDbCodec`, `.Cognex` 프로젝트)**

- `.Cognex.csproj`:
  ```xml
  <PropertyGroup>
    <CognexBin>$(ProgramFiles)\Cognex\VisionPro\bin</CognexBin>
    <CognexAvailable Condition="Exists('$(CognexBin)\Cognex.VisionPro.Core.dll')">true</CognexAvailable>
    <DefineConstants Condition="'$(CognexAvailable)'=='true'">$(DefineConstants);COGNEX</DefineConstants>
  </PropertyGroup>
  <ItemGroup Condition="'$(CognexAvailable)'=='true'">
    <Reference Include="Cognex.VisionPro.Core"><HintPath>$(CognexBin)\Cognex.VisionPro.Core.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Cognex.VisionPro.ImageFile"><HintPath>$(CognexBin)\Cognex.VisionPro.ImageFile.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
  ```
- 한 파일에서 `#if COGNEX` 분기:
  - `#if COGNEX` — `CogImageFile` 로 `.idb` 프레임을 열어 `ICogImage` → `Bitmap` → `BitmapSource`
    변환(decode), 반대로 `BitmapSource` → `CogImage8Grey`/`CogImage24PlanarColor` → `CogImageFile`
    append(encode). 압축 옵션은 `CogImageFileModeConstants` 로 매핑.
  - `#else` — 스텁: `CanDecode`/`CanEncode` 는 `false`, `Decode`/`Encode` 는
    `NotSupportedException`. 빌드 서버에서 이 경로로 컴파일된다.
- 산출 DLL 은 셸의 `<None>` 조건부 복사로 `VisionSupport\bin\...\plugins\` 에 들어간다.
  큰 Cognex DLL 은 복사하지 않는다 — 런타임에 `AssemblyResolve` 로 VisionPro bin 에서 해결.

## 5. View (`ImageConverterView : UserControl`)

`Background="{StaticResource Bg}"`, 셸의 `Theme/Dark.xaml` 암시 스타일을 그대로 받는다
(메모리 모니터 뷰와 동일). 별도 리소스 사전 머지 없음.

레이아웃 (3열):

- **좌: 큐** — 상단 버튼(파일 추가 / 폴더 추가 / 선택 삭제 / 비우기), `ListBox`(또는 `DataGrid`)
  `ItemsSource=Queue`, 항목에 상태 점·파일명·사유. 하단에 `하위 폴더 포함` 체크박스.
- **중: 옵션 패널** — 타깃 포맷 `ComboBox`; 포맷에 따라 품질 슬라이더 / TIFF 압축 드롭다운 /
  "PNG 는 압축률 조절 불가" 안내를 `Visibility` 로 토글; 리사이즈(라디오 None/픽셀/퍼센트 +
  입력란 + 비율 유지 체크); 그레이스케일 체크; 비트뎁스 `ComboBox`.
- **우: 저장 + 미리보기** — 저장 방식 라디오(묻기 / 지정 폴더에 바로) + 폴더 경로 `TextBox` +
  `찾아보기`; 선택 항목 썸네일 `Image`; 하단에 `전체 변환` / `취소` 버튼과 `Summary` 텍스트.
- **드롭존**: 셸이 관리자 권한으로 돌아 WPF `AllowDrop`(OLE 드래그드롭)이 동작하지 않는다
  (§8 참고). 대신 레거시 경로 — `Loaded` 에서 `RevokeDragDrop` 로 WPF 가 건 OLE 타깃을 떼고,
  `DragAcceptFiles` + `WM_DROPFILES` 훅으로 최상위 창에서 직접 파일 목록을 받는다.
  `DragDropElevation` 이 `ChangeWindowMessageFilter` 로 그 메시지를 UIPI 통과시킨다.
  레거시 경로는 hover 이벤트가 없어 드래그 중 오버레이는 없고, 큐 영역의 안내 문구가 상시 표시.

코드비하인드는 드롭 처리와 포맷별 옵션 가시성만 담당하고 나머지는 바인딩/커맨드.

## 6. 오류 처리 / 격리

- 변환은 백그라운드 스레드(`Task.Run`). 파일별 try/catch 로 한 건 실패가 배치를 멈추지 않음.
- 손상 파일·미지원 조합 → 항목 `Failed` + 사유. UI 는 계속 반응.
- `.idb` 코덱 로드 실패 → 기능은 정상, idb 타깃만 비활성 + 안내.
- `OnStopAsync` 는 `CancellationTokenSource.Cancel()` → 진행 중 루프 탈출 → ViewModel `Dispose`.
  절대 예외를 셸로 전파하지 않음 (`FeatureModule` 계약).
- `TaskScheduler.UnobservedTaskException` / `DispatcherUnhandledException` 은 셸이 이미 잡아
  기능 단위로 격리 (기존 구조).

## 7. 테스트 (`tests/VisionSupport.Tests`)

새 파일 `ImageConverterTests.cs` + `ViewConstructionTests.cs` 에 1건 추가.

- `ImageConverterView_constructs_detached` — STA 스레드에서 `new ImageConverterView(new ImageConverterViewModel(...))` (기존 패턴).
- `OutputPathResolver`:
  - `DirectToFolder` 가 확장자를 바꾸고 파일명을 유지한다.
  - 같은 이름이 있으면 `(1)`, `(2)` 로 회피한다.
  - 원본=출력 경로면 `(1)` 로 분기한다.
- `FormatCatalog`:
  - Jpeg/Tiff/JpegXr 만 품질·압축 컨트롤을 허용한다.
  - `.idb` 는 코덱 미로드 시 입력·출력에서 빠진다.
- `ConversionPipeline` (WPF 코덱, 임시 파일):
  - bmp → png → jpg 라운드트립이 크기(픽셀 수)를 보존한다.
  - `Resize.Percent(50)` 이 폭·높이를 절반으로 만든다.
  - `Grayscale` 결과의 `PixelFormat` 이 `Gray8` 이다.
- `CognexCodecLoader` — `plugins` 폴더가 비면 `null` 을 반환하고 예외를 던지지 않는다.
- `.Cognex` 실제 코덱은 CI 대상 아님. `IImageDbCodec` 소비 측은 fake 구현으로 검증.

## 8. 리스크 / 미해결

- **PNG 압축률**: WPF 내장 인코더로는 불가. 현재 설계는 UI 안내로 처리. 요구가 강해지면
  이 포맷만 별도 인코더(예: 직접 deflate 레벨 지정) 검토 — 별도 작업.
- **VisionPro 어셈블리 이름·API**: `Cognex.VisionPro.Core` / `Cognex.VisionPro.ImageFile` 와
  `CogImageFile` API 시그니처는 설치본에서 확정해야 한다. 스펙의 타입명은 잠정.
- **`.idb` 색공간**: mono8/mono16/RGB 프레임을 `BitmapSource` 로 올릴 때 16bit → 8bit 다운컨버전이
  기본. 손실 안내 필요.
- **빌드 순서**: 셸의 `ReferenceOutputAssembly=false` ProjectReference 로 `.Cognex` 가 먼저
  빌드되도록 강제. CI(병렬 빌드)에서 `<None>` 조건부 복사가 산출물을 놓치지 않는지 확인.

- **관리자 권한 + 드래그드롭** (구현 중 확인): 셸이 `requireAdministrator` 라 탐색기(중간 무결성)
  → 셸(높은 무결성)로 가는 드롭이 UIPI 로 막힌다. WPF `HwndSource` 가 최상위 창에 OLE
  `IDropTarget` 을 등록해 두는데, 이게 있으면 (1) 탐색기가 OLE 프로토콜로만 대화해 UIPI 에
  차단되고 (2) 레거시 `WM_DROPFILES` fallback 도 안 온다. 해결: `RevokeDragDrop` 으로 OLE
  타깃 제거 + `DragAcceptFiles`/`WM_DROPFILES` + `ChangeWindowMessageFilter`(프로세스 전역;
  per-window `...Ex` 는 OLE 숨은 창을 못 덮어 효과 없음).
