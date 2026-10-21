using System.Diagnostics;
using MiniERP2.MFG.Config;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Import;

namespace MiniERP2.MFG.Forms;

/// <summary>설정(MiniERP2 DB 경로) · 백업/복원 · 초기 데이터 가져오기.</summary>
public class SettingsTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly MfgBackupService _backup = new();
    private readonly TextBox _erpPath = Ui.Text(520);
    private readonly Label _erpStatus = Ui.Label("");
    private readonly TextBox _recipePath = Ui.Text(520);
    private readonly TextBox _pmPath = Ui.Text(520);
    private readonly DataGridView _backups = Ui.Grid();

    public SettingsTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var erpBox = new GroupBox { Text = "MiniERP2 연동", Dock = DockStyle.Top, Height = 110 };
        erpBox.Controls.Add(Ui.Bar(Ui.Label("MiniERP2 DB 경로"), _erpPath, Ui.Button("찾아보기", (_, _) => BrowseErp()),
            Ui.Button("기본값", (_, _) => { _erpPath.Text = ""; SaveErp(); }), Ui.Button("저장", (_, _) => SaveErp()), _erpStatus));

        var importBox = new GroupBox { Text = "초기 데이터 가져오기 (원본 엑셀은 읽기만 합니다 · 전부 '미확정'으로 들어옵니다)", Dock = DockStyle.Top, Height = 150 };
        var importForm = Ui.Form(
            ("신제조법(제조법관련(신규).xlsx)", Row(_recipePath, Ui.Button("찾아보기", (_, _) => Browse(_recipePath)))),
            ("product_master.xlsx", Row(_pmPath, Ui.Button("찾아보기", (_, _) => Browse(_pmPath)))));
        importBox.Controls.Add(importForm);
        importBox.Controls.Add(Ui.Bar(Ui.Button("초기 데이터 가져오기", (_, _) => RunImport()),
            Ui.Label("이미 데이터가 있으면 백업 후 지우고 다시 가져옵니다(매입처 목록은 유지).")));
        foreach (Control c in importBox.Controls) if (c is FlowLayoutPanel f) f.Dock = DockStyle.Bottom;

        _backups.Columns.AddRange(Ui.Col("kind", "종류", 90), Ui.Col("time", "시각", 150), Ui.Col("size", "크기", 80, right: true), Ui.Col("file", "파일", 420));
        var backupBox = new GroupBox { Text = "mfgdb 백업 (수동·작업 전 3개 / 일일 2개월 / 월간 보관 — MiniERP2와 같은 규칙)", Dock = DockStyle.Fill, MinimumSize = new Size(0, 260) };
        backupBox.Controls.Add(_backups);
        backupBox.Controls.Add(Ui.Bar(Ui.Button("지금 백업", (_, _) => { _backup.CreateBackup("manual"); RefreshData(); }),
            Ui.Button("선택 백업으로 복원", (_, _) => Restore()), Ui.Button("백업 폴더 열기", (_, _) => OpenFolder(MfgPaths.BackupsFolder)),
            Ui.Button("데이터 폴더 열기", (_, _) => OpenFolder(MfgPaths.AppDataFolder)), Ui.Label($"mfgdb: {MfgPaths.DatabaseFilePath}")));

        Controls.Add(backupBox);
        Controls.Add(importBox);
        Controls.Add(erpBox);
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        p.Controls.AddRange(controls);
        return p;
    }

    public void RefreshData()
    {
        _erpPath.Text = _state.Settings.MiniErp2DatabasePath;
        _erpPath.PlaceholderText = MfgPaths.DefaultMiniErp2DatabasePath + " (기본값)";
        var bridge = _state.Bridge;
        _erpStatus.Text = bridge.IsAvailable ? "연결됨" : "파일 없음";
        _erpStatus.ForeColor = bridge.IsAvailable ? Color.SeaGreen : Ui.WarnFore;
        _recipePath.Text = _state.Settings.LastRecipeWorkbookPath;
        _pmPath.Text = _state.Settings.LastProductMasterPath;

        _backups.Rows.Clear();
        foreach (var f in _backup.GetAllBackups())
        {
            var kind = f.Name.Contains("_DailyBackup_") ? "일일" : f.Name.Contains("_MonthlyBackup_") ? "월간" : "수동/작업 전";
            var i = _backups.Rows.Add(kind, f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), $"{f.Length / 1024:N0} KB", f.Name);
            _backups.Rows[i].Tag = f.FullName;
        }
    }

    private void BrowseErp()
    {
        using var dlg = new OpenFileDialog { Filter = "SQLite DB (*.sqlite)|*.sqlite|모든 파일|*.*", FileName = "ERP_Database.sqlite" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { _erpPath.Text = dlg.FileName; SaveErp(); }
    }

    private void SaveErp()
    {
        _state.Settings.MiniErp2DatabasePath = _erpPath.Text.Trim();
        _state.Settings.Save();
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void Browse(TextBox target)
    {
        using var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = target.Text };
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
    }

    private void RunImport()
    {
        var recipe = _recipePath.Text.Trim();
        var pm = _pmPath.Text.Trim();
        if (!File.Exists(recipe)) { Ui.Warn(this, "신제조법 파일을 찾을 수 없습니다."); return; }
        if (pm.Length > 0 && !File.Exists(pm)) { Ui.Warn(this, "product_master 파일을 찾을 수 없습니다."); return; }

        if (SeedImporter.HasData())
        {
            if (!Ui.Confirm(this, "mfgdb에 이미 원료·제조법·완제품 데이터가 있습니다.\n백업을 만든 뒤 모두 지우고 엑셀에서 다시 가져올까요?\n(그동안 입력한 단가·확정 표시도 모두 사라집니다 — 백업으로 되돌릴 수 있음)")) return;
            _backup.CreateBackup("before_reimport");
            SeedImporter.ClearAll();
        }
        else if (File.Exists(MfgPaths.DatabaseFilePath))
        {
            _backup.CreateBackup("before_import");
        }

        _state.Settings.LastRecipeWorkbookPath = recipe;
        _state.Settings.LastProductMasterPath = pm;
        _state.Settings.Save();

        Cursor = Cursors.WaitCursor;
        try
        {
            // 매입처를 먼저 MiniERP2에서 불러와야 부자재의 매입처 자동 연결이 된다.
            var bridge = _state.Bridge;
            var supplierNote = "";
            if (bridge.IsAvailable)
            {
                var (added, _) = new SupplierRepository().Sync(bridge.GetPurchaseChannels());
                supplierNote = $"매입처 동기화: MiniERP2 매입 채널 (신규 {added})";
            }
            else
            {
                supplierNote = "MiniERP2 DB가 없어 매입처를 불러오지 못했습니다(부자재 매입처 자동 연결 생략).";
            }

            var importer = new SeedImporter();
            importer.Run(recipe, pm.Length > 0 ? pm : null, new SupplierRepository().GetAll());
            var report = BuildReport(importer.Report, supplierNote);
            var reportPath = Path.Combine(MfgPaths.AppDataFolder, $"import_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(reportPath, report);
            _state.NotifyDataChanged();
            RefreshData();
            Ui.ShowText(this, "초기 데이터 가져오기 결과", report + $"\n\n(이 리포트는 {reportPath} 에도 저장했습니다.)");
        }
        catch (Exception ex)
        {
            Utils.Log.Write($"[Import] 실패: {ex}");
            Ui.Warn(this, "가져오기 실패(아무것도 반영되지 않았습니다): " + ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string BuildReport(ImportReport r, string supplierNote)
    {
        var snap = CostSnapshot.Load();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[가져오기 완료 {DateTime.Now:yyyy-MM-dd HH:mm}]");
        sb.AppendLine(supplierNote);
        sb.AppendLine($"원료 {r.Materials} / 단가이력 {r.PriceHistory} / 혼합물 구성 {r.Mixtures} / 제조법 {r.Recipes} / 부자재 {r.Packs} / 완제품 {r.Products}");
        sb.AppendLine("모든 제조법·완제품은 '미확정'입니다. 확인하면서 확정 버튼으로 표시하세요.");
        sb.AppendLine("가격은 엑셀(VAT별도) × 1.1 = VAT포함으로 저장했습니다. VAT별도 보기로 바꾸면 엑셀 숫자와 같습니다.");
        sb.AppendLine();
        sb.AppendLine("[검증] 제조법 kg당 원료비(VAT별도): 엑셀 시트 F4 vs 이관 후 계산");
        var mismatch = 0;
        foreach (var (code, name, sheetKg, id) in r.RecipeChecks)
        {
            var recipe = snap.Recipes.FirstOrDefault(x => x.Id == id);
            if (recipe is null) continue;
            var calc = snap.Recipe(recipe, false).KgCost;
            if (Math.Abs(calc - sheetKg) > 0.01)
            {
                mismatch++;
                sb.AppendLine($"  ✗ {code} {name}: 시트 {Fmt.Won(sheetKg)} / 계산 {Fmt.Won(calc)}");
            }
        }
        sb.AppendLine(mismatch == 0 ? "  모두 일치" : $"  불일치 {mismatch}건 (아래 경고 참고)");
        sb.AppendLine();
        if (r.PacksWithoutSupplier.Count > 0)
        {
            sb.AppendLine($"[매입처 자동 연결 못 한 부자재 {r.PacksWithoutSupplier.Count}개] — 부자재 탭에서 기본 매입처를 지정하세요");
            foreach (var n in r.PacksWithoutSupplier) sb.AppendLine("  - " + n);
            sb.AppendLine();
        }
        sb.AppendLine($"[경고 {r.Warnings.Count}건]");
        foreach (var w in r.Warnings) sb.AppendLine("  - " + w);
        return sb.ToString();
    }

    private void Restore()
    {
        if (_backups.CurrentRow?.Tag is not string path) return;
        if (!Ui.Confirm(this, $"선택한 백업으로 mfgdb를 되돌릴까요?\n{Path.GetFileName(path)}\n\n현재 상태도 'before_restore' 백업으로 남깁니다.")) return;
        _backup.Restore(path);
        _state.NotifyDataChanged();
        RefreshData();
        Ui.Info(this, "복원했습니다. 열려 있는 화면은 다시 읽습니다.");
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
