using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Ads
{
    /// <summary>NodeMap의 노드 하나가 ADS Raw 메모리(IndexGroup/IndexOffset)에 매핑된 정보.</summary>
    public sealed class AdsNodeMemory
    {
        public string Name { get; set; }

        public uint IndexGroup { get; set; }

        public uint IndexOffset { get; set; }

        public int ByteLength { get; set; }

        public PlcDataType DataType { get; set; }

        public bool IsArray { get; set; }

        public int ArrayLength { get; set; }
    }
}
