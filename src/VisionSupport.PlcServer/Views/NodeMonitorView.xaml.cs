using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    public partial class NodeMonitorView : UserControl
    {
        public NodeMonitorView(NodeMap nodeMap, string headerInfo)
        {
            InitializeComponent();
            DataContext = new NodeMonitorViewModel(nodeMap, headerInfo);
        }

        private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(Grid.SelectedItem is NodeRowViewModel row) || !(DataContext is NodeMonitorViewModel viewModel))
            {
                return;
            }

            var dialog = new NodeValueDialog(row.Definition);
            object result = await DialogHost.Show(dialog, "MonitorDialog");
            if (result != null)
            {
                viewModel.SetValue(row.Name, result);
            }
        }

        private async void OnAddNodeClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is NodeMonitorViewModel viewModel))
            {
                return;
            }

            var dialog = new AddNodeDialog();
            object result = await DialogHost.Show(dialog, "MonitorDialog");
            if (result is NodeDefinition definition)
            {
                viewModel.TryAddNode(definition);
            }
        }

        private void OnRemoveClicked(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is NodeRowViewModel row && DataContext is NodeMonitorViewModel viewModel)
            {
                viewModel.RemoveNode(row.Name);
            }
        }

        private void OnClearClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is NodeMonitorViewModel viewModel)
            {
                viewModel.ClearAll();
            }
        }
    }
}
