using System;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Common
{
    /// <summary>
    /// OPC UA/ADS가 공유하는 노드(변수) 정의. Value는 스칼라면 boxed 기본형, 배열이면 typed 배열(T[])을 담는다.
    /// </summary>
    public sealed class NodeDefinition
    {
        public NodeDefinition(string name, PlcDataType dataType, bool isArray, int arrayLength, object value)
        {
            Name = name;
            DataType = dataType;
            IsArray = isArray;
            ArrayLength = isArray ? Math.Max(1, arrayLength) : 1;
            Value = value;
            LastUpdated = DateTime.Now;
        }

        public string Name { get; }

        public PlcDataType DataType { get; }

        public bool IsArray { get; }

        public int ArrayLength { get; }

        public object Value { get; set; }

        public DateTime LastUpdated { get; set; }

        public static Type ClrElementType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return typeof(bool);
                case PlcDataType.Int16: return typeof(short);
                case PlcDataType.Int32: return typeof(int);
                case PlcDataType.Float: return typeof(float);
                case PlcDataType.Double: return typeof(double);
                case PlcDataType.String: return typeof(string);
                default: throw new ArgumentOutOfRangeException(nameof(dataType));
            }
        }

        public static object DefaultElementValue(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return false;
                case PlcDataType.Int16: return (short)0;
                case PlcDataType.Int32: return 0;
                case PlcDataType.Float: return 0f;
                case PlcDataType.Double: return 0d;
                case PlcDataType.String: return string.Empty;
                default: throw new ArgumentOutOfRangeException(nameof(dataType));
            }
        }
    }
}
