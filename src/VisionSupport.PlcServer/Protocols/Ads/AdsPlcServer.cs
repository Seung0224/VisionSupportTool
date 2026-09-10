using System.Threading.Tasks;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.Ads
{
    public sealed class AdsPlcServer : IPlcServer
    {
        public AdsPlcServer(AdsServerConfig config)
        {
            Config = config;
            NodeMap = new NodeMap();
            AdsCommunication = new AdsCommunication(config, NodeMap);
        }

        public AdsServerConfig Config { get; }

        public NodeMap NodeMap { get; }

        public AdsCommunication AdsCommunication { get; }

        public ProtocolType ProtocolType => ProtocolType.Ads;

        public IPlcMap Map => NodeMap;

        public ICommunication Communication => AdsCommunication;

        public Task StartAsync() => AdsCommunication.StartAsync();

        public Task StopAsync() => AdsCommunication.StopAsync();
    }
}
