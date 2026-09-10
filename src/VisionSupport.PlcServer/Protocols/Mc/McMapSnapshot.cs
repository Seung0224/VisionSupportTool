namespace VirtualPlcServer.Protocols.Mc
{
    /// <summary>McMap의 JSON 직렬화용 스냅샷.</summary>
    public sealed class McMapSnapshot
    {
        public int StartAddress { get; set; }

        public int Size { get; set; }

        public ushort[] Values { get; set; }
    }
}
