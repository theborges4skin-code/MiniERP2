using System.Diagnostics;
using MiniERP2.MFG.Config;
using MiniERP2.MFG.Data;

namespace MiniERP2.MFG.Forms;

/// <summary>mfgdb 백업 목록·지금 백업·복원(설정 탭과 데이터 관리 탭의 "백업/롤백"이 같이 쓴다).</summary>
public class BackupPanel : UserControl
{
    private readonly AppState _state;
    private readonly MfgBackupService _backup = new();
    private readonly DataGridView _grid = Ui.Grid();

    public BackupPanel(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        _grid.MultiSelect = false;
        _grid.Columns.AddRange(Ui.Col("kind", "종류", 90), Ui.Col("time", "시각", 150), Ui.Col("size", "크기", 80, right: true), Ui.Col("file", "파일", 460));
        Controls.Add(_grid);
        Controls.Add(Ui.Bar(Ui.Button("지금 백업", (_, _) => { _backup.CreateBackup("manual"); RefreshList(); }),
            Ui.Button("선택 백업으로 복원", (_, _) => Restore()), Ui.Button("백업 폴더 열기", (_, _) => Open(MfgPaths.BackupsFolder)),
            Ui.Button("데이터 폴더 열기", (_, _) => Open(MfgPaths.AppDataFolder)),
            Ui.Label("수동·작업 전 3개 / 일일 2개월 / 월간 보관 (MiniERP2와 같은 규칙)")));
    }

    public void RefreshList()
    {
        _grid.Rows.Clear();
        foreach (var f in _backup.GetAllBackups())
        {
            var kind = f.Name.Contains("_DailyBackup_") ? "일일" : f.Name.Contains("_MonthlyBackup_") ? "월간" : "수동/작업 전";
            var i = _grid.Rows.Add(kind, f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), $"{f.Length / 1024:N0} KB", f.Name);
            _grid.Rows[i].Tag = f.FullName;
        }
    }

    private void Restore()
    {
        if (_grid.CurrentRow?.Tag is not string path) return;
        if (!Ui.Confirm(this, $"선택한 백업으로 mfgdb를 되돌릴까요?\n{Path.GetFileName(path)}\n\n현재 상태도 'before_restore' 백업으로 남깁니다.")) return;
        _backup.Restore(path);
        _state.NotifyDataChanged();
        RefreshList();
        Ui.Info(this, "복원했습니다. 열려 있는 화면은 다시 읽습니다.");
    }

    private static void Open(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
