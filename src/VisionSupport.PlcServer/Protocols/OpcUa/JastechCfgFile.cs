using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.OpcUa
{
    /// <summary>
    /// Jastech.Framework.Opcua 의 OpcuaMemoryMap 설정 파일(.cfg, 내용은 JSON)을 읽고 쓴다.
    ///
    /// 그쪽 클래스를 직접 참조하지는 않는다 - 그 어셈블리는 .NET Framework 4.8.1용이라 이 앱(.NET 9)에서
    /// 그대로 쓸 수 없고, 어차피 필요한 건 "어떤 이름의 노드를 어떤 타입으로 만들지" 뿐이라 텍스트만 읽는다.
    ///
    /// NodeType 숫자의 의미는 추측이 아니라 실제 소스에서 확인한 값이다
    /// (Jastech.Framework.Opcua/Configs/OpcuaMemoryMapConfig.cs 의 OpcuaNodeVariableType 선언 순서).
    /// </summary>
    public static class JastechCfgFile
    {
        /// <summary>OpcuaNodeVariableType 선언 순서 그대로. null은 값 노드로 만들 수 없는 항목(NONE).</summary>
        private static readonly PlcDataType?[] VariableTypes =
        {
            PlcDataType.Bool,    // 0 BIT
            PlcDataType.SByte,   // 1 SBYTE
            PlcDataType.Byte,    // 2 BYTE
            PlcDataType.Int16,   // 3 INT16
            PlcDataType.UInt16,  // 4 UINT16
            PlcDataType.Int32,   // 5 INT32
            PlcDataType.UInt32,  // 6 UINT32
            PlcDataType.Float,   // 7 FLOAT
            PlcDataType.Double,  // 8 DOUBLE
            PlcDataType.String,  // 9 STRING
            null,                // 10 NONE
        };

        private static readonly string[] VariableTypeNames =
        {
            "BIT", "SBYTE", "BYTE", "INT16", "UINT16", "INT32", "UINT32", "FLOAT", "DOUBLE", "STRING", "NONE"
        };

        /// <summary>이 서버가 노드를 올리는 네임스페이스 인덱스. 내보낼 때 NodeId 앞에 붙인다.</summary>
        private const int ExportNamespaceIndex = 2;

        /// <summary>
        /// 설정 파일을 읽어 만들 노드 목록을 돌려준다. 이 포맷에는 배열 길이가 없으므로
        /// <see cref="CfgNodeInfo.ArrayLength"/>는 "제안값"일 뿐이고, 서버를 만들기 전에 사용자가 확정해야 한다.
        /// </summary>
        public static List<CfgNodeInfo> Load(string path)
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            if (!(root["NodeInfos"] is JObject nodeInfos))
            {
                throw new FormatException("\"NodeInfos\" 객체가 없습니다 - OpcuaMemoryMap 설정 파일이 아닌 것 같습니다.");
            }

            var result = new List<CfgNodeInfo>();
            foreach (KeyValuePair<string, JToken> entry in nodeInfos)
            {
                if (!(entry.Value is JObject info))
                {
                    continue;
                }

                CfgNodeInfo node = ParseNode(entry.Key, info);
                if (node != null)
                {
                    result.Add(node);
                }
            }

            return result;
        }

        /// <summary>현재 맵의 노드들을 같은 포맷으로 저장한다.</summary>
        public static void Save(string path, IEnumerable<NodeDefinition> nodes)
        {
            var nodeInfos = new JObject();
            foreach (NodeDefinition node in nodes)
            {
                nodeInfos[node.Name] = new JObject
                {
                    ["$type"] = "Jastech.Framework.Opcua.Configs.OpcuaNodeMemoryInfo, Jastech.Framework.Opcua",
                    ["NodeId"] = "ns=" + ExportNamespaceIndex + ";s=" + node.Name,
                    ["ClassType"] = 1, // OpcuaNodeClassType.Variable
                    ["NodeType"] = (int)ToVariableType(node.DataType),
                    ["ValueRank"] = node.IsArray ? 1 : -1,
                    ["UseSubscribe"] = false,
                };
            }

            var root = new JObject
            {
                ["$type"] = "Jastech.Framework.Opcua.Configs.OpcuaMemoryMap, Jastech.Framework.Opcua",
                ["NodeInfos"] = new JObject
                {
                    ["$type"] = "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],"
                                + "[Jastech.Framework.Opcua.Configs.OpcuaNodeMemoryInfo, Jastech.Framework.Opcua]], mscorlib",
                },
            };

            // "$type"이 딕셔너리의 첫 속성이어야 해서 나머지를 뒤에 이어 붙인다.
            var target = (JObject)root["NodeInfos"];
            foreach (KeyValuePair<string, JToken> pair in nodeInfos)
            {
                target[pair.Key] = pair.Value;
            }

            File.WriteAllText(path, root.ToString(Formatting.Indented));
        }

        private static CfgNodeInfo ParseNode(string key, JObject info)
        {
            // ClassType이 Variable(1)이 아닌 항목(폴더 등)은 값 노드가 아니므로 건너뛴다.
            if (TryReadEnum(info["ClassType"], new[] { "OBJECT", "VARIABLE" }, out int classType) && classType != 1)
            {
                return null;
            }

            string nodeId = info["NodeId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new FormatException("\"" + key + "\" 항목에 NodeId가 없습니다.");
            }

            if (!TryReadEnum(info["NodeType"], VariableTypeNames, out int nodeType) ||
                nodeType < 0 || nodeType >= VariableTypes.Length)
            {
                throw new FormatException("\"" + key + "\" 항목의 NodeType(" + info["NodeType"] + ")을 해석할 수 없습니다.");
            }

            PlcDataType? dataType = VariableTypes[nodeType];
            if (dataType == null)
            {
                // NONE - 값 타입이 없는 항목이라 노드로 만들지 않는다.
                return null;
            }

            string name = ExtractIdentifier(nodeId);
            bool isArray = info["ValueRank"]?.Value<int>() == 1;

            return new CfgNodeInfo
            {
                Key = key,
                Name = name,
                DataType = dataType.Value,
                IsArray = isArray,
                ArrayLength = isArray ? SuggestArrayLength(name) : 1,
            };
        }

        /// <summary>NodeId는 "ns=2;s=VPC1_COMMON_V_READY" 꼴이다. 이 서버는 자기 네임스페이스 인덱스로
        /// 노드를 올리므로 파일에 적힌 ns 번호는 무시하고 "s=" 뒤 식별자만 쓴다.</summary>
        private static string ExtractIdentifier(string nodeId)
        {
            // "s="를 그냥 IndexOf로 찾으면 앞의 "n(s=)2"가 먼저 걸려서 "2;s=TAG"가 나온다.
            // 세미콜론으로 끊어서 "s="로 시작하는 조각만 골라야 한다.
            foreach (string part in nodeId.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("s=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(2).Trim();
                }
            }

            return nodeId.Trim();
        }

        /// <summary>숫자로 적혀 있으면 그대로, 이름("INT16" 등)으로 적혀 있으면 순서를 찾아 숫자로 바꾼다.</summary>
        private static bool TryReadEnum(JToken token, string[] names, out int value)
        {
            value = -1;
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Integer)
            {
                value = token.Value<int>();
                return true;
            }

            string text = token.Value<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    value = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>파일에 배열 길이가 없어서 쓰는 제안값. 이미 알고 있는 태그는 JastechDefaultNodes가
        /// 쓰는 값과 같게 맞추고, 처음 보는 태그는 1로 두고 사용자가 확정하게 한다.</summary>
        private static int SuggestArrayLength(string name)
        {
            if (name.EndsWith("_TIMESYNC", StringComparison.OrdinalIgnoreCase))
            {
                return JastechDefaultNodes.TimeSyncArrayLength;
            }

            if (name.EndsWith("_ALIGN", StringComparison.OrdinalIgnoreCase))
            {
                return JastechDefaultNodes.AlignArrayLength;
            }

            if (name.EndsWith("_SERVO_POSITION", StringComparison.OrdinalIgnoreCase))
            {
                return JastechDefaultNodes.ServoPositionArrayLength;
            }

            return 1;
        }

        private static int ToVariableType(PlcDataType dataType)
        {
            for (int i = 0; i < VariableTypes.Length; i++)
            {
                if (VariableTypes[i] == dataType)
                {
                    return i;
                }
            }

            return VariableTypes.Length - 1; // NONE
        }
    }

    /// <summary>설정 파일에서 읽어낸 노드 한 개. 배열 길이는 파일에 없어서 사용자가 확정한다.</summary>
    public sealed class CfgNodeInfo
    {
        /// <summary>설정 파일의 딕셔너리 키(예: MASTER_VISION_READY). 화면에 보여주기 위한 값이고
        /// 실제 OPC UA 노드 이름은 <see cref="Name"/>이다.</summary>
        public string Key { get; set; }

        public string Name { get; set; }

        public PlcDataType DataType { get; set; }

        public bool IsArray { get; set; }

        public int ArrayLength { get; set; }

        public NodeDefinition ToDefinition()
        {
            int length = Math.Max(1, ArrayLength);
            object value = IsArray
                ? Array.CreateInstance(NodeDefinition.ClrElementType(DataType), length)
                : NodeDefinition.DefaultElementValue(DataType);

            return new NodeDefinition(Name, DataType, IsArray, length, value);
        }
    }
}
