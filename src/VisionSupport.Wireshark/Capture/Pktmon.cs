using System.Diagnostics;
using System.Text;

namespace VisionSupport.Wireshark.Capture;

/// <summary>Runs pktmon.exe and returns what it printed. Redirected, it writes UTF-8 (checked on a
/// Korean Windows 10: "드라이버" arrives as EB 93 9C ...), not the console's OEM code page.</summary>
internal static class Pktmon
{
    public static (int ExitCode, string Output) Run(string arguments)
    {
        Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var info = new ProcessStartInfo("pktmon.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
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
