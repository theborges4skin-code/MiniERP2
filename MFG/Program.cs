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
        // 화면 없이 코드 체계 변환(옛 RM###/PK###/H14 → mfg_ + 원료 코드 master 차용). 결과는 code_scheme_report_*.txt.
        if (args.Contains("--migrate-codes", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var settings = MfgSettings.Load();
                var path = Import.SeedRunner.MigrateCodesWithBackup(new Integration.MiniErp2Bridge(settings.ResolvedMiniErp2DatabasePath));
                Log.Write($"[CodeScheme] 완료 — 리포트 {path}");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write($"[CodeScheme] 실패: {ex}");
                return 1;
            }
        }
        // 단일 실행: MiniERP2의 "제조원가(MFG)" 버튼을 여러 번 눌러도 창이 하나만 뜨고, 이미 떠 있으면 앞으로 가져온다.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            return 0;
        }

        // 렌더링 초기화(SetCompatibleTextRenderingDefault 포함)는 창이 하나라도 만들어지기 전에 가장 먼저 해야 한다.
        // SetColorMode를 먼저 부르면 내부 창이 생겨 "첫 번째 IWin32Window 전에 호출해야 합니다" 예외로 시작 즉시 종료됐다
        // (2026-10-21 MiniERP2 버튼으로 실행 시 무반응 — WER 로그로 확인).
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);

        Directory.CreateDirectory(MfgPaths.AppDataFolder);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Write($"[Program] 처리되지 않은 UI 예외: {e.Exception}");
            MessageBox.Show($"작업 중 오류가 발생해 이 작업만 취소했습니다.\n\n{e.Exception.Message}\n\n로그: {MfgPaths.DiagnosticsLogFilePath}",
                "MiniERP2_MFG 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            // 시작 단계 오류가 조용히 사라지지 않게 로그와 안내창을 남긴다.
            Log.Write($"[Program] 시작 실패: {ex}");
            MessageBox.Show($"제조원가관리 프로그램을 시작하지 못했습니다.\n\n{ex.Message}\n\n로그: {MfgPaths.DiagnosticsLogFilePath}",
                "MiniERP2_MFG 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
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
