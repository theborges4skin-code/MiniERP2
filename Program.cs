using System.Threading;
using MiniERP2.Config;
using MiniERP2.Forms;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(PathProvider.AppDataFolder);

        // 처리되지 않은 UI 예외를 직접 받는다. 기본 동작(JIT 디버깅 안내가 붙은 표준 오류창)에서
        // [계속]을 누르면 앱은 살아있지만 예외 때문에 중간에 끊긴 창이 비활성/깨진 배치 상태로
        // 남아 "그 창만 조작이 안 되는" 문제가 있었다 — 여기서 로그로 남기고 창 상태를 복구한다.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;

        Application.SetColorMode(SystemColorMode.System);
        ApplicationConfiguration.Initialize();

        var mainHub = new MainHub();
        FormManager.ApplyBoundsTracking(mainHub);
        Application.Run(mainHub);
    }

    private static void OnThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        DiagnosticsLogger.Log($"[Program] 처리되지 않은 UI 예외: {e.Exception}");

        // 모달 다이얼로그가 떠 있는 동안 예외가 났다면 비활성화는 정상 상태이므로 건드리지 않는다.
        FormManager.RestoreFrozenForms();

        MessageBox.Show(
            $"작업 중 오류가 발생해 이 작업만 취소했습니다. 프로그램은 계속 사용할 수 있습니다.\n\n" +
            $"{e.Exception.Message}\n\n자세한 내용은 진단 로그에 기록했습니다:\n{PathProvider.DiagnosticsLogFilePath}",
            "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
