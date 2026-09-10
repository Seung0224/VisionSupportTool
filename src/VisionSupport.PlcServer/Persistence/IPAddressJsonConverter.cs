using System;
using System.Net;
using Newtonsoft.Json;

namespace VirtualPlcServer.Persistence
{
    /// <summary>
    /// Newtonsoft.Json이 IPAddress.ScopeId를 읽으려다 SocketException을 던지는 문제(IPv4 주소는 ScopeId를 지원하지 않음)를
    /// 피하기 위해, IPAddress를 문자열로만 직렬화/역직렬화한다.
    /// </summary>
    public sealed class IPAddressJsonConverter : JsonConverter
    {
        /// <summary>
        /// 하위 타입까지 받아야 한다. .NET Framework에서는 IPAddress.Any가 그냥 IPAddress였지만,
        /// .NET Core 이후로는 내부 하위 클래스 IPAddress+ReadOnlyIPAddress다. 정확한 타입 비교로는
        /// 이 컨버터가 선택되지 않아 Newtonsoft가 리플렉션 경로로 떨어지고, 거기서 ScopeId를 읽다가
        /// 위 주석의 SocketException이 그대로 터진다 - 모듈 저장이 통째로 실패한 원인이었다.
        /// </summary>
        public override bool CanConvert(Type objectType) => typeof(IPAddress).IsAssignableFrom(objectType);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((IPAddress)value)?.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            string text = reader.Value as string;
            return string.IsNullOrEmpty(text) ? null : IPAddress.Parse(text);
        }
    }
}
