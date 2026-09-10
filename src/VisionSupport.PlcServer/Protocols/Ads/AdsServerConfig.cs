namespace VirtualPlcServer.Protocols.Ads
{
    public sealed class AdsServerConfig
    {
        /// <summary>AMS 포트 번호 (커스텀 서버는 CUSTOMER 대역 사용을 권장, 로컬 테스트용으로 임의 지정)</summary>
        public ushort AmsPort { get; set; } = 27906;

        public string PortName { get; set; } = "VirtualPlcServer";
    }
}
