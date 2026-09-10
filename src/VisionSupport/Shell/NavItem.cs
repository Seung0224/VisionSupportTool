using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using VisionSupport.Features;

namespace VisionSupport.Shell;

/// <summary>
/// One row in the nav rail. Wraps either a hosted feature or a plain screen (the idle page),
/// so the rail can render both from a single list without knowing which is which.
/// </summary>
public sealed partial class NavItem : ObservableObject
{
    private readonly Func<UserControl> _viewFactory;

    [ObservableProperty]
    private bool _isSelected;

    public NavItem(string title, Func<UserControl> viewFactory, IFeatureModule? module = null)
    {
        Title = title;
        _viewFactory = viewFactory;
        Module = module;

        if (module is not null)
        {
            module.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(HasModule));
            };
        }
    }

    public string Title { get; }

    /// <summary>Null for screens the shell owns itself, like the idle page.</summary>
    public IFeatureModule? Module { get; }

    public bool HasModule => Module is not null;

    public FeatureState State => Module?.State ?? FeatureState.Stopped;

    public UserControl BuildView() => _viewFactory();
}
