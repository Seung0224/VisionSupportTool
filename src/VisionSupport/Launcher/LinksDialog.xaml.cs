using System.Windows;

namespace VisionSupport.Launcher;

public partial class LinksDialog : Window
{
    public LinksDialog(LinksViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();
    }
}
