using System.Data;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.DataManagement;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// [데이터 관리] — MiniERP2 데이터 관리창과 같은 파일 일괄수정 기능(사용자 요청 2026-10-21).
/// 표마다: 엑셀 내보내기(열 선택·필터) → 엑셀에서 고치기 → 엑셀 불러오기(신규 추가 / 중복 덮어쓰기 선택, 미저장 스테이징)
/// → 화면에서 확인 → [변경내역 저장](저장 직전 mfgdb 자동 백업, 한 트랜잭션). 그리드에서 직접 고치거나 붙여넣기도 된다.
/// 내보내기 기록과 백업/롤백 탭도 같이 둔다.
/// </summary>
public class DataManagementTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly List<TableContext> _contexts = [];
    private readonly DataGridView _logGrid = Ui.Grid();
    private readonly BackupPanel _backupPanel;
    private readonly Label _status = Ui.Label("준비됨.");

    private sealed class TableContext(ManagedTable adapter)
    {
        public ManagedTable Adapter { get; } = adapter;
        public DataTable? Table { get; set; }
        public ExcelLikeGrid Grid { get; } = new();
        public TextBox Search { get; } = new() { Width = 180, PlaceholderText = "검색...", Margin = new Padding(8, 6, 0, 0) };
        public Label Status { get; } = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        public bool Stale { get; set; } = true;
    }

    public DataManagementTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        _backupPanel = new BackupPanel(state);

        foreach (var adapter in MfgTables.All()) _tabs.TabPages.Add(CreateTableTab(adapter));

        _logGrid.Columns.AddRange(Ui.Col("time", "시각", 150), Ui.Col("table", "표", 110), Ui.Col("rows", "건수", 70, right: true),
            Ui.Col("file", "파일", 420), Ui.Col("headers", "내보낸 열", 400));
        var logPage = new TabPage("내보내기 기록");
        logPage.Controls.Add(_logGrid);
        _tabs.TabPages.Add(logPage);
        var backupPage = new TabPage("백업/롤백");
        backupPage.Controls.Add(_backupPanel);
        _tabs.TabPages.Add(backupPage);
        _tabs.SelectedIndexChanged += (_, _) => LoadSelected();

        Controls.Add(_tabs);
        Controls.Add(_status);
        _status.Dock = DockStyle.Bottom;
        _state.DataChanged += () => { foreach (var c in _contexts) if (c.Table?.GetChanges() is null) c.Stale = true; };
    }

    public void RefreshData() => LoadSelected();

    /// <summary>저장 안 한 변경이 있는 표 이름들(닫기·탭 이동 전 확인용).</summary>
    public IReadOnlyList<string> DirtyTables => _contexts.Where(c => c.Table?.GetChanges() is not null).Select(c => c.Adapter.DisplayName).ToList();

    private TabPage CreateTableTab(ManagedTable adapter)
    {
        var ctx = new TableContext(adapter);
        _contexts.Add(ctx);
        var page = new TabPage(adapter.DisplayName) { Tag = ctx };

        var save = Ui.Button("변경내역 저장", (_, _) => Save(ctx));
        save.Font = new Font(save.Font, FontStyle.Bold);
        var bar = Ui.Bar(Ui.Button("엑셀 내보내기", (_, _) => Export(ctx)), Ui.Button("엑셀 불러오기", (_, _) => Import(ctx)), save,
            Ui.Button("변경내역 취소", (_, _) => Discard(ctx)), Ui.Button("새로고침", (_, _) => Discard(ctx)), Ui.Label("검색:"), ctx.Search);
        var desc = new Label { Text = adapter.Description, Dock = DockStyle.Top, AutoSize = false, Height = 36, ForeColor = Theme.MutedFore, Padding = new Padding(6, 2, 6, 2) };
        ctx.Search.TextChanged += (_, _) => ApplySearch(ctx);
        ctx.Grid.AllowUserToAddRows = adapter.AllowInsert;
        ctx.Grid.AllowUserToDeleteRows = adapter.AllowDelete;
        ctx.Grid.CellEndEdit += (_, _) => UpdateStatus(ctx);
        ctx.Grid.UserDeletedRow += (_, _) => UpdateStatus(ctx);

        page.Controls.Add(ctx.Grid);
        page.Controls.Add(ctx.Status);
        page.Controls.Add(desc);
        page.Controls.Add(bar);
        return page;
    }

    private void LoadSelected()
    {
        var page = _tabs.SelectedTab;
        if (page?.Tag is TableContext ctx)
        {
            if (ctx.Table is null || (ctx.Stale && ctx.Table.GetChanges() is null)) Reload(ctx);
        }
        else if (page?.Text == "내보내기 기록") LoadLog();
        else if (page?.Text == "백업/롤백") _backupPanel.RefreshList();
    }

    private void Reload(TableContext ctx)
    {
        ctx.Table = ctx.Adapter.LoadCurrent();
        ctx.Stale = false;
        ctx.Grid.DataSource = ctx.Table.DefaultView;
        foreach (DataGridViewColumn col in ctx.Grid.Columns)
        {
            var info = ctx.Adapter.InfoColumns.Contains(col.DataPropertyName) || (col.DataPropertyName == ManagedTable.IdColumn) ||
                       (!ctx.Adapter.AllowInsert && ctx.Adapter.KeyColumns.Contains(col.DataPropertyName));
            col.ReadOnly = info;
            if (info) Theme.ApplyReadOnly(col.DefaultCellStyle);
            else Theme.ClearColors(col.DefaultCellStyle);
            if (col.ValueType == typeof(double)) col.DefaultCellStyle.Format = "#,##0.####";
        }
        ApplySearch(ctx);
        UpdateStatus(ctx);
    }

    private static void ApplySearch(TableContext ctx)
    {
        if (ctx.Table is null) return;
        var text = ctx.Search.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        ctx.Table.DefaultView.RowFilter = text.Length == 0 ? string.Empty
            : string.Join(" OR ", ctx.Table.Columns.Cast<DataColumn>().Where(c => c.DataType == typeof(string)).Select(c => $"[{c.ColumnName}] LIKE '%{text}%'"));
    }

    private static void UpdateStatus(TableContext ctx)
    {
        if (ctx.Table is null) return;
        var count = ctx.Table.Rows.Cast<DataRow>().Count(r => r.RowState != DataRowState.Deleted);
        var changes = ctx.Table.GetChanges();
        ctx.Status.Text = changes is null
            ? $"{count}건 로드됨. 변경사항 없음."
            : $"{count}건 로드됨. 저장하지 않은 변경 {changes.Rows.Count}건 — '변경내역 저장'을 눌러야 DB에 반영됩니다.";
        ctx.Status.ForeColor = changes is null ? Theme.TextFore : Theme.WarnFore;
    }

    private void Export(TableContext ctx)
    {
        if (ctx.Table is null) return;
        ctx.Grid.EndEdit();
        var columns = ctx.Table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        using var dlg = new ExportColumnSelectionDialog(columns);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var save = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx", FileName = $"MFG_{ctx.Adapter.DisplayName}_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = ManagedTableExcelIO.Export(ctx.Table, dlg.SelectedColumns, dlg.FilterColumn, dlg.FilterValue, save.FileName, ctx.Adapter.InfoColumns);
            using (var c = MfgDb.Open())
            {
                MfgDb.Exec(c, "INSERT INTO ExportLog(ExportedAt,TableName,FilePath,RowCount,Headers) VALUES($t,$n,$f,$r,$h)", null,
                    ("$t", MfgDb.Now()), ("$n", ctx.Adapter.DisplayName), ("$f", save.FileName), ("$r", count), ("$h", string.Join(", ", dlg.SelectedColumns)));
            }
            _status.Text = $"'{ctx.Adapter.DisplayName}' {count}건을 엑셀로 내보냈습니다: {save.FileName}";
            if (Ui.Confirm(this, $"{count}건을 내보냈습니다.\n{save.FileName}\n\n파일을 열까요?", "내보내기 완료"))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(save.FileName) { UseShellExecute = true });
            }
        }
        catch (IOException ex)
        {
            Ui.Warn(this, $"저장하지 못했습니다(같은 이름의 파일이 엑셀에 열려 있으면 닫고 다시 시도하세요).\n{ex.Message}");
        }
    }

    private void Import(TableContext ctx)
    {
        if (ctx.Table is null) return;
        ctx.Grid.EndEdit();
        using var ofd = new OpenFileDialog { Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv", Title = "불러올 엑셀/CSV 파일을 선택하세요" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (headers, rows) = ManagedTableExcelIO.Read(ofd.FileName);
            if (rows.Count == 0) { Ui.Info(this, "파일에서 읽을 데이터가 없습니다."); return; }
            var plan = ManagedTableImporter.Analyze(ctx.Adapter, ctx.Table, headers, rows);
            if (plan.Errors.Count > 0)
            {
                Ui.ShowText(this, "불러오기 오류 — 아무것도 반영하지 않았습니다", string.Join(Environment.NewLine, plan.Errors));
                return;
            }

            var overwrite = false;
            if (plan.Matches.Count > 0)
            {
                var answer = MessageBox.Show(this,
                    $"파일 {rows.Count}건 중 {plan.Matches.Count}건이 이미 있는 행입니다(Id 또는 {string.Join("/", ctx.Adapter.FallbackKeyColumns.DefaultIfEmpty("키"))}로 매칭).\n" +
                    $"예: 기존 {plan.Matches.Count}건을 파일 값으로 덮어쓰고 신규 {plan.NewRows.Count}건도 추가\n" +
                    $"아니오: 신규 {plan.NewRows.Count}건만 추가(기존 행은 건드리지 않음)\n취소: 불러오기 취소",
                    "중복 데이터 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                overwrite = answer == DialogResult.Yes;
            }

            var (added, overwritten) = ManagedTableImporter.Stage(ctx.Adapter, ctx.Table, plan, overwrite);
            UpdateStatus(ctx);
            var unknown = plan.UnknownHeaders.Count > 0 ? $"\n\n알 수 없는 열(무시함): {string.Join(", ", plan.UnknownHeaders)}" : "";
            _status.Text = $"'{ctx.Adapter.DisplayName}' 파일 {rows.Count}건: 신규 {added}건, 바뀐 기존 행 {overwritten}건 반영 준비됨(미저장).";
            Ui.Info(this, $"신규 {added}건, 값이 바뀐 기존 행 {overwritten}건을 화면에 반영했습니다.\n아직 DB에는 저장되지 않았습니다 — 확인한 뒤 '변경내역 저장'을 누르세요.{unknown}", "불러오기 완료(미저장)");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Ui.Warn(this, $"파일을 읽는 중 오류가 발생했습니다(엑셀에서 열려 있으면 닫고 다시 시도).\n{ex.Message}");
        }
    }

    private void Save(TableContext ctx)
    {
        if (ctx.Table is null) return;
        ctx.Grid.EndEdit();
        // 그리드에서 편집 중이던 행을 DataTable에 확정한다(안 하면 마지막으로 고친 행이 변경분에서 빠질 수 있음).
        if (ctx.Grid.BindingContext?[ctx.Table.DefaultView] is CurrencyManager cm) cm.EndCurrentEdit();
        var changes = ctx.Table.GetChanges();
        if (changes is null) { Ui.Info(this, "저장할 변경사항이 없습니다."); return; }
        if (!Ui.Confirm(this, $"'{ctx.Adapter.DisplayName}' 변경 {changes.Rows.Count}건을 mfgdb에 반영할까요?\n반영 직전에 mfgdb 전체를 자동 백업합니다(백업/롤백 탭에서 복원 가능).", "변경내역 저장 확인")) return;
        try
        {
            new MfgBackupService().CreateBackup("before_save_" + string.Join("_", ctx.Adapter.DisplayName.Split(Path.GetInvalidFileNameChars())));
            var result = ManagedTableChangeApplier.Apply(ctx.Adapter, ctx.Table);
            Reload(ctx);
            _state.NotifyDataChanged();
            _status.Text = $"'{ctx.Adapter.DisplayName}' 저장 완료 — 신규 {result.Inserted} / 수정 {result.Updated} / 삭제 {result.Deleted}";
            Ui.Info(this, $"저장했습니다.\n신규 {result.Inserted}건 / 수정 {result.Updated}건 / 삭제 {result.Deleted}건", "저장 완료");
        }
        catch (InvalidOperationException ex)
        {
            Ui.Warn(this, ex.Message, "저장 오류");
        }
    }

    private void Discard(TableContext ctx)
    {
        if (ctx.Table?.GetChanges() is not null && !Ui.Confirm(this, "저장하지 않은 변경사항을 버리고 다시 불러올까요?")) return;
        Reload(ctx);
        _status.Text = $"'{ctx.Adapter.DisplayName}' 다시 불러왔습니다.";
    }

    private void LoadLog()
    {
        _logGrid.Rows.Clear();
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM ExportLog ORDER BY Id DESC LIMIT 200");
        using var r = cmd.ExecuteReader();
        while (r.Read()) _logGrid.Rows.Add(r.Str("ExportedAt"), r.Str("TableName"), r.Lng("RowCount"), r.Str("FilePath"), r.Str("Headers"));
    }
}
