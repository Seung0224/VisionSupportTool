using System.Threading.Tasks;

namespace VirtualPlcServer.Core
{
    /// <summary>
    /// 가상 PLC 서버 하나(Map + Communication)를 표현하는 최상위 계약.
    /// MC/OPC UA/ADS 각각의 XxxPlcServer가 이를 구현한다.
    /// </summary>
    public interface IPlcServer
    {
        ProtocolType ProtocolType { get; }

        IPlcMap Map { get; }

        ICommunication Communication { get; }

        Task StartAsync();

        Task StopAsync();
    }
}
