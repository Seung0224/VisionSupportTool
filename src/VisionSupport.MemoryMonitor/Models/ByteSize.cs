namespace MemMon.Models;

public static class ByteSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(ulong bytes) => Format((double)bytes);

    public static string FormatSigned(long bytes)
    {
        string sign = bytes > 0 ? "+" : bytes < 0 ? "-" : "";
        return sign + Format(Math.Abs((double)bytes));
    }

    private static string Format(double value)
    {
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:N0} {Units[unit]}" : $"{value:N1} {Units[unit]}";
    }
}
