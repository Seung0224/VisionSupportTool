using System.Collections.Generic;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.OpcUa
{
    /// <summary>
    /// Jastech 3DVision(COG) 클라이언트가 시작 시 자동 등록하는 OPC UA 노드 목록을 그대로 재현한다.
    /// 출처: D:\3DVISION\...\Source\Apps\COG\Core\Services\StartupService.cs (InitializeAutoRun 내 defaultValue 등록부)
    ///       및 D:\3DVISION\...\Source\Core\Jastech.Framework.Opcua\Configs\OpcuaMemoryMapConfig.cs (OpcuaNodeMemoryInfo)
    ///
    /// 클라이언트는 NodeId를 "ns={nsID};s=VPC{PCID+1}_{UNIT|COMMON}_{태그}" 형태의 문자열로 구성해서 접속한다.
    /// 이 서버는 커스텀 네임스페이스가 항상 인덱스 2에 배정되는 것을 실행 검증했으므로(OPC UA .NET 표준 스택 기준:
    /// 0=OPC UA 표준, 1=ApplicationUri, 2=최초 커스텀 네임스페이스), 노드 이름에 "ns=2;s=" 접두어 없이 아래
    /// 태그 문자열("VPC1_COMMON_V_READY" 등)만 그대로 등록하면 클라이언트가 기대하는 ns=2;s=... 와 정확히 일치한다.
    ///
    /// 주의: 클라이언트 소스에는 배열 노드(P_TIMESYNC/V_ALIGN/P_SERVO_POSITION)의 정확한 배열 길이가 명시돼 있지
    /// 않다(OpcuaNodeMemoryInfo는 ValueRank=1이라는 "배열 여부"만 가지고 있고 길이는 없음). 아래 기본 길이는
    /// 추정값이며, 실제 값과 다르면 이 파일의 상수만 바꾸면 된다. 배열 길이 자체는 서버-클라이언트 간 강제되지
    /// 않으므로(ArrayDimensions 미설정), 클라이언트가 다른 길이로 쓰면 그 길이로 그대로 반영된다.
    /// </summary>
    public static class JastechDefaultNodes
    {
        // internal인 이유: JastechCfgFile이 설정 파일을 읽을 때 같은 추정값을 배열 길이 기본값으로
        // 재사용한다(그 파일 포맷에도 배열 길이가 없다 - 위 주석 참고).
        internal const int TimeSyncArrayLength = 6;      // 추정값 (예: 년/월/일/시/분/초)
        internal const int AlignArrayLength = 3;          // 추정값 (예: X/Y/Theta)
        internal const int ServoPositionArrayLength = 3;  // 추정값 (예: X/Y/Z)

        /// <param name="pcId">0-based PC 번호. VPC{pcId+1}로 변환된다 (기본값 0 -> VPC1).</param>
        /// <param name="unitCount">AutoRun2DCount에 해당하는 유닛 개수. 기본값 1 (실제 COG 앱의 기본 설정값).</param>
        public static List<NodeDefinition> GetDefaultNodes(int pcId = 0, int unitCount = 1)
        {
            string pc = "VPC" + (pcId + 1);
            var nodes = new List<NodeDefinition>();

            // ---- Common (Master, unitId = -1) ----
            // Vision -> PLC
            nodes.Add(new NodeDefinition(pc + "_COMMON_V_READY", PlcDataType.Bool, false, 1, false));
            nodes.Add(new NodeDefinition(pc + "_COMMON_V_MODEL_NO", PlcDataType.Int16, false, 1, (short)0));
            nodes.Add(new NodeDefinition(pc + "_COMMON_V_STATUS", PlcDataType.Int16, false, 1, (short)0));
            nodes.Add(new NodeDefinition(pc + "_COMMON_V_ALIVE", PlcDataType.Bool, false, 1, false));

            // PLC -> Vision
            nodes.Add(new NodeDefinition(pc + "_COMMON_P_READY", PlcDataType.Bool, false, 1, false));
            nodes.Add(new NodeDefinition(pc + "_COMMON_P_MODEL_CHANGE_NO", PlcDataType.Int16, false, 1, (short)0));
            nodes.Add(new NodeDefinition(pc + "_COMMON_P_COMMAND", PlcDataType.Int16, false, 1, (short)0));
            nodes.Add(new NodeDefinition(pc + "_COMMON_P_TIMESYNC", PlcDataType.Int16, true, TimeSyncArrayLength, new short[TimeSyncArrayLength]));
            nodes.Add(new NodeDefinition(pc + "_COMMON_P_ALIVE", PlcDataType.Bool, false, 1, false));

            // ---- Unit별 (unitId = 0..unitCount-1 -> UNIT01, UNIT02, ...) ----
            for (int i = 0; i < unitCount; i++)
            {
                string unit = pc + "_UNIT" + (i + 1).ToString("D2");

                // Vision -> PLC
                nodes.Add(new NodeDefinition(unit + "_V_STATUS", PlcDataType.Int16, false, 1, (short)0));

                // PLC -> Vision
                nodes.Add(new NodeDefinition(unit + "_P_COMMAND", PlcDataType.Int16, false, 1, (short)0));
                nodes.Add(new NodeDefinition(unit + "_P_CELLID", PlcDataType.String, false, 1, string.Empty));
                nodes.Add(new NodeDefinition(unit + "_P_JOB_NUMBER", PlcDataType.Int16, false, 1, (short)0));

                // 원본 코드에서 i == 0 일 때만 추가로 등록되는 노드
                if (i == 0)
                {
                    nodes.Add(new NodeDefinition(unit + "_V_MOVE_REQ", PlcDataType.Bool, false, 1, false));
                    nodes.Add(new NodeDefinition(unit + "_V_ALIGN", PlcDataType.Int16, true, AlignArrayLength, new short[AlignArrayLength]));
                    nodes.Add(new NodeDefinition(unit + "_P_MOVE_END", PlcDataType.Bool, false, 1, false));
                    nodes.Add(new NodeDefinition(unit + "_P_SERVO_POSITION", PlcDataType.Int32, true, ServoPositionArrayLength, new int[ServoPositionArrayLength]));
                }
            }

            return nodes;
        }
    }
}
