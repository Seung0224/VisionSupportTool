using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    public partial class ScenarioRuleDialog : UserControl
    {
        public ScenarioRuleDialog()
        {
            InitializeComponent();
        }

        private void OnAddClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is ScenarioRuleDialogViewModel viewModel))
            {
                return;
            }

            var rule = viewModel.TryBuildRule();
            if (rule != null)
            {
                DialogHost.CloseDialogCommand.Execute(rule, this);
            }
        }
    }
}
