using System;
using System.Collections.Generic;
using VirtualPlcServer.Core;
using VirtualPlcServer.Scenarios;

namespace VirtualPlcServer.Persistence
{
    /// <summary>보드에 올라갈 수 있는 카드의 종류. 옛 저장 파일에는 이 필드가 없는데,
    /// JSON에 없는 enum 필드는 기본값(0=PlcServer)으로 채워져서 기존 통신 서버 레코드와도 호환된다.</summary>
    public enum ModuleKind
    {
        PlcServer,
        Scenario
    }

    /// <summary>
    /// 보드 위 카드 하나를 다시 만들 수 있을 만큼의 정보.
    /// PlcServer 카드는 Config/ProtocolType/MapSnapshot을, Scenario 카드는 IsActive/Rules를 쓴다.
    /// Config/MapSnapshot은 역직렬화 시 JObject로 들어오므로, 실제 IPlcServer로 복원할 때 ProtocolType을
    /// 보고 알맞은 구체 타입(McServerConfig 등)으로 다시 변환한다.
    /// </summary>
    public sealed class ModuleRecord
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public ModuleKind Kind { get; set; } = ModuleKind.PlcServer;

        public ProtocolType ProtocolType { get; set; }

        public DateTime CreatedAt { get; set; }

        // PlcServer 전용
        public object Config { get; set; }

        public object MapSnapshot { get; set; }

        // Scenario 전용 - 재시작 후에는 통신 서버와 마찬가지로 항상 비활성(Stopped)으로 복원되므로
        // IsActive는 저장은 하되 복원 시에는 참고하지 않는다.
        public bool IsActive { get; set; }

        public List<ScenarioRule> Rules { get; set; }
    }
}
