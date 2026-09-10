using System.Net;

namespace VirtualPlcServer.Protocols.Mc
{
    /// <summary>
    /// MC프로토콜(3E 바이너리 프레임)을 TCP로 얹은 서버 설정 - LS산전 PLC의 "MC프로토콜 호환모드"(TCP,
    /// 지속 연결)를 흉내낸다. VO_Bio1FInspector의 실제 클라이언트(VO_MCProtoclTCP.cpp)가 Read용/Write용
    /// TCP 소켓을 별도 포트로 여는 것과 똑같이, 여기도 포트를 두 개(ReadPort/WritePort) 받는다 - 실제로는
    /// 어느 포트로 붙든 같은 방식으로 응답하므로(프레임 자체에 명령 종류가 들어있음), 두 값이 같아도
    /// 문제없이 동작한다(그 경우 리스너 하나만 띄운다).
    /// </summary>
    public sealed class McTcpServerConfig
    {
        public IPAddress ListenAddress { get; set; } = IPAddress.Any;

        /// <summary>Read 요청을 받는 TCP 리슨 포트</summary>
        public int ReadPort { get; set; } = 9003;

        /// <summary>Write 요청을 받는 TCP 리슨 포트</summary>
        public int WritePort { get; set; } = 9004;

        /// <summary>디바이스 문자(D/R/W 중 하나) - VO_Bio1FInspector 등 LS PLC 클라이언트는 기본적으로
        /// 'R'(파일 레지스터)을 쓴다.</summary>
        public char Device { get; set; } = 'R';

        /// <summary>디바이스 영역 시작 번지</summary>
        public int StartAddress { get; set; }

        /// <summary>디바이스 영역 읽을/보유할 워드 크기</summary>
        public int Size { get; set; } = 500;
    }
}
