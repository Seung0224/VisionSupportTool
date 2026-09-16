using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace VirtualPlcServer.Views
{
    public partial class WriteValueDialog : UserControl
    {
        public WriteValueDialog(string addressLabel, short currentValue)
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
            if (!short.TryParse(ValueBox.Text, out short value))
            {
                ErrorText.Text = "Enter an integer between -32768 and 32767.";
                return;
            }

            DialogHost.CloseDialogCommand.Execute(value, this);
        }
    }
}
