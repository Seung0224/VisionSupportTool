using System;
using System.Threading.Tasks;
using System.Windows;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Ads;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Protocols.OpcUa;
using VirtualPlcServer.Views;

namespace VirtualPlcServer.Modules
{
    /// <summary>
    /// 기존 IPlcServer(McPlcServer/OpcUaPlcServer/AdsPlcServer)를 모듈 허브의 IHardwareModule로 감싼다.
    /// 첫 번째이자 지금 유일하게 실제로 동작하는 모듈 종류.
    /// </summary>
    public sealed class PlcServerModule : IHardwareModule
    {
        private readonly object _initialSnapshot;
        private ModuleRunState _state;

        /// <param name="server">이미 원하는 값으로 채워진(복원 시) 또는 방금 만들어진(신규 추가 시) 서버.</param>
        /// <param name="name">사용자가 붙인 이름. 같은 프로토콜의 모듈이 여러 개일 때 서로 구분하는 유일한 근거.</param>
        /// <param name="id">저장된 모듈을 복원할 때만 준다. 새로 추가할 땐 새 Guid를 만든다.</param>
        /// <param name="createdAt">저장된 모듈을 복원할 때만 준다(원래 생성 시각 유지). 새로 추가할 땐 지금 시각.</param>
        public PlcServerModule(IPlcServer server, string name, Guid? id = null, DateTime? createdAt = null)
        {
            Server = server;
            Name = string.IsNullOrWhiteSpace(name) ? "Communication Server (" + server.ProtocolType + ")" : name.Trim();
            Id = id ?? Guid.NewGuid();
            CreatedAt = createdAt ?? DateTime.Now;

            // 이 시점의 맵 내용이 "재초기화" 기준점이다 - 신규 추가면 비어있는 초기값, 복원이면 저장돼있던 값.
            _initialSnapshot = server.Map.CreateSnapshot();
            _state = ModuleRunState.Stopped;
        }

        public IPlcServer Server { get; }

        public Guid Id { get; }

        public string Name { get; }

        public string ModuleTypeName => "Communication Server (" + Server.ProtocolType + ")";

        public DateTime CreatedAt { get; }

        public DateTime? StartedAt { get; private set; }

        public ModuleRunState State
        {
            get => _state;
            private set
            {
                _state = value;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string SummaryInfo => BuildSummary();

        public event EventHandler StateChanged;

        public async Task StartAsync()
        {
            State = ModuleRunState.Starting;
            try
            {
                // ConfigureAwait(false): Dispose()가 종료 시점에 이 메서드를 UI 스레드에서
                // .GetAwaiter().GetResult()로 동기 대기하는데, 여기서 UI(Dispatcher) 컨텍스트로
                // 돌아가려 하면 그 UI 스레드 자체가 막혀 있어 데드락이 난다. 계속 스레드풀에서 이어가면 안전하다.
                await Server.StartAsync().ConfigureAwait(false);
                StartedAt = DateTime.Now;
                State = ModuleRunState.Running;
            }
            catch
            {
                State = ModuleRunState.Error;
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (State == ModuleRunState.Stopped)
            {
                return;
            }

            State = ModuleRunState.Stopping;
            await Server.StopAsync().ConfigureAwait(false);
            State = ModuleRunState.Stopped;
        }

        public async Task ReinitializeAsync()
        {
            if (State == ModuleRunState.Running || State == ModuleRunState.Starting)
            {
                await StopAsync().ConfigureAwait(false);
            }

            if (Server.Map is NodeMap nodeMap)
            {
                // NodeMap.RestoreSnapshot은 추가만 하고 지우지 않으므로 먼저 비워야 진짜 초기화가 된다.
                nodeMap.Clear();
            }

            Server.Map.RestoreSnapshot(_initialSnapshot);
            State = ModuleRunState.Stopped;
        }

        public Window CreateDetailWindow()
        {
            return new PlcMonitorWindow(this);
        }

        public void Dispose()
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // 앱 종료 중 정리 실패는 무시한다.
            }
        }

        private string BuildSummary()
        {
            switch (Server)
            {
                case McPlcServer mc:
                    return "IP=" + FormatListenAddress(mc.Config.ListenAddress) +
                           " · UDP Read=" + mc.Config.ReadPort + " Write=" + mc.Config.WritePort +
                           " · D" + mc.Config.StartAddress + "~D" + (mc.Config.StartAddress + mc.Config.Size - 1);
                case McTcpPlcServer mcTcp:
                    return "IP=" + FormatListenAddress(mcTcp.Config.ListenAddress) +
                           " · TCP Read=" + mcTcp.Config.ReadPort + " Write=" + mcTcp.Config.WritePort +
                           " · " + mcTcp.Config.Device + mcTcp.Config.StartAddress + "~" + mcTcp.Config.Device + (mcTcp.Config.StartAddress + mcTcp.Config.Size - 1);
                case OpcUaPlcServer opcUa:
                    return "opc.tcp://*:" + opcUa.Config.Port + "/" + opcUa.Config.ApplicationName;
                case AdsPlcServer ads:
                    return "AMS Port " + ads.Config.AmsPort + " · " + ads.Config.PortName;
                default:
                    return string.Empty;
            }
        }

        private static string FormatListenAddress(System.Net.IPAddress address)
        {
            if (System.Net.IPAddress.Any.Equals(address))
            {
                return "0.0.0.0 (all interfaces, incl. 127.0.0.1)";
            }

            if (System.Net.IPAddress.Loopback.Equals(address))
            {
                return "127.0.0.1 (loopback only)";
            }

            return address.ToString();
        }
    }
}
