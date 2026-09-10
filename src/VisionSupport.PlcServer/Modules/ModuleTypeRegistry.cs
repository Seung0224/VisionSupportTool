using System.Collections.Generic;
using FontAwesome.Sharp;

namespace VirtualPlcServer.Modules
{
    /// <summary>Add Module 1단계(타입 선택)에서 실제로 어떤 종류의 모듈을 만들어야 하는지 구분하는 값.
    /// 표시 이름(문자열) 대신 이걸로 분기해야 이름을 바꿔도 로직이 깨지지 않는다.</summary>
    public enum HardwareTypeKind
    {
        PlcServer,
        Scenario,
        Other
    }

    /// <summary>Add Module 1단계(타입 선택)에 보여줄 항목. 통신 서버와 시나리오는 실제로 동작하고,
    /// 기타 하드웨어는 "준비 중" 비활성 항목으로만 노출해 확장 가능한 틀임을 보여준다.</summary>
    public sealed class ModuleTypeDescriptor
    {
        public ModuleTypeDescriptor(string name, IconChar icon, HardwareTypeKind kind, bool isAvailable, string unavailableReason = null)
        {
            Name = name;
            Icon = icon;
            Kind = kind;
            IsAvailable = isAvailable;
            UnavailableReason = unavailableReason;
        }

        public string Name { get; }

        public IconChar Icon { get; }

        public HardwareTypeKind Kind { get; }

        public bool IsAvailable { get; }

        public string UnavailableReason { get; }
    }

    public static class ModuleTypeRegistry
    {
        public static readonly IReadOnlyList<ModuleTypeDescriptor> Types = new List<ModuleTypeDescriptor>
        {
            new ModuleTypeDescriptor("Communication Server", IconChar.NetworkWired, HardwareTypeKind.PlcServer, true),
            new ModuleTypeDescriptor("Scenario", IconChar.Sitemap, HardwareTypeKind.Scenario, true),
            new ModuleTypeDescriptor("Other Hardware", IconChar.Microchip, HardwareTypeKind.Other, false, "Coming Soon")
        };
    }
}
