using System.Data;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.DataManagement;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
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
    private readonly MappingRepository _mappingRepository = new();
    private readonly PurchaseSkuRepository _purchaseSkuRepository = new();

    private readonly List<TableTabContext> _tableTabs = [];
    private TabControl _tabControl = new();
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
        public TextBox SearchBox { get; } = new() { Width = 160, Margin = new Padding(8, 5, 0, 0), PlaceholderText = "검색..." };
    }

    private void InitializeComponent()
    {
        Text = "데이터 관리";
        Size = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        _tabControl = new TabControl { Dock = DockStyle.Fill };

        _tabControl.TabPages.Add(CreateTableTab(new MasterSkuManagedTable(), ConfigureMasterSkuTab));
        _tabControl.TabPages.Add(CreateCskuTabWithRules());
        _tabControl.TabPages.Add(CreatePurchaseSkuTabWithHistory());
        _tabControl.TabPages.Add(CreateTableTab(new SimpleMappingManagedTable(MappingRuleType.Exact, "1:1 매핑")));
        _tabControl.TabPages.Add(CreateTableTab(new SimpleMappingManagedTable(MappingRuleType.Temp, "임시 매핑")));
        _tabControl.TabPages.Add(CreateTableTab(new SimpleMappingManagedTable(MappingRuleType.Exception, "예외 매핑")));
        _tabControl.TabPages.Add(CreateTableTab(new ConditionalMappingManagedTable()));
        _tabControl.TabPages.Add(CreateTableTab(new FboCskuManagedTable()));
        _tabControl.TabPages.Add(CreateTableTab(new FboChannelConfigManagedTable()));
        _tabControl.TabPages.Add(CreateBackupTab());
        _tabControl.TabPages.Add(CreateExportLogTab());
        _tabControl.TabPages.Add(CreateLegacyImportTab());

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "준비됨.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(_tabControl, 0, 0);
        mainLayout.Controls.Add(_statusLabel, 0, 1);
        Controls.Add(mainLayout);

        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// 지정된 표시 이름의 탭으로 전환한다(예: 풀필먼트 발주처리 화면의 "품목/설정 관리" 버튼이
    /// 여기 있는 "FBO 품목마스터"/"FBO 채널설정" 탭으로 바로 이동할 때 사용).
    /// </summary>
    public void SelectTabByDisplayName(string displayName)
    {
        foreach (TabPage page in _tabControl.TabPages)
        {
            if (page.Text != displayName) continue;
            _tabControl.SelectedTab = page;
            return;
        }
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

    private TabPage CreateTableTab(IManagedDataTable adapter, Action<TableTabContext>? configure = null)
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

        context.SearchBox.TextChanged += (s, e) =>
        {
            context.Table.DefaultView.RowFilter = BuildRowFilter(context.Table, context.SearchBox.Text.Trim());
        };

        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnImport);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnDiscard);
        toolStrip.Controls.Add(btnReload);
        toolStrip.Controls.Add(new Label { Text = "검색:", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        toolStrip.Controls.Add(context.SearchBox);

        context.Grid.DataSource = context.Table.DefaultView;
        context.Grid.DataError += (s, e) => { e.ThrowException = false; };
        configure?.Invoke(context);

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(context.Grid, 0, 1);
        layout.Controls.Add(context.StatusLabel, 0, 2);
        tabPage.Controls.Add(layout);

        UpdateTabStatus(context);
        return tabPage;
    }

    /// <summary>
    /// 마스터SKU 탭 — "Sku" 열을 더블클릭하면 그 SKU에 연결된 모든 CSKU(채널 전체)를 보여주는
    /// 채널 SKU 관리창을 새 창으로 연다. 별도 화면인 MasterSkuForm의 "해당 CSKU 보기"와 같은
    /// CSkuForm을 그대로 재사용한다.
    /// </summary>
    private void ConfigureMasterSkuTab(TableTabContext context)
    {
        context.Grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (context.Grid.Rows[e.RowIndex].DataBoundItem is not DataRowView rowView) return;

            var sku = rowView.Row["Sku"]?.ToString();
            if (string.IsNullOrWhiteSpace(sku)) return;

            using var cskuForm = new CSkuForm(sku);
            FormManager.ApplyBoundsTracking(cskuForm);
            cskuForm.ShowDialog(this);
        };
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
        context.Grid.DataSource = context.Table.DefaultView;
        context.Table.DefaultView.RowFilter = BuildRowFilter(context.Table, context.SearchBox.Text.Trim());
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
            context.Grid.DataSource = context.Table.DefaultView;
            context.Table.DefaultView.RowFilter = BuildRowFilter(context.Table, context.SearchBox.Text.Trim());
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
        using var ofd = new OpenFileDialog { Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*", Title = "불러올 엑셀/CSV 파일을 선택하세요" };
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

    private static string BuildRowFilter(DataTable table, string searchText)
    {
        if (string.IsNullOrEmpty(searchText)) return string.Empty;
        var escaped = searchText.Replace("'", "''");
        var conditions = table.Columns.Cast<DataColumn>()
            .Where(c => c.DataType == typeof(string))
            .Select(c => $"[{c.ColumnName}] LIKE '%{escaped}%'");
        return string.Join(" OR ", conditions);
    }

    // ===================== CSKU 탭 (매핑 규칙 패널 포함) =====================

    private TabPage CreateCskuTabWithRules()
    {
        var adapter = new CskuManagedTable();
        var context = new TableTabContext(adapter);
        _tableTabs.Add(context);

        var tabPage = new TabPage(adapter.DisplayName);

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 350,
        };

        // ── 상단: CSKU 그리드 (CreateTableTab과 동일) ──
        var topLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnExport = new Button { Text = "엑셀 내보내기", Size = new Size(110, 30) };
        var btnImport = new Button { Text = "엑셀 불러오기", Size = new Size(110, 30) };
        var btnSave = new Button { Text = "변경내역 저장", Size = new Size(110, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnDiscard = new Button { Text = "변경내역 취소", Size = new Size(110, 30) };
        var btnReload = new Button { Text = "새로고침", Size = new Size(90, 30) };
        var btnDeleteCsku = new Button { Text = "선택한 CSKU 삭제", Size = new Size(120, 30) };

        btnExport.Click += (s, e) => OnExportClick(context);
        btnImport.Click += (s, e) => OnImportClick(context);
        btnSave.Click += (s, e) => OnSaveChangesClick(context);
        btnDiscard.Click += (s, e) => OnDiscardChangesClick(context);
        btnReload.Click += (s, e) => OnDiscardChangesClick(context);
        btnDeleteCsku.Click += (s, e) => OnDeleteCskuRowsClick(context);

        context.SearchBox.TextChanged += (s, e) =>
        {
            context.Table.DefaultView.RowFilter = BuildRowFilter(context.Table, context.SearchBox.Text.Trim());
        };

        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnImport);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnDiscard);
        toolStrip.Controls.Add(btnReload);
        toolStrip.Controls.Add(btnDeleteCsku);
        toolStrip.Controls.Add(new Label { Text = "검색:", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        toolStrip.Controls.Add(context.SearchBox);

        context.Grid.DataSource = context.Table.DefaultView;
        context.Grid.DataError += (s, e) => { e.ThrowException = false; };

        topLayout.Controls.Add(toolStrip, 0, 0);
        topLayout.Controls.Add(context.Grid, 0, 1);
        topLayout.Controls.Add(context.StatusLabel, 0, 2);
        splitContainer.Panel1.Controls.Add(topLayout);

        // ── 하단: 선택한 CSKU를 참조하는 매핑 규칙 패널 ──
        var rulesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
        };
        rulesGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "채널", DataPropertyName = "ChannelCode", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "규칙 유형", DataPropertyName = "RuleTypeLabel", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "매핑 키(상품명+옵션명)", DataPropertyName = "Key", Width = 250 },
            new DataGridViewTextBoxColumn { HeaderText = "상세 조건", DataPropertyName = "ConditionSummary", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var rulesHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 4, 0, 0) };
        var rulesHeaderLabel = new Label { Text = "이 CSKU를 참조하는 매핑 규칙 (CSKU 행을 선택하면 표시)", AutoSize = true, ForeColor = Color.DimGray };
        var btnDeleteRule = new Button { Text = "선택한 규칙 삭제", Size = new Size(120, 22), Margin = new Padding(15, 1, 0, 0) };
        rulesHeader.Controls.Add(rulesHeaderLabel);
        rulesHeader.Controls.Add(btnDeleteRule);

        bottomLayout.Controls.Add(rulesHeader, 0, 0);
        bottomLayout.Controls.Add(rulesGrid, 0, 1);
        splitContainer.Panel2.Controls.Add(bottomLayout);

        // CSKU 선택 → 규칙 목록 갱신
        var ruleItems = new List<CskuRuleItem>();
        context.Grid.SelectionChanged += (s, e) =>
        {
            ruleItems.Clear();
            if (context.Grid.CurrentRow?.DataBoundItem is not DataRowView rowView) { rulesGrid.DataSource = null; return; }
            var cskuCode = rowView.Row["CskuCode"]?.ToString();
            if (string.IsNullOrEmpty(cskuCode)) { rulesGrid.DataSource = null; return; }

            foreach (var ruleType in new[] { MappingRuleType.Exact, MappingRuleType.Temp, MappingRuleType.Exception })
            {
                var allRules = _mappingRepository.GetAllRules(ruleType);
                foreach (var rule in allRules.Where(r => string.Equals(r.TargetSku, cskuCode, StringComparison.OrdinalIgnoreCase)))
                {
                    ruleItems.Add(new CskuRuleItem(rule.Id, rule.RuleType, rule.ChannelCode, rule.Key, string.Empty));
                }
            }
            foreach (var (rule, details) in _mappingRepository.GetAllConditionRulesWithDetails()
                .Where(x => string.Equals(x.Rule.TargetSku, cskuCode, StringComparison.OrdinalIgnoreCase)))
            {
                ruleItems.Add(new CskuRuleItem(rule.Id, rule.RuleType, rule.ChannelCode, rule.Key, FormatConditionDetails(details)));
            }

            rulesGrid.DataSource = null;
            rulesGrid.DataSource = ruleItems;
        };

        btnDeleteRule.Click += (s, e) =>
        {
            if (rulesGrid.CurrentRow?.DataBoundItem is not CskuRuleItem item) return;
            var confirm = MessageBox.Show(
                $"[{item.RuleTypeLabel}] 채널 '{item.ChannelCode}' / 키 '{item.Key}' 규칙을 삭제하시겠습니까?",
                "규칙 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            if (item.RuleType == MappingRuleType.Condition)
                _mappingRepository.DeleteConditionRule(item.Id);
            else
                _mappingRepository.DeleteRule(item.RuleType, item.Id);

            ruleItems.Remove(item);
            rulesGrid.DataSource = null;
            rulesGrid.DataSource = ruleItems;
            _statusLabel.Text = "규칙을 삭제했습니다.";
        };

        tabPage.Controls.Add(splitContainer);
        UpdateTabStatus(context);
        return tabPage;
    }

    /// <summary>
    /// CSKU 그리드에서 행 헤더로 전체 행을 선택해 Delete 키를 눌러도 삭제 대상 표시는 이미
    /// 가능하지만(AllowUserToDeleteRows), 발견하기 어렵다는 지적이 있어 명시적 버튼으로 추가.
    /// 이 버튼을 눌러도 즉시 DB에서 지워지지 않고 다른 행과 동일하게 그리드에서 삭제 표시만
    /// 되며(백업/취소 가능), '변경내역 저장'을 눌러야 실제로 반영된다. 삭제하려는 CSKU를 참조하는
    /// 매핑 규칙이 있으면(하단 패널과 동일한 기준으로 조회) 규칙은 함께 지워지지 않는다는 것을
    /// 미리 경고한다.
    /// </summary>
    private void OnDeleteCskuRowsClick(TableTabContext context)
    {
        var selectedRows = context.Grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as DataRowView)
            .Where(v => v != null).Cast<DataRowView>().ToList();
        if (selectedRows.Count == 0 && context.Grid.CurrentRow?.DataBoundItem is DataRowView current)
        {
            selectedRows.Add(current);
        }
        if (selectedRows.Count == 0)
        {
            MessageBox.Show("삭제할 CSKU 행을 먼저 선택하세요(행 머리글을 클릭해 전체 행을 선택할 수 있습니다).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cskuCodes = selectedRows.Select(v => v.Row["CskuCode"]?.ToString() ?? "").Where(c => c != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var referencingRuleCount = cskuCodes.Sum(CountReferencingRules);

        var warning = referencingRuleCount > 0
            ? $"\n\n⚠ 선택한 CSKU를 참조하는 매핑 규칙이 {referencingRuleCount}건 있습니다. CSKU만 삭제되고 그 규칙들은 그대로 남아(연결 대상 없이) 유지되니, 필요하면 하단 '이 CSKU를 참조하는 매핑 규칙' 패널에서 먼저 정리하세요."
            : "";
        var confirm = MessageBox.Show(
            $"선택한 CSKU {selectedRows.Count}건을 삭제 대상으로 표시하시겠습니까?\n(실제 DB 반영은 '변경내역 저장'을 눌러야 합니다){warning}",
            "CSKU 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        foreach (var rowView in selectedRows) rowView.Row.Delete();
        UpdateTabStatus(context);
        _statusLabel.Text = $"CSKU {selectedRows.Count}건을 삭제 대상으로 표시했습니다. '변경내역 저장'을 눌러야 실제로 반영됩니다.";
    }

    private int CountReferencingRules(string cskuCode)
    {
        var count = 0;
        foreach (var ruleType in new[] { MappingRuleType.Exact, MappingRuleType.Temp, MappingRuleType.Exception })
        {
            count += _mappingRepository.GetAllRules(ruleType).Count(r => string.Equals(r.TargetSku, cskuCode, StringComparison.OrdinalIgnoreCase));
        }
        count += _mappingRepository.GetAllConditionRulesWithDetails().Count(x => string.Equals(x.Rule.TargetSku, cskuCode, StringComparison.OrdinalIgnoreCase));
        return count;
    }

    // ===================== 매입SKU 탭 (매입가 이력 패널 포함) — B2B 견적관리(M2) =====================

    private TabPage CreatePurchaseSkuTabWithHistory()
    {
        var adapter = new PurchaseSkuManagedTable();
        var context = new TableTabContext(adapter);
        _tableTabs.Add(context);

        var tabPage = new TabPage(adapter.DisplayName);

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 350,
        };

        // ── 상단: 매입SKU 그리드 (CreateTableTab과 동일) ──
        var topLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

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

        context.SearchBox.TextChanged += (s, e) =>
        {
            context.Table.DefaultView.RowFilter = BuildRowFilter(context.Table, context.SearchBox.Text.Trim());
        };

        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnImport);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnDiscard);
        toolStrip.Controls.Add(btnReload);
        toolStrip.Controls.Add(new Label { Text = "검색:", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        toolStrip.Controls.Add(context.SearchBox);

        context.Grid.DataSource = context.Table.DefaultView;
        context.Grid.DataError += (s, e) => { e.ThrowException = false; };

        topLayout.Controls.Add(toolStrip, 0, 0);
        topLayout.Controls.Add(context.Grid, 0, 1);
        topLayout.Controls.Add(context.StatusLabel, 0, 2);
        splitContainer.Panel1.Controls.Add(topLayout);

        // ── 하단: 선택한 매입SKU의 매입가 변경 이력(§M2 "매입가 이력") ──
        var historyGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
        };
        historyGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "변경일시", DataPropertyName = "ChangedAt", DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" }, Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "이전 매입가", DataPropertyName = "OldPrice", DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }, Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "새 매입가", DataPropertyName = "NewPrice", DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }, Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "사유", DataPropertyName = "Reason", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "비고", DataPropertyName = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var historyHeaderLabel = new Label { Text = "선택한 매입SKU의 매입가 변경 이력", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(5, 4, 0, 0) };
        bottomLayout.Controls.Add(historyHeaderLabel, 0, 0);
        bottomLayout.Controls.Add(historyGrid, 0, 1);
        splitContainer.Panel2.Controls.Add(bottomLayout);

        // 매입SKU 선택 → 이력 갱신
        context.Grid.SelectionChanged += (s, e) =>
        {
            if (context.Grid.CurrentRow?.DataBoundItem is not DataRowView rowView) { historyGrid.DataSource = null; return; }
            var channelCode = rowView.Row["ChannelCode"]?.ToString();
            var msku = rowView.Row["Msku"]?.ToString();
            if (string.IsNullOrEmpty(channelCode) || string.IsNullOrEmpty(msku)) { historyGrid.DataSource = null; return; }

            historyGrid.DataSource = _purchaseSkuRepository.GetPriceHistory(channelCode, msku);
        };

        tabPage.Controls.Add(splitContainer);
        UpdateTabStatus(context);
        return tabPage;
    }

    private record CskuRuleItem(long Id, MappingRuleType RuleType, string ChannelCode, string Key, string ConditionSummary)
    {
        public string RuleTypeLabel => RuleType switch
        {
            MappingRuleType.Exact => "1:1 매핑",
            MappingRuleType.Temp => "임시 매핑",
            MappingRuleType.Exception => "예외 매핑",
            MappingRuleType.Condition => "조건부 매핑",
            _ => RuleType.ToString(),
        };
    }

    private static string FormatConditionDetails(List<MappingConditionDetail> details)
    {
        if (details.Count == 0) return string.Empty;
        return string.Join(", ", details.Select((d, i) =>
        {
            var opStr = d.Operator switch
            {
                ConditionOperator.Contains => "포함",
                ConditionOperator.NotContains => "제외",
                ConditionOperator.Equals => "=",
                _ => d.Operator.ToString(),
            };
            var fieldStr = d.HeaderField switch
            {
                StdField.ProductName => "상품명",
                StdField.OptionName => "옵션명",
                StdField.Quantity => "수량",
                _ => d.HeaderField.ToString(),
            };
            var logicStr = i > 0 ? (d.Logic == ConditionLogic.And ? " AND " : " OR ") : "";
            return $"{logicStr}{fieldStr} {opStr} '{d.TargetValue}'";
        }));
    }

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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

        layout.Controls.Add(CreateLegacyImportSection(
            "구버전 MiniERP(V3) 데이터 가져오기",
            "구버전 C# MiniERP(V3)의 ERP_Database.sqlite 파일 1개를 선택하세요. 채널/마스터SKU/CSKU(채널별 납품가)/" +
            "매핑규칙(1:1·예외·조건부)을 이 창의 각 탭에 바로 반영합니다. 기존 채널코드/SKU는 갱신되고, 1:1/예외 규칙은 " +
            "병합되지만 조건부 매핑 규칙은 재실행 시 중복 추가될 수 있으니 같은 DB로 두 번 가져오지 마세요.",
            "파일 선택...", OnLegacySqliteImportClick), 0, 0);

        layout.Controls.Add(CreateChannelConfigImportSection(), 0, 1);

        layout.Controls.Add(CreateAdLegacyImportSection(), 0, 2);

        layout.Controls.Add(CreateLegacyImportSection(
            "거래명세표(엑셀) 마이그레이션",
            "과거 엑셀 거래명세표 파일들이 있는 폴더를 스캔해 거래처/발행건/품목라인을 DB로 이식합니다. " +
            "즉시 반영되지 않고 스캔 결과를 검토(포함/제외 체크)한 뒤 커밋하는 별도 창이 열립니다.",
            "마이그레이션 열기...", (s, e) => new TradeStatementMigrationDialog().ShowDialog(this)), 0, 3);

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

    private Control CreateChannelConfigImportSection()
    {
        var group = new GroupBox { Text = "SalesManagerV2 채널 설정 가져오기", Dock = DockStyle.Fill };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        inner.Controls.Add(new Label
        {
            Text = "SalesManagerV2의 config 폴더(channels_config.json이 있는 폴더)를 선택하세요. " +
                   "채널별 정산서 매핑/환율/채널유형/쿠팡그로스 보조소스를 반영합니다. " +
                   "'전체'는 모든 채널을 갱신/신규 등록하고, '선택'은 골라서 가져옵니다.",
            Dock = DockStyle.Fill, AutoSize = false,
        }, 0, 0);

        var btnAll = new Button { Text = "전체 가져오기", Dock = DockStyle.Top, Height = 30 };
        btnAll.Click += OnLegacyChannelConfigImportClick;
        inner.Controls.Add(btnAll, 1, 0);

        var btnSelect = new Button { Text = "선택 가져오기", Dock = DockStyle.Top, Height = 30 };
        btnSelect.Click += OnLegacyChannelConfigSelectiveImportClick;
        inner.Controls.Add(btnSelect, 2, 0);

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
                context.Grid.DataSource = context.Table.DefaultView;
                context.Table.DefaultView.RowFilter = BuildRowFilter(context.Table, context.SearchBox.Text.Trim());
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

    private void OnLegacyChannelConfigSelectiveImportClick(object? sender, EventArgs e)
    {
        using var folderDialog = new FolderBrowserDialog { Description = "SalesManagerV2의 config 폴더를 선택하세요" };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var names = _channelLegacyMigrationService.ReadChannelNames(folderDialog.SelectedPath);
        if (names.Count == 0)
        {
            MessageBox.Show("channels_config.json에서 채널을 읽을 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selected = ShowChannelSelectDialog(names);
        if (selected == null || selected.Count == 0) return;

        try
        {
            var result = _channelLegacyMigrationService.MigrateSelected(folderDialog.SelectedPath, selected);

            var message = $"신규 채널 {result.CreatedChannels.Count}개, 기존 채널 갱신 {result.UpdatedChannels.Count}개를 이관했습니다.";
            if (result.CreatedChannels.Count > 0) message += $"\n\n신규: {string.Join(", ", result.CreatedChannels)}";
            if (result.UpdatedChannels.Count > 0) message += $"\n갱신: {string.Join(", ", result.UpdatedChannels)}";
            if (result.UnsupportedConditionalFields.Count > 0)
                message += $"\n\n다음 항목은 자동 이관하지 못했습니다(직접 확인 필요):\n{string.Join(", ", result.UnsupportedConditionalFields)}";
            if (result.Warnings.Count > 0) message += $"\n\n{string.Join("\n", result.Warnings)}";

            MessageBox.Show(message, "이관 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statusLabel.Text = "SalesManagerV2 채널 설정(선택)을 가져왔습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이관 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private HashSet<string>? ShowChannelSelectDialog(IReadOnlyList<string> channelNames)
    {
        using var dlg = new Form
        {
            Text = "가져올 채널 선택",
            Size = new Size(340, 460),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog,
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label { Text = "가져올 채널을 선택하세요:", Dock = DockStyle.Fill }, 0, 0);

        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        foreach (var name in channelNames) list.Items.Add(name, false);
        layout.Controls.Add(list, 0, 1);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "가져오기", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 70, Height = 30 };
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);
        layout.Controls.Add(btnPanel, 0, 2);

        dlg.Controls.Add(layout);
        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCancel;

        if (dlg.ShowDialog(this) != DialogResult.OK) return null;
        return list.CheckedItems.Cast<string>().ToHashSet();
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
