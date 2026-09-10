using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MemMon.Models;
using MemMon.Services;

namespace MemMon;

/// <summary>Formats a raw byte count as "12.3 MB".</summary>
public sealed class BytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ulong u => ByteSize.Format(u),
            long l => ByteSize.Format((ulong)Math.Max(0, l)),
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a delta as "+12.3 MB" / "-4.0 KB".</summary>
public sealed class SignedBytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long l ? ByteSize.FormatSigned(l) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a signed count as "+90" / "-4".</summary>
public sealed class SignedCountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (i > 0 ? $"+{i:N0}" : i.ToString("N0")) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Growth is what we are hunting, so it is coloured; shrink and no-change stay quiet.</summary>
public sealed class DeltaBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Growth = new(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly SolidColorBrush Shrink = new(Color.FromRgb(0x6A, 0x99, 0x55));
    private static readonly SolidColorBrush Neutral = new(Color.FromRgb(0x9D, 0x9D, 0x9D));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long delta = value switch { long l => l, int i => i, _ => 0 };
        return delta > 0 ? Growth : delta < 0 ? Shrink : Neutral;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Spells out why a collection landed in the unusual list.</summary>
public sealed class AnomalyReasonConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GcEvent gc ? GcAnomaly.Describe(gc) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colours a glossary row by whether seeing that value is fine, worth a look, or bad.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Problem = new(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly SolidColorBrush Watch = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush Normal = new(Color.FromRgb(0x6A, 0x99, 0x55));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Tone.Problem => Problem,
            Tone.Watch => Watch,
            _ => Normal,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>The one-word label that goes with the tone colour.</summary>
public sealed class ToneLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Tone.Problem => "문제",
            Tone.Watch => "주의",
            _ => "정상",
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

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colours a GC pause by how much a person would feel it. A 100-200 ms operation budget makes
/// anything past ~50 ms worth a look and anything past ~200 ms an outright stall.
/// </summary>
public sealed class PauseBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Stall = new(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly SolidColorBrush Notable = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush Normal = new(Color.FromRgb(0xD4, 0xD4, 0xD4));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ms = value is double d ? d : 0;
        return ms >= 200 ? Stall : ms >= 50 ? Notable : Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows the empty-state hint only while a collection has nothing in it.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
