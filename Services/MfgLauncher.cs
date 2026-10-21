using System.Diagnostics;
using MiniERP2.Config;

namespace MiniERP2.Services;

/// <summary>
/// 제조원가관리 프로그램 MiniERP2_MFG(별도 단독 실행 프로그램, 저장소의 MFG\ 프로젝트) 실행기.
/// MiniERP2는 MFG를 실행만 한다 — MFG는 자체 DB(mfgdb)를 쓰고, 확정 원가만 MiniERP2 ItemTable로 보낸다
/// (docs/MFG_PLAN.md). MFG가 이미 떠 있으면 MFG 쪽 단일 실행 처리가 기존 창을 앞으로 가져온다.
/// </summary>
public static class MfgLauncher
{
    private const string ExeName = "MiniERP2_MFG.exe";
    private const string SettingsKey = "MfgExePath";

    public static void Launch(IWin32Window owner)
    {
        var settings = new SettingsService();
        var exe = FindExe(settings.GetLastFolder(SettingsKey));
        if (exe is null)
        {
            MessageBox.Show(owner,
                "제조원가관리 프로그램(MiniERP2_MFG.exe)을 찾지 못했습니다.\n파일 위치를 지정해 주세요. 한 번 지정하면 기억합니다.\n\n" +
                "(빌드 위치 예: MiniERP2\\MFG\\bin\\Debug\\net10.0-windows\\MiniERP2_MFG.exe)",
                "제조원가관리(MFG)", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var dlg = new OpenFileDialog { Filter = "MiniERP2_MFG.exe|MiniERP2_MFG.exe|실행 파일 (*.exe)|*.exe", FileName = ExeName };
            if (dlg.ShowDialog(owner) != DialogResult.OK) return;
            exe = dlg.FileName;
        }

        settings.SetLastFolder(SettingsKey, exe);
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"제조원가관리 프로그램을 실행하지 못했습니다.\n{exe}\n\n{ex.Message}", "제조원가관리(MFG)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// 찾는 순서: ① 지정해 둔 경로 ② MiniERP2.exe와 같은 폴더 ③ 저장소의 MFG\bin\(Debug|Release)\net10.0-windows (가장 최근 빌드).
    /// </summary>
    internal static string? FindExe(string? savedPath)
    {
        if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath)) return savedPath;

        var sideBySide = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (File.Exists(sideBySide)) return sideBySide;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 6; depth++, dir = dir.Parent)
        {
            var mfgBin = Path.Combine(dir.FullName, "MFG", "bin");
            if (!Directory.Exists(mfgBin)) continue;
            var candidate = Directory.EnumerateFiles(mfgBin, ExeName, SearchOption.AllDirectories)
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (candidate is not null) return candidate.FullName;
        }
        return null;
    }
}
