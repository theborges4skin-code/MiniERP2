using MiniERP2.MFG.Config;

namespace MiniERP2.MFG.Utils;

/// <summary>진단 로그(%AppData%\MiniERP2_MFG\diagnostics.log). 기록 실패는 무시한다.</summary>
public static class Log
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(MfgPaths.AppDataFolder);
            File.AppendAllText(MfgPaths.DiagnosticsLogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // 로그 실패가 작업을 막지 않게 한다.
        }
    }
}
