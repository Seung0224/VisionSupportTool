namespace VirtualPlcServer.Protocols.OpcUa
{
    public sealed class OpcUaServerConfig
    {
        public int Port { get; set; } = 4840;

        public string ApplicationName { get; set; } = "VirtualPlcServer";
    }
}
