using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace VirtualPlcServer.Views
{
    public partial class WriteValueDialog : UserControl
    {
        public WriteValueDialog(string addressLabel, ushort currentValue)
        {
            InitializeComponent();
            TitleText.Text = "Write " + addressLabel;
            ValueBox.Text = currentValue.ToString();
            Loaded += (s, e) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        private void OnOkClicked(object sender, RoutedEventArgs e)
        {
            if (!ushort.TryParse(ValueBox.Text, out ushort value))
            {
                ErrorText.Text = "Enter an integer between 0 and 65535.";
                return;
            }

            DialogHost.CloseDialogCommand.Execute(value, this);
        }
    }
}
