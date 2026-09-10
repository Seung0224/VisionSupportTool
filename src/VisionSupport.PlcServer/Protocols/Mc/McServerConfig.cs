using System.Net;

namespace VirtualPlcServer.Protocols.Mc
{
    public sealed class McServerConfig
    {
        public IPAddress ListenAddress { get; set; } = IPAddress.Any;

        /// <summary>Read(0x0401) 요청을 받을 UDP 포트</summary>
        public int ReadPort { get; set; } = 9003;

        /// <summary>Write(0x1401) 요청을 받을 UDP 포트</summary>
        public int WritePort { get; set; } = 9004;

        /// <summary>D영역 시작 번지 (예: 100 -> D100)</summary>
        public int StartAddress { get; set; }

        /// <summary>D영역 읽을/보유할 워드 크기</summary>
        public int Size { get; set; } = 100;
    }
}
