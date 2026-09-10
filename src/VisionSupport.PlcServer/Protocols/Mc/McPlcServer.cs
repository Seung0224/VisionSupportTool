using System.Threading.Tasks;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Mc
{
    public sealed class McPlcServer : IPlcServer
    {
        public McPlcServer(McServerConfig config)
        {
            Config = config;
            McMap = new McMap(config.StartAddress, config.Size);
            McCommunication = new McCommunication(config, McMap);
        }

        public McServerConfig Config { get; }

        public McMap McMap { get; }

        public McCommunication McCommunication { get; }

        public ProtocolType ProtocolType => ProtocolType.Mc;

        public IPlcMap Map => McMap;

        public ICommunication Communication => McCommunication;

        public Task StartAsync() => McCommunication.StartAsync();

        public Task StopAsync() => McCommunication.StopAsync();
    }
}
