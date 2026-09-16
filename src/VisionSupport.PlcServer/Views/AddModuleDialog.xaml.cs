using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.OpcUa;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    public partial class AddModuleDialog : UserControl
    {
        /// <summary>프로토콜 콤보박스에서 OPC UA의 위치. AddModuleDialogViewModel의 매핑과 같아야 한다.</summary>
        private const int OpcUaProtocolIndex = 1;

        public AddModuleDialog()
        {
            InitializeComponent();
        }

        private void OnBrowseCfgClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is AddModuleDialogViewModel viewModel))
            {
                return;
            }

            var picker = new OpenFileDialog
            {
                Title = "Select OPC UA node config",
                Filter = "Node config (*.cfg;*.json)|*.cfg;*.json|All files (*.*)|*.*"
            };

            if (picker.ShowDialog() == true)
            {
                viewModel.OpcUaCfgPath = picker.FileName;
            }
        }

        private void OnClearCfgClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is AddModuleDialogViewModel viewModel)
            {
                viewModel.OpcUaCfgPath = string.Empty;
                viewModel.PendingCfgNodes = null;
            }
        }

        private void OnAddClicked(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is AddModuleDialogViewModel viewModel))
            {
                return;
            }

            // 설정 파일을 골랐다면 서버를 만들기 전에 읽고 배열 길이를 확인받는다.
            if (!TryPrepareCfgNodes(viewModel))
            {
                return;
            }

            IHardwareModule module = viewModel.TryBuildModule();
            if (module != null)
            {
                DialogHost.CloseDialogCommand.Execute(module, this);
            }
        }

        /// <summary>파일을 고르지 않았으면 아무것도 하지 않고 통과시킨다(기본 노드로 생성).
        /// 골랐으면 읽어서 배열 길이 확인 창을 띄우고, 취소하면 생성 자체를 멈춘다.</summary>
        private bool TryPrepareCfgNodes(AddModuleDialogViewModel viewModel)
        {
            viewModel.PendingCfgNodes = null;

            if (viewModel.ProtocolIndex != OpcUaProtocolIndex || string.IsNullOrWhiteSpace(viewModel.OpcUaCfgPath))
            {
                return true;
            }

            try
            {
                List<CfgNodeInfo> nodes = JastechCfgFile.Load(viewModel.OpcUaCfgPath.Trim());
                if (nodes.Count == 0)
                {
                    viewModel.ErrorMessage = "설정 파일에서 만들 수 있는 노드가 없습니다.";
                    return false;
                }

                if (nodes.Any(n => n.IsArray))
                {
                    var confirm = new ConfirmArrayLengthDialog(nodes) { Owner = Window.GetWindow(this) };
                    if (confirm.ShowDialog() != true)
                    {
                        return false;
                    }
                }

                viewModel.PendingCfgNodes = nodes;
                return true;
            }
            catch (Exception ex)
            {
                viewModel.ErrorMessage = ex.Message;
                return false;
            }
        }
    }
}
