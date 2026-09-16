using System;
using System.Text;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.Ads
{
    /// <summary>NodeDefinition 값 <-> ADS Raw 바이트 버퍼 사이의 인코딩/디코딩을 담당한다.</summary>
    internal static class AdsValueCodec
    {
        public const int StringByteLength = 128;

        public static int ElementSize(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return 1;
                case PlcDataType.Int16: return 2;
                case PlcDataType.Int32: return 4;
                case PlcDataType.Float: return 4;
                case PlcDataType.Double: return 8;
                case PlcDataType.String: return StringByteLength;
                case PlcDataType.SByte: return 1;
                case PlcDataType.Byte: return 1;
                case PlcDataType.UInt16: return 2;
                case PlcDataType.UInt32: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(dataType));
            }
        }

        public static int TotalByteLength(PlcDataType dataType, int arrayLength)
        {
            return ElementSize(dataType) * Math.Max(1, arrayLength);
        }

        public static void WriteValue(byte[] buffer, int offset, NodeDefinition definition)
        {
            WriteValue(buffer, offset, definition.DataType, definition.IsArray, definition.ArrayLength, definition.Value);
        }

        public static void WriteValue(byte[] buffer, int offset, PlcDataType dataType, bool isArray, int arrayLength, object value)
        {
            int elementSize = ElementSize(dataType);

            if (isArray)
            {
                Array array = (Array)value;
                for (int i = 0; i < arrayLength; i++)
                {
                    object element = array != null && i < array.Length ? array.GetValue(i) : NodeDefinition.DefaultElementValue(dataType);
                    WriteElement(buffer, offset + i * elementSize, dataType, element);
                }
            }
            else
            {
                WriteElement(buffer, offset, dataType, value);
            }
        }

        public static object ReadValue(byte[] buffer, int offset, PlcDataType dataType, bool isArray, int arrayLength)
        {
            int elementSize = ElementSize(dataType);

            if (!isArray)
            {
                return ReadElement(buffer, offset, dataType);
            }

            Array array = Array.CreateInstance(NodeDefinition.ClrElementType(dataType), arrayLength);
            for (int i = 0; i < arrayLength; i++)
            {
                array.SetValue(ReadElement(buffer, offset + i * elementSize, dataType), i);
            }

            return array;
        }

        private static void WriteElement(byte[] buffer, int offset, PlcDataType dataType, object value)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    buffer[offset] = (bool)value ? (byte)1 : (byte)0;
                    break;
                case PlcDataType.Int16:
                    {
                        byte[] bytes = BitConverter.GetBytes((short)value);
                        Array.Copy(bytes, 0, buffer, offset, 2);
                        break;
                    }
                case PlcDataType.Int32:
                    {
                        byte[] bytes = BitConverter.GetBytes((int)value);
                        Array.Copy(bytes, 0, buffer, offset, 4);
                        break;
                    }
                case PlcDataType.Float:
                    {
                        byte[] bytes = BitConverter.GetBytes((float)value);
                        Array.Copy(bytes, 0, buffer, offset, 4);
                        break;
                    }
                case PlcDataType.Double:
                    {
                        byte[] bytes = BitConverter.GetBytes((double)value);
                        Array.Copy(bytes, 0, buffer, offset, 8);
                        break;
                    }
                case PlcDataType.SByte:
                    buffer[offset] = unchecked((byte)(sbyte)value);
                    break;
                case PlcDataType.Byte:
                    buffer[offset] = (byte)value;
                    break;
                case PlcDataType.UInt16:
                    {
                        byte[] bytes = BitConverter.GetBytes((ushort)value);
                        Array.Copy(bytes, 0, buffer, offset, 2);
                        break;
                    }
                case PlcDataType.UInt32:
                    {
                        byte[] bytes = BitConverter.GetBytes((uint)value);
                        Array.Copy(bytes, 0, buffer, offset, 4);
                        break;
                    }
                case PlcDataType.String:
                    {
                        Array.Clear(buffer, offset, StringByteLength);
                        string text = (string)value ?? string.Empty;
                        byte[] bytes = Encoding.UTF8.GetBytes(text);
                        int count = Math.Min(bytes.Length, StringByteLength - 1);
                        Array.Copy(bytes, 0, buffer, offset, count);
                        break;
                    }
            }
        }

        private static object ReadElement(byte[] buffer, int offset, PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    return buffer[offset] != 0;
                case PlcDataType.Int16:
                    return BitConverter.ToInt16(buffer, offset);
                case PlcDataType.Int32:
                    return BitConverter.ToInt32(buffer, offset);
                case PlcDataType.Float:
                    return BitConverter.ToSingle(buffer, offset);
                case PlcDataType.Double:
                    return BitConverter.ToDouble(buffer, offset);
                case PlcDataType.SByte:
                    return unchecked((sbyte)buffer[offset]);
                case PlcDataType.Byte:
                    return buffer[offset];
                case PlcDataType.UInt16:
                    return BitConverter.ToUInt16(buffer, offset);
                case PlcDataType.UInt32:
                    return BitConverter.ToUInt32(buffer, offset);
                case PlcDataType.String:
                    {
                        int len = Array.IndexOf<byte>(buffer, 0, offset, StringByteLength);
                        int count = len >= 0 ? len - offset : StringByteLength;
                        return Encoding.UTF8.GetString(buffer, offset, count);
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataType));
            }
        }
    }
}
