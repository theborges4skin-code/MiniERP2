using MiniERP2.Controls;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 엑셀로 읽은 제조원가 중 마스터DB(ItemTable.CostPrice)와 값이 다른 행만 모아 보여주고,
/// 행마다 반영 여부를 체크박스로 선택하게 하는 검토창. 동일한 값은 이 창에 아예 올라오지 않는다
/// (MasterCostUpdatePlanner에서 이미 제외됨).
/// </summary>
public class MasterCostUpdateReviewDialog : Form
{
    private readonly List<MasterCostUpdateRow> _rows;
    private DataGridView _grid = new();
    private Label _summaryLabel = new();

    public List<MasterCostUpdateRow> SelectedRows { get; } = new();

    public MasterCostUpdateReviewDialog(List<MasterCostUpdateRow> rows, int unchangedCount, int notFoundCount, int duplicateSkuCount)
    {
        _rows = rows;
        InitializeComponent(unchangedCount, notFoundCount, duplicateSkuCount);
        PopulateGrid();
    }

    private void InitializeComponent(int unchangedCount, int notFoundCount, int duplicateSkuCount)
    {
        Text = "제조원가 업데이트 검토";
        Size = new Size(760, 560);
        MinimumSize = new Size(600, 400);
        StartPosition = FormStartPosition.CenterParent;

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        _summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Text = $"변경 대상 {_rows.Count}건 (마스터와 동일 {unchangedCount}건 제외, 마스터DB 미등록 {notFoundCount}건 제외"
                + (duplicateSkuCount > 0 ? $", 파일 내 중복 SKU {duplicateSkuCount}건 제외" : "") + ")",
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
        _grid.Columns.Add("Sku", "SKU");
        _grid.Columns.Add("ItemName", "상품명");
        _grid.Columns.Add("OldCost", "기존 원가");
        _grid.Columns.Add("NewCost", "신규 원가");
        _grid.Columns.Add("Diff", "차액");

        _grid.Columns["OldCost"]!.DefaultCellStyle.Format = "N2";
        _grid.Columns["NewCost"]!.DefaultCellStyle.Format = "N2";
        _grid.Columns["Diff"]!.DefaultCellStyle.Format = "N2";
        _grid.Columns["OldCost"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns["NewCost"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns["Diff"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        _grid.CellFormatting += OnCellFormatting;

        return _grid;
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Diff") return;
        if (e.Value is not decimal diff) return;
        e.CellStyle!.ForeColor = diff > 0 ? Color.Firebrick : diff < 0 ? Color.DarkBlue : Color.Black;
    }

    private void PopulateGrid()
    {
        foreach (var row in _rows)
        {
            _grid.Rows.Add(true, row.Sku, row.ItemName, row.OldCost, row.NewCost, row.Diff);
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
                $"체크된 {SelectedRows.Count}건의 제조원가를 마스터DB에 반영합니다. 계속하시겠습니까?",
                "반영 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
