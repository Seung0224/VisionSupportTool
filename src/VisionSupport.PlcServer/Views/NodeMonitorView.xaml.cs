using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.OpcUa;
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

        /// <summary>설정 파일(.cfg)을 읽어 지금 맵에 노드를 추가한다. 이름이 겹치는 노드는 건너뛴다.</summary>
        private void OnImportCfgClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is NodeMonitorViewModel viewModel))
            {
                return;
            }

            var picker = new OpenFileDialog
            {
                Title = "Import OPC UA node config",
                Filter = "Node config (*.cfg;*.json)|*.cfg;*.json|All files (*.*)|*.*"
            };

            if (picker.ShowDialog() != true)
            {
                return;
            }

            try
            {
                List<CfgNodeInfo> nodes = JastechCfgFile.Load(picker.FileName);
                if (nodes.Count == 0)
                {
                    MessageBox.Show("만들 수 있는 노드가 없습니다.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 배열이 있으면 길이를 확인받고 나서 만든다 - 파일에 길이가 없기 때문이다.
                if (nodes.Any(n => n.IsArray))
                {
                    var confirm = new ConfirmArrayLengthDialog(nodes) { Owner = Window.GetWindow(this) };
                    if (confirm.ShowDialog() != true)
                    {
                        return;
                    }
                }

                int added = nodes.Count(n => viewModel.TryAddNode(n.ToDefinition()));
                MessageBox.Show(
                    added + " / " + nodes.Count + " 개 노드를 추가했습니다." +
                    (added < nodes.Count ? " (이름이 이미 있는 노드는 건너뜁니다.)" : string.Empty),
                    "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Import 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>지금 맵에 있는 노드를 같은 설정 파일 포맷으로 저장한다.</summary>
        private void OnExportCfgClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is NodeMonitorViewModel viewModel))
            {
                return;
            }

            var picker = new SaveFileDialog
            {
                Title = "Export OPC UA node config",
                Filter = "Node config (*.cfg)|*.cfg|All files (*.*)|*.*",
                FileName = "OpcuaMemoryMap.cfg"
            };

            if (picker.ShowDialog() != true)
            {
                return;
            }

            try
            {
                JastechCfgFile.Save(picker.FileName, viewModel.Rows.Select(r => r.Definition));
                MessageBox.Show(viewModel.Rows.Count + " 개 노드를 저장했습니다.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
