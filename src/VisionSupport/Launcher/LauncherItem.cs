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
    private readonly Func<bool>? _isRunning;

    /// <param name="isRunning">
    /// For a tile whose ring does not follow one feature - the tools tile. Whoever owns what it
    /// reads calls <see cref="RefreshRunning"/> when that changes.
    /// </param>
    public LauncherItem(string glyph, string title, double x, double y,
                        Action activate, IFeatureModule? module = null,
                        Func<bool>? isRunning = null)
    {
        Glyph = glyph;
        Title = title;
        X = x;
        Y = y;
        _activate = activate;
        _isRunning = isRunning;
        Module = module;

        if (module is not null)
        {
            module.Changed += (_, _) => OnPropertyChanged(nameof(IsRunning));
        }
    }

    public string Glyph { get; }

    public string Title { get; }

    /// <summary>Canvas position of the tile's top-left inside the expanded launcher window.</summary>
    public double X { get; }

    public double Y { get; }

    /// <summary>Null for the overview tile, which is not a feature.</summary>
    public IFeatureModule? Module { get; }

    public bool IsRunning => _isRunning?.Invoke() ?? Module?.IsWorking ?? false;

    public void RefreshRunning() => OnPropertyChanged(nameof(IsRunning));

    [RelayCommand]
    private void Activate() => _activate();
}
