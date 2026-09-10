using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Common
{
    /// <summary>
    /// OPC UA / TwinCAT ADS가 공용으로 사용하는 이름 기반 노드 테이블.
    /// </summary>
    public sealed class NodeMap : IPlcMap
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, NodeDefinition> _nodes = new Dictionary<string, NodeDefinition>();

        public event EventHandler<MapValueChangedEventArgs> ValueChanged;

        /// <summary>새 노드가 추가되었을 때 통신 계층(OPC UA/ADS)이 실제 서버 객체를 만들 수 있도록 알린다.</summary>
        public event EventHandler<NodeDefinition> NodeAdded;

        /// <summary>노드가 제거되었을 때 통신 계층이 실제 서버 객체(OPC UA 주소공간 등)도 함께 지울 수 있도록 알린다.</summary>
        public event EventHandler<string> NodeRemoved;

        public bool TryAddNode(NodeDefinition definition)
        {
            lock (_lock)
            {
                if (_nodes.ContainsKey(definition.Name))
                {
                    return false;
                }

                _nodes[definition.Name] = definition;
            }

            NodeAdded?.Invoke(this, definition);
            ValueChanged?.Invoke(this, new MapValueChangedEventArgs(definition.Name, definition.Value));
            return true;
        }

        public bool TryGetNode(string name, out NodeDefinition definition)
        {
            lock (_lock)
            {
                return _nodes.TryGetValue(name, out definition);
            }
        }

        public void SetValue(string name, object value)
        {
            NodeDefinition definition;
            lock (_lock)
            {
                if (!_nodes.TryGetValue(name, out definition))
                {
                    return;
                }

                definition.Value = value;
                definition.LastUpdated = DateTime.Now;
            }

            ValueChanged?.Invoke(this, new MapValueChangedEventArgs(name, value));
        }

        /// <summary>이름으로 노드 하나를 제거한다. 존재했으면 true.</summary>
        public bool RemoveNode(string name)
        {
            lock (_lock)
            {
                if (!_nodes.Remove(name))
                {
                    return false;
                }
            }

            NodeRemoved?.Invoke(this, name);
            return true;
        }

        /// <summary>모든 노드를 제거한다(REMOVE 전부).</summary>
        public void Clear()
        {
            List<string> names;
            lock (_lock)
            {
                names = new List<string>(_nodes.Keys);
                _nodes.Clear();
            }

            foreach (string name in names)
            {
                NodeRemoved?.Invoke(this, name);
            }
        }

        public List<NodeDefinition> GetAllNodes()
        {
            lock (_lock)
            {
                return _nodes.Values.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public object CreateSnapshot()
        {
            lock (_lock)
            {
                var snapshot = new NodeMapSnapshot();
                foreach (NodeDefinition node in _nodes.Values)
                {
                    snapshot.Nodes.Add(new NodeSnapshotEntry
                    {
                        Name = node.Name,
                        DataType = node.DataType,
                        IsArray = node.IsArray,
                        ArrayLength = node.ArrayLength,
                        Value = node.Value
                    });
                }

                return snapshot;
            }
        }

        public void RestoreSnapshot(object snapshot)
        {
            if (!(snapshot is NodeMapSnapshot nodeSnapshot) || nodeSnapshot.Nodes == null)
            {
                return;
            }

            foreach (NodeSnapshotEntry entry in nodeSnapshot.Nodes)
            {
                object coerced = CoerceValue(entry.Value, entry.DataType, entry.IsArray);
                var definition = new NodeDefinition(entry.Name, entry.DataType, entry.IsArray, entry.ArrayLength, coerced);
                TryAddNode(definition);
            }
        }

        private static object CoerceValue(object rawValue, PlcDataType dataType, bool isArray)
        {
            Type elementType = NodeDefinition.ClrElementType(dataType);

            if (isArray)
            {
                if (rawValue is JArray jArray)
                {
                    Array array = Array.CreateInstance(elementType, jArray.Count);
                    for (int i = 0; i < jArray.Count; i++)
                    {
                        array.SetValue(ConvertScalar(jArray[i].ToObject(elementType), elementType), i);
                    }

                    return array;
                }

                // JSON을 거치지 않고 메모리에서 바로 스냅샷을 옮기는 경우(예: 연결설정 재오픈)는
                // rawValue가 이미 올바른 타입의 배열이므로 그대로(필요 시 요소 타입만 맞춰) 사용한다.
                if (rawValue is Array existingArray)
                {
                    Array array = Array.CreateInstance(elementType, existingArray.Length);
                    for (int i = 0; i < existingArray.Length; i++)
                    {
                        array.SetValue(ConvertScalar(existingArray.GetValue(i), elementType), i);
                    }

                    return array;
                }

                return Array.CreateInstance(elementType, 0);
            }

            if (rawValue is JValue jValue)
            {
                return ConvertScalar(jValue.ToObject(elementType), elementType);
            }

            return ConvertScalar(rawValue, elementType) ?? NodeDefinition.DefaultElementValue(dataType);
        }

        private static object ConvertScalar(object value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            return Convert.ChangeType(value, targetType);
        }
    }
}
