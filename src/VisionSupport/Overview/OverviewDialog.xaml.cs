using System.Windows;
using VisionSupport.Windows;

namespace VisionSupport.Overview;

public partial class OverviewDialog : Window
{
    /// <summary>Matches Theme/Dark.xaml's RadiusWindow.</summary>
    private const double WindowCornerRadius = 16;

    public OverviewDialog(OverviewViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();

        SourceInitialized += (_, _) => WindowEffects.AttachRoundedCorners(this, WindowCornerRadius);
        Loaded += (_, _) => viewModel.Activate();
        Closed += (_, _) => viewModel.Deactivate();
    }
}
