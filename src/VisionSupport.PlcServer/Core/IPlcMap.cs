using System;

namespace VirtualPlcServer.Core
{
    public sealed class MapValueChangedEventArgs : EventArgs
    {
        public MapValueChangedEventArgs(string key, object value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public object Value { get; }
    }

    /// <summary>
    /// 프로토콜별 데이터 영역(D영역 워드 배열 또는 노드 테이블)을 표현하는 공통 계약.
    /// MC는 주소 기반, OPC UA/ADS는 이름 기반 노드 테이블을 이 인터페이스로 노출한다.
    /// </summary>
    public interface IPlcMap
    {
        event EventHandler<MapValueChangedEventArgs> ValueChanged;

        /// <summary>JSON 직렬화 가능한 스냅샷 객체를 생성한다.</summary>
        object CreateSnapshot();

        /// <summary>저장된 스냅샷(역직렬화된 JObject/전용 타입)으로 맵 상태를 복원한다.</summary>
        void RestoreSnapshot(object snapshot);
    }
}
