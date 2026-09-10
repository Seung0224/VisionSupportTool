using System.Windows;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Ads;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Protocols.OpcUa;

namespace VirtualPlcServer.Views
{
    public partial class PlcMonitorWindow : Window
    {
        public PlcMonitorWindow(PlcServerModule module)
        {
            InitializeComponent();
            Title = module.ModuleTypeName;

            switch (module.Server)
            {
                case McPlcServer mc:
                    HostContent.Content = new McMonitorView(mc.McMap, module.SummaryInfo);
                    break;
                case McTcpPlcServer mcTcp:
                    HostContent.Content = new McMonitorView(mcTcp.McMap, module.SummaryInfo);
                    break;
                case OpcUaPlcServer opcUa:
                    HostContent.Content = new NodeMonitorView(opcUa.NodeMap, module.SummaryInfo);
                    break;
                case AdsPlcServer ads:
                    HostContent.Content = new NodeMonitorView(ads.NodeMap, module.SummaryInfo);
                    break;
            }
        }
    }
}
