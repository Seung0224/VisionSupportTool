using System.Windows.Controls;

namespace VisionSupport.Idle;

public partial class IdleView : UserControl
{
    public IdleView()
    {
        InitializeComponent();

        // The clock and the self-cost readout only need to tick while this page is on screen.
        Loaded += (_, _) => (DataContext as IdleViewModel)?.Activate();
        Unloaded += (_, _) => (DataContext as IdleViewModel)?.Deactivate();
    }
}
