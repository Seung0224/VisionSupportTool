using System.Windows;
using VisionSupport.Shell;

namespace VisionSupport.Launcher;

public partial class LinksDialog : Window
{
    public LinksDialog(LinksViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => AutomationDisconnect.Disconnect(this);
    }
}
