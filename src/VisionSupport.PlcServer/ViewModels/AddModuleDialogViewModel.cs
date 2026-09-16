using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Persistence;
using VirtualPlcServer.Protocols.Ads;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Protocols.OpcUa;
using VirtualPlcServer.Scenarios;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>Add Module 다이얼로그의 뷰모델. 1단계(타입 선택) → 2단계(타입별 설정) → 서버 생성까지 담당한다.</summary>
    public partial class AddModuleDialogViewModel : ObservableObject
    {
        private readonly ScenarioEngine _scenarioEngine;
        private readonly Func<IEnumerable<PlcServerModule>> _plcModulesProvider;

        public AddModuleDialogViewModel(ScenarioEngine scenarioEngine, Func<IEnumerable<PlcServerModule>> plcModulesProvider)
        {
            _scenarioEngine = scenarioEngine;
            _plcModulesProvider = plcModulesProvider;
        }

        public IReadOnlyList<ModuleTypeDescriptor> Types => ModuleTypeRegistry.Types;

        [ObservableProperty]
        private ModuleTypeDescriptor selectedType;

        [ObservableProperty]
        private bool typeChosen;

        public bool IsPlcServerSelected => SelectedType?.Kind == HardwareTypeKind.PlcServer;

        public bool IsScenarioSelected => SelectedType?.Kind == HardwareTypeKind.Scenario;

        partial void OnSelectedTypeChanged(ModuleTypeDescriptor value)
        {
            OnPropertyChanged(nameof(IsPlcServerSelected));
            OnPropertyChanged(nameof(IsScenarioSelected));
        }

        /// <summary>사용자가 붙이는 모듈 이름. 같은 프로토콜의 모듈을 여러 개 만들 때 서로 구분하고,
        /// "마지막 저장 상태 불러오기"가 같은 이름의 설정만 찾아 불러오는 기준이 된다.</summary>
        [ObservableProperty]
        private string moduleName = string.Empty;

        // 통신 서버 설정 - 기존 WinForms StartupForm과 동일한 항목.
        [ObservableProperty]
        private int protocolIndex;

        [ObservableProperty]
        private string mcListenIp = string.Empty;

        [ObservableProperty]
        private int mcReadPort = 9003;

        [ObservableProperty]
        private int mcWritePort = 9004;

        [ObservableProperty]
        private int mcStartAddress;

        [ObservableProperty]
        private int mcSize = 100;

        // MC Protocol (TCP/LS) - LS산전 PLC의 MC프로토콜 호환모드(TCP, 지속연결). 실제 클라이언트
        // (VO_Bio1FInspector 등)를 직접 읽어서 확인한 기본값: 디바이스 R, 포트 9003/9004.
        [ObservableProperty]
        private string mcTcpListenIp = string.Empty;

        [ObservableProperty]
        private int mcTcpReadPort = 9003;

        [ObservableProperty]
        private int mcTcpWritePort = 9004;

        [ObservableProperty]
        private string mcTcpDevice = "R";

        [ObservableProperty]
        private int mcTcpStartAddress = 25000;

        [ObservableProperty]
        private int mcTcpSize = 500;

        [ObservableProperty]
        private int opcUaPort = 4840;

        [ObservableProperty]
        private string opcUaAppName = "VirtualPlcServer";

        [ObservableProperty]
        private bool opcUaSeedJastechNodes = true;

        /// <summary>고른 노드 설정 파일(.cfg) 경로. 비어 있으면 기본 노드로 만든다.</summary>
        [ObservableProperty]
        private string opcUaCfgPath = string.Empty;

        /// <summary>설정 파일에서 읽고 배열 길이까지 확인된 노드 목록. 다이얼로그가 ADD 직전에 채워 넣는다.
        /// 비어 있으면 <see cref="OpcUaSeedJastechNodes"/>에 따라 기본 노드를 쓴다.</summary>
        public List<CfgNodeInfo> PendingCfgNodes { get; set; }

        [ObservableProperty]
        private int adsAmsPort = 27906;

        [ObservableProperty]
        private string adsPortName = "VirtualPlcServer";

        [ObservableProperty]
        private bool loadLastState;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        [RelayCommand]
        private void ChooseType(ModuleTypeDescriptor type)
        {
            if (type == null || !type.IsAvailable)
            {
                return;
            }

            SelectedType = type;
            TypeChosen = true;
        }

        [RelayCommand]
        private void Back()
        {
            TypeChosen = false;
        }

        /// <summary>2단계 설정으로 실제 IHardwareModule을 만든다. 실패하면 null이고 ErrorMessage에 이유가 담긴다.</summary>
        public IHardwareModule TryBuildModule()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(ModuleName))
            {
                ErrorMessage = "Enter a module name (e.g. \"MC 1\") - it's how this module's saved state is told apart from others of the same protocol.";
                return null;
            }

            if (IsScenarioSelected)
            {
                return new ScenarioModule(_scenarioEngine, _plcModulesProvider, ModuleName, new List<ScenarioRule>());
            }

            try
            {
                ProtocolType protocol = ProtocolIndexToType(ProtocolIndex);
                IPlcServer server = CreateServer(protocol);

                if (LoadLastState)
                {
                    LoadState(protocol, server, ModuleName);
                }

                if (protocol == ProtocolType.OpcUa && server is OpcUaPlcServer opcUaServer)
                {
                    // 이미 존재하는 노드(복원된 상태 등)는 건드리지 않고, 없는 것만 채운다.
                    // 설정 파일을 골랐으면 그 파일이 기본 노드를 대신한다.
                    if (PendingCfgNodes != null && PendingCfgNodes.Count > 0)
                    {
                        foreach (CfgNodeInfo node in PendingCfgNodes)
                        {
                            opcUaServer.NodeMap.TryAddNode(node.ToDefinition());
                        }
                    }
                    else if (OpcUaSeedJastechNodes)
                    {
                        foreach (var definition in JastechDefaultNodes.GetDefaultNodes())
                        {
                            opcUaServer.NodeMap.TryAddNode(definition);
                        }
                    }
                }

                return new PlcServerModule(server, ModuleName);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                return null;
            }
        }

        // ComboBox 순서(AddModuleDialog.xaml)와 정확히 일치해야 한다: 0=MC(UDP), 1=OPC UA, 2=ADS, 3=MC(TCP/LS).
        private static ProtocolType ProtocolIndexToType(int index)
        {
            switch (index)
            {
                case 0: return ProtocolType.Mc;
                case 1: return ProtocolType.OpcUa;
                case 3: return ProtocolType.McTcp;
                default: return ProtocolType.Ads;
            }
        }

        private IPlcServer CreateServer(ProtocolType protocol)
        {
            switch (protocol)
            {
                case ProtocolType.Mc:
                    var mcConfig = new McServerConfig
                    {
                        ListenAddress = string.IsNullOrWhiteSpace(McListenIp) ? IPAddress.Any : IPAddress.Parse(McListenIp.Trim()),
                        ReadPort = McReadPort,
                        WritePort = McWritePort,
                        StartAddress = McStartAddress,
                        Size = McSize
                    };
                    return new McPlcServer(mcConfig);

                case ProtocolType.McTcp:
                    char device = string.IsNullOrWhiteSpace(McTcpDevice) ? 'R' : char.ToUpperInvariant(McTcpDevice.Trim()[0]);
                    var mcTcpConfig = new McTcpServerConfig
                    {
                        ListenAddress = string.IsNullOrWhiteSpace(McTcpListenIp) ? IPAddress.Any : IPAddress.Parse(McTcpListenIp.Trim()),
                        ReadPort = McTcpReadPort,
                        WritePort = McTcpWritePort,
                        Device = device,
                        StartAddress = McTcpStartAddress,
                        Size = McTcpSize
                    };
                    return new McTcpPlcServer(mcTcpConfig);

                case ProtocolType.OpcUa:
                    var opcConfig = new OpcUaServerConfig
                    {
                        Port = OpcUaPort,
                        ApplicationName = string.IsNullOrWhiteSpace(OpcUaAppName) ? "VirtualPlcServer" : OpcUaAppName.Trim()
                    };
                    return new OpcUaPlcServer(opcConfig);

                default:
                    var adsConfig = new AdsServerConfig
                    {
                        AmsPort = (ushort)AdsAmsPort,
                        PortName = string.IsNullOrWhiteSpace(AdsPortName) ? "VirtualPlcServer" : AdsPortName.Trim()
                    };
                    return new AdsPlcServer(adsConfig);
            }
        }

        /// <summary>
        /// 현재 보드에 있(었)던 모듈들의 저장 상태(ModuleStateStore) 중에서, 같은 프로토콜이면서
        /// 이름이 정확히 같은 것만 찾아 그 맵 스냅샷을 불러온다 - MC 1과 MC 2처럼 같은 프로토콜의
        /// 모듈이 여러 개 있어도 서로의 설정을 잘못 불러오지 않도록 이름으로 구분한다.
        /// </summary>
        private static void LoadState(ProtocolType protocol, IPlcServer server, string name)
        {
            ModuleRecord match = ModuleStateStore.LoadAll()
                .FirstOrDefault(r => r.Kind == ModuleKind.PlcServer && r.ProtocolType == protocol &&
                                      string.Equals((r.Name ?? string.Empty).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                ModuleSnapshotHelper.ApplySnapshot(server, match.MapSnapshot);
            }
        }
    }
}
