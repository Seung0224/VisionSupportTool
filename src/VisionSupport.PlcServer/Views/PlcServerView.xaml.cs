using System.Windows.Controls;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    /// <summary>
    /// The virtual hardware hub's screen. Was MainWindow.
    ///
    /// The window-closing logic that used to live here is gone on purpose. It ended with
    /// Application.Current.Shutdown(), which inside the support shell would take the whole
    /// process down with it. Saving module state and stopping the servers now belongs to the
    /// shell's feature module, which does it when the feature is stopped rather than when a
    /// window is closed - and the shell's own window handles the same async-teardown and
    /// re-entrancy hazards that made the old handler necessary.
    /// </summary>
    public partial class PlcServerView : UserControl
    {
        public PlcServerView(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
