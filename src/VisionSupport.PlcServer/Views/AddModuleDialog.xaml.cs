using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.Modules;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    public partial class AddModuleDialog : UserControl
    {
        public AddModuleDialog()
        {
            InitializeComponent();
        }

        private void OnAddClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is AddModuleDialogViewModel viewModel))
            {
                return;
            }

            IHardwareModule module = viewModel.TryBuildModule();
            if (module != null)
            {
                DialogHost.CloseDialogCommand.Execute(module, this);
            }
        }
    }
}
