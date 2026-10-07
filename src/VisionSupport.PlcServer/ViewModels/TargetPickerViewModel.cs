using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Scenarios;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>규칙 다이얼로그에서 번지 하나를 고르는 칸 묶음: 모듈 + (MC면 D번지 입력, OPC UA/ADS면 노드 선택)
    /// + 배열 노드면 원소 인덱스. 감시 대상, 쓰기 대상, 비교 대상 번지, AND 조건의 번지가 모두 이걸 쓴다.</summary>
    public partial class TargetPickerViewModel : ObservableObject
    {
        public TargetPickerViewModel(IReadOnlyList<PlcServerModule> availableModules)
        {
            AvailableModules = availableModules;

            // 보드에 통신 서버가 하나뿐이면 매번 고를 필요가 없다.
            if (availableModules.Count == 1)
            {
                SelectedModule = availableModules[0];
            }
        }

        public IReadOnlyList<PlcServerModule> AvailableModules { get; }

        [ObservableProperty]
        private PlcServerModule selectedModule;

        /// <summary>MC 모듈일 때만 직접 입력하는 D번지. OPC UA/ADS는 SelectedNode 선택 시 자동으로 채워진다.</summary>
        [ObservableProperty]
        private string address = string.Empty;

        /// <summary>OPC UA/ADS 모듈일 때, 목록에서 고른 노드.</summary>
        [ObservableProperty]
        private NodeDefinition selectedNode;

        [ObservableProperty]
        private int arrayIndex;

        public bool IsNodeBased => PlcTargetAccessor.IsNodeBased(SelectedModule);

        public IReadOnlyList<NodeDefinition> Nodes => PlcTargetAccessor.GetNodeDefinitions(SelectedModule);

        public bool IsArraySelected => SelectedNode?.IsArray == true;

        public bool IsStringSelected => SelectedNode?.DataType == PlcDataType.String;

        public bool IsComplete => SelectedModule != null && !string.IsNullOrWhiteSpace(Address);

        partial void OnSelectedModuleChanged(PlcServerModule value)
        {
            SelectedNode = null;
            Address = string.Empty;
            OnPropertyChanged(nameof(IsNodeBased));
            OnPropertyChanged(nameof(Nodes));
        }

        partial void OnSelectedNodeChanged(NodeDefinition value)
        {
            ArrayIndex = 0;
            if (value != null)
            {
                Address = value.Name;
            }

            OnPropertyChanged(nameof(IsArraySelected));
            OnPropertyChanged(nameof(IsStringSelected));
        }

        /// <summary>저장된 규칙을 고치려고 다이얼로그를 다시 열 때, 그 번지로 칸을 채운다.
        /// 모듈이 보드에서 사라졌으면 모듈 칸은 비우고 주소만 남긴다.</summary>
        public void Load(TargetRef target)
        {
            if (target == null)
            {
                return;
            }

            SelectedModule = AvailableModules.FirstOrDefault(m => m.Id == target.ModuleId);
            if (IsNodeBased)
            {
                SelectedNode = Nodes.FirstOrDefault(n => string.Equals(n.Name, target.Address, StringComparison.OrdinalIgnoreCase));
            }

            Address = target.Address ?? string.Empty;
            ArrayIndex = target.ArrayIndex ?? 0;
        }

        public TargetRef ToTargetRef()
        {
            return new TargetRef
            {
                ModuleId = SelectedModule.Id,
                Address = Address.Trim(),
                ArrayIndex = IsArraySelected ? ArrayIndex : (int?)null
            };
        }
    }
}
