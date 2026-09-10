using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VisionSupport.Features;

namespace VisionSupport.Shell;

/// <summary>Paints the state dot and the command bar badge. One place, so every surface agrees.</summary>
public sealed class StateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            FeatureState.Running => "StateRunning",
            FeatureState.Starting => "StateRunning",
            FeatureState.Paused => "StatePaused",
            FeatureState.Stopping => "StatePaused",
            FeatureState.Faulted => "StateFaulted",
            _ => "StateStopped",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>State to the Korean label shown in the command bar badge.</summary>
public sealed class StateTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FeatureState.Running => "실행 중",
            FeatureState.Starting => "시작하는 중",
            FeatureState.Paused => "정지됨",
            FeatureState.Stopping => "종료하는 중",
            FeatureState.Faulted => "오류",
            _ => "대기",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

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
