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

    /// <summary>
    /// 노드 하나의 값 타입. 순서를 바꾸거나 중간에 끼워 넣으면 안 된다 - AddNodeDialog가 콤보박스의
    /// SelectedIndex를 이 enum으로 그대로 캐스팅하고, 저장된 스냅샷도 이 값을 숫자로 들고 있다.
    /// 뒤에 덧붙이는 것만 안전하다.
    ///
    /// SByte~UInt32는 Jastech OPC UA 설정 파일(OpcuaNodeVariableType: BIT/SBYTE/BYTE/INT16/
    /// UINT16/INT32/UINT32/FLOAT/DOUBLE/STRING)에 나오는 타입까지 그대로 받기 위해 추가했다.
    /// </summary>
    public enum PlcDataType
    {
        Bool,
        Int16,
        Int32,
        Float,
        Double,
        String,
        SByte,
        Byte,
        UInt16,
        UInt32
    }
}
