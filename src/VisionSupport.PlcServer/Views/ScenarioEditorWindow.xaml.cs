using System.Windows;
using VirtualPlcServer.Modules;
using VirtualPlcServer.ViewModels;

namespace VirtualPlcServer.Views
{
    public partial class ScenarioEditorWindow : Window
    {
        public ScenarioEditorWindow(ScenarioModule module)
        {
            InitializeComponent();
            Title = module.Name + " (Scenario)";
            DataContext = new ScenarioEditorViewModel(module);
        }
    }
}
