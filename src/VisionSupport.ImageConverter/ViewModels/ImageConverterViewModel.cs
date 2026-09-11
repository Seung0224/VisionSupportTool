using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VisionSupport.ImageConverter.Services;

namespace VisionSupport.ImageConverter.ViewModels;

/// <summary>
/// Drives the image converter page: the drop queue, the options panel, and the batch run. The
/// pixel work is delegated to an <see cref="IConversionRunner"/>; everything here is queue
/// bookkeeping and progress.
/// </summary>
public sealed partial class ImageConverterViewModel : ObservableObject, IDisposable
{
    private readonly IConversionRunner _runner;
    private CancellationTokenSource? _batchCts;

    [ObservableProperty]
    private bool _recurseFolders;

    [ObservableProperty]
    private ConversionItem? _selectedItem;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    public ImageConverterViewModel(IConversionRunner runner, ImageConverterSettings settings)
    {
        _runner = runner;
        Options = settings.Options;
        _recurseFolders = settings.RecurseFolders;
        OutputFormats = FormatCatalog.OutputFormats();
        Queue.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public ObservableCollection<ConversionItem> Queue { get; } = new();

    public ConversionOptions Options { get; }

    public IReadOnlyList<ImageFormat> OutputFormats { get; }

    public Array ResizeKindOptions { get; } = Enum.GetValues<ResizeKind>();

    public Array BitDepthOptions { get; } = Enum.GetValues<BitDepth>();

    public Array TiffCompressionOptions { get; } = Enum.GetValues<TiffCompressionKind>();

    /// <summary>The save-mode radio as a bool: checked means "ask me where each time".</summary>
    public bool AskEachTime
    {
        get => Options.SaveMode == SaveMode.Ask;
        set
        {
            SaveMode desired = value ? SaveMode.Ask : SaveMode.DirectToFolder;
            if (Options.SaveMode == desired) return;
            Options.SaveMode = desired;
            OnPropertyChanged();
        }
    }

    /// <summary>Proxy over <see cref="ConversionOptions.OutputFolder"/> so the folder textbox and
    /// the "browse" button stay in sync - the options object itself does not raise change events.</summary>
    public string OutputFolder
    {
        get => Options.OutputFolder;
        set
        {
            if (Options.OutputFolder == value) return;
            Options.OutputFolder = value;
            OnPropertyChanged();
        }
    }

    public int DoneCount => Queue.Count(i => i.Status == ItemStatus.Done);

    public int FailedCount => Queue.Count(i => i.Status == ItemStatus.Failed);

    /// <summary>One line for the idle screen and the shell command bar.</summary>
    public string Summary
    {
        get
        {
            if (Queue.Count == 0) return "";
            if (IsConverting) return $"변환 중 {DoneCount + FailedCount}/{Queue.Count}";
            if (DoneCount == 0 && FailedCount == 0) return $"대기 {Queue.Count}개";
            return FailedCount == 0 ? $"완료 {DoneCount}" : $"완료 {DoneCount} · 실패 {FailedCount}";
        }
    }

    /// <summary>Adds dropped or picked paths to the queue. Folders are walked (recursively when
    /// <see cref="RecurseFolders"/> is set); unsupported extensions and duplicates are dropped.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                SearchOption depth = RecurseFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string file in Directory.EnumerateFiles(path, "*", depth)) TryAdd(file);
            }
            else if (File.Exists(path))
            {
                TryAdd(path);
            }
        }
    }

    [RelayCommand]
    public void ClearQueue()
    {
        Queue.Clear();
        SelectedItem = null;
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "이미지 파일|*.bmp;*.png;*.jpg;*.jpeg;*.gif;*.tif;*.tiff;*.jxr;*.wdp;*.ico|모든 파일|*.*",
        };
        if (dialog.ShowDialog() == true) AddPaths(dialog.FileNames);
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true) AddPaths(new[] { dialog.FolderName });
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedItem is { } item) Queue.Remove(item);
    }

    [RelayCommand]
    private void PickOutputFolder()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = OutputFolder };
        if (dialog.ShowDialog() == true) OutputFolder = dialog.FolderName;
    }

    [RelayCommand]
    private async Task ConvertAll()
    {
        if (Queue.Count == 0 || IsConverting) return;

        string folder = OutputFolder;
        if (Options.SaveMode == SaveMode.Ask)
        {
            var dialog = new OpenFolderDialog { InitialDirectory = folder };
            if (dialog.ShowDialog() != true) return;
            folder = dialog.FolderName;
        }

        if (string.IsNullOrWhiteSpace(folder)) return;
        OutputFolder = folder;

        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();
        await ConvertAllAsync(_batchCts.Token);
    }

    [RelayCommand]
    private void Cancel() => _batchCts?.Cancel();

    /// <summary>Converts every not-yet-done item into <see cref="ConversionOptions.OutputFolder"/>.
    /// One item's failure does not stop the rest; a cancelled run leaves the remainder
    /// <see cref="ItemStatus.Skipped"/>.</summary>
    public async Task ConvertAllAsync(CancellationToken ct)
    {
        if (Queue.Count == 0) return;

        IsConverting = true;
        string folder = Options.OutputFolder;
        try
        {
            await Task.Run(() =>
            {
                foreach (ConversionItem item in Queue.ToArray())
                {
                    if (item.Status == ItemStatus.Done) continue;
                    if (ct.IsCancellationRequested)
                    {
                        SetStatus(item, ItemStatus.Skipped, "");
                        continue;
                    }

                    SetStatus(item, ItemStatus.Converting, "");
                    try
                    {
                        string outputPath = _runner.Convert(item.SourcePath, folder, Options);
                        RunOnUi(() => item.OutputPath = outputPath);
                        SetStatus(item, ItemStatus.Done, outputPath);
                    }
                    catch (Exception ex)
                    {
                        SetStatus(item, ItemStatus.Failed, ex.Message);
                    }
                }
            }, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            foreach (ConversionItem item in Queue.Where(i => i.Status is ItemStatus.Pending or ItemStatus.Converting))
            {
                SetStatus(item, ItemStatus.Skipped, "");
            }
        }
        finally
        {
            IsConverting = false;
            OnPropertyChanged(nameof(DoneCount));
            OnPropertyChanged(nameof(FailedCount));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public void Dispose()
    {
        _batchCts?.Cancel();
        _batchCts?.Dispose();
        _batchCts = null;
        Queue.Clear();
        PreviewImage = null;
    }

    partial void OnIsConvertingChanged(bool value) => OnPropertyChanged(nameof(Summary));

    partial void OnSelectedItemChanged(ConversionItem? value)
    {
        PreviewImage = null;
        if (value is null) return;

        string extension = Path.GetExtension(value.SourcePath);

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(value.SourcePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 240;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage = bitmap;
        }
        catch
        {
            PreviewImage = null;
        }
    }

    private void TryAdd(string file)
    {
        if (!FormatCatalog.IsSupportedInput(Path.GetExtension(file))) return;
        if (Queue.Any(i => string.Equals(i.SourcePath, file, StringComparison.OrdinalIgnoreCase))) return;
        Queue.Add(new ConversionItem(file));
    }

    private static void SetStatus(ConversionItem item, ItemStatus status, string message)
        => RunOnUi(() =>
        {
            item.Status = status;
            item.Message = message;
        });

    /// <summary>Status is written from a background thread; marshal it so bound controls update
    /// on the UI thread. Same shape as the shell's FeatureModule.RaiseChanged.</summary>
    private static void RunOnUi(Action action)
    {
        Application? app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        app.Dispatcher.Invoke(action);
    }
}
