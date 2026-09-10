namespace VirtualPlcServer.Core
{
    public enum ProtocolType
    {
        Mc,
        OpcUa,
        Ads,

        /// <summary>MC프로토콜(3E 바이너리 프레임)을 TCP로 얹은 변형. LS산전 PLC의 "MC프로토콜 호환모드"가
        /// 이 방식(UDP가 아닌 TCP, 지속 연결)을 쓴다 - 커맨드/디바이스 코드 등 프레임 내용 자체는
        /// 기존 Mc(UDP, 미쓰비시)와 동일하지만 전송 계층이 달라 별도 통신 구현이 필요하다.</summary>
        McTcp
    }

    public enum PlcDataType
    {
        Bool,
        Int16,
        Int32,
        Float,
        Double,
        String
    }
}
