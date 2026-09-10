using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    public partial class McMonitorView : UserControl
    {
        public McMonitorView(McMap map, string headerInfo)
        {
            InitializeComponent();
            DataContext = new McMonitorViewModel(map, headerInfo);
        }

        private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(Grid.SelectedItem is McWordRowViewModel row) || !(DataContext is McMonitorViewModel viewModel))
            {
                return;
            }

            var dialog = new WriteValueDialog(row.AddressDisplay, row.Value);
            object result = await DialogHost.Show(dialog, "MonitorDialog");
            if (result is ushort newValue)
            {
                viewModel.WriteValue(row.Address, newValue);
            }
        }
    }
}
