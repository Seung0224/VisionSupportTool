using System.Threading.Tasks;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Mc
{
    /// <summary>MC프로토콜(TCP) 서버 - LS산전 PLC의 "MC프로토콜 호환모드"를 흉내낸다. 프레임 포맷은
    /// 기존 McPlcServer(UDP)와 같지만 전송 계층이 TCP·지속 연결이라 별도 구현(McTcpCommunication)을 쓴다.</summary>
    public sealed class McTcpPlcServer : IPlcServer
    {
        public McTcpPlcServer(McTcpServerConfig config)
        {
            Config = config;
            McMap = new McMap(config.StartAddress, config.Size, config.Device.ToString());
            McCommunication = new McTcpCommunication(config, McMap);
        }

        public McTcpServerConfig Config { get; }

        public McMap McMap { get; }

        public McTcpCommunication McCommunication { get; }

        public ProtocolType ProtocolType => ProtocolType.McTcp;

        public IPlcMap Map => McMap;

        public ICommunication Communication => McCommunication;

        public Task StartAsync() => McCommunication.StartAsync();

        public Task StopAsync() => McCommunication.StopAsync();
    }
}
