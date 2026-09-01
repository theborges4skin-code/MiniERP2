using MiniERP2.Controls;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 풀필먼트 발주 처리(FboOrderForm)에서 "지난 CSKU 불러오기"로 최근에 실제로 나갔던 CSKU의
/// 박스/품목 구성을 그대로 다시 담기 위한 선택 다이얼로그. CSKU당 최근 2개 발주일까지, 최근에
/// 나간 CSKU 30종까지만 보여준다(FboOrderRepository.GetRecentCskuGroups). "지난 발주 불러오기"가
/// 발주 1건 전체를 복사하는 것과 달리, 이건 CSKU 단위로 필요한 것만 골라 담을 때 쓴다.
/// </summary>
public class FboRecentCskuPickerDialog : Form
{
    public List<FboRecentCskuGroup> SelectedGroups { get; } = new();

    private readonly List<FboRecentCskuGroup> _allGroups;
    private List<FboRecentCskuGroup> _filteredGroups = [];

    private TextBox _searchBox = new();
    private DataGridView _grid = new();

    public FboRecentCskuPickerDialog(List<FboRecentCskuGroup> recentGroups)
    {
        _allGroups = recentGroups;
        InitializeComponent();
        ApplyFilter();
    }

    private void InitializeComponent()
    {
        Text = "지난 CSKU 불러오기";
        Size = new Size(680, 480);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _searchBox = new TextBox { Width = 260, PlaceholderText = "CSKU / 품목명 검색" };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchBar.Controls.AddRange(new Control[]
        {
            new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _searchBox,
            new Label { Text = $"(CSKU 최대 30종 × 최근 2건, 전체 {_allGroups.Count}건)", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(12, 5, 0, 0) },
        });

        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "선택", Width = 45, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, FillWeight = 1 });
        _grid.Columns.Add("Csku", "CSKU");
        _grid.Columns.Add("ItemName", "품목명");
        _grid.Columns.Add("OrderDate", "발주일");
        _grid.Columns.Add("BoxCount", "박스수");
        _grid.Columns.Add("TotalQty", "총수량");
        _grid.Columns["Csku"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["Csku"]!.Width = 100;
        _grid.Columns["OrderDate"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["OrderDate"]!.Width = 90;
        _grid.Columns["BoxCount"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["BoxCount"]!.Width = 70;
        _grid.Columns["TotalQty"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["TotalQty"]!.Width = 70;
        // 체크박스 열 더블클릭은 단순 토글, 그 외 열 더블클릭은 그 한 건만 바로 추가(빠른 단건 추가).
        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 0)
            {
                var cell = _grid.Rows[e.RowIndex].Cells["Selected"];
                cell.Value = !(cell.Value is true);
                return;
            }
            _grid.EndEdit();
            Confirm(singleRowIndex: e.RowIndex);
        };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnOk = new Button { Text = "선택 항목 추가", Width = 110 };
        var btnCancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
        var btnSelectAll = new Button { Text = "전체선택", Width = 90 };
        var btnSelectNone = new Button { Text = "전체해제", Width = 90 };
        btnSelectAll.Click += (s, e) => ToggleAll(true);
        btnSelectNone.Click += (s, e) => ToggleAll(false);
        btnOk.Click += (s, e) => Confirm();
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnSelectNone);
        btnPanel.Controls.Add(btnSelectAll);

        layout.Controls.Add(searchBar, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(btnPanel, 0, 2);
        Controls.Add(layout);
        CancelButton = btnCancel;
    }

    private void ApplyFilter()
    {
        var search = _searchBox.Text.Trim();
        _filteredGroups = _allGroups.Where(g =>
            search.Length == 0 ||
            g.Csku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            g.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        _grid.Rows.Clear();
        foreach (var g in _filteredGroups)
            _grid.Rows.Add(false, g.Csku, g.ItemName, g.OrderDate.ToString("yyyy-MM-dd"), g.BoxCount, g.TotalQty);
    }

    private void ToggleAll(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows) row.Cells["Selected"].Value = value;
    }

    /// <summary>singleRowIndex가 지정되면(더블클릭) 체크 상태와 무관하게 그 행 한 건만 추가한다.
    /// null이면(버튼 클릭) 체크된 행 전부를 추가한다.</summary>
    private void Confirm(int? singleRowIndex = null)
    {
        SelectedGroups.Clear();

        if (singleRowIndex is { } idx)
        {
            SelectedGroups.Add(_filteredGroups[idx]);
        }
        else
        {
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                if (_grid.Rows[i].Cells["Selected"].Value is true) SelectedGroups.Add(_filteredGroups[i]);
            }
        }

        if (SelectedGroups.Count == 0)
        {
            MessageBox.Show("추가할 항목을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
