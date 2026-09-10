using System.Collections.Generic;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Common
{
    public sealed class NodeSnapshotEntry
    {
        public string Name { get; set; }

        public PlcDataType DataType { get; set; }

        public bool IsArray { get; set; }

        public int ArrayLength { get; set; }

        public object Value { get; set; }
    }

    /// <summary>NodeMap의 JSON 직렬화용 스냅샷.</summary>
    public sealed class NodeMapSnapshot
    {
        public List<NodeSnapshotEntry> Nodes { get; set; } = new List<NodeSnapshotEntry>();
    }
}
