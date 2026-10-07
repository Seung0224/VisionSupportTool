using System.Windows;
using VisionSupport.Shell;

namespace VisionSupport.Overview;

public partial class OverviewDialog : Window
{
    public OverviewDialog(OverviewViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();

        Loaded += (_, _) => viewModel.Activate();
        Closed += (_, _) =>
        {
            viewModel.Deactivate();
            AutomationDisconnect.Disconnect(this);
        };
    }
}
