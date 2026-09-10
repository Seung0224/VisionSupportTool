using System;
using System.Threading.Tasks;
using System.Windows;

namespace VirtualPlcServer.Modules
{
    /// <summary>모듈의 재생/정지/재초기화 상태.</summary>
    public enum ModuleRunState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    /// <summary>
    /// 허브에 올라가는 모든 가상 하드웨어 모듈의 공용 계약. 통신 서버(MC/OPC UA/ADS)가 첫 구현체이고,
    /// 카메라 등 다른 하드웨어는 이후 이 인터페이스를 구현하는 새 클래스를 추가하는 식으로 확장한다.
    /// StartAsync는 배너의 "재생" 버튼을 눌렀을 때만 호출된다 - 모듈이 추가(생성)된 시점에는 절대 자동으로
    /// 호출하지 않는다(요구사항: 생성 시점이 아니라 가동 시점부터 실제로 켜짐).
    /// </summary>
    public interface IHardwareModule : IDisposable
    {
        Guid Id { get; }

        /// <summary>사용자가 붙인 모듈 이름. 같은 프로토콜의 모듈을 여러 개 만들 때 서로 구분하고,
        /// "마지막 저장 상태 불러오기"가 같은 이름의 설정만 찾아 불러오는 기준이 된다.</summary>
        string Name { get; }

        /// <summary>배너 2열 아이콘/타입 표시에 쓰는 모듈 종류 이름.</summary>
        string ModuleTypeName { get; }

        DateTime CreatedAt { get; }

        DateTime? StartedAt { get; }

        ModuleRunState State { get; }

        /// <summary>배너 4열에 표시할 간략 정보(이름/타입/IP/포트 등, 모듈마다 다름).</summary>
        string SummaryInfo { get; }

        event EventHandler StateChanged;

        Task StartAsync();

        Task StopAsync();

        /// <summary>정지 후 초기 상태(모듈을 추가했을 때의 스냅샷)로 값을 되돌린다. 설정 자체는 바뀌지 않는다.</summary>
        Task ReinitializeAsync();

        /// <summary>배너의 "보기" 버튼이 여는 별도 상세 모니터링 창.</summary>
        Window CreateDetailWindow();
    }
}
