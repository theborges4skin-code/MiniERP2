using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 레거시 거래명세표 마이그레이션으로 적재된 과거 발행건을 거래처·발행일 기간으로 조회하고,
/// 선택한 건을 엑셀(사내 재현 양식)로 다시 내보낸다. 마이그레이션 다이얼로그(스캔→검토→커밋)는
/// 1회성 이관용이고, 이 창은 이관 이후 상시 조회/백업용이다.
/// </summary>
public class DocStatementBrowserForm : Form
{
    private readonly DocStatementRepository _statementRepo = new();
    private readonly DocPartyRepository _partyRepo = new();
    private readonly SettingsService _settingsService = new();

    private ComboBox _partyComboBox = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ExcelLikeDataGridView _grid = new();
    private Label _statusLabel = new();

    private Dictionary<int, DocParty> _partiesById = new();

    public DocStatementBrowserForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        Load += (s, e) => OnLoadClick(this, EventArgs.Empty);
    }

    private void InitializeComponent()
    {
        Text = "거래명세표 조회/내보내기";
        Size = new Size(1150, 650);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _partiesById = _partyRepo.GetAll().ToDictionary(p => p.Id);
        var parties = new List<DocParty> { new() { Id = 0, CompanyName = "(전체)" } };
        parties.AddRange(_partiesById.Values.OrderBy(p => p.CompanyName));
        _partyComboBox = new ComboBox { Size = new Size(180, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _partyComboBox.DataSource = parties;
        _partyComboBox.DisplayMember = "CompanyName";
        _partyComboBox.ValueMember = "Id";

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        var btnQuickDate = DateRangeQuickSelect.CreateButton(_fromDatePicker, _toDatePicker);
        // 이관된 과거 문서는 시작시점을 특정할 수 없어, 표준 6종 외에 "전체기간"을 추가로 제공한다.
        DateRangeQuickSelect.AddExtraItem(btnQuickDate, "전체기간", (_, _) =>
        {
            _fromDatePicker.Value = new DateTime(2000, 1, 1);
            _toDatePicker.Value = DateTime.Today;
        });

        var btnLoad = new Button { Text = "조회", Size = new Size(80, 30) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };
        btnLoad.Click += OnLoadClick;
        btnExport.Click += OnExportClick;

        toolStrip.Controls.Add(new Label { Text = "거래처:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_partyComboBox);
        toolStrip.Controls.Add(new Label { Text = "발행일:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnQuickDate);
        toolStrip.Controls.Add(btnLoad);
        toolStrip.Controls.Add(btnExport);

        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "DocStatementBrowserForm.Grid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "거래처명", Name = "PartyName", DataPropertyName = "PartyName", Width = 160, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "등록번호", Name = "PartyRegNo", DataPropertyName = "PartyRegNo", Width = 100, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "발행일", Name = "IssueDateText", DataPropertyName = "IssueDateText", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "발행년월", Name = "IssueYearMonth", DataPropertyName = "IssueYearMonth", Width = 80, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "총공급가액", Name = "TotalSupply", DataPropertyName = "TotalSupply", Width = 100, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "총세액", Name = "TotalTax", DataPropertyName = "TotalTax", Width = 90, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "총합계", Name = "TotalAmount", DataPropertyName = "TotalAmount", Width = 100, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "총수량", Name = "TotalQty", DataPropertyName = "TotalQty", Width = 70, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "시그니처", Name = "TemplateSignature", DataPropertyName = "TemplateSignature", Width = 70, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "원본파일명", Name = "SourceFileName", DataPropertyName = "SourceFileName", Width = 150, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "원본시트명", Name = "SourceSheetName", DataPropertyName = "SourceSheetName", Width = 140, ReadOnly = true }
        );

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "조회 버튼을 눌러 이관된 거래명세표를 불러오세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(mainLayout);
    }

    private void OnLoadClick(object? sender, EventArgs e)
    {
        _partiesById = _partyRepo.GetAll().ToDictionary(p => p.Id);
        int? partyId = _partyComboBox.SelectedValue is int id && id != 0 ? id : null;
        var from = _fromDatePicker.Value.Date;
        var to = _toDatePicker.Value.Date;

        var statements = _statementRepo.GetFiltered(partyId, from, to);
        var rows = statements.Select(s => new StatementRow(s, _partiesById.GetValueOrDefault(s.PartyId))).ToList();
        _grid.DataSource = new BindingList<StatementRow>(rows);
        _statusLabel.Text = $"{rows.Count}건 조회됨.";
    }

    /// <summary>OutboundHistoryForm의 선택 수집 패턴과 동일 — 행 머리글 선택과 셀 선택을 모두 허용한다.</summary>
    private List<StatementRow> GetSelectedRows()
    {
        var rowIndices = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index)
            .Union(_grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex))
            .Distinct();
        return rowIndices.Where(i => i >= 0 && i < _grid.Rows.Count && !_grid.Rows[i].IsNewRow)
            .Select(i => _grid.Rows[i].DataBoundItem).OfType<StatementRow>().ToList();
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0)
        {
            if (_grid.Rows.Count == 0)
            {
                MessageBox.Show("내보낼 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show($"선택된 항목이 없습니다. 조회된 전체 {_grid.Rows.Count}건을 내보낼까요?",
                    "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            selected = _grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => r.DataBoundItem).OfType<StatementRow>().ToList();
        }

        var defaultSupplier = _partyRepo.GetDefaultSupplier() ?? new DocParty();

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"거래명세표_이관재현_{DateTime.Today:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("DocStatementExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("DocStatementExport", Path.GetDirectoryName(filePath)!);

        try
        {
            var items = selected.Select(row =>
            {
                var lines = _statementRepo.GetLines(row.Statement.Id);
                var buyer = _partiesById.GetValueOrDefault(row.Statement.PartyId) ?? new DocParty();
                return new LegacyStatementExportItem(row.Statement, defaultSupplier, buyer, lines);
            }).ToList();

            DocumentExporter.ExportLegacyStatements(items, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
            _statusLabel.Text = $"{items.Count}건 내보내기 완료.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"내보내기 실패: {ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed class StatementRow(DocStatement statement, DocParty? party)
    {
        public DocStatement Statement { get; } = statement;
        public string PartyName { get; } = string.IsNullOrWhiteSpace(party?.CompanyName) ? "(식별불가)" : party!.CompanyName;
        public string PartyRegNo { get; } = party?.RegNo ?? "";
        public string IssueDateText { get; } = statement.IssueDate?.ToString("yyyy-MM-dd") ?? "";
        public string IssueYearMonth { get; } = statement.IssueYearMonth;
        public decimal TotalSupply { get; } = statement.TotalSupply;
        public decimal TotalTax { get; } = statement.TotalTax;
        public decimal TotalAmount { get; } = statement.TotalAmount;
        public decimal TotalQty { get; } = statement.TotalQty;
        public string TemplateSignature { get; } = statement.TemplateSignature;
        public string SourceFileName { get; } = statement.SourceFileName;
        public string SourceSheetName { get; } = statement.SourceSheetName;
    }
}
