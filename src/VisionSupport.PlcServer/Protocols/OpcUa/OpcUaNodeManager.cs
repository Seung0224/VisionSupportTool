using System;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;
using VirtualPlcServer.Protocols.Common;
using PlcDataType = VirtualPlcServer.Core.PlcDataType;

namespace VirtualPlcServer.Protocols.OpcUa
{
    /// <summary>
    /// NodeMap에 등록된 노드를 실제 OPC UA 주소공간의 변수 노드로 노출하는 커스텀 노드 매니저.
    /// </summary>
    public sealed class OpcUaNodeManager : CustomNodeManager2
    {
        public const string NamespaceUri = "urn:VirtualPlcServer:OpcUa";

        private readonly NodeMap _nodeMap;
        private readonly Dictionary<string, BaseDataVariableState> _variables = new Dictionary<string, BaseDataVariableState>();
        private FolderState _rootFolder;

        public OpcUaNodeManager(IServerInternal server, ApplicationConfiguration configuration, NodeMap nodeMap)
            : base(server, configuration, NamespaceUri)
        {
            _nodeMap = nodeMap;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                IList<IReference> references;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    references = new List<IReference>();
                    externalReferences[ObjectIds.ObjectsFolder] = references;
                }

                _rootFolder = new FolderState(null)
                {
                    SymbolicName = "VirtualPlc",
                    ReferenceTypeId = ReferenceTypes.Organizes,
                    TypeDefinitionId = ObjectTypeIds.FolderType,
                    NodeId = new NodeId("VirtualPlc", NamespaceIndex),
                    BrowseName = new QualifiedName("VirtualPlc", NamespaceIndex),
                    DisplayName = new LocalizedText("en", "VirtualPlc"),
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    EventNotifier = EventNotifiers.None
                };

                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _rootFolder.NodeId));
                _rootFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);

                AddPredefinedNode(SystemContext, _rootFolder);

                foreach (NodeDefinition definition in _nodeMap.GetAllNodes())
                {
                    CreateVariableNode(definition);
                }
            }
        }

        public void CreateVariableNode(NodeDefinition definition)
        {
            lock (Lock)
            {
                if (_variables.ContainsKey(definition.Name))
                {
                    return;
                }

                var variable = new BaseDataVariableState(_rootFolder)
                {
                    SymbolicName = definition.Name,
                    ReferenceTypeId = ReferenceTypes.Organizes,
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    NodeId = new NodeId(definition.Name, NamespaceIndex),
                    BrowseName = new QualifiedName(definition.Name, NamespaceIndex),
                    DisplayName = new LocalizedText("en", definition.Name),
                    DataType = MapDataTypeId(definition.DataType),
                    ValueRank = definition.IsArray ? ValueRanks.OneDimension : ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Historizing = false,
                    Value = definition.Value,
                    StatusCode = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow
                };

                // OnSimpleWriteValue가 아니라 OnWriteValue를 써야 한다: SDK의 BaseVariableState.WriteValueAttribute는
                // OnSimpleWriteValue가 걸려 있는 상태에서 indexRange가 지정된 쓰기(배열 원소 하나만 쓰는 경우 등)가
                // 들어오면 콜백을 부르지도 않고 바로 에러(BadWriteNotSupported류)를 돌려준다 - 배열 노드의
                // 특정 인덱스만 쓰는 외부 클라이언트의 쓰기가 조용히 실패하는 원인이었다.
                // OnWriteValue를 쓰면 indexRange를 직접 받아서 처리해야 하므로, 지정돼 있으면 현재 전체 배열에
                // 병합한 뒤 그 결과를 반영한다(지정 안 됐으면 기존과 동일하게 전체 값 교체).
                variable.OnWriteValue = (ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp) =>
                    OnClientWrite(definition.Name, indexRange, ref value);

                _rootFolder?.AddChild(variable);
                AddPredefinedNode(SystemContext, variable);
                _variables[definition.Name] = variable;
            }
        }

        /// <summary>주소공간에서 변수 노드를 실제로 제거한다(CustomNodeManager2.DeleteNode로 검증됨).</summary>
        public void RemoveVariableNode(string name)
        {
            lock (Lock)
            {
                if (!_variables.TryGetValue(name, out BaseDataVariableState variable))
                {
                    return;
                }

                DeleteNode(SystemContext, variable.NodeId);
                _variables.Remove(name);
            }
        }

        /// <summary>이 서버에서 실제로 배정된 네임스페이스 인덱스 (외부 클라이언트가 ns=N;s=... 형태로 접속할 때 필요).</summary>
        public ushort ResolvedNamespaceIndex => NamespaceIndex;

        /// <summary>클라이언트가 접속에 사용할 전체 NodeId 문자열 (예: ns=2;s=NodeName).</summary>
        public string GetResolvedNodeIdString(string name)
        {
            return "ns=" + NamespaceIndex + ";s=" + name;
        }

        public void UpdateVariableValue(string name, object value)
        {
            lock (Lock)
            {
                if (_variables.TryGetValue(name, out BaseDataVariableState variable))
                {
                    variable.Value = value;
                    variable.Timestamp = DateTime.UtcNow;
                    variable.StatusCode = StatusCodes.Good;
                    variable.ClearChangeMasks(SystemContext, false);
                }
            }
        }

        private ServiceResult OnClientWrite(string name, NumericRange indexRange, ref object value)
        {
            if (indexRange != NumericRange.Empty)
            {
                object current = _nodeMap.TryGetNode(name, out NodeDefinition existing) ? existing.Value : null;
                StatusCode mergeStatus = indexRange.UpdateRange(ref current, value);
                if (StatusCode.IsBad(mergeStatus))
                {
                    return mergeStatus;
                }

                value = current;
            }

            _nodeMap.SetValue(name, value);
            return ServiceResult.Good;
        }

        private static NodeId MapDataTypeId(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return DataTypeIds.Boolean;
                case PlcDataType.Int16: return DataTypeIds.Int16;
                case PlcDataType.Int32: return DataTypeIds.Int32;
                case PlcDataType.Float: return DataTypeIds.Float;
                case PlcDataType.Double: return DataTypeIds.Double;
                case PlcDataType.String: return DataTypeIds.String;
                default: throw new ArgumentOutOfRangeException(nameof(dataType));
            }
        }
    }
}
