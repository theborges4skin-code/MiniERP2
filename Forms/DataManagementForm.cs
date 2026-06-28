using System.Data;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.DataManagement;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 마스터SKU/CSKU/매핑규칙(1:1·임시·예외·조건부) 등 핵심 DB를 한 곳에서 백업, 엑셀 내보내기/
/// 불러오기로 관리하는 창입니다. 불러온 변경은 바로 반영되지 않고 그리드에 표시(스테이징)되며,
/// "변경내역 저장"을 눌러야 실제 DB에 반영됩니다. 저장 직전에는 항상 전체 DB 파일을 백업하고
/// (최근 3개만 보관), 문제가 생기면 "백업/롤백" 탭에서 이전 시점으로 되돌릴 수 있습니다.
/// </summary>
public class DataManagementForm : Form
{
    private readonly DbBackupService _backupService = new();
    private readonly ExportLogRepository _exportLogRepository = new();
    private readonly LegacyMigrationService _legacyMigrationService = new();
    private readonly SalesChannelLegacyMigrationService _channelLegacyMigrationService = new();
    private readonly AdLegacyMigrationService _adLegacyMigrationService = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    private readonly List<TableTabContext> _tableTabs = [];
    private DataGridView _backupGrid = new();
    private DataGridView _exportLogGrid = new();
    private ComboBox _adImportChannelCombo = new();
    private Label _statusLabel = new();

    public DataManagementForm()
    {
        InitializeComponent();
    }

    /// <summary>한 관리 테이블 탭의 상태(어댑터/현재 스테이징 중인 DataTable/그리드)를 담습니다.</summary>
    private class TableTabContext(IManagedDataTable adapter)
    {
        public IManagedDataTable Adapter { get; } = adapter;
        public DataTable Table { get; set; } = adapter.LoadCurrent();
        public DataGridView Grid { get; } = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true };
        public Label StatusLabel { get; } = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };
    }

    private void InitializeComponent()
    {
        Text = "데이터 관리";
        Size = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var tabControl = new TabControl { Dock = DockStyle.Fill };

        tabControl.TabPages.Add(CreateTableTab(new MasterSkuManagedTable()));
        tabControl.TabPages.Add(CreateTableTab(new CskuManagedTable()));
        tabControl.TabPages.Add(CreateTableTab(new SimpleMappingManagedTable(MappingRuleType.Exact, "1:1 매핑")));
        tabControl.TabPages.Add(CreateTableTab(new SimpleMappingManagedTable(MappingRuleType.Temp, "임시 매핑")));
        tabControl.TabPages.Add(CreateTableTab(new SimpleMappingManagedTable(MappingRuleType.Exception, "예외 매핑")));
        tabControl.TabPages.Add(CreateTableTab(new ConditionalMappingManagedTable()));
        tabControl.TabPages.Add(CreateBackupTab());
        tabControl.TabPages.Add(CreateExportLogTab());
        tabControl.TabPages.Add(CreateLegacyImportTab());

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "준비됨.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(tabControl, 0, 0);
        mainLayout.Controls.Add(_statusLabel, 0, 1);
        Controls.Add(mainLayout);

        FormClosing += OnFormClosing;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        var dirtyTabs = _tableTabs.Where(t => t.Table.GetChanges() != null).ToList();
        if (dirtyTabs.Count == 0) return;

        var names = string.Join(", ", dirtyTabs.Select(t => t.Adapter.DisplayName));
        var result = MessageBox.Show(
            $"저장하지 않은 변경사항이 있습니다({names}). 저장하지 않고 닫으시겠습니까?",
            "저장되지 않은 변경사항", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) e.Cancel = true;
    }

    // ===================== 테이블 탭(마스터SKU/CSKU/매핑규칙 공통) =====================

    private TabPage CreateTableTab(IManagedDataTable adapter)
    {
        var context = new TableTabContext(adapter);
        _tableTabs.Add(context);

        var tabPage = new TabPage(adapter.DisplayName);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnExport = new Button { Text = "엑셀 내보내기", Size = new Size(110, 30) };
        var btnImport = new Button { Text = "엑셀 불러오기", Size = new Size(110, 30) };
        var btnSave = new Button { Text = "변경내역 저장", Size = new Size(110, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnDiscard = new Button { Text = "변경내역 취소", Size = new Size(110, 30) };
        var btnReload = new Button { Text = "새로고침", Size = new Size(90, 30) };

        btnExport.Click += (s, e) => OnExportClick(context);
        btnImport.Click += (s, e) => OnImportClick(context);
        btnSave.Click += (s, e) => OnSaveChangesClick(context);
        btnDiscard.Click += (s, e) => OnDiscardChangesClick(context);
        btnReload.Click += (s, e) => OnDiscardChangesClick(context);

        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnImport);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnDiscard);
        toolStrip.Controls.Add(btnReload);

        context.Grid.DataSource = context.Table;
        context.Grid.DataError += (s, e) => { e.ThrowException = false; };

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(context.Grid, 0, 1);
        layout.Controls.Add(context.StatusLabel, 0, 2);
        tabPage.Controls.Add(layout);

        UpdateTabStatus(context);
        return tabPage;
    }

    private void UpdateTabStatus(TableTabContext context)
    {
        var changes = context.Table.GetChanges();
        var rowCount = context.Table.Rows.Cast<DataRow>().Count(r => r.RowState != DataRowState.Deleted);
        context.StatusLabel.Text = changes == null
            ? $"{rowCount}건 로드됨. 변경사항 없음."
            : $"{rowCount}건 로드됨. 저장하지 않은 변경사항이 있습니다 — '변경내역 저장'을 눌러주세요.";
    }

    private void OnDiscardChangesClick(TableTabContext context)
    {
        if (context.Table.GetChanges() != null)
        {
            var result = MessageBox.Show("저장하지 않은 변경사항을 버리고 다시 불러오시겠습니까?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        context.Table = context.Adapter.LoadCurrent();
        context.Grid.DataSource = context.Table;
        UpdateTabStatus(context);
        _statusLabel.Text = $"'{context.Adapter.DisplayName}' 다시 불러왔습니다.";
    }

    private void OnSaveChangesClick(TableTabContext context)
    {
        if (context.Table.GetChanges() == null)
        {
            MessageBox.Show("저장할 변경사항이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"'{context.Adapter.DisplayName}'의 변경사항을 실제 DB에 반영하시겠습니까?\n" +
            "반영 직전에 전체 DB가 자동으로 백업됩니다(최근 3개까지 보관, '백업/롤백' 탭에서 복원 가능).",
            "변경내역 저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        try
        {
            _backupService.CreateBackup("before_save_" + SanitizeForFileName(context.Adapter.DisplayName));
            var applyResult = ManagedTableChangeApplier.Apply(context.Adapter, context.Table);

            context.Table = context.Adapter.LoadCurrent();
            context.Grid.DataSource = context.Table;
            UpdateTabStatus(context);

            _statusLabel.Text = $"'{context.Adapter.DisplayName}' 저장 완료 — 신규 {applyResult.Inserted}건, 수정 {applyResult.Updated}건, 삭제 {applyResult.Deleted}건.";
            MessageBox.Show(
                $"저장되었습니다.\n신규 {applyResult.Inserted}건 / 수정 {applyResult.Updated}건 / 삭제 {applyResult.Deleted}건",
                "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}\n\n방금 만든 백업으로 '백업/롤백' 탭에서 복원할 수 있습니다.", "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExportClick(TableTabContext context)
    {
        var allColumns = context.Table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        using var columnDialog = new ExportColumnSelectionDialog(allColumns);
        if (columnDialog.ShowDialog(this) != DialogResult.OK) return;

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"{context.Adapter.DisplayName}_{DateTime.Now:yyyyMMdd}.xlsx",
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var rowCount = ManagedTableExcelIO.Export(context.Table, columnDialog.SelectedColumns, columnDialog.FilterColumn, columnDialog.FilterValue, sfd.FileName);

            _exportLogRepository.Add(new ExportLogEntry
            {
                TableName = context.Adapter.DisplayName,
                FilePath = sfd.FileName,
                RowCount = rowCount,
                Headers = string.Join(", ", columnDialog.SelectedColumns),
            });

            _statusLabel.Text = $"'{context.Adapter.DisplayName}' {rowCount}건을 엑셀로 내보냈습니다.";
            ExportHelper.ShowPostExportDialog(this, sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportClick(TableTabContext context)
    {
        using var ofd = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*", Title = "불러올 엑셀 파일을 선택하세요" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var (_, importedRows) = ManagedTableExcelIO.Read(ofd.FileName);
            if (importedRows.Count == 0)
            {
                MessageBox.Show("엑셀 파일에서 읽을 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var keyColumns = context.Adapter.KeyColumns;
            var newRows = new List<Dictionary<string, string?>>();
            var duplicateMatches = new List<(DataRow Existing, Dictionary<string, string?> Values)>();

            foreach (var row in importedRows)
            {
                var keyValues = keyColumns.Select(k => (object)(row.GetValueOrDefault(k) ?? string.Empty)).ToArray();
                var existing = context.Table.Rows.Find(keyValues.Length == 1 ? keyValues[0] : keyValues);
                if (existing != null && existing.RowState != DataRowState.Deleted)
                {
                    duplicateMatches.Add((existing, row));
                }
                else
                {
                    newRows.Add(row);
                }
            }

            var overwrite = false;
            if (duplicateMatches.Count > 0)
            {
                var result = MessageBox.Show(
                    $"엑셀 {importedRows.Count}건 중 {duplicateMatches.Count}건이 이미 존재합니다(중복).\n" +
                    $"예: 중복 {duplicateMatches.Count}건을 덮어쓰고 신규 {newRows.Count}건도 추가\n" +
                    $"아니오: 신규 {newRows.Count}건만 추가(중복 건은 건드리지 않음)\n" +
                    "취소: 가져오기를 취소합니다.",
                    "중복 데이터 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (result == DialogResult.Cancel) return;
                overwrite = result == DialogResult.Yes;
            }

            foreach (var row in newRows)
            {
                var newRow = context.Table.NewRow();
                foreach (DataColumn column in context.Table.Columns)
                {
                    if (row.TryGetValue(column.ColumnName, out var text))
                    {
                        newRow[column.ColumnName] = ManagedTableExcelIO.ConvertValue(column.DataType, text) ?? DBNull.Value;
                    }
                }
                context.Table.Rows.Add(newRow);
            }

            if (overwrite)
            {
                foreach (var (existing, values) in duplicateMatches)
                {
                    foreach (DataColumn column in context.Table.Columns)
                    {
                        if (keyColumns.Contains(column.ColumnName)) continue; // 키 컬럼은 매칭에 쓰였으니 그대로 둔다.
                        if (values.TryGetValue(column.ColumnName, out var text))
                        {
                            existing[column.ColumnName] = ManagedTableExcelIO.ConvertValue(column.DataType, text) ?? DBNull.Value;
                        }
                    }
                }
            }

            UpdateTabStatus(context);
            var appliedDuplicates = overwrite ? duplicateMatches.Count : 0;
            _statusLabel.Text = $"엑셀 {importedRows.Count}건 중 신규 {newRows.Count}건 추가, 중복 {duplicateMatches.Count}건 중 {appliedDuplicates}건 반영 준비됨. '변경내역 저장'을 눌러야 실제 DB에 반영됩니다.";
            MessageBox.Show(
                $"신규 {newRows.Count}건, 덮어쓰기 대상 {appliedDuplicates}건이 그리드에 반영되었습니다.\n" +
                "아직 DB에는 저장되지 않았습니다 — 내용을 확인한 뒤 '변경내역 저장'을 눌러주세요.",
                "불러오기 완료(미저장)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SanitizeForFileName(string text) => string.Join("_", text.Split(Path.GetInvalidFileNameChars()));

    // ===================== 백업/롤백 탭 =====================

    private TabPage CreateBackupTab()
    {
        var tabPage = new TabPage("백업/롤백");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnBackupNow = new Button { Text = "지금 전체 백업", Size = new Size(120, 30) };
        var btnRefresh = new Button { Text = "목록 새로고침", Size = new Size(110, 30) };
        btnBackupNow.Click += (s, e) =>
        {
            try
            {
                var path = _backupService.CreateBackup("manual");
                RefreshBackupGrid();
                _statusLabel.Text = $"백업 완료: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"백업 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        btnRefresh.Click += (s, e) => RefreshBackupGrid();
        toolStrip.Controls.Add(btnBackupNow);
        toolStrip.Controls.Add(btnRefresh);

        _backupGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _backupGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "백업 파일명", DataPropertyName = "Name", Width = 350 },
            new DataGridViewTextBoxColumn { HeaderText = "생성 시각", DataPropertyName = "LastWriteTime", Width = 150 }
        );

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnRestore = new Button { Text = "선택한 시점으로 복원", Size = new Size(150, 30) };
        btnRestore.Click += OnRestoreClick;
        bottomPanel.Controls.Add(btnRestore);
        bottomPanel.Controls.Add(new Label
        {
            Text = "복원은 직전 3개 자동 백업(변경내역 저장 시) + 수동 백업 중에서 고를 수 있습니다. 복원 후에는 앱을 재시작해야 합니다.",
            AutoSize = true,
            Padding = new Padding(10, 8, 0, 0),
            ForeColor = Color.DimGray,
        });

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(_backupGrid, 0, 1);
        layout.Controls.Add(bottomPanel, 0, 2);
        tabPage.Controls.Add(layout);

        RefreshBackupGrid();
        return tabPage;
    }

    private void RefreshBackupGrid()
    {
        _backupGrid.DataSource = _backupService.GetBackups();
    }

    private void OnRestoreClick(object? sender, EventArgs e)
    {
        if (_backupGrid.SelectedRows.Count == 0 || _backupGrid.SelectedRows[0].DataBoundItem is not FileInfo backup)
        {
            MessageBox.Show("복원할 백업을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"'{backup.Name}' 시점으로 복원하시겠습니까?\n" +
            "현재 DB는 복원 직전에 다시 한번 자동 백업되지만, 그 사이에 입력한 내용은 복원 후 사라집니다.\n" +
            "복원 후에는 반드시 앱을 다시 시작해야 합니다.",
            "복원 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        try
        {
            _backupService.CreateBackup("before_restore");
            _backupService.Restore(backup.FullName);

            MessageBox.Show("복원이 완료되었습니다. 확인을 누르면 프로그램이 종료됩니다 — 다시 실행해주세요.", "복원 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"복원 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ===================== 엑셀 내보내기 로그 탭 =====================

    private TabPage CreateExportLogTab()
    {
        var tabPage = new TabPage("내보내기 로그");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnRefresh = new Button { Text = "새로고침", Size = new Size(90, 30) };
        btnRefresh.Click += (s, e) => RefreshExportLogGrid();
        toolStrip.Controls.Add(btnRefresh);

        _exportLogGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        _exportLogGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "시각", DataPropertyName = "ExportedAt", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "테이블", DataPropertyName = "TableName", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "파일", DataPropertyName = "FilePath", Width = 300 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", DataPropertyName = "RowCount", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "출력 항목", DataPropertyName = "Headers", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(_exportLogGrid, 0, 1);
        tabPage.Controls.Add(layout);

        RefreshExportLogGrid();
        return tabPage;
    }

    private void RefreshExportLogGrid()
    {
        _exportLogGrid.DataSource = _exportLogRepository.GetRecent();
    }

    // ===================== 레거시 가져오기 탭 =====================

    /// <summary>
    /// 다른 화면(채널설정창/광고매핑창)에 흩어져 있던 레거시 이관 진입점을 한 곳에 모아둔다 —
    /// 사용자가 "데이터 관리창에 폴더 불러오기가 없다"고 지적한 것을 반영. 이 탭이 다루는 3가지는
    /// 서로 다른 소스/대상이라 섹션을 나눠 보여준다.
    /// </summary>
    private TabPage CreateLegacyImportTab()
    {
        var tabPage = new TabPage("레거시 가져오기");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

        layout.Controls.Add(CreateLegacyImportSection(
            "구버전 MiniERP(V3) 데이터 가져오기",
            "구버전 C# MiniERP(V3)의 ERP_Database.sqlite 파일 1개를 선택하세요. 채널/마스터SKU/CSKU(채널별 납품가)/" +
            "매핑규칙(1:1·예외·조건부)을 이 창의 각 탭에 바로 반영합니다. 기존 채널코드/SKU는 갱신되고, 1:1/예외 규칙은 " +
            "병합되지만 조건부 매핑 규칙은 재실행 시 중복 추가될 수 있으니 같은 DB로 두 번 가져오지 마세요.",
            "파일 선택...", OnLegacySqliteImportClick), 0, 0);

        layout.Controls.Add(CreateLegacyImportSection(
            "SalesManagerV2 채널 설정 가져오기",
            "SalesManagerV2의 config 폴더(channels_config.json이 있는 폴더)를 선택하세요. 채널별 정산서 매핑/환율/" +
            "채널유형/쿠팡그로스 보조소스를 채널설정에 반영합니다(이름이 일치하는 채널은 갱신, 없으면 새로 만듦).",
            "폴더 선택...", OnLegacyChannelConfigImportClick), 0, 1);

        layout.Controls.Add(CreateAdLegacyImportSection(), 0, 2);

        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private Control CreateLegacyImportSection(string title, string description, string buttonText, EventHandler onClick)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        inner.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
        var button = new Button { Text = buttonText, Dock = DockStyle.Top, Height = 30 };
        button.Click += onClick;
        inner.Controls.Add(button, 1, 0);

        group.Controls.Add(inner);
        return group;
    }

    private Control CreateAdLegacyImportSection()
    {
        var group = new GroupBox { Text = "SalesManagerV2 광고매핑 가져오기", Dock = DockStyle.Fill };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        inner.Controls.Add(new Label
        {
            Text = "같은 config 폴더의 ad_condition_rules.json/ad_exception_rules.json을 가져올 채널을 먼저 고르세요" +
                   "(레거시 파일엔 채널코드가 없는 공용 규칙이라 한 채널로 모두 들어갑니다).",
            Dock = DockStyle.Fill,
            AutoSize = false,
        }, 0, 0);

        var channels = new List<SalesChannel> { new() { ChannelCode = "", ChannelName = "(채널 선택)" } };
        channels.AddRange(_salesChannelRepository.GetAll());
        _adImportChannelCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        _adImportChannelCombo.DataSource = channels;
        _adImportChannelCombo.DisplayMember = "ChannelName";
        _adImportChannelCombo.ValueMember = "ChannelCode";
        inner.Controls.Add(_adImportChannelCombo, 1, 0);

        var button = new Button { Text = "폴더 선택...", Dock = DockStyle.Top, Height = 30 };
        button.Click += OnLegacyAdImportClick;
        inner.Controls.Add(button, 2, 0);

        group.Controls.Add(inner);
        return group;
    }

    private void OnLegacySqliteImportClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "SQLite DB (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            Title = "구버전 MiniERP(V3) ERP_Database.sqlite 파일을 선택하세요",
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        if (MessageBox.Show(
                "선택한 구버전 DB의 채널/마스터SKU/매핑규칙(1:1/예외/조건부)/CSKU(채널별 납품가)를 가져옵니다.\n" +
                "조건부 매핑 규칙은 재실행 시 중복 추가될 수 있으니 같은 DB로 두 번 가져오지 마세요. 계속하시겠습니까?",
                "가져오기 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var result = _legacyMigrationService.Migrate(ofd.FileName);
            foreach (var context in _tableTabs)
            {
                context.Table = context.Adapter.LoadCurrent();
                context.Grid.DataSource = context.Table;
                UpdateTabStatus(context);
            }

            MessageBox.Show(
                $"가져오기 완료.\n\n채널: {result.ChannelsImported}개 / 마스터SKU: {result.ItemsImported}개 / " +
                $"임시SKU(레거시): {result.TempSkusImported}개 / CSKU(채널별 납품가): {result.ChannelSkusImported}건 / " +
                $"매핑 규칙(1:1+예외): {result.RulesImported}건 / 조건부 매핑 규칙: {result.ConditionRulesImported}건",
                "가져오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statusLabel.Text = "구버전 MiniERP(V3) 데이터를 가져왔습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"가져오는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnLegacyChannelConfigImportClick(object? sender, EventArgs e)
    {
        using var folderDialog = new FolderBrowserDialog { Description = "SalesManagerV2의 config 폴더(channels_config.json이 있는 폴더)를 선택하세요" };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        if (MessageBox.Show(
                "channels_config.json의 채널별 정산서 매핑/환율/채널유형/쿠팡그로스 보조소스를 이식합니다.\n" +
                "채널명이 일치하는 기존 채널은 설정이 덮어써지고, 없는 채널명은 새로 만들어집니다. 계속하시겠습니까?",
                "이관 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var result = _channelLegacyMigrationService.Migrate(folderDialog.SelectedPath);

            var message = $"신규 채널 {result.CreatedChannels.Count}개, 기존 채널 갱신 {result.UpdatedChannels.Count}개를 이관했습니다.";
            if (result.CreatedChannels.Count > 0) message += $"\n\n신규: {string.Join(", ", result.CreatedChannels)}";
            if (result.UpdatedChannels.Count > 0) message += $"\n갱신: {string.Join(", ", result.UpdatedChannels)}";
            if (result.UnsupportedConditionalFields.Count > 0)
            {
                message += $"\n\n다음 항목은 자동 이관하지 못했습니다(직접 확인 필요):\n{string.Join(", ", result.UnsupportedConditionalFields)}";
            }
            if (result.Warnings.Count > 0) message += $"\n\n{string.Join("\n", result.Warnings)}";

            // 2026-06-28 점검: 이 모달은 직전에 그리드를 새로 그리거나 다른 창을 막 닫는 등의
            // 위험 패턴 없이(중간에 실제 이관 작업이라는 시간이 끼어있어) 뜨는 것이라, 다른
            // 화면들에서 재현됐던 경쟁 상태와는 모양이 달라 모달을 그대로 유지한다. 내용이 길어
            // (건너뛴 항목 목록 등) 한 줄짜리 상태표시줄로는 잘려 보일 수 있어 모달이 더 적합하다.
            MessageBox.Show(message, "이관 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statusLabel.Text = "SalesManagerV2 채널 설정을 가져왔습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이관 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLegacyAdImportClick(object? sender, EventArgs e)
    {
        var channelCode = _adImportChannelCombo.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 규칙을 이관할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var folderDialog = new FolderBrowserDialog { Description = "SalesManagerV2의 config 폴더(ad_condition_rules.json 등이 있는 폴더)를 선택하세요" };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var channelName = (_adImportChannelCombo.SelectedItem as SalesChannel)?.ChannelName ?? channelCode;
        if (MessageBox.Show(
                $"'{channelName}' 채널로 광고매핑 조건부/예외 규칙을 이관합니다(레거시 파일엔 채널코드가 없어 전체 규칙을 이 채널로 가져옵니다). 계속하시겠습니까?",
                "이관 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var result = _adLegacyMigrationService.Migrate(folderDialog.SelectedPath, channelCode);

            var message = $"조건부 매핑 {result.ConditionRulesImported}건, 예외처리 {result.ExceptionRulesImported}건, " +
                           $"채널 필드 매핑 {result.ChannelFieldMappingsImported}건을 가져왔습니다.";
            if (result.UnmatchedChannelNames.Count > 0)
            {
                message += $"\n\n다음 레거시 채널명은 현재 채널 목록과 일치하지 않아 필드 매핑을 건너뛰었습니다:\n{string.Join(", ", result.UnmatchedChannelNames.Distinct())}";
            }
            if (result.UntranslatedHeaders.Count > 0)
            {
                message += $"\n\n다음 조건 헤더는 표준 필드로 자동 번역하지 못했습니다(광고매핑창의 조건부 매핑(상세) 탭에서 확인 필요):\n{string.Join(", ", result.UntranslatedHeaders.Distinct())}";
            }

            // 위 채널설정 이관과 같은 이유로 모달을 유지한다.
            MessageBox.Show(message, "이관 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statusLabel.Text = $"'{channelName}' 채널로 광고매핑 데이터를 가져왔습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이관 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
