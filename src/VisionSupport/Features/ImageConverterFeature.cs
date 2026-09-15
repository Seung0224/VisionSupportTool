using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VisionSupport.ImageConverter;
using VisionSupport.ImageConverter.Services;
using VisionSupport.ImageConverter.ViewModels;

namespace VisionSupport.Features;

/// <summary>
/// The image converter as a hosted feature.
///
/// It has no server, no attach, no background pump: a conversion runs only while its window is
/// open, and the window refuses to close mid-batch. Stop still matters for the shell's health in
/// one way: it drops the queue, and it writes the option set back to disk so the next visit opens
/// where this one left off.
/// </summary>
public sealed class ImageConverterFeature : FeatureModule
{
    private readonly SettingsStore _store = SettingsStore.Default;

    private ImageConverterSettings? _settings;
    private ImageConverterViewModel? _viewModel;

    public override string Title => "이미지 변환기";

    public override string Description
        => "bmp·png·jpg·tiff·코그넥스 idb 등을 상호 변환합니다. 압축률·리사이즈·그레이스케일 설정과 드래그앤드롭 일괄 변환을 지원합니다.";

    public override string Glyph => "ImageMultipleOutline";

    public override Size PreferredWindowSize => new(980, 700);

    public override string StatusLine => _viewModel?.Summary ?? string.Empty;

    /// <summary>A batch never outlives its window - closing is refused while one runs - so there
    /// is never anything to keep in the background.</summary>
    public override bool IsWorking => false;

    public override string? CloseBlockedReason => _viewModel is { IsConverting: true }
        ? "변환 중에는 닫을 수 없습니다.\n취소하거나 변환이 끝난 뒤 닫아 주세요."
        : null;

    /// <summary>
    /// Built on first use: loading settings reads a file and probing for the Cognex plugin walks
    /// the plugins folder, neither of which should happen just because the shell started.
    /// </summary>
    private ImageConverterViewModel ViewModel
    {
        get
        {
            if (_viewModel is not null) return _viewModel;

            _settings = _store.Load();
            var runner = new ConversionRunner(new WpfImageCodec());

            _viewModel = new ImageConverterViewModel(runner, _settings);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            return _viewModel;
        }
    }

    protected override UserControl CreateView() => new ImageConverterView(ViewModel);

    protected override Task OnStopAsync()
    {
        if (_viewModel is not null && _settings is not null)
        {
            _settings.RecurseFolders = _viewModel.RecurseFolders;
            try
            {
                _store.Save(_settings);
            }
            catch
            {
                // A settings file we cannot write is not worth faulting the feature over.
            }

            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }

        return Task.CompletedTask;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageConverterViewModel.Summary)) RaiseChanged();
    }
}
