using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using VirtualPlcServer.Scenarios;
using VirtualPlcServer.Views;

namespace VirtualPlcServer.Modules
{
    /// <summary>
    /// "하드웨어"는 아니지만 같은 보드/같은 프레임워크에 얹히는 자동화 규칙 묶음.
    /// 체크박스로 켜고 끄는 것 = ScenarioEngine에 규칙을 등록/해제하는 것과 같다.
    /// StartAsync/StopAsync를 그 활성화 스위치로 재사용한다.
    /// </summary>
    public sealed class ScenarioModule : IHardwareModule
    {
        private readonly ScenarioEngine _engine;
        private ModuleRunState _state;

        public ScenarioModule(ScenarioEngine engine, Func<IEnumerable<PlcServerModule>> plcModulesProvider,
            string name, List<ScenarioRule> rules, Guid? id = null, DateTime? createdAt = null)
        {
            _engine = engine;
            PlcModulesProvider = plcModulesProvider;
            Name = string.IsNullOrWhiteSpace(name) ? "Scenario" : name.Trim();
            Rules = rules ?? new List<ScenarioRule>();
            Id = id ?? Guid.NewGuid();
            CreatedAt = createdAt ?? DateTime.Now;
            _state = ModuleRunState.Stopped;
        }

        public Func<IEnumerable<PlcServerModule>> PlcModulesProvider { get; }

        public List<ScenarioRule> Rules { get; }

        public Guid Id { get; }

        public string Name { get; private set; }

        /// <summary>보드의 수정 버튼으로 이름만 바꾼다. 규칙과 활성 상태는 그대로다.</summary>
        public void Rename(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Name = name.Trim();
            }
        }

        public string ModuleTypeName => "Scenario";

        public DateTime CreatedAt { get; }

        public DateTime? StartedAt { get; private set; }

        public ModuleRunState State
        {
            get => _state;
            private set
            {
                _state = value;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string SummaryInfo => Rules.Count == 1 ? "1 rule" : Rules.Count + " rules";

        public event EventHandler StateChanged;

        /// <summary>규칙이 추가/삭제/수정될 때 편집창이 올려서, 보드가 이를 듣고 즉시 저장할 수 있게 한다.</summary>
        public event EventHandler RulesChanged;

        /// <summary>규칙이 추가/수정/삭제된 뒤 편집창이 부른다. 엔진은 맵 구독을 규칙 목록에서 만들어 두므로,
        /// 켜져 있는 시나리오면 다시 맞춰야 한다 - 그러지 않으면 지운 규칙이 체크박스를 껐다 켤 때까지 계속 돈다.</summary>
        public void NotifyRulesChanged()
        {
            if (State == ModuleRunState.Running)
            {
                _engine.Activate(this);
            }

            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task StartAsync()
        {
            _engine.Activate(this);
            StartedAt = DateTime.Now;
            State = ModuleRunState.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _engine.Deactivate(this);
            State = ModuleRunState.Stopped;
            return Task.CompletedTask;
        }

        public Task ReinitializeAsync()
        {
            foreach (ScenarioRule rule in Rules)
            {
                rule.ElapsedSeconds = 0;
            }

            return Task.CompletedTask;
        }

        public Window CreateDetailWindow()
        {
            return new ScenarioEditorWindow(this);
        }

        public void Dispose()
        {
            try
            {
                _engine.Deactivate(this);
            }
            catch
            {
                // 앱 종료 중 정리 실패는 무시한다.
            }
        }
    }
}
