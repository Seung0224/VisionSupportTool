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
        public McWordRowViewModel(int address, short value, string devicePrefix)
        {
            Address = address;
            DevicePrefix = devicePrefix;
            this.value = value;
        }

        public int Address { get; }

        private string DevicePrefix { get; }

        public string AddressDisplay => DevicePrefix + Address;

        /// <summary>
        /// 워드는 16비트 그대로지만 D영역 값은 관례상 부호 있는 정수로 읽는다 - -1을 65535로 보여주면
        /// 래더에서 보는 값과 달라진다. 저장(McMap)은 ushort 그대로 두고 화면 표시와 입력만 short로
        /// 해석한다. 둘 다 16비트라 unchecked 캐스트는 비트 패턴을 그대로 두고 해석만 바꾼다.
        /// </summary>
        [ObservableProperty]
        private short value;
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

        public void WriteValue(int address, short value)
        {
            _map.WriteWord(address, unchecked((ushort)value));
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
                Rows.Add(new McWordRowViewModel(_map.StartAddress + i, unchecked((short)values[i]), _map.DevicePrefix));
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
                        row.Value = unchecked((short)(ushort)e.Value);
                        break;
                    }
                }
            });
        }
    }
}
