using System;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Mc
{
    /// <summary>
    /// MC(Mitsubishi) D영역을 표현하는 워드 배열 기반 맵.
    /// 주소는 StartAddress를 기준으로 한 D디바이스 절대 번지이다.
    /// </summary>
    public sealed class McMap : IPlcMap
    {
        private readonly object _lock = new object();
        private ushort[] _words;

        public McMap(int startAddress, int size, string devicePrefix = "D")
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            StartAddress = startAddress;
            Size = size;
            DevicePrefix = string.IsNullOrEmpty(devicePrefix) ? "D" : devicePrefix;
            _words = new ushort[size];
        }

        /// <summary>주소 표시/이벤트 키에 붙는 디바이스 문자(D, R, W 등). 기존 UDP MC 서버는 항상 "D"이고,
        /// TCP(LS) 변형은 실제 대상 클라이언트가 쓰는 디바이스(기본 "R")로 다르게 표시하기 위해 추가했다.</summary>
        public string DevicePrefix { get; private set; }

        public int StartAddress { get; private set; }

        public int Size { get; private set; }

        public event EventHandler<MapValueChangedEventArgs> ValueChanged;

        public bool IsInRange(int address, int count)
        {
            return address >= StartAddress && (address + count) <= (StartAddress + Size);
        }

        public ushort[] ReadWords(int address, int count)
        {
            lock (_lock)
            {
                var result = new ushort[count];
                Array.Copy(_words, address - StartAddress, result, 0, count);
                return result;
            }
        }

        public ushort ReadWord(int address)
        {
            lock (_lock)
            {
                return _words[address - StartAddress];
            }
        }

        public void WriteWord(int address, ushort value)
        {
            lock (_lock)
            {
                _words[address - StartAddress] = value;
            }

            ValueChanged?.Invoke(this, new MapValueChangedEventArgs(DevicePrefix + address, value));
        }

        public void WriteWords(int address, ushort[] values)
        {
            lock (_lock)
            {
                Array.Copy(values, 0, _words, address - StartAddress, values.Length);
            }

            for (int i = 0; i < values.Length; i++)
            {
                ValueChanged?.Invoke(this, new MapValueChangedEventArgs(DevicePrefix + (address + i), values[i]));
            }
        }

        public ushort[] Snapshot()
        {
            lock (_lock)
            {
                var copy = new ushort[_words.Length];
                Array.Copy(_words, copy, _words.Length);
                return copy;
            }
        }

        public object CreateSnapshot()
        {
            return new McMapSnapshot
            {
                StartAddress = StartAddress,
                Size = Size,
                Values = Snapshot()
            };
        }

        public void RestoreSnapshot(object snapshot)
        {
            if (!(snapshot is McMapSnapshot mcSnapshot) || mcSnapshot.Values == null)
            {
                return;
            }

            // 주소 기준으로 겹치는 구간만 복사한다 (시작 번지/크기가 바뀐 상태로 재오픈해도 안전하게 이관됨).
            lock (_lock)
            {
                int overlapStart = Math.Max(StartAddress, mcSnapshot.StartAddress);
                int overlapEndExclusive = Math.Min(StartAddress + Size, mcSnapshot.StartAddress + mcSnapshot.Values.Length);

                for (int address = overlapStart; address < overlapEndExclusive; address++)
                {
                    _words[address - StartAddress] = mcSnapshot.Values[address - mcSnapshot.StartAddress];
                }
            }

            for (int i = 0; i < _words.Length; i++)
            {
                ValueChanged?.Invoke(this, new MapValueChangedEventArgs(DevicePrefix + (StartAddress + i), _words[i]));
            }
        }
    }
}
