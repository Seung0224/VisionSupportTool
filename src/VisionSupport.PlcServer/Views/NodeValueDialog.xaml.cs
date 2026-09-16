using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Views
{
    public partial class NodeValueDialog : UserControl
    {
        private readonly NodeDefinition _definition;

        public NodeValueDialog(NodeDefinition definition)
        {
            InitializeComponent();
            _definition = definition;
            TitleText.Text = "Write " + definition.Name;
            TypeText.Text = definition.IsArray
                ? definition.DataType + "[" + definition.ArrayLength + "]"
                : definition.DataType.ToString();

            if (definition.IsArray)
            {
                ArrayBox.Visibility = Visibility.Visible;
                ArrayBox.Text = FormatArrayForEdit(definition.Value);
                Loaded += (s, e) => { ArrayBox.Focus(); ArrayBox.SelectAll(); };
            }
            else if (definition.DataType == PlcDataType.Bool)
            {
                BoolBox.Visibility = Visibility.Visible;
                BoolBox.IsChecked = definition.Value is bool b && b;
            }
            else
            {
                ValueBox.Visibility = Visibility.Visible;
                ValueBox.Text = definition.Value?.ToString() ?? string.Empty;
                Loaded += (s, e) => { ValueBox.Focus(); ValueBox.SelectAll(); };
            }
        }

        private static string FormatArrayForEdit(object value)
        {
            if (value is Array array)
            {
                return string.Join(", ", array.Cast<object>());
            }

            return string.Empty;
        }

        private void OnOkClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                object result = _definition.IsArray ? ParseArray() : ParseScalar(ValueBox.Text.Trim());
                DialogHost.CloseDialogCommand.Execute(result, this);
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }

        private object ParseScalar(string text)
        {
            switch (_definition.DataType)
            {
                case PlcDataType.Bool:
                    return BoolBox.IsChecked == true;
                case PlcDataType.Int16:
                    return short.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Int32:
                    return int.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Float:
                    return float.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Double:
                    return double.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.SByte:
                    return sbyte.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Byte:
                    return byte.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.UInt16:
                    return ushort.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.UInt32:
                    return uint.Parse(text, CultureInfo.InvariantCulture);
                default:
                    return text;
            }
        }

        private Array ParseArray()
        {
            string[] tokens = ArrayBox.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).ToArray();
            Type elementType = NodeDefinition.ClrElementType(_definition.DataType);
            Array array = Array.CreateInstance(elementType, Math.Max(tokens.Length, _definition.ArrayLength));
            for (int i = 0; i < tokens.Length && i < array.Length; i++)
            {
                array.SetValue(ParseElement(tokens[i]), i);
            }

            return array;
        }

        private object ParseElement(string text)
        {
            switch (_definition.DataType)
            {
                case PlcDataType.Bool:
                    return bool.Parse(text);
                case PlcDataType.Int16:
                    return short.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Int32:
                    return int.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Float:
                    return float.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Double:
                    return double.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.SByte:
                    return sbyte.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.Byte:
                    return byte.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.UInt16:
                    return ushort.Parse(text, CultureInfo.InvariantCulture);
                case PlcDataType.UInt32:
                    return uint.Parse(text, CultureInfo.InvariantCulture);
                default:
                    return text;
            }
        }
    }
}
