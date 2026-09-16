using System;
using System.Collections.Generic;
using System.Globalization;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Ads;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Protocols.OpcUa;

namespace VirtualPlcServer.Scenarios
{
    /// <summary>
    /// McMap(주소 기반, 항상 숫자)과 NodeMap(이름 기반, 불리언/숫자/문자열/배열)의 차이를 감추고,
    /// 시나리오 엔진이 프로토콜을 신경 쓰지 않고 값을 읽고 쓸 수 있게 해주는 어댑터.
    /// </summary>
    public static class PlcTargetAccessor
    {
        public static bool IsNodeBased(PlcServerModule module)
        {
            return module?.Server is OpcUaPlcServer || module?.Server is AdsPlcServer;
        }

        /// <summary>OPC UA/ADS 모듈이 갖고 있는 노드 전체 목록(이름/타입 선택용). MC 모듈이면 빈 목록.</summary>
        public static List<NodeDefinition> GetNodeDefinitions(PlcServerModule module)
        {
            return TryGetNodeMap(module, out NodeMap nodeMap) ? nodeMap.GetAllNodes() : new List<NodeDefinition>();
        }

        public static bool TryGetNodeDefinition(PlcServerModule module, string address, out NodeDefinition definition)
        {
            if (TryGetNodeMap(module, out NodeMap nodeMap) && !string.IsNullOrWhiteSpace(address))
            {
                return nodeMap.TryGetNode(address.Trim(), out definition);
            }

            definition = null;
            return false;
        }

        /// <summary>대상 값을 읽는다. 배열 노드 + ArrayIndex 지정 시 그 원소 하나만 돌려준다.
        /// MC는 항상 short(부호 있는 16비트 - WriteRaw가 담는 2의 보수를 그대로 되돌린 값),
        /// NodeMap 쪽은 bool/short/int/float/double/string 중 하나.</summary>
        public static object ReadRaw(PlcServerModule module, TargetRef target)
        {
            if (TryGetMcMap(module, out McMap mcMap))
            {
                int mcAddress = ParseMcAddress(target.Address);
                return mcMap.IsInRange(mcAddress, 1) ? (object)unchecked((short)mcMap.ReadWord(mcAddress)) : null;
            }

            if (TryGetNodeMap(module, out NodeMap nodeMap) && nodeMap.TryGetNode(target.Address.Trim(), out NodeDefinition definition))
            {
                if (target.ArrayIndex.HasValue && definition.Value is Array array &&
                    target.ArrayIndex.Value >= 0 && target.ArrayIndex.Value < array.Length)
                {
                    return array.GetValue(target.ArrayIndex.Value);
                }

                return definition.Value;
            }

            return null;
        }

        /// <summary>대상에 값을 쓴다. value는 string(문자열 노드용) 또는 숫자류(그 외 전부)면 되고,
        /// 실제 대상 타입에 맞춰 이 메서드가 알아서 변환한다. 배열 노드 + ArrayIndex 지정 시 그 원소만 바꾼다.</summary>
        public static void WriteRaw(PlcServerModule module, TargetRef target, object value)
        {
            if (TryGetMcMap(module, out McMap mcMap))
            {
                int mcAddress = ParseMcAddress(target.Address);
                if (!mcMap.IsInRange(mcAddress, 1))
                {
                    return;
                }

                // MC 워드는 16비트라 음수는 2의 보수로 담는다(-1033 -> 64503 등).
                ushort word = unchecked((ushort)(short)Math.Round(ToDouble(value), MidpointRounding.AwayFromZero));
                mcMap.WriteWord(mcAddress, word);
                return;
            }

            if (!TryGetNodeMap(module, out NodeMap nodeMap))
            {
                return;
            }

            string name = target.Address.Trim();
            if (!nodeMap.TryGetNode(name, out NodeDefinition definition))
            {
                return;
            }

            if (target.ArrayIndex.HasValue && definition.Value is Array array &&
                target.ArrayIndex.Value >= 0 && target.ArrayIndex.Value < array.Length)
            {
                array.SetValue(CoerceElement(definition.DataType, value), target.ArrayIndex.Value);
                nodeMap.SetValue(name, array);
                return;
            }

            nodeMap.SetValue(name, CoerceElement(definition.DataType, value));
        }

        /// <summary>"D30126", "R25000", "d30126", "30126" 모두 허용한다 - McTcp(LS) 모듈은 디바이스가
        /// D/R/W 중 하나일 수 있어서, 맨 앞 글자가 숫자가 아니면(어떤 디바이스 문자든) 하나만 잘라낸다.</summary>
        public static int ParseMcAddress(string address)
        {
            string trimmed = (address ?? string.Empty).Trim();
            if (trimmed.Length > 0 && !char.IsDigit(trimmed[0]))
            {
                trimmed = trimmed.Substring(1);
            }

            return int.Parse(trimmed, CultureInfo.InvariantCulture);
        }

        /// <summary>McPlcServer(UDP)/McTcpPlcServer(TCP·LS) 둘 다 결국 McMap 기반이라 같은 방식으로 다룬다.</summary>
        private static bool TryGetMcMap(PlcServerModule module, out McMap mcMap)
        {
            switch (module?.Server)
            {
                case McPlcServer mc:
                    mcMap = mc.McMap;
                    return true;
                case McTcpPlcServer mcTcp:
                    mcMap = mcTcp.McMap;
                    return true;
                default:
                    mcMap = null;
                    return false;
            }
        }

        private static bool TryGetNodeMap(PlcServerModule module, out NodeMap nodeMap)
        {
            switch (module?.Server)
            {
                case OpcUaPlcServer opcUa:
                    nodeMap = opcUa.NodeMap;
                    return true;
                case AdsPlcServer ads:
                    nodeMap = ads.NodeMap;
                    return true;
                default:
                    nodeMap = null;
                    return false;
            }
        }

        private static object CoerceElement(PlcDataType dataType, object value)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return value is bool b ? b : ToDouble(value) != 0;
                case PlcDataType.Int16: return (short)ToDouble(value);
                case PlcDataType.Int32: return (int)ToDouble(value);
                case PlcDataType.Float: return (float)ToDouble(value);
                case PlcDataType.Double: return ToDouble(value);
                case PlcDataType.SByte: return (sbyte)ToDouble(value);
                case PlcDataType.Byte: return (byte)ToDouble(value);
                case PlcDataType.UInt16: return (ushort)ToDouble(value);
                case PlcDataType.UInt32: return (uint)ToDouble(value);
                default: return value?.ToString() ?? string.Empty;
            }
        }

        public static double ToDouble(object value)
        {
            switch (value)
            {
                case null: return 0;
                case bool b: return b ? 1 : 0;
                case short s: return s;
                case int i: return i;
                case float f: return f;
                case double d: return d;
                // ushort는 이 코드베이스에서 McMap의 워드값(WriteWord/ReadWord, ValueChanged 이벤트)에서만
                // 나온다 - WriteRaw가 음수를 2의 보수로 담으므로(-2001 -> 63535), 여기서도 부호 있는
                // 16비트로 되돌려야(63535 -> -2001) 시나리오 트리거 비교("값 < 0" 등)와 액션에 쓰는
                // 값이 실제 부호와 일치한다.
                case ushort us: return unchecked((short)us);
                // OPC UA/ADS 노드에서만 나오는 타입들 - 부호 재해석 없이 값 그대로 쓴다.
                case sbyte sb: return sb;
                case byte by: return by;
                case uint ui: return ui;
                case string str:
                    return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
                default:
                    return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fallback) ? fallback : 0;
            }
        }
    }
}
