using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.OpcUa;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Jastech OpcuaMemoryMap 설정 파일(.cfg)을 읽고 쓰는 부분. NodeType 숫자의 의미는 실제 소스
/// (Jastech.Framework.Opcua의 OpcuaNodeVariableType: BIT=0, INT16=3, INT32=5, STRING=9)에서 확인한 값이라,
/// 이 매핑이 틀어지면 노드 타입이 통째로 잘못 만들어진다 - 그래서 대표 타입들을 고정해 둔다.
/// </summary>
public class JastechCfgFileTests
{
    private const string Sample = @"{
  ""$type"": ""Jastech.Framework.Opcua.Configs.OpcuaMemoryMap, Jastech.Framework.Opcua"",
  ""NodeInfos"": {
    ""MASTER_VISION_READY"": { ""NodeId"": ""ns=2;s=VPC1_COMMON_V_READY"", ""ClassType"": 1, ""NodeType"": 0, ""ValueRank"": -1, ""UseSubscribe"": false },
    ""MASTER_PLC_TIMESYNC"": { ""NodeId"": ""ns=2;s=VPC1_COMMON_P_TIMESYNC"", ""ClassType"": 1, ""NodeType"": 3, ""ValueRank"": 1, ""UseSubscribe"": false },
    ""UNIT0_PLC_SERVO_POSITION"": { ""NodeId"": ""ns=2;s=VPC1_UNIT01_P_SERVO_POSITION"", ""ClassType"": 1, ""NodeType"": 5, ""ValueRank"": 1, ""UseSubscribe"": false },
    ""UNIT0_PLC_CELL_ID"": { ""NodeId"": ""ns=2;s=VPC1_UNIT01_P_CELLID"", ""ClassType"": 1, ""NodeType"": 9, ""ValueRank"": -1, ""UseSubscribe"": false }
  }
}";

    [Fact]
    public void Reads_node_names_and_types_from_the_real_format()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "map.cfg");
        File.WriteAllText(path, Sample);

        List<CfgNodeInfo> nodes = JastechCfgFile.Load(path);

        Assert.Equal(4, nodes.Count);

        // NodeId의 "ns=2;s=" 접두어는 떼고 태그만 노드 이름이 된다.
        CfgNodeInfo ready = nodes.Single(n => n.Name == "VPC1_COMMON_V_READY");
        Assert.Equal(PlcDataType.Bool, ready.DataType);
        Assert.False(ready.IsArray);

        CfgNodeInfo timeSync = nodes.Single(n => n.Name == "VPC1_COMMON_P_TIMESYNC");
        Assert.Equal(PlcDataType.Int16, timeSync.DataType);
        Assert.True(timeSync.IsArray);

        Assert.Equal(PlcDataType.Int32, nodes.Single(n => n.Name == "VPC1_UNIT01_P_SERVO_POSITION").DataType);
        Assert.Equal(PlcDataType.String, nodes.Single(n => n.Name == "VPC1_UNIT01_P_CELLID").DataType);
    }

    [Fact]
    public void Array_nodes_come_back_as_arrays_after_a_save()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "out.cfg");

        var definitions = new List<NodeDefinition>
        {
            new NodeDefinition("A_BOOL", PlcDataType.Bool, false, 1, false),
            new NodeDefinition("A_WORDS", PlcDataType.Int16, true, 6, new short[6]),
            new NodeDefinition("A_TEXT", PlcDataType.String, false, 1, string.Empty),
        };

        JastechCfgFile.Save(path, definitions);
        List<CfgNodeInfo> loaded = JastechCfgFile.Load(path);

        Assert.Equal(3, loaded.Count);
        Assert.Equal(PlcDataType.Bool, loaded.Single(n => n.Name == "A_BOOL").DataType);
        Assert.Equal(PlcDataType.String, loaded.Single(n => n.Name == "A_TEXT").DataType);

        // 길이는 이 포맷이 담지 못한다 - 배열이라는 사실만 살아남고, 길이는 사용자가 다시 확정한다.
        Assert.True(loaded.Single(n => n.Name == "A_WORDS").IsArray);
    }
}
