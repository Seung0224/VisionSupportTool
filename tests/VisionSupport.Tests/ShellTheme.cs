using System.Windows;
using VisionSupport.Shell;

namespace VisionSupport.Tests;

/// <summary>
/// The shell's theme and the converters App.xaml registers, merged into the test Application so
/// feature views that name them can be built. Keys on the same marker as the automation tests'
/// own helper, so whichever runs first, the dictionary is merged once.
/// </summary>
internal static class ShellTheme
{
    public static void EnsureMerged()
    {
        ResourceDictionary resources = Application.Current!.Resources;
        if (resources.Contains("EmptyToVis")) return;

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/VisionSupport;component/Theme/Dark.xaml")
        });
        resources["StateText"] = new StateTextConverter();
        resources["BoolToVis"] = new BoolToVisibilityConverter();
        resources["EmptyToVis"] = new EmptyToVisibilityConverter();
    }
}
