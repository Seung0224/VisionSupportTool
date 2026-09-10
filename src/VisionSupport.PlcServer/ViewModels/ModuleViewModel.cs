using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontAwesome.Sharp;
using VirtualPlcServer.Modules;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>모듈 배너 하나를 위한 뷰모델. IHardwareModule을 감싸서 바인딩 가능한 형태로 노출한다.</summary>
    public partial class ModuleViewModel : ObservableObject
    {
        public ModuleViewModel(IHardwareModule module)
        {
            Module = module;
            state = module.State;
            module.StateChanged += OnModuleStateChanged;
        }

        public IHardwareModule Module { get; }

        public string Name => Module.Name;

        public string ModuleTypeName => Module.ModuleTypeName;

        public string CreatedAtDisplay => Module.CreatedAt.ToString("yyyy-MM-dd HH:mm");

        public bool IsScenario => Module is ScenarioModule;

        public IconChar TypeIcon => IsScenario ? IconChar.Sitemap : IconChar.NetworkWired;

        public string SummaryInfo => Module.SummaryInfo;

        [ObservableProperty]
        private ModuleRunState state;

        public bool IsRunning => State == ModuleRunState.Running || State == ModuleRunState.Starting;

        /// <summary>시나리오 배너의 체크박스가 바인딩하는 활성/비활성 스위치. 통신 서버의 재생/정지 버튼과
        /// 동일한 Start/StopAsync를 재사용하되, 체크박스 하나로 켜고 끌 수 있게 감싼다.</summary>
        public bool IsActive
        {
            get => IsRunning;
            set
            {
                if (value)
                {
                    StartCommand.Execute(null);
                }
                else
                {
                    StopCommand.Execute(null);
                }
            }
        }

        public event EventHandler DeleteRequested;

        /// <summary>
        /// Raised when this banner opens its detail window. The support shell listens so that
        /// stopping the feature also closes the window - one left open would keep polling a
        /// server that no longer exists.
        /// </summary>
        public event EventHandler<Window> DetailWindowOpened;

        [RelayCommand]
        private async Task Start()
        {
            try
            {
                await Module.StartAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start module:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task Stop()
        {
            await Module.StopAsync();
        }

        [RelayCommand]
        private async Task Reinit()
        {
            await Module.ReinitializeAsync();
        }

        [RelayCommand]
        private void View()
        {
            Window window = Module.CreateDetailWindow();
            DetailWindowOpened?.Invoke(this, window);
            window.Show();
        }

        [RelayCommand]
        private void Delete()
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnModuleStateChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                State = Module.State;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(SummaryInfo));
            });
        }
    }
}
