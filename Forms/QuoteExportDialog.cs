using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// ⚠ 임시(실험용) — DocLineHistoryForm의 "견적서 작성함"(장바구니)에서 "견적서 작성" 버튼을 누르면
/// 뜨는 발행 다이얼로그. 필드 구성은 DocsForm의 견적서 탭(BuildQuoteRightPanel)과 동일하게
/// 맞췄고, 실제 렌더링은 DocumentExporter.ExportQuote를 그대로 호출해 DocsForm과 결과물이
/// 100% 동일하다(새 익스포터를 만들지 않는다).
/// </summary>
public class QuoteExportDialog : Form
{
    private readonly DocPartyRepository _partyRepo = new();
    private readonly List<QuoteCartLine> _lines;

    private ComboBox _supplierCombo = new();
    private TextBox _recipientBox = new();
    private TextBox _titleBox = new();
    private TextBox _priceBasisBox = new();
    private TextBox _greetingBox = new();
    private DataGridView _lineGrid = new();
    private Label _totalLabel = new();

    /// <summary>발행에 성공하면 실제로 내보낸 QuoteDoc(재적재용 데이터 원본). 취소/실패 시 null.</summary>
    public QuoteDoc? ResultDoc { get; private set; }

    /// <summary>발행에 성공한 엑셀 파일 경로. 취소/실패 시 null.</summary>
    public string? ResultFilePath { get; private set; }

    public QuoteExportDialog(List<QuoteCartLine> lines)
    {
        _lines = lines;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "견적서 발행 (임시 문서관리 메인창)";
        Size = new Size(760, 580);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var suppliers = _partyRepo.GetAll();
        _supplierCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(DocParty.CompanyName), Dock = DockStyle.Fill };
        _supplierCombo.DataSource = suppliers;
        var defaultSupplier = _partyRepo.GetDefaultSupplier();
        if (defaultSupplier != null)
        {
            var match = suppliers.FirstOrDefault(p => p.Id == defaultSupplier.Id);
            if (match != null) _supplierCombo.SelectedItem = match;
        }

        _recipientBox = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "수신처명" };
        _titleBox = new TextBox { Dock = DockStyle.Fill, Text = "견 적 서" };
        _priceBasisBox = new TextBox { Dock = DockStyle.Fill, Text = "VAT 포함" };
        _greetingBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "인사문 — 한 줄에 하나씩(생략 시 기본 문구 사용)" };

        layout.Controls.Add(FieldRow("공급자", _supplierCombo), 0, 0);
        layout.Controls.Add(FieldRow("수신처", _recipientBox), 0, 1);
        layout.Controls.Add(FieldRow("문서제목", _titleBox), 0, 2);
        layout.Controls.Add(FieldRow("가격기준", _priceBasisBox), 0, 3);
        layout.Controls.Add(FieldRow("인사문", _greetingBox), 0, 4);

        _lineGrid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
        };
        _lineGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ItemName", DataPropertyName = "ItemName", Width = 180, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "단위", Name = "Unit", DataPropertyName = "Unit", Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "단가", Name = "UnitPrice", DataPropertyName = "UnitPrice", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "비고", Name = "Note", DataPropertyName = "Note", Width = 140, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _lineGrid.DataSource = new BindingList<QuoteCartLine>(_lines);
        _lineGrid.CellValueChanged += (s, e) => UpdateTotalLabel();
        _lineGrid.CellEndEdit += (s, e) => _lineGrid.InvalidateRow(e.RowIndex);

        layout.Controls.Add(_lineGrid, 0, 5);

        var bottomBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnIssue = new Button { Text = "발행", Size = new Size(90, 30) };
        var btnCancel = new Button { Text = "취소", Size = new Size(90, 30) };
        btnIssue.Click += OnIssueClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        _totalLabel = new Label { AutoSize = true, Padding = new Padding(4, 8, 0, 0) };
        bottomBar.Controls.Add(btnCancel);
        bottomBar.Controls.Add(btnIssue);
        bottomBar.Controls.Add(_totalLabel);
        layout.Controls.Add(bottomBar, 0, 6);

        Controls.Add(layout);
        UpdateTotalLabel();
    }

    private static Control FieldRow(string label, Control input)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        row.Controls.Add(input, 1, 0);
        return row;
    }

    private void UpdateTotalLabel()
    {
        var total = _lines.Sum(l => Math.Round(l.Qty * l.UnitPrice, 0, MidpointRounding.AwayFromZero));
        _totalLabel.Text = $"줄 {_lines.Count}건 / 합계(세전) {total:N0}원";
    }

    private void OnIssueClick(object? sender, EventArgs e)
    {
        _lineGrid.EndEdit();

        if (string.IsNullOrWhiteSpace(_recipientBox.Text))
        {
            MessageBox.Show("수신처를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_lines.Count == 0)
        {
            MessageBox.Show("담긴 품목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_supplierCombo.SelectedItem is not DocParty supplier)
        {
            MessageBox.Show("공급자를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var doc = new QuoteDoc
        {
            DocType = DocType.QuoteWithQty,
            Supplier = supplier,
            RecipientName = _recipientBox.Text.Trim(),
            DocTitle = _titleBox.Text.Trim().Length > 0 ? _titleBox.Text.Trim() : "견 적 서",
            HeaderText = _greetingBox.Text.Trim(),
            PriceBasis = _priceBasisBox.Text.Trim().Length > 0 ? _priceBasisBox.Text.Trim() : "VAT 포함",
            IssueDate = DateTime.Today,
        };
        foreach (var line in _lines)
        {
            doc.Lines.Add(new QuoteLineItem
            {
                ItemName = line.ItemName,
                Unit = line.Unit,
                Packing = line.Packing,
                UnitPrice = line.UnitPrice,
                Qty = line.Qty,
                Note = line.Note,
            });
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"견적서_{doc.RecipientName}_{DateTime.Today:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            DocumentExporter.ExportQuote(doc, filePath);
            ResultDoc = doc;
            ResultFilePath = filePath;
            ExportHelper.ShowPostExportDialog(this, filePath);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"발행 실패: {ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
