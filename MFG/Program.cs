using System.Diagnostics;
using System.Runtime.InteropServices;
using MiniERP2.MFG.Config;
using MiniERP2.MFG.Forms;
using MiniERP2.MFG.Utils;

namespace MiniERP2.MFG;

internal static class Program
{
    private const string SingleInstanceMutexName = "MiniERP2_MFG_SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        // 화면 없이 초기 데이터 가져오기: MiniERP2_MFG.exe --seed [--force]
        // (경로는 설정의 마지막 경로 사용, --force면 기존 데이터를 백업 후 지우고 다시 가져옴). 결과는 import_report_*.txt.
        if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
        {
            return RunSeedHeadless(args.Contains("--force", StringComparer.OrdinalIgnoreCase));
        }
        // 단일 실행: MiniERP2의 "제조원가(MFG)" 버튼을 여러 번 눌러도 창이 하나만 뜨고, 이미 떠 있으면 앞으로 가져온다.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            return 0;
        }

        Directory.CreateDirectory(MfgPaths.AppDataFolder);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Write($"[Program] 처리되지 않은 UI 예외: {e.Exception}");
            MessageBox.Show($"작업 중 오류가 발생해 이 작업만 취소했습니다.\n\n{e.Exception.Message}\n\n로그: {MfgPaths.DiagnosticsLogFilePath}",
                "MiniERP2_MFG 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Application.SetColorMode(SystemColorMode.System);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunSeedHeadless(bool force)
    {
        try
        {
            Directory.CreateDirectory(MfgPaths.AppDataFolder);
            var settings = MfgSettings.Load();
            var pm = File.Exists(settings.LastProductMasterPath) ? settings.LastProductMasterPath : null;
            var (_, reportPath) = Import.SeedRunner.Run(settings.LastRecipeWorkbookPath, pm,
                new Integration.MiniErp2Bridge(settings.ResolvedMiniErp2DatabasePath), force);
            Log.Write($"[Seed] 완료 — 리포트 {reportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write($"[Seed] 실패: {ex}");
            return 1;
        }
    }

    private static void ActivateExistingInstance()
    {
        var current = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(current.ProcessName))
        {
            if (p.Id == current.Id || p.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
            SetForegroundWindow(p.MainWindowHandle);
            break;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
