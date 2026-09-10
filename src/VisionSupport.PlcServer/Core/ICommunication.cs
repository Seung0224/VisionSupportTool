using System;
using System.Threading.Tasks;

namespace VirtualPlcServer.Core
{
    /// <summary>
    /// 프로토콜별 네트워크 통신 계층(TCP 서버, OPC UA 서버, ADS 서버 등)의 공통 계약.
    /// </summary>
    public interface ICommunication
    {
        bool IsRunning { get; }

        Task StartAsync();

        Task StopAsync();

        event EventHandler<string> StatusChanged;

        event EventHandler<Exception> ErrorOccurred;
    }
}
