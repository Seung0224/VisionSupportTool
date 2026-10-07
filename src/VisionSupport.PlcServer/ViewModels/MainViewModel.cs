using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Persistence;
using VirtualPlcServer.Protocols.Ads;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Protocols.OpcUa;
using VirtualPlcServer.Scenarios;
using VirtualPlcServer.Views;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>허브 메인 화면의 뷰모델. 모듈 목록을 들고 있고, 추가/삭제될 때마다 디스크에 저장한다.</summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly ScenarioEngine _scenarioEngine;

        public ObservableCollection<ModuleViewModel> Modules { get; } = new ObservableCollection<ModuleViewModel>();

        /// <summary>보드 보기 방식: false = 카드(칸을 나눠 채움), true = 목록(한 줄에 모듈 하나).
        /// 창 제목줄의 토글이 바꾸고, 다음에 열 때도 그대로 열리도록 저장한다.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGridLayout))]
        private bool isListLayout = BoardSettingsStore.Load().IsListLayout;

        public bool IsGridLayout
        {
            get => !IsListLayout;
            set => IsListLayout = !value;
        }

        partial void OnIsListLayoutChanged(bool value)
        {
            BoardSettingsStore.Save(new BoardSettings { IsListLayout = value });
        }

        public MainViewModel()
        {
            _scenarioEngine = new ScenarioEngine(GetPlcModules);
            RestorePersistedModules();
            Modules.CollectionChanged += (s, e) =>
            {
                _scenarioEngine.NotifyModulesChanged();
                ModulesChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        private IEnumerable<PlcServerModule> GetPlcModules()
        {
            return Modules.Select(vm => vm.Module).OfType<PlcServerModule>();
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task OpenAddModule()
        {
            var dialogViewModel = new AddModuleDialogViewModel(_scenarioEngine, GetPlcModules);
            var dialog = new AddModuleDialog { DataContext = dialogViewModel };
            object result = await DialogHost.Show(dialog, "RootDialog");
            if (result is IHardwareModule module)
            {
                AddModule(module);
            }
        }

        /// <summary>Add Module 다이얼로그가 서버(혹은 시나리오)를 만들고 나면 이걸 호출해서 배너 목록에 추가한다(아직 시작은 안 됨).</summary>
        public void AddModule(IHardwareModule module)
        {
            var vm = new ModuleViewModel(module);
            Track(vm);
            if (module is ScenarioModule scenarioModule)
            {
                scenarioModule.RulesChanged += (s, e) => SaveModules();
            }

            Modules.Add(vm);
            SaveModules();
        }

        /// <summary>
        /// 배너의 수정 버튼. 시나리오는 이름만 바꾼다. 통신 서버는 같은 Id로 새로 만든 모듈로 배너를 바꿔
        /// 끼운다 - 서버 설정(IP/포트/번지)은 소켓을 열 때 한 번만 읽히므로 실행 중인 서버를 고쳐 쓸 수 없다.
        /// 옛 서버를 멈추고, 옛 서버를 보고 있던 상세 창을 닫고, 실행 중이었으면 새 서버를 다시 시작한다.
        /// </summary>
        private async System.Threading.Tasks.Task EditModule(ModuleViewModel vm)
        {
            var dialogViewModel = AddModuleDialogViewModel.ForEdit(vm.Module, _scenarioEngine, GetPlcModules);
            var dialog = new AddModuleDialog { DataContext = dialogViewModel };
            object result = await DialogHost.Show(dialog, "RootDialog");
            if (!(result is IHardwareModule edited))
            {
                return;
            }

            if (ReferenceEquals(edited, vm.Module))
            {
                vm.RefreshName();
                SaveModules();
                ModulesChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            int index = Modules.IndexOf(vm);
            if (index < 0)
            {
                edited.Dispose();
                return;
            }

            bool wasRunning = vm.IsRunning;
            vm.CloseDetailWindows();
            await vm.Module.StopAsync();
            vm.Module.Dispose();

            var replacement = new ModuleViewModel(edited);
            Track(replacement);
            Modules[index] = replacement;
            SaveModules();

            if (wasRunning)
            {
                await replacement.StartCommand.ExecuteAsync(null);
            }
        }

        private void RemoveModule(ModuleViewModel vm)
        {
            vm.Module.Dispose();
            Modules.Remove(vm);
            SaveModules();
        }

        public void SaveModules()
        {
            var records = Modules
                .Select(vm => vm.Module)
                .Select(BuildRecord)
                .Where(r => r != null)
                .ToList();

            ModuleStateStore.SaveAll(records);
        }

        private static ModuleRecord BuildRecord(IHardwareModule module)
        {
            switch (module)
            {
                case PlcServerModule plcModule:
                    return BuildPlcRecord(plcModule);
                case ScenarioModule scenarioModule:
                    return new ModuleRecord
                    {
                        Id = scenarioModule.Id,
                        Name = scenarioModule.Name,
                        Kind = ModuleKind.Scenario,
                        CreatedAt = scenarioModule.CreatedAt,
                        IsActive = scenarioModule.State == ModuleRunState.Running,
                        Rules = scenarioModule.Rules
                    };
                default:
                    return null;
            }
        }

        private static ModuleRecord BuildPlcRecord(PlcServerModule module)
        {
            object config;
            switch (module.Server)
            {
                case McPlcServer mc:
                    config = mc.Config;
                    break;
                case McTcpPlcServer mcTcp:
                    config = mcTcp.Config;
                    break;
                case OpcUaPlcServer opcUa:
                    config = opcUa.Config;
                    break;
                case AdsPlcServer ads:
                    config = ads.Config;
                    break;
                default:
                    return null;
            }

            return new ModuleRecord
            {
                Id = module.Id,
                Name = module.Name,
                Kind = ModuleKind.PlcServer,
                ProtocolType = module.Server.ProtocolType,
                CreatedAt = module.CreatedAt,
                Config = config,
                MapSnapshot = module.Server.Map.CreateSnapshot()
            };
        }

        private void RestorePersistedModules()
        {
            foreach (ModuleRecord record in ModuleStateStore.LoadAll())
            {
                try
                {
                    if (record.Kind == ModuleKind.Scenario)
                    {
                        var scenarioModule = new ScenarioModule(_scenarioEngine, GetPlcModules,
                            record.Name, record.Rules, record.Id, record.CreatedAt);
                        scenarioModule.RulesChanged += (s, e) => SaveModules();
                        Modules.Add(new ModuleViewModel(scenarioModule));
                        continue;
                    }

                    IPlcServer server = RebuildServer(record);
                    if (server == null)
                    {
                        continue;
                    }

                    ModuleSnapshotHelper.ApplySnapshot(server, record.MapSnapshot);

                    // 재시작 후에는 항상 Stopped/비활성 상태로 복원한다(요구사항: 생성이 아니라 재생 시점부터 가동).
                    var module = new PlcServerModule(server, record.Name, record.Id, record.CreatedAt);
                    Modules.Add(new ModuleViewModel(module));
                }
                catch
                {
                    // 손상되었거나 형식이 안 맞는 레코드는 건너뛴다.
                }
            }

            foreach (ModuleViewModel vm in Modules)
            {
                Track(vm);
            }
        }

        /// <summary>
        /// Wires the two things the banner reports back: its delete button, and any detail window
        /// it opens. Both hookups have to happen for every banner regardless of whether it was
        /// just added or restored from disk, so they live in one place.
        /// </summary>
        private void Track(ModuleViewModel vm)
        {
            vm.DeleteRequested += (s, e) => RemoveModule(vm);
            vm.EditRequested += async (s, e) => await EditModule(vm);
            vm.DetailWindowOpened += (s, window) => DetailWindowOpened?.Invoke(this, window);
            vm.Module.StateChanged += (s, e) => ModulesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised when the module list changes or any module starts or stops. The support
        /// shell uses it to keep the one-line summary in its command bar honest.</summary>
        public event EventHandler ModulesChanged;

        // ---- host controls ---------------------------------------------------
        //
        // What the support shell reads and calls. Modules are started and stopped from their own
        // banners; the shell only asks whether any is up, and releases the board when none is.

        /// <summary>Raised when any banner opens a detail window, so the host can close it on teardown.</summary>
        public event EventHandler<System.Windows.Window> DetailWindowOpened;

        public int RunningCount => Modules.Count(vm => vm.IsRunning);

        /// <summary>Releases every module. Saves first, so runtime value changes survive a restart.</summary>
        public async System.Threading.Tasks.Task DisposeAllAsync()
        {
            SaveModules();
            foreach (ModuleViewModel vm in Modules.ToList())
            {
                await vm.Module.StopAsync();
                vm.Module.Dispose();
            }

            // Last, and not in a finally: the engine's running timer is what holds this whole board
            // in memory, but if a module failed to stop the board is kept and reused, and its
            // scenarios still need the timer.
            _scenarioEngine.Dispose();
        }

        private static readonly JsonSerializer ConfigSerializer = JsonSerializer.Create(
            new JsonSerializerSettings { Converters = { new IPAddressJsonConverter() } });

        private static IPlcServer RebuildServer(ModuleRecord record)
        {
            var configObject = record.Config as JObject;
            if (configObject == null)
            {
                return null;
            }

            switch (record.ProtocolType)
            {
                case ProtocolType.Mc:
                    return new McPlcServer(configObject.ToObject<McServerConfig>(ConfigSerializer));
                case ProtocolType.McTcp:
                    return new McTcpPlcServer(configObject.ToObject<McTcpServerConfig>(ConfigSerializer));
                case ProtocolType.OpcUa:
                    return new OpcUaPlcServer(configObject.ToObject<OpcUaServerConfig>(ConfigSerializer));
                case ProtocolType.Ads:
                    return new AdsPlcServer(configObject.ToObject<AdsServerConfig>(ConfigSerializer));
                default:
                    return null;
            }
        }
    }
}
