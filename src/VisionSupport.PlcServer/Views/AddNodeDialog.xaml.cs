using System;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Views
{
    public partial class AddNodeDialog : UserControl
    {
        public AddNodeDialog()
        {
            InitializeComponent();
        }

        private void OnIsArrayChanged(object sender, RoutedEventArgs e)
        {
            ArrayLengthBox.IsEnabled = IsArrayBox.IsChecked == true;
        }

        private void OnAddClicked(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ErrorText.Text = "Enter a node name.";
                return;
            }

            var dataType = (PlcDataType)DataTypeBox.SelectedIndex;
            bool isArray = IsArrayBox.IsChecked == true;
            int arrayLength = 1;
            if (isArray && !int.TryParse(ArrayLengthBox.Text, out arrayLength))
            {
                ErrorText.Text = "Enter a valid array length.";
                return;
            }

            object defaultValue = isArray
                ? Array.CreateInstance(NodeDefinition.ClrElementType(dataType), Math.Max(1, arrayLength))
                : NodeDefinition.DefaultElementValue(dataType);

            var definition = new NodeDefinition(name, dataType, isArray, arrayLength, defaultValue);
            DialogHost.CloseDialogCommand.Execute(definition, this);
        }
    }
}
