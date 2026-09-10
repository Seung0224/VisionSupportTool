using System.Threading.Tasks;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.OpcUa
{
    public sealed class OpcUaPlcServer : IPlcServer
    {
        public OpcUaPlcServer(OpcUaServerConfig config)
        {
            Config = config;
            NodeMap = new NodeMap();
            OpcUaCommunication = new OpcUaCommunication(config, NodeMap);
        }

        public OpcUaServerConfig Config { get; }

        public NodeMap NodeMap { get; }

        public OpcUaCommunication OpcUaCommunication { get; }

        public ProtocolType ProtocolType => ProtocolType.OpcUa;

        public IPlcMap Map => NodeMap;

        public ICommunication Communication => OpcUaCommunication;

        public Task StartAsync() => OpcUaCommunication.StartAsync();

        public Task StopAsync() => OpcUaCommunication.StopAsync();
    }
}
