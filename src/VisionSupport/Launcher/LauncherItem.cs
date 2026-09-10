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
