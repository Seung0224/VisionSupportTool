using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Scenarios;
using VirtualPlcServer.Views;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>규칙 하나를 목록에 보여주기 위한 행. 사람이 읽을 수 있는 설명 문자열 + 수정/삭제 커맨드만 갖는다.</summary>
    public partial class ScenarioRuleRowViewModel : ObservableObject
    {
        public ScenarioRuleRowViewModel(ScenarioRule rule, string description)
        {
            Rule = rule;
            Description = description;
        }

        public ScenarioRule Rule { get; }

        public string Description { get; }

        public event EventHandler DeleteRequested;

        public event EventHandler EditRequested;

        [RelayCommand]
        private void Edit()
        {
            EditRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Delete()
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>시나리오 편집창(ScenarioEditorWindow)의 뷰모델. 규칙 추가/수정/삭제를 담당한다.</summary>
    public partial class ScenarioEditorViewModel : ObservableObject
    {
        private readonly ScenarioModule _module;

        public ScenarioEditorViewModel(ScenarioModule module)
        {
            _module = module;
            Rules = new ObservableCollection<ScenarioRuleRowViewModel>();
            RefreshRows();
        }

        public string ScenarioName => _module.Name;

        public ObservableCollection<ScenarioRuleRowViewModel> Rules { get; }

        private IReadOnlyList<PlcServerModule> AvailableModules => _module.PlcModulesProvider().ToList();

        [RelayCommand]
        private async Task AddRule()
        {
            var dialogViewModel = new ScenarioRuleDialogViewModel(AvailableModules);
            var dialog = new ScenarioRuleDialog { DataContext = dialogViewModel };
            object result = await DialogHost.Show(dialog, "ScenarioDialog");
            if (result is ScenarioRule rule)
            {
                _module.Rules.Add(rule);
                _module.NotifyRulesChanged();
                RefreshRows();
            }
        }

        /// <summary>규칙을 다이얼로그에 채워 다시 열고, 저장하면 같은 자리에 바꿔 넣는다(Id 유지).</summary>
        private async Task EditRule(ScenarioRuleRowViewModel row)
        {
            var dialogViewModel = new ScenarioRuleDialogViewModel(AvailableModules);
            dialogViewModel.LoadFrom(row.Rule);
            var dialog = new ScenarioRuleDialog { DataContext = dialogViewModel };
            object result = await DialogHost.Show(dialog, "ScenarioDialog");
            if (!(result is ScenarioRule edited))
            {
                return;
            }

            int index = _module.Rules.IndexOf(row.Rule);
            if (index < 0)
            {
                return;
            }

            edited.Id = row.Rule.Id;
            _module.Rules[index] = edited;
            _module.NotifyRulesChanged();
            RefreshRows();
        }

        private void RemoveRule(ScenarioRuleRowViewModel row)
        {
            _module.Rules.Remove(row.Rule);
            _module.NotifyRulesChanged();
            RefreshRows();
        }

        private void RefreshRows()
        {
            Rules.Clear();
            Dictionary<Guid, string> nameLookup = AvailableModules.ToDictionary(m => m.Id, m => m.Name);
            foreach (ScenarioRule rule in _module.Rules)
            {
                var row = new ScenarioRuleRowViewModel(rule, rule.Describe(id => nameLookup.TryGetValue(id, out string name) ? name : null));
                row.DeleteRequested += (s, e) => RemoveRule(row);
                row.EditRequested += async (s, e) => await EditRule(row);
                Rules.Add(row);
            }
        }
    }
}
