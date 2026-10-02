using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VisionSupport.Wireshark.Capture;

/// <summary>Runs pktmon.exe and returns what it printed. Its console output is in the OEM code page.</summary>
internal static class Pktmon
{
    public static (int ExitCode, string Output) Run(string arguments)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var info = new ProcessStartInfo("pktmon.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };

        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException("pktmon.exe를 실행할 수 없음");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new InvalidOperationException($"pktmon {arguments} 응답 없음");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
