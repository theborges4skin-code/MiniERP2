using MiniERP2.Controls;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 엑셀로 다시 가져온 CSKU 중 현재 DB와 값이 하나라도 다른 행만 모아 보여주고, 행마다 반영 여부를
/// 체크박스로 선택하게 하는 검토창(ChannelCskuForm의 [엑셀로 일괄수정] 전용). 값이 바뀐 필드는
/// "기존값 → 새값" 형태로 표시해 무엇이 바뀌는지 한눈에 보이게 한다. MasterCostUpdateReviewDialog와
/// 같은 관례를 따른다.
/// </summary>
public class ChannelCskuBulkUpdateReviewDialog : Form
{
    private readonly List<ChannelCskuUpdateRow> _rows;
    private DataGridView _grid = new();
    private Label _summaryLabel = new();

    public List<ChannelCskuUpdateRow> SelectedRows { get; } = new();

    public ChannelCskuBulkUpdateReviewDialog(List<ChannelCskuUpdateRow> rows, int unchangedCount, int notFoundCount, int invalidMskuCount, int duplicateCskuCount, int codeConflictCount)
    {
        _rows = rows;
        InitializeComponent(unchangedCount, notFoundCount, invalidMskuCount, duplicateCskuCount, codeConflictCount);
        PopulateGrid();
    }

    private void InitializeComponent(int unchangedCount, int notFoundCount, int invalidMskuCount, int duplicateCskuCount, int codeConflictCount)
    {
        Text = "CSKU 일괄수정 검토";
        Size = new Size(920, 580);
        MinimumSize = new Size(700, 420);
        StartPosition = FormStartPosition.CenterParent;

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var excluded = new List<string>();
        if (unchangedCount > 0) excluded.Add($"기존과 동일 {unchangedCount}건");
        if (notFoundCount > 0) excluded.Add($"현재 거래처에 없는 CSKU {notFoundCount}건");
        if (invalidMskuCount > 0) excluded.Add($"마스터SKU 미기재/미등록 {invalidMskuCount}건");
        if (duplicateCskuCount > 0) excluded.Add($"파일 내 중복 CSKU {duplicateCskuCount}건");
        if (codeConflictCount > 0) excluded.Add($"CSKU 코드 충돌 {codeConflictCount}건");

        var codeChangeCount = _rows.Count(r => r.CodeChanged);
        var summaryText = $"변경 대상 {_rows.Count}건" + (excluded.Count > 0 ? $" ({string.Join(", ", excluded)} 제외)" : string.Empty);
        if (codeChangeCount > 0)
            summaryText += $"\nCSKU 코드가 바뀌는 행 {codeChangeCount}건은 가격/변경 이력이 새 코드로 이동하고, 이 코드를 가리키던 매핑 규칙도 함께 옮겨집니다.";

        _summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 4, 0, 0),
            Text = summaryText,
        };

        main.Controls.Add(_summaryLabel, 0, 0);
        main.Controls.Add(BuildGrid(), 0, 1);
        main.Controls.Add(BuildBottomBar(), 0, 2);
        Controls.Add(main);
    }

    private Control BuildGrid()
    {
        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Apply",
            HeaderText = "반영",
            Width = 45,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
        });
        _grid.Columns.Add("CskuCode", "CSKU 코드");
        _grid.Columns["CskuCode"]!.MinimumWidth = 140;
        _grid.Columns.Add("Msku", "마스터SKU");
        _grid.Columns.Add("InvoiceDisplayName", "송장표시명");
        _grid.Columns.Add("SupplyPrice", "납품가");
        _grid.Columns.Add("Unit", "단위");
        _grid.Columns.Add("Packing", "포장단위");
        _grid.Columns.Add("Note", "비고");

        _grid.CellFormatting += OnCellFormatting;

        return _grid;
    }

    /// <summary>바뀐 필드만 빨간 글씨로 강조한다("기존 → 새값" 텍스트 자체는 PopulateGrid가 만든다).</summary>
    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var row = _rows[e.RowIndex];
        var changed = _grid.Columns[e.ColumnIndex].Name switch
        {
            "CskuCode" => row.CodeChanged,
            "Msku" => row.MskuChanged,
            "InvoiceDisplayName" => row.InvoiceDisplayNameChanged,
            "SupplyPrice" => row.SupplyPriceChanged,
            "Unit" => row.UnitChanged,
            "Packing" => row.PackingChanged,
            "Note" => row.NoteChanged,
            _ => false,
        };
        if (changed) e.CellStyle!.ForeColor = Color.Firebrick;
    }

    private static string FieldText(bool changed, string oldText, string newText) => changed ? $"{oldText} → {newText}" : newText;

    private void PopulateGrid()
    {
        foreach (var row in _rows)
        {
            _grid.Rows.Add(
                true,
                FieldText(row.CodeChanged, row.Existing.CskuCode, row.NewCskuCode),
                FieldText(row.MskuChanged, row.Existing.Msku, row.NewMsku),
                FieldText(row.InvoiceDisplayNameChanged, row.Existing.InvoiceDisplayName ?? string.Empty, row.NewInvoiceDisplayName ?? string.Empty),
                FieldText(row.SupplyPriceChanged, row.Existing.SupplyPrice.ToString("N0"), row.NewSupplyPrice.ToString("N0")),
                FieldText(row.UnitChanged, row.Existing.Unit, row.NewUnit),
                FieldText(row.PackingChanged, row.Existing.Packing ?? string.Empty, row.NewPacking ?? string.Empty),
                FieldText(row.NoteChanged, row.Existing.Note ?? string.Empty, row.NewNote ?? string.Empty));
        }
    }

    private Control BuildBottomBar()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var btnSelectAll = new Button { Text = "전체 선택", Width = 90, Height = 30 };
        btnSelectAll.Click += (s, e) => SetAllChecked(true);
        var btnDeselectAll = new Button { Text = "전체 해제", Width = 90, Height = 30 };
        btnDeselectAll.Click += (s, e) => SetAllChecked(false);

        var btnApply = new Button { Text = "반영", Width = 90, Height = 30 };
        btnApply.Click += OnApplyClick;
        var btnCancel = new Button { Text = "취소", Width = 90, Height = 30 };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        flow.Controls.AddRange(new Control[] { btnSelectAll, btnDeselectAll, btnApply, btnCancel });
        return flow;
    }

    private void SetAllChecked(bool value)
    {
        _grid.EndEdit();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Cells["Apply"].Value = value;
        }
        _grid.Refresh();
    }

    private void OnApplyClick(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        SelectedRows.Clear();
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].Cells["Apply"].Value is true)
            {
                SelectedRows.Add(_rows[i]);
            }
        }

        if (SelectedRows.Count == 0)
        {
            MessageBox.Show("반영할 항목을 하나 이상 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                $"체크된 {SelectedRows.Count}건의 CSKU 정보를 반영합니다. 계속하시겠습니까?",
                "반영 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
