using MiniERP2.Config;

namespace MiniERP2.Utils;

/// <summary>
/// 작업이 멈춘 것처럼 보일 때 어느 단계까지 진행됐는지 추적하기 위한 진단용 파일 로그.
/// 각 호출이 즉시 파일을 열고-쓰고-닫으므로(File.AppendAllText), 그 다음 줄에서 프로그램이
/// 멈추거나 강제 종료되어도 그 직전까지의 기록은 디스크에 남는다. 정상 운영 기능이 아니라
/// 성능 문제를 추적하는 동안만 쓰는 임시 도구다 — 로그 파일 경로는
/// <see cref="PathProvider.DiagnosticsLogFilePath"/>(보통 exe와 같은 폴더의 diagnostics.log).
/// </summary>
public static class DiagnosticsLogger
{
    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(PathProvider.DiagnosticsLogFilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // 로그 기록 자체가 실패해도(디스크 권한 등) 본 작업에는 영향을 주지 않는다.
        }
    }
}
