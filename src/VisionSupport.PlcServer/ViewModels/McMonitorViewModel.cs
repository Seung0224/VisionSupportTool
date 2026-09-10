using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Mc;

namespace VirtualPlcServer.ViewModels
{
    public sealed partial class McWordRowViewModel : ObservableObject
    {
        public McWordRowViewModel(int address, ushort value, string devicePrefix)
        {
            Address = address;
            DevicePrefix = devicePrefix;
            this.value = value;
        }

        public int Address { get; }

        private string DevicePrefix { get; }

        public string AddressDisplay => DevicePrefix + Address;

        [ObservableProperty]
        private ushort value;
    }

    /// <summary>McPlcServer(UDP)와 McTcpPlcServer(TCP/LS) 둘 다 이 뷰를 그대로 재사용할 수 있도록,
    /// 구체 서버 타입이 아니라 McMap만 받는다 - 둘 다 결국 McMap 기반이라 화면 표시 로직은 동일하다.</summary>
    public partial class McMonitorViewModel : ObservableObject
    {
        private readonly McMap _map;

        public McMonitorViewModel(McMap map, string headerInfo)
        {
            _map = map;
            HeaderInfo = headerInfo;
            Rows = new ObservableCollection<McWordRowViewModel>();
            LoadAll();
            _map.ValueChanged += OnValueChanged;
        }

        public ObservableCollection<McWordRowViewModel> Rows { get; }

        public string HeaderInfo { get; }

        public void WriteValue(int address, ushort value)
        {
            _map.WriteWord(address, value);
        }

        public void Detach()
        {
            _map.ValueChanged -= OnValueChanged;
        }

        private void LoadAll()
        {
            Rows.Clear();
            ushort[] values = _map.Snapshot();
            for (int i = 0; i < values.Length; i++)
            {
                Rows.Add(new McWordRowViewModel(_map.StartAddress + i, values[i], _map.DevicePrefix));
            }
        }

        private void OnValueChanged(object sender, MapValueChangedEventArgs e)
        {
            if (!e.Key.StartsWith(_map.DevicePrefix) ||
                !int.TryParse(e.Key.Substring(_map.DevicePrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int address))
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (McWordRowViewModel row in Rows)
                {
                    if (row.Address == address)
                    {
                        row.Value = (ushort)e.Value;
                        break;
                    }
                }
            });
        }
    }
}
