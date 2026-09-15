using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VisionSupport.Features;

namespace VisionSupport.Shell;

/// <summary>
/// Paints a feature's state dot - the feature window's caption and the overview rows share it, so
/// every surface agrees. Faulted and mid-release come from the shell; whether it is running comes
/// from the tool itself.
/// </summary>
public static class FeatureBrushes
{
    public static Brush For(IFeatureModule module)
    {
        string key = module.State switch
        {
            FeatureState.Faulted => "StateFaulted",
            FeatureState.Stopping => "StatePaused",
            _ => module.IsWorking ? "StateRunning" : "StateStopped",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}

/// <summary>State to the Korean label shown in the command bar badge.</summary>
public sealed class StateTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FeatureState.Stopping => "종료하는 중",
            FeatureState.Faulted => "오류",
            _ => "대기",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// True shows, false hides. Pass "invert" as the converter parameter to swap that, for the rows
/// that appear when a flag is off rather than on.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool show = value is true;
        if (parameter as string == "invert") show = !show;

        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Hides a text row when the string is empty, so blank status lines leave no gap.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
