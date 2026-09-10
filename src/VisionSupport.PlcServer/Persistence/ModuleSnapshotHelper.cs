using Newtonsoft.Json.Linq;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.Mc;

namespace VirtualPlcServer.Persistence
{
    /// <summary>ModuleStateStore에서 읽은(=JObject로 역직렬화된) 맵 스냅샷을 실제 서버에 적용한다.</summary>
    public static class ModuleSnapshotHelper
    {
        public static void ApplySnapshot(IPlcServer server, object rawSnapshot)
        {
            if (!(rawSnapshot is JObject snapshotObject))
            {
                return;
            }

            switch (server.ProtocolType)
            {
                case ProtocolType.Mc:
                case ProtocolType.McTcp:
                    server.Map.RestoreSnapshot(snapshotObject.ToObject<McMapSnapshot>());
                    break;
                case ProtocolType.OpcUa:
                case ProtocolType.Ads:
                    server.Map.RestoreSnapshot(snapshotObject.ToObject<NodeMapSnapshot>());
                    break;
            }
        }
    }
}
