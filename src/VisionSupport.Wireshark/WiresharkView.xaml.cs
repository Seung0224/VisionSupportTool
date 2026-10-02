using System.Windows.Controls;
using VisionSupport.Wireshark.ViewModels;

namespace VisionSupport.Wireshark;

public partial class WiresharkView : UserControl
{
    public WiresharkView(WiresharkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
